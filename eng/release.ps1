[CmdletBinding()]
param(
    [string]$ExpectedVersion,
    [string]$OutputDirectory,
    [string]$NuGetPackagesDirectory,
    [switch]$RequireVersionTag,
    [switch]$AllowGeneratedArtifacts,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$buildProperties = [xml](Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props'))
$versionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props does not define a release Version.'
}
$packageVersion = $versionNode.InnerText.Trim()
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $ExpectedVersion -ne $packageVersion) {
    throw "Requested version $ExpectedVersion differs from Directory.Build.props version $packageVersion."
}

$tag = if ($RequireVersionTag) { "v$packageVersion" } else { '' }
& (Join-Path $PSScriptRoot 'verify-public-snapshot.ps1') `
    -ExpectedTag $tag `
    -AllowGeneratedArtifacts:$AllowGeneratedArtifacts

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts/release/packages'
}
if ([string]::IsNullOrWhiteSpace($NuGetPackagesDirectory)) {
    $NuGetPackagesDirectory = Join-Path $repositoryRoot 'artifacts/release/nuget-packages'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$NuGetPackagesDirectory = [IO.Path]::GetFullPath($NuGetPackagesDirectory)

New-Item -ItemType Directory -Force -Path $OutputDirectory, $NuGetPackagesDirectory | Out-Null
$existingOutput = Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1
if ($null -ne $existingOutput) {
    throw "Output directory '$OutputDirectory' is not empty; use an empty directory."
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'restore-package-baselines.ps1') `
    -NuGetPackagesDirectory $NuGetPackagesDirectory `
    -BaselineVersion ([string]$manifest.baselineVersion)

$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $NuGetPackagesDirectory

    Invoke-DotNet @(
        'restore', (Join-Path $repositoryRoot 'Quillwright.slnx'),
        '--configfile', (Join-Path $repositoryRoot 'nuget.config'),
        '--packages', $NuGetPackagesDirectory,
        '--force'
    )
    Invoke-DotNet @(
        'format', (Join-Path $repositoryRoot 'Quillwright.slnx'),
        '--verify-no-changes', '--no-restore', '--severity', 'warn'
    )
    Invoke-DotNet @(
        'build', (Join-Path $repositoryRoot 'Quillwright.slnx'),
        '--configuration', 'Release', '--no-restore', '-t:Rebuild',
        '-p:ContinuousIntegrationBuild=true'
    )
    if (-not $SkipTests) {
        Invoke-DotNet @(
            'test', (Join-Path $repositoryRoot 'Quillwright.slnx'),
            '--configuration', 'Release', '--no-build', '--no-restore'
        )
    }

    foreach ($package in $manifest.packages) {
        Invoke-DotNet @(
            'pack', (Join-Path $repositoryRoot ([string]$package.project)),
            '--configuration', 'Release', '--no-build', '--no-restore',
            "-p:PackageOutputPath=$OutputDirectory",
            '-p:ContinuousIntegrationBuild=true',
            '-p:EnablePackageValidation=true',
            "-p:PackageValidationBaselineVersion=$($manifest.baselineVersion)"
        )
    }

    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read release commit.'
    }
    Invoke-DotNet @(
        'run', '--project', (Join-Path $PSScriptRoot 'PackageVerifier/PackageVerifier.csproj'),
        '--configuration', 'Release', '--',
        '--package-directory', $OutputDirectory,
        '--version', $packageVersion,
        '--commit', $commit,
        '--repository-root', $repositoryRoot,
        '--manifest', $manifestPath
    )

    & (Join-Path $PSScriptRoot 'test-packages.ps1') `
        -PackageDirectory $OutputDirectory `
        -PackageVersion $packageVersion
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}

$packageFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -File |
    Where-Object { $_.Name -like '*.nupkg' -or $_.Name -like '*.snupkg' } |
    Sort-Object Name)
$checksumLines = foreach ($file in $packageFiles) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines(
    (Join-Path $OutputDirectory 'SHA256SUMS'),
    $checksumLines,
    [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'verify-package-checksums.ps1') -PackageDirectory $OutputDirectory

Write-Host "Release candidate $packageVersion is ready in '$OutputDirectory'."
