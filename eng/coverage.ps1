[CmdletBinding()]
param(
    [switch] $NoRestore,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solution = Join-Path $repositoryRoot "Quillwright.slnx"
$coverageDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts/coverage"))
$expectedPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (!$coverageDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a coverage directory outside the repository: $coverageDirectory"
}

if (Test-Path -LiteralPath $coverageDirectory) {
    Remove-Item -LiteralPath $coverageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $coverageDirectory | Out-Null

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    if (!$NoRestore) {
        Invoke-DotNet @("restore", $solution, "--nologo")
    }
    if (!$NoBuild) {
        $buildArguments = @("build", $solution, "-c", "Release", "--nologo")
        if ($NoRestore) {
            $buildArguments += "--no-restore"
        }
        Invoke-DotNet $buildArguments
    }

    $testRuns = @(
        @{ Project = "tests/Quillwright.Tests/Quillwright.Tests.csproj"; Module = "Quillwright.dll" },
        @{ Project = "tests/Quillwright.Doc.Tests/Quillwright.Doc.Tests.csproj"; Module = "Quillwright.Doc.dll" },
        @{ Project = "tests/Quillwright.Pdf.Tests/Quillwright.Pdf.Tests.csproj"; Module = "Quillwright.Pdf.dll" },
        @{ Project = "tests/Quillwright.Rtf.Tests/Quillwright.Rtf.Tests.csproj"; Module = "Quillwright.Rtf.dll" },
        @{ Project = "tests/Quillwright.Templates.Tests/Quillwright.Templates.Tests.csproj"; Module = "Quillwright.Templates.dll" }
    )
    $coverageBase = Join-Path $coverageDirectory "coverage"
    $coverageJson = "$coverageBase.json"

    for ($index = 0; $index -lt $testRuns.Count; $index++) {
        $testRun = $testRuns[$index]
        $arguments = @(
            "test", $testRun.Project,
            "-c", "Release",
            "--no-build",
            "--no-restore",
            "--nologo",
            "/p:CollectCoverage=true",
            "/p:CoverletOutput=$coverageBase",
            "/p:CoverletOutputFormat=json",
            "/p:Include=[Quillwright*]*"
        )
        if ($index -gt 0) {
            $arguments += "/p:MergeWith=$coverageJson"
        }
        Invoke-DotNet $arguments

        if (!(Test-Path -LiteralPath $coverageJson)) {
            throw "Coverlet did not produce $coverageJson for $($testRun.Project)."
        }
        $partialCoverage = Get-Content -LiteralPath $coverageJson -Raw | ConvertFrom-Json -AsHashtable
        if (!$partialCoverage.ContainsKey($testRun.Module)) {
            throw "Coverlet did not instrument $($testRun.Module) while running $($testRun.Project)."
        }
    }
}
finally {
    Pop-Location
}

if (!(Test-Path -LiteralPath $coverageJson)) {
    throw "Coverlet did not produce $coverageJson."
}

# These are per-runtime-package ratchets, deliberately below the measured value of a clean clone
# without either optional corpus. Raise them with coverage evidence; do not lower them to land a
# change. Requiring all five modules also catches a silently failed instrumentation pass.
$thresholds = [ordered]@{
    "Quillwright.dll"           = @{ Line = 76.0; Branch = 59.0 }
    "Quillwright.Doc.dll"       = @{ Line = 80.0; Branch = 68.0 }
    "Quillwright.Pdf.dll"       = @{ Line = 83.0; Branch = 70.0 }
    "Quillwright.Rtf.dll"       = @{ Line = 75.0; Branch = 61.0 }
    "Quillwright.Templates.dll" = @{ Line = 70.0; Branch = 61.0 }
}

$coverage = Get-Content -LiteralPath $coverageJson -Raw | ConvertFrom-Json -AsHashtable
$failures = [Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "Coverage ratchet"
Write-Host "----------------"
foreach ($module in $thresholds.Keys) {
    if (!$coverage.ContainsKey($module)) {
        $failures.Add("$module was not instrumented")
        continue
    }

    [long] $totalLines = 0
    [long] $coveredLines = 0
    [long] $totalBranches = 0
    [long] $coveredBranches = 0
    foreach ($document in $coverage[$module].Values) {
        foreach ($class in $document.Values) {
            foreach ($method in $class.Values) {
                foreach ($hits in $method.Lines.Values) {
                    $totalLines++
                    if ([long] $hits -gt 0) {
                        $coveredLines++
                    }
                }
                foreach ($branch in $method.Branches) {
                    $totalBranches++
                    if ([long] $branch.Hits -gt 0) {
                        $coveredBranches++
                    }
                }
            }
        }
    }

    if ($totalLines -eq 0) {
        $failures.Add("$module contains no instrumented sequence points")
        continue
    }
    if ($totalBranches -eq 0) {
        $failures.Add("$module contains no instrumented branches")
        continue
    }

    $lineRate = 100.0 * $coveredLines / $totalLines
    $branchRate = 100.0 * $coveredBranches / $totalBranches
    $minimum = $thresholds[$module]
    Write-Host ("{0,-27} line {1,6:N2}% (min {2:N2}%)  branch {3,6:N2}% (min {4:N2}%)" -f
        $module, $lineRate, $minimum.Line, $branchRate, $minimum.Branch)

    if ($lineRate -lt $minimum.Line) {
        $failures.Add("$module line coverage is $($lineRate.ToString('N2'))%; expected at least $($minimum.Line)%")
    }
    if ($branchRate -lt $minimum.Branch) {
        $failures.Add("$module branch coverage is $($branchRate.ToString('N2'))%; expected at least $($minimum.Branch)%")
    }
}

if ($failures.Count -gt 0) {
    throw "Coverage ratchet failed:`n - $($failures -join "`n - ")"
}

Write-Host "Coverage ratchet passed. Raw merged data: $coverageJson"
