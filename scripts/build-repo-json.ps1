#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates the Dalamud custom-repository JSON (plugin master) for GoodGlam.

.DESCRIPTION
    Emits a single-entry plugin-master array with GoodGlam's two distribution channels:

      * Stable   -> AssemblyVersion         + DownloadLinkInstall / DownloadLinkUpdate
      * Testing  -> TestingAssemblyVersion  + DownloadLinkTesting

    Dalamud consumers see the stable channel by default and the testing channel only when the user
    enables "Get plugin testing builds". One repo URL therefore serves both.

    Static fields (Name, Author, Punchline, ...) come from the locally built manifest passed via
    -ManifestPath. The per-channel VERSIONS are derived from the actual GitHub Releases so that this
    script produces a correct, complete repo.json no matter which release job invoked it (the testing
    job and the stable job each only touch their own channel's release, but both must emit the full
    file without clobbering the other channel). For each channel we download that channel's uploaded
    GoodGlam.json asset and read its AssemblyVersion / DalamudApiLevel, so the advertised version
    always matches exactly what ships in that channel's latest.zip.

    Bootstrap: before the first stable release exists, the stable channel falls back to the testing
    build and the entry is marked IsTestingExclusive = true, so Dalamud only offers it to testing
    users until a real stable release is cut.

.PARAMETER ManifestPath
    Path to the locally built plugin manifest (GoodGlam.json) used for the static fields.

.PARAMETER OutputPath
    Where to write the generated repo.json.

.PARAMETER Repo
    owner/repo slug. Defaults to the GITHUB_REPOSITORY environment variable.

.PARAMETER TestingTag
    The rolling prerelease tag used for the testing channel. Defaults to "testing".

.NOTES
    Requires the GitHub CLI (gh) authenticated with repo read access (GH_TOKEN / GITHUB_TOKEN).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Repo = $env:GITHUB_REPOSITORY,

    [string]$TestingTag = "testing"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# We intentionally inspect $LASTEXITCODE after gh calls to treat "not found" (e.g. no stable release
# yet) as a normal fallback rather than a failure. On PowerShell 7.4+ runners where
# $PSNativeCommandUseErrorActionPreference defaults to $true, a non-zero native exit under
# ErrorActionPreference=Stop becomes a terminating error before that check runs - so opt out here.
$PSNativeCommandUseErrorActionPreference = $false

if ([string]::IsNullOrWhiteSpace($Repo)) {
    throw "Repository slug not provided. Pass -Repo owner/name or set GITHUB_REPOSITORY."
}
if (-not (Test-Path $ManifestPath)) {
    throw "Built manifest not found: $ManifestPath"
}

# --- helpers ---------------------------------------------------------------

# Invoke gh and return stdout, or $null if the command failed (e.g. release not found).
function Invoke-GhOrNull {
    param([string[]]$GhArgs)

    $out = & gh @GhArgs 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}

# Download the GoodGlam.json asset attached to a given release tag and return it parsed, or $null if
# the release (or its manifest asset) doesn't exist.
function Get-ReleaseManifest {
    param([string]$Tag)

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    try {
        $null = Invoke-GhOrNull @("release", "download", $Tag, "--repo", $Repo, "--pattern", "GoodGlam.json", "--dir", $tmp)
        $manifestFile = Join-Path $tmp "GoodGlam.json"
        if (-not (Test-Path $manifestFile)) { return $null }
        return Get-Content $manifestFile -Raw | ConvertFrom-Json
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# The tag of the newest non-prerelease release (GitHub's "latest"), or $null when none exists yet.
function Get-StableTag {
    $tag = Invoke-GhOrNull @("api", "repos/$Repo/releases/latest", "--jq", ".tag_name")
    if ([string]::IsNullOrWhiteSpace($tag)) { return $null }
    return $tag.Trim()
}

# --- static fields (from the locally built manifest) -----------------------

$local = Get-Content $ManifestPath -Raw | ConvertFrom-Json

# --- channel state (from GitHub Releases) ----------------------------------

$stableTag        = Get-StableTag
$stableManifest   = if ($stableTag) { Get-ReleaseManifest -Tag $stableTag } else { $null }
$testingManifest  = Get-ReleaseManifest -Tag $TestingTag

$baseUrl          = "https://github.com/$Repo/releases"
$stableZipUrl     = "$baseUrl/latest/download/latest.zip"
$testingZipUrl    = "$baseUrl/download/$TestingTag/latest.zip"

$hasStable = $null -ne $stableManifest
$hasTesting = $null -ne $testingManifest

if (-not $hasStable -and -not $hasTesting) {
    throw "No stable and no testing ($TestingTag) release found for $Repo. Publish a release before generating repo.json."
}

# Prefer each channel's own manifest for its version + API level; fall back across channels so a
# bootstrap (testing-only) or a stable-only repo still emits every required, non-nullable field.
$stableVersion  = if ($hasStable)  { [string]$stableManifest.AssemblyVersion } else { [string]$testingManifest.AssemblyVersion }
$testingVersion = if ($hasTesting) { [string]$testingManifest.AssemblyVersion } else { [string]$stableManifest.AssemblyVersion }

$stableApi = if ($hasStable) { [int]$stableManifest.DalamudApiLevel } else { [int]$local.DalamudApiLevel }
$testingApi = if ($hasTesting) { [int]$testingManifest.DalamudApiLevel } else { [int]$local.DalamudApiLevel }

# Until a real stable release exists, only offer the plugin to testing users.
$isTestingExclusive = -not $hasStable

# Stable download links point at GitHub's "latest" (non-prerelease); before that exists they fall
# back to the testing zip so the non-nullable link fields stay valid.
$stableInstallUrl = if ($hasStable) { $stableZipUrl } else { $testingZipUrl }

# The testing link normally points at the rolling testing zip. If a stable release exists but no
# testing release does yet (a stable release cut before any testing build), fall back to the stable
# zip so the advertised testing download is always resolvable rather than a 404.
$testingInstallUrl = if ($hasTesting) { $testingZipUrl } else { $stableInstallUrl }

# --- assemble the plugin-master entry --------------------------------------

$entry = [ordered]@{
    Author                = [string]$local.Author
    Name                  = [string]$local.Name
    InternalName          = if ($local.PSObject.Properties.Name -contains "InternalName") { [string]$local.InternalName } else { "GoodGlam" }
    Punchline             = [string]$local.Punchline
    Description           = [string]$local.Description
    RepoUrl               = [string]$local.RepoUrl
    ApplicableVersion     = if ($local.PSObject.Properties.Name -contains "ApplicableVersion") { [string]$local.ApplicableVersion } else { "any" }
    Tags                  = @($local.Tags)
    DalamudApiLevel       = $stableApi
    IconUrl               = [string]$local.IconUrl
    AssemblyVersion       = $stableVersion
    TestingAssemblyVersion = $testingVersion
    TestingDalamudApiLevel = $testingApi
    IsTestingExclusive    = $isTestingExclusive
    IsHide                = $false
    LastUpdate            = [int64][Math]::Floor(([DateTimeOffset]::UtcNow).ToUnixTimeSeconds())
    DownloadLinkInstall   = $stableInstallUrl
    DownloadLinkUpdate    = $stableInstallUrl
    DownloadLinkTesting   = $testingInstallUrl
}

# -AsArray guarantees a JSON array even with a single entry; without it a one-element array is
# unwrapped to a bare object, which a Dalamud plugin master (an array of manifests) can't parse.
# Pass the bare object (not @(...)) so -AsArray wraps it exactly once.
$json = ConvertTo-Json $entry -Depth 6 -AsArray

$outDir = Split-Path -Parent $OutputPath
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
# Write UTF-8 without BOM so the file served over HTTP is clean.
[System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $OutputPath"
Write-Host "  stable (AssemblyVersion):        $stableVersion  (api $stableApi, testing-exclusive: $isTestingExclusive)"
Write-Host "  testing (TestingAssemblyVersion): $testingVersion  (api $testingApi)"

# gh calls above may leave a non-zero $LASTEXITCODE from a tolerated "not found" (e.g. no stable
# release yet). We only reach here on success - real errors throw - so clear it explicitly; GitHub
# Actions' pwsh wrapper exits the step with $LASTEXITCODE.
exit 0
