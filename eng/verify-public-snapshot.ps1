[CmdletBinding()]
param(
    [string]$ExpectedTag,
    [string]$RepositoryRoot,
    [string]$ManifestPath,
    [switch]$AllowGeneratedArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
}
$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$manifestPath = [IO.Path]::GetFullPath($ManifestPath)
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git -C $repositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Get-GitBlobSha256 {
    param([Parameter(Mandatory)][string]$ObjectSpec)

    $startInfo = [Diagnostics.ProcessStartInfo]::new('git')
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.ArgumentList.Add('cat-file')
    $startInfo.ArgumentList.Add('blob')
    $startInfo.ArgumentList.Add($ObjectSpec)

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Could not start git cat-file for the public sentinel.'
    }
    try {
        $stderr = $process.StandardError.ReadToEndAsync()
        $content = [IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($content)
            $process.WaitForExit()
            $errorText = $stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw "git cat-file failed for '$ObjectSpec': $($errorText.Trim())"
            }
            return [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($content.ToArray()))
        }
        finally {
            $content.Dispose()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Normalize-RepositoryUrl {
    param([Parameter(Mandatory)][string]$Url)

    $normalized = $Url.Trim().TrimEnd('/')
    if ($normalized.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(0, $normalized.Length - 4)
    }
    if ($normalized -match '^(?i)https://github\.com/(?<path>.+)$') {
        return $Matches.path.ToLowerInvariant()
    }
    if ($normalized -match '^(?i)git@github\.com:(?<path>.+)$') {
        return $Matches.path.ToLowerInvariant()
    }
    if ($normalized -match '^(?i)ssh://git@github\.com/(?<path>.+)$') {
        return $Matches.path.ToLowerInvariant()
    }
    throw "Unsupported origin URL '$Url'. Use the official GitHub HTTPS or SSH remote."
}

$gitRootLines = @(Invoke-Git @('rev-parse', '--show-toplevel'))
$actualGitRoot = [IO.Path]::GetFullPath($gitRootLines[0].Trim())
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $actualGitRoot.Equals($repositoryRoot, $pathComparison)) {
    throw @"
Release packages must be built from the standalone public repository root.
Expected Git root: $repositoryRoot
Actual Git root:   $actualGitRoot
The private monorepo commit is not a valid Source Link commit for the public package.
"@
}

$originLines = @(Invoke-Git @('remote', 'get-url', 'origin'))
$originUrl = $originLines[0].Trim()
$actualRepository = Normalize-RepositoryUrl $originUrl
$expectedRepository = Normalize-RepositoryUrl ([string]$manifest.repositoryUrl)
if ($actualRepository -ne $expectedRepository) {
    throw "origin is '$originUrl'; release origin must be '$($manifest.repositoryUrl)'."
}

$status = @(Invoke-Git @('status', '--porcelain=v1', '--untracked-files=all', '--', '.'))
if ($status.Count -gt 0) {
    throw "Release checkout is not clean:`n$($status -join [Environment]::NewLine)"
}

$ignored = @(Invoke-Git @('ls-files', '--others', '--ignored', '--exclude-standard', '--', '.'))
$unexpectedIgnored = @(
    if ($AllowGeneratedArtifacts) {
        $ignored | Where-Object {
            $_.EndsWith('.user', [StringComparison]::OrdinalIgnoreCase) -or
            -not $_.StartsWith('artifacts/', [StringComparison]::OrdinalIgnoreCase)
        }
    }
    else {
        $ignored
    }
)
if ($unexpectedIgnored.Count -gt 0) {
    throw @"
Release checkout contains ignored files. Ignored MSBuild inputs such as *.csproj.user and
default-Compile files under src can change the package without changing the commit. Build the
release from a fresh clone after reviewing these paths:
$($unexpectedIgnored -join [Environment]::NewLine)
"@
}

$eolLines = @(Invoke-Git @(
    'ls-files', '--eol', '--',
    '*.cs', '*.csproj', '*.props', '*.targets', '*.slnx',
    '*.json', '*.md', '*.xml', '*.config', '*.yml', '*.yaml',
    '*.ps1', '*.sh', '.editorconfig', '.gitattributes', '.gitignore'
))
$invalidEol = @($eolLines | Where-Object {
    $_ -notmatch '^i/lf\s+w/lf\s+'
})
if ($invalidEol.Count -gt 0) {
    throw @"
Tracked release inputs are not LF in both Git and the checkout. Re-clone after committing
.gitattributes; do not publish PDB checksums made from CRLF files.
$($invalidEol -join [Environment]::NewLine)
"@
}

$commitLines = @(Invoke-Git @('rev-parse', 'HEAD'))
$commit = $commitLines[0].Trim().ToLowerInvariant()
if ($commit -notmatch '^[0-9a-f]{40}$') {
    throw "Git returned invalid commit id '$commit'."
}

$sentinelPath = 'Directory.Build.props'
$sentinelUri = [Uri]::new(
    "https://raw.githubusercontent.com/WrightDocumentSuite/quillwright/$commit/$sentinelPath")
$localSentinelHash = Get-GitBlobSha256 "$commit`:$sentinelPath"
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('quillwright-release-guard')
$remoteSentinelHash = $null
$sentinelFailure = 'no response'
try {
    foreach ($attempt in 1..3) {
        $response = $null
        try {
            $response = $client.GetAsync($sentinelUri).GetAwaiter().GetResult()
            if ([int]$response.StatusCode -eq 200) {
                [byte[]]$remoteBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                $remoteSentinelHash = [Convert]::ToHexString(
                    [Security.Cryptography.SHA256]::HashData($remoteBytes))
                break
            }
            $sentinelFailure = "HTTP $([int]$response.StatusCode) ($($response.ReasonPhrase))"
        }
        catch {
            $sentinelFailure = $_.Exception.Message
        }
        finally {
            if ($null -ne $response) {
                $response.Dispose()
            }
        }

        if ($attempt -lt 3) {
            Start-Sleep -Seconds $attempt
        }
    }
}
finally {
    $client.Dispose()
}
if ($null -eq $remoteSentinelHash) {
    throw "Commit $commit is not anonymously readable from the canonical public raw URL: $sentinelFailure"
}
if ($remoteSentinelHash -ne $localSentinelHash) {
    throw "Public sentinel bytes for commit $commit differ from the local Git blob."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedTag)) {
    $tagLines = @(Invoke-Git @('rev-parse', "$ExpectedTag^{commit}"))
    $tagCommit = $tagLines[0].Trim().ToLowerInvariant()
    if ($tagCommit -ne $commit) {
        throw "Tag '$ExpectedTag' points to $tagCommit, not release commit $commit."
    }

    $remoteTag = @(Invoke-Git @(
        'ls-remote', 'origin',
        "refs/tags/$ExpectedTag",
        "refs/tags/$ExpectedTag^{}"
    ))
    if (-not ($remoteTag | Where-Object { $_ -match "^$commit\s" })) {
        throw "Public origin does not advertise tag '$ExpectedTag' at commit $commit."
    }
}

Write-Host "Public snapshot verified: $commit ($originUrl)."
