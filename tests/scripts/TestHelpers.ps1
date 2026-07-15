#requires -Version 7.0
<#
    Shared helpers for the Pester tests that cover scripts/build-repo-json.ps1 and
    scripts/compute-version.ps1.

    The scripts shell out to the GitHub CLI (gh) and end with `exit 0`, so they are exercised in a
    child pwsh process with a fake `gh` on PATH (rather than in-process, where `exit` would tear down
    the Pester runner). This also validates the real exit-code behavior end to end.

    The fake gh is data-driven by a per-test fixture directory ($FAKE_GH_DIR):
      <dir>/latest_tag          - content is the "latest" stable tag; absent => releases/latest 404
      <dir>/manifests/<tag>.json - manifest returned by `gh release download <tag>`
      <dir>/tags/<tag>           - marker file; `gh release view <tag>` succeeds iff present
#>

$RepoRoot        = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).Path
$ComputeVersionScript = Join-Path $RepoRoot "scripts" "compute-version.ps1"
$BuildRepoJsonScript  = Join-Path $RepoRoot "scripts" "build-repo-json.ps1"
$PwshExe         = Join-Path $PSHOME ($IsWindows ? "pwsh.exe" : "pwsh")

# One reusable fake gh + a GitHub-Actions-style wrapper, generated once into a temp bin dir. Each
# test points the fake at a fresh fixture dir via $env:FAKE_GH_DIR.
$FakeGhBin     = Join-Path ([System.IO.Path]::GetTempPath()) "gg-fakegh-bin"
$WrapperScript = Join-Path $FakeGhBin "invoke-like-actions.ps1"

function Initialize-FakeGh {
    New-Item -ItemType Directory -Force -Path $FakeGhBin | Out-Null

    # Cross-platform fake gh. PowerShell resolves `gh` to gh.ps1 on both Windows and Linux, and a
    # `& gh` call to a .ps1 sets $LASTEXITCODE from its `exit` without throwing - matching how the
    # scripts consume the real (native) gh once they disable native-command error termination. This
    # keeps the tests runnable on the same OS the scripts ship on (windows-latest in release.yml).
    $ghPs1 = @'
$ErrorActionPreference = "Stop"
$cmd = $args[0]; $sub = $args[1]
switch ($cmd) {
  "api" {
    $lt = Join-Path $env:FAKE_GH_DIR "latest_tag"
    if (Test-Path $lt) { Get-Content $lt -Raw; exit 0 } else { exit 1 }
  }
  "release" {
    switch ($sub) {
      "download" {
        $tag = $args[2]; $dir = $null
        for ($i = 0; $i -lt $args.Count; $i++) { if ($args[$i] -eq "--dir") { $dir = $args[$i + 1] } }
        $m = Join-Path $env:FAKE_GH_DIR "manifests/$tag.json"
        if ($dir -and (Test-Path $m)) { Copy-Item $m (Join-Path $dir "GoodGlam.json"); exit 0 } else { exit 1 }
      }
      "view"   { if (Test-Path (Join-Path $env:FAKE_GH_DIR "tags/$($args[2])")) { exit 0 } else { exit 1 } }
      "delete" { exit 0 }
      "create" { exit 0 }
      "upload" { exit 0 }
      default  { exit 1 }
    }
  }
  default { exit 1 }
}
'@
    [System.IO.File]::WriteAllText((Join-Path $FakeGhBin "gh.ps1"), ($ghPs1 -replace "`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))

    # Reproduce GitHub Actions' pwsh step exactly: it runs `pwsh -command ". '{0}'"` and exits with
    # $LASTEXITCODE. A plain `pwsh -File script.ps1` does NOT propagate a stale $LASTEXITCODE left by
    # a tolerated native failure, so it would mask the very bug these tests guard. We instead invoke
    # via this wrapper (dot-source the target, then exit $LASTEXITCODE) so the behavior matches a
    # runner. Using $args (not param()) forwards `-Name value` arguments cleanly.
    $wrapper = @'
$target = $args[0]
$rest = @($args[1..($args.Count - 1)])
. $target @rest
if (Test-Path variable:LASTEXITCODE) { exit $LASTEXITCODE }
'@
    [System.IO.File]::WriteAllText($WrapperScript, ($wrapper -replace "`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))

    return $FakeGhBin
}

# Create a fresh, empty fixture dir for one test.
function New-GhFixture {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "gg-fixture-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Force -Path (Join-Path $dir "manifests") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $dir "tags") | Out-Null
    return $dir
}

# Register a release: writes its manifest (so `gh release download` returns it) and a tag marker
# (so `gh release view` finds it). Omit -Latest for prereleases like the rolling "testing" tag.
function Add-FakeRelease {
    param(
        [Parameter(Mandatory)] [string]$FixtureDir,
        [Parameter(Mandatory)] [string]$Tag,
        [Parameter(Mandatory)] [string]$AssemblyVersion,
        [int]$ApiLevel = 15,
        [switch]$Latest
    )
    $manifest = [ordered]@{
        Name            = "GoodGlam"
        Author          = "forteddyt"
        Punchline       = "P"
        Description     = "D"
        RepoUrl         = "https://github.com/forteddyt/GoodGlam"
        Tags            = @("glamour")
        CategoryTags    = @("inventory", "utility")
        ApplicableVersion = "any"
        DalamudApiLevel = $ApiLevel
        IconUrl         = "https://raw.githubusercontent.com/forteddyt/GoodGlam/main/src/GoodGlam/Assets/Logo.png"
        AcceptsFeedback = $true
        FeedbackMessage = "Use the About tab to report bugs or suggest features."
        InternalName    = "GoodGlam"
        AssemblyVersion = $AssemblyVersion
    }
    $json = ConvertTo-Json $manifest -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $FixtureDir "manifests" "$Tag.json"), $json, [System.Text.UTF8Encoding]::new($false))
    New-Item -ItemType File -Force -Path (Join-Path $FixtureDir "tags" $Tag) | Out-Null
    if ($Latest) {
        [System.IO.File]::WriteAllText((Join-Path $FixtureDir "latest_tag"), $Tag, [System.Text.UTF8Encoding]::new($false))
    }
}

# Write a fake built manifest (the -ManifestPath input for build-repo-json.ps1).
function New-LocalManifest {
    param(
        [string]$AssemblyVersion = "0.1.0.0",
        [int]$ApiLevel = 15,
        [string[]]$ImageUrls,
        [switch]$OmitOptionalInstallerMetadata
    )
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "gg-local-$([guid]::NewGuid()).json"
    $manifest = [ordered]@{
        Name = "GoodGlam"; Author = "forteddyt"; Punchline = "P"; Description = "D"
        RepoUrl = "https://github.com/forteddyt/GoodGlam"; Tags = @("glamour", "loot")
        ApplicableVersion = "any"; DalamudApiLevel = $ApiLevel
        IconUrl = "https://raw.githubusercontent.com/forteddyt/GoodGlam/main/src/GoodGlam/Assets/Logo.png"
        InternalName = "GoodGlam"; AssemblyVersion = $AssemblyVersion
    }
    if (-not $OmitOptionalInstallerMetadata) {
        $manifest["CategoryTags"] = @("inventory", "utility")
        $manifest["AcceptsFeedback"] = $true
        $manifest["FeedbackMessage"] = "Use the About tab to report bugs or suggest features."
    }
    if ($null -ne $ImageUrls) {
        $manifest["ImageUrls"] = @($ImageUrls)
    }
    [System.IO.File]::WriteAllText($path, (ConvertTo-Json $manifest -Depth 5), [System.Text.UTF8Encoding]::new($false))
    return $path
}

# Write a fake csproj carrying <Version> (validated by compute-version.ps1).
function New-Csproj {
    param([string]$Version = "0.1.0.0")
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "gg-proj-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir "GoodGlam.csproj"
    "<Project Sdk=`"Dalamud.NET.Sdk/15.0.0`"><PropertyGroup><Version>$Version</Version></PropertyGroup></Project>" |
        Set-Content -Path $path -Encoding utf8
    return $path
}

# Run one of the scripts in a child pwsh with the fake gh on PATH. Returns the exit code, stdout,
# and any GITHUB_OUTPUT key/value pairs the script wrote.
function Invoke-ScriptUnderTest {
    param(
        [Parameter(Mandatory)] [string]$ScriptPath,
        [Parameter(Mandatory)] [string]$FixtureDir,
        [Parameter(Mandatory)] [string[]]$Arguments
    )
    $ghOut = Join-Path ([System.IO.Path]::GetTempPath()) "gg-ghout-$([guid]::NewGuid()).txt"
    New-Item -ItemType File -Force -Path $ghOut | Out-Null

    $prevPath = $env:PATH
    $prevFake = $env:FAKE_GH_DIR
    $prevGhOut = $env:GITHUB_OUTPUT
    try {
        $env:PATH = "$FakeGhBin$([System.IO.Path]::PathSeparator)$prevPath"
        $env:FAKE_GH_DIR = $FixtureDir
        $env:GITHUB_OUTPUT = $ghOut

        # Invoke via the GitHub-Actions-style wrapper (see Initialize-FakeGh) so a stale $LASTEXITCODE
        # from a tolerated gh failure surfaces exactly as it would on a runner - not swallowed as a
        # plain `pwsh -File` would.
        $stdout = & $PwshExe -NoProfile -File $WrapperScript $ScriptPath @Arguments 2>&1
        $code = $LASTEXITCODE

        $outputs = @{}
        foreach ($line in (Get-Content $ghOut -ErrorAction SilentlyContinue)) {
            if ($line -match "^([^=]+)=(.*)$") { $outputs[$Matches[1]] = $Matches[2] }
        }
        return [pscustomobject]@{
            ExitCode = $code
            StdOut   = ($stdout -join "`n")
            Outputs  = $outputs
        }
    }
    finally {
        $env:PATH = $prevPath
        $env:FAKE_GH_DIR = $prevFake
        $env:GITHUB_OUTPUT = $prevGhOut
        Remove-Item $ghOut -ErrorAction SilentlyContinue
    }
}
