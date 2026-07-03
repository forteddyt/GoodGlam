#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Computes the version to stamp for a GoodGlam release build.

.DESCRIPTION
    Two channels:

      * dev  -> "<X.Y.Z>.<RunNumber>", where X.Y.Z is the current stable version (so the dev build is
                always just ahead of stable, satisfying Dalamud's "testing is ahead" semantics). Uses
                the csproj base version when no stable release exists yet.

      * prod -> the next stable X.Y.Z. When no stable release exists yet, this is the csproj base
                version (the first release; -Bump is ignored). Otherwise the requested component of the
                current stable version is incremented (patch/minor/major).

    Guardrails for prod (defence-in-depth so a stable version can never go backwards or collide):
      * SemVer-format validation of the computed X.Y.Z.
      * Strict monotonic check: computed version must be greater than the current stable version.
      * Collision guard: fail if a release tag v<version> already exists.

    The current stable version is read from the newest non-prerelease GitHub Release (via gh);
    the base/seed version is read from the plugin csproj <Version>.

.PARAMETER Channel
    "dev" or "prod".

.PARAMETER Bump
    For prod: which component to increment - patch (default), minor, or major. Ignored for the first
    release and for dev.

.PARAMETER RunNumber
    For dev: the monotonically increasing build number (e.g. github.run_number).

.PARAMETER Repo
    owner/repo slug. Defaults to GITHUB_REPOSITORY.

.PARAMETER CsprojPath
    Path to the plugin csproj holding the base <Version>. Defaults to src/GoodGlam/GoodGlam.csproj.

.OUTPUTS
    Writes the computed version to stdout. When GITHUB_OUTPUT is set, also appends
    "version=<value>" and "tag=v<value>" (prod only) for workflow consumption.

.NOTES
    Requires the GitHub CLI (gh) authenticated with repo read access.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "prod")]
    [string]$Channel,

    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",

    [int]$RunNumber = 0,

    [string]$Repo = $env:GITHUB_REPOSITORY,

    [string]$CsprojPath = "src/GoodGlam/GoodGlam.csproj"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# The guards below rely on inspecting $LASTEXITCODE after gh calls: Get-StableXyz treats a missing
# "latest" release (404) as "no stable yet", and the collision guard treats a non-zero
# `gh release view` as "tag is free". On PowerShell 7.4+ runners where
# $PSNativeCommandUseErrorActionPreference defaults to $true, a non-zero native exit under
# ErrorActionPreference=Stop would terminate before those checks - so opt out here.
$PSNativeCommandUseErrorActionPreference = $false

if ([string]::IsNullOrWhiteSpace($Repo)) {
    throw "Repository slug not provided. Pass -Repo owner/name or set GITHUB_REPOSITORY."
}
if (-not (Test-Path $CsprojPath)) {
    throw "csproj not found: $CsprojPath"
}

# --- base (seed) version from the csproj ------------------------------------

$csproj = Get-Content $CsprojPath -Raw
$m = [regex]::Match($csproj, "<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)(?:\.[0-9]+)?\s*</Version>")
if (-not $m.Success) {
    throw "Could not read a X.Y.Z <Version> from $CsprojPath"
}
$baseXyz = $m.Groups[1].Value

# --- current stable X.Y.Z from GitHub Releases ------------------------------

function Get-StableXyz {
    $tag = & gh api "repos/$Repo/releases/latest" --jq ".tag_name" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tag)) { return $null }
    $tag = $tag.Trim().TrimStart("v")
    $tm = [regex]::Match($tag, "^([0-9]+\.[0-9]+\.[0-9]+)")
    if (-not $tm.Success) { return $null }
    return $tm.Groups[1].Value
}

$stableXyz = Get-StableXyz

# --- compute -----------------------------------------------------------------

function Step-Version {
    param([string]$Xyz, [string]$Which)
    $parts = $Xyz.Split(".")
    [int]$maj = $parts[0]; [int]$min = $parts[1]; [int]$pat = $parts[2]
    switch ($Which) {
        "major" { $maj++; $min = 0; $pat = 0 }
        "minor" { $min++; $pat = 0 }
        "patch" { $pat++ }
    }
    return "$maj.$min.$pat"
}

if ($Channel -eq "dev") {
    $effectiveBase = if ($stableXyz) { $stableXyz } else { $baseXyz }
    $version = "$effectiveBase.$RunNumber"
}
else {
    # prod
    if (-not $stableXyz) {
        # First stable release: ship the csproj base version as-is (bump ignored).
        $version = $baseXyz
    }
    else {
        $version = Step-Version -Xyz $stableXyz -Which $Bump
    }

    # SemVer-format validation.
    if ($version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
        throw "Computed prod version '$version' is not a valid X.Y.Z."
    }

    # Strict monotonic guard against the current stable version.
    if ($stableXyz) {
        if ([version]"$version.0" -le [version]"$stableXyz.0") {
            throw "Computed prod version '$version' does not increase over current stable '$stableXyz'."
        }
    }

    # Collision guard: the tag must not already exist.
    $null = & gh release view "v$version" --repo $Repo 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "Release tag 'v$version' already exists; refusing to clobber a shipped release."
    }
}

Write-Output $version

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    if ($Channel -eq "prod") {
        "tag=v$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    }
}

# gh calls above may leave a non-zero $LASTEXITCODE from a tolerated "not found" (e.g. no stable
# release yet, or a free tag in the collision guard). We only reach here on success - real errors
# throw - so clear it explicitly; GitHub Actions' pwsh wrapper exits the step with $LASTEXITCODE.
exit 0
