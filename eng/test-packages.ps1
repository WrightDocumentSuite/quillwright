[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$PackageVersion,
    [string]$RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') |
    ConvertFrom-Json
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Package directory '$PackageDirectory' does not exist."
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $platform = if ($IsWindows) {
        'win'
    }
    elseif ($IsLinux) {
        $hostRid = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier.ToLowerInvariant()
        $hasMuslLoader = @(Get-ChildItem -Path '/lib/ld-musl-*.so.1' -File -ErrorAction SilentlyContinue).Count -gt 0
        if ($hostRid.StartsWith('linux-musl-', [StringComparison]::Ordinal) -or
            (Test-Path -LiteralPath '/etc/alpine-release' -PathType Leaf) -or
            $hasMuslLoader) {
            'linux-musl'
        }
        else {
            'linux'
        }
    }
    elseif ($IsMacOS) {
        'osx'
    }
    else {
        throw 'Native AOT smoke is supported only on Windows, Linux, and macOS.'
    }
    $RuntimeIdentifier = "$platform-$architecture"
}

$smokeRoot = Join-Path $repositoryRoot "artifacts/package-smoke/$([Guid]::NewGuid().ToString('N'))"
$packagesCache = Join-Path $smokeRoot 'nuget-packages'
$publishDirectory = Join-Path $smokeRoot 'publish'
$nugetConfig = Join-Path $smokeRoot 'NuGet.config'
New-Item -ItemType Directory -Force -Path $smokeRoot, $packagesCache, $publishDirectory | Out-Null

$escapedPackageDirectory = [Security.SecurityElement]::Escape($PackageDirectory.Replace('\', '/'))
$packageMappings = ($manifest.packages | ForEach-Object {
    "      <package pattern=`"$([Security.SecurityElement]::Escape([string]$_.id))`" />"
}) -join [Environment]::NewLine
$configText = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate">
$packageMappings
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
[IO.File]::WriteAllText($nugetConfig, $configText, [Text.UTF8Encoding]::new($false))

$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $packagesCache
    $smokeProject = Join-Path $PSScriptRoot 'PackageSmoke/PackageSmoke.csproj'
    & dotnet restore $smokeProject `
        --configfile $nugetConfig `
        --packages $packagesCache `
        --force `
        --no-http-cache `
        "-p:SmokePackageVersion=$PackageVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring the packed consumer failed with exit code $LASTEXITCODE."
    }

    foreach ($package in $manifest.packages) {
        $id = ([string]$package.id).ToLowerInvariant()
        $candidate = Join-Path $PackageDirectory "$($package.id).$PackageVersion.nupkg"
        $cached = Join-Path $packagesCache "$id/$PackageVersion/$id.$PackageVersion.nupkg"
        if (-not (Test-Path -LiteralPath $cached -PathType Leaf)) {
            throw "Packed consumer did not restore '$($package.id)' $PackageVersion."
        }
        $candidateHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash
        $cachedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cached).Hash
        if ($candidateHash -ne $cachedHash) {
            throw "Packed consumer restored different bytes for '$($package.id)' $PackageVersion."
        }
    }

    & dotnet run `
        --project $smokeProject `
        --configuration Release `
        --no-restore `
        "-p:SmokePackageVersion=$PackageVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Managed packed-consumer smoke failed with exit code $LASTEXITCODE."
    }

    # A RID/AOT restore must happen after the managed run: both use the same project.assets.json,
    # and a managed build intentionally does not retain the Native AOT SDK item graph.
    & dotnet restore $smokeProject `
        --configfile $nugetConfig `
        --packages $packagesCache `
        --force `
        --no-http-cache `
        "-p:RuntimeIdentifier=$RuntimeIdentifier" `
        '-p:SelfContained=true' `
        "-p:SmokePackageVersion=$PackageVersion" `
        '-p:PublishAot=true'
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring the Native AOT packed consumer failed with exit code $LASTEXITCODE."
    }

    & dotnet publish $smokeProject `
        --configuration Release `
        --no-restore `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $publishDirectory `
        "-p:SmokePackageVersion=$PackageVersion" `
        '-p:PublishAot=true' `
        '-p:StripSymbols=true' `
        '-p:DebugType=None'
    if ($LASTEXITCODE -ne 0) {
        throw "Native AOT publish failed with exit code $LASTEXITCODE."
    }

    $executableName = if ($IsWindows) { 'PackageSmoke.exe' } else { 'PackageSmoke' }
    $executable = Join-Path $publishDirectory $executableName
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Native AOT publish did not create '$executable'."
    }
    & $executable
    if ($LASTEXITCODE -ne 0) {
        throw "Native AOT packed-consumer smoke failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}

Write-Host "Managed and Native AOT package smoke passed for $RuntimeIdentifier."
