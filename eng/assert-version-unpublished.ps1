[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') |
    ConvertFrom-Json
$headers = @{ 'User-Agent' = 'quillwright-release-preflight' }

foreach ($package in $manifest.packages) {
    $id = ([string]$package.id).ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$id/index.json"
    $index = Invoke-RestMethod -Uri $uri -Headers $headers
    if (@($index.versions) -contains $PackageVersion.ToLowerInvariant()) {
        throw "NuGet.org already contains '$($package.id)' $PackageVersion. Versions are immutable."
    }
}

Write-Host "Version $PackageVersion is unpublished for all $($manifest.packages.Count) package IDs."
