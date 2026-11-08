[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$checksumPath = Join-Path $PackageDirectory 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Checksum manifest '$checksumPath' does not exist."
}

$listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadAllLines($checksumPath)) {
    if ($line -notmatch '^(?<hash>[0-9a-fA-F]{64})  (?<name>[^/\\]+)$') {
        throw "Invalid SHA256SUMS line '$line'."
    }
    $name = $Matches.name
    if (-not $listed.Add($name)) {
        throw "SHA256SUMS lists '$name' more than once."
    }
    $path = Join-Path $PackageDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "SHA256SUMS references missing file '$name'."
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actual -ne $Matches.hash) {
        throw "SHA-256 mismatch for '$name'."
    }
}

$actualPackages = @(Get-ChildItem -LiteralPath $PackageDirectory -File |
    Where-Object { $_.Name -like '*.nupkg' -or $_.Name -like '*.snupkg' } |
    ForEach-Object Name)
if ($actualPackages.Count -ne $listed.Count -or
    @($actualPackages | Where-Object { -not $listed.Contains($_) }).Count -gt 0) {
    throw 'Package directory and SHA256SUMS contain different package file sets.'
}

Write-Host "Verified SHA-256 for $($listed.Count) package files."
