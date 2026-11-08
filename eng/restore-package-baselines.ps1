[CmdletBinding()]
param(
    [string]$NuGetPackagesDirectory,
    [string]$BaselineVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($BaselineVersion)) {
    $BaselineVersion = [string]$manifest.baselineVersion
}
if ([string]::IsNullOrWhiteSpace($NuGetPackagesDirectory)) {
    $NuGetPackagesDirectory = Join-Path $repositoryRoot 'artifacts/release/nuget-packages'
}

$NuGetPackagesDirectory = [IO.Path]::GetFullPath($NuGetPackagesDirectory)
New-Item -ItemType Directory -Force -Path $NuGetPackagesDirectory | Out-Null
$existingCacheEntry = Get-ChildItem -LiteralPath $NuGetPackagesDirectory -Force |
    Select-Object -First 1
if ($null -ne $existingCacheEntry) {
    throw "Baseline restore requires an empty NuGet cache; '$NuGetPackagesDirectory' is not empty."
}

$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $NuGetPackagesDirectory
    & dotnet restore (Join-Path $PSScriptRoot 'PackageValidationBaselines/PackageValidationBaselines.csproj') `
        --configfile (Join-Path $repositoryRoot 'nuget.config') `
        --packages $NuGetPackagesDirectory `
        --force `
        --no-http-cache `
        "-p:BaselineVersion=$BaselineVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring package-validation baselines failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}

foreach ($package in $manifest.packages) {
    $id = ([string]$package.id).ToLowerInvariant()
    $nupkg = Join-Path $NuGetPackagesDirectory "$id/$BaselineVersion/$id.$BaselineVersion.nupkg"
    if (-not (Test-Path -LiteralPath $nupkg -PathType Leaf)) {
        throw "Baseline package '$($package.id)' $BaselineVersion was not restored to '$nupkg'."
    }
}

Write-Host "Restored $($manifest.packages.Count) package-validation baselines at $BaselineVersion."
