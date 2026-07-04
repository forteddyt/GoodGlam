#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Computes the version to stamp for a GoodGlam release build.

.DESCRIPTION
    Two channels:

      * testing -> "<X.Y.Z>.<RunNumber>", where X.Y.Z is the current stable version (so testing stays
                   just ahead of stable, matching Dalamud's "Get plugin testing builds" semantics).
                   Uses the explicit first stable seed when no stable release exists yet.

      * stable  -> the next stable X.Y.Z. When no stable release exists yet, this is the explicit
                   first stable seed (the first release; -Bump is ignored). Otherwise the requested component of
                   the current stable version is incremented (patch/minor/major).

    Guardrails for stable (defence-in-depth so a stable version can never go backwards or collide):
      * SemVer-format validation of the computed X.Y.Z.
      * Strict monotonic check: computed version must be greater than the current stable version.
      * Collision guard: fail if a release tag v<version> already exists.

    The current stable version is read from the newest non-prerelease GitHub Release (via gh).
    The first stable seed is explicit so release seeding cannot regress if the csproj local-build
    sentinel changes.

.PARAMETER Channel
    "testing" or "stable".

.PARAMETER Bump
    For stable: which component to increment - patch (default), minor, or major. Ignored for the
    first release and for testing.

.PARAMETER RunNumber
    For testing: the monotonically increasing build number (e.g. github.run_number).

.PARAMETER StableBase
    For testing only: an explicit stable X.Y.Z to use as the base instead of reading the newest stable
    release from GitHub. The release workflow passes this after a stable release (from the stable
    job's computed version) so the testing build adopts the just-cut stable base deterministically,
    without depending on the eventual consistency of the releases/latest API. Ignored for stable;
    when empty, the stable base is read from GitHub Releases as usual.

.PARAMETER Repo
    owner/repo slug. Defaults to GITHUB_REPOSITORY.

.PARAMETER FirstStableSeed
    Explicit first stable X.Y.Z used when no stable release exists yet. Defaults to 0.0.1.

.PARAMETER CsprojPath
    Path to the plugin csproj. The script validates that the file contains a numeric <Version>, but
    release seeding does not derive from it.

.OUTPUTS
    Writes the computed version to stdout. When GITHUB_OUTPUT is set, also appends
    "version=<value>" and "tag=v<value>" (stable only) for workflow consumption.

.NOTES
    Requires the GitHub CLI (gh) authenticated with repo read access.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("testing", "stable")]
    [string]$Channel,

    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",

    [int]$RunNumber = 0,

    [string]$StableBase = "",

    [string]$Repo = $env:GITHUB_REPOSITORY,

    [string]$FirstStableSeed = "0.0.1",

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
if ($FirstStableSeed -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
    throw "Provided -FirstStableSeed '$FirstStableSeed' is not a valid X.Y.Z."
}

# --- local-build version validation from the csproj --------------------------

$csproj = Get-Content $CsprojPath -Raw
$m = [regex]::Match($csproj, "<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)(?:\.[0-9]+)?\s*</Version>")
if (-not $m.Success) {
    throw "Could not read a X.Y.Z <Version> from $CsprojPath"
}

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

if ($Channel -eq "testing") {
    # An explicit -StableBase (passed by the release workflow after a stable release) takes
    # precedence over the API-read stable version so the testing build deterministically adopts the
    # just-cut stable base. Validate its format defensively even though the caller derives it from a
    # guarded stable computation. Fall back to the current stable release, then the explicit first
    # stable seed.
    if (-not [string]::IsNullOrWhiteSpace($StableBase)) {
        if ($StableBase -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
            throw "Provided -StableBase '$StableBase' is not a valid X.Y.Z."
        }
        $effectiveBase = $StableBase
    }
    elseif ($stableXyz) { $effectiveBase = $stableXyz }
    else { $effectiveBase = $FirstStableSeed }
    $version = "$effectiveBase.$RunNumber"
}
else {
    # stable
    if (-not $stableXyz) {
        # First stable release: ship the explicit first stable seed as-is (bump ignored).
        $version = $FirstStableSeed
    }
    else {
        $version = Step-Version -Xyz $stableXyz -Which $Bump
    }

    # SemVer-format validation.
    if ($version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
        throw "Computed stable version '$version' is not a valid X.Y.Z."
    }

    # Strict monotonic guard against the current stable version.
    if ($stableXyz) {
        if ([version]"$version.0" -le [version]"$stableXyz.0") {
            throw "Computed stable version '$version' does not increase over current stable '$stableXyz'."
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
    if ($Channel -eq "stable") {
        "tag=v$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    }
}

# gh calls above may leave a non-zero $LASTEXITCODE from a tolerated "not found" (e.g. no stable
# release yet, or a free tag in the collision guard). We only reach here on success - real errors
# throw - so clear it explicitly; GitHub Actions' pwsh wrapper exits the step with $LASTEXITCODE.
exit 0
