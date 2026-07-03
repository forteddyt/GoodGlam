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

# One reusable fake-gh shim; each test points it at a fresh fixture dir via $env:FAKE_GH_DIR.
$FakeGhBin = Join-Path ([System.IO.Path]::GetTempPath()) "gg-fakegh-bin"
function Initialize-FakeGh {
    New-Item -ItemType Directory -Force -Path $FakeGhBin | Out-Null
    $ghPath = Join-Path $FakeGhBin "gh"
    $shim = @'
#!/usr/bin/env bash
set -u
cmd="${1:-}"; sub="${2:-}"
case "$cmd" in
  api)
    if [[ -f "$FAKE_GH_DIR/latest_tag" ]]; then cat "$FAKE_GH_DIR/latest_tag"; exit 0; fi
    echo "release not found" >&2; exit 1 ;;
  release)
    case "$sub" in
      download)
        tag="${3:-}"; dir=""; prev=""
        for a in "$@"; do [[ "$prev" == "--dir" ]] && dir="$a"; prev="$a"; done
        if [[ -n "$dir" && -f "$FAKE_GH_DIR/manifests/$tag.json" ]]; then
          cp "$FAKE_GH_DIR/manifests/$tag.json" "$dir/GoodGlam.json"; exit 0
        fi
        echo "not found" >&2; exit 1 ;;
      view)
        tag="${3:-}"
        [[ -f "$FAKE_GH_DIR/tags/$tag" ]] && exit 0
        echo "not found" >&2; exit 1 ;;
      delete|create|upload) exit 0 ;;
      *) exit 1 ;;
    esac ;;
  *) exit 1 ;;
esac
'@
    # Write with LF endings and a POSIX-executable bit.
    [System.IO.File]::WriteAllText($ghPath, ($shim -replace "`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) { & chmod "+x" $ghPath }
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
# (so `gh release view` finds it). Omit -Latest for prereleases like the rolling "dev" tag.
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
        RepoUrl         = "https://github.com/forteddyt/goodglam"
        Tags            = @("glamour")
        ApplicableVersion = "any"
        DalamudApiLevel = $ApiLevel
        IconUrl         = ""
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
    param([string]$AssemblyVersion = "0.1.0.0", [int]$ApiLevel = 15)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "gg-local-$([guid]::NewGuid()).json"
    $manifest = [ordered]@{
        Name = "GoodGlam"; Author = "forteddyt"; Punchline = "P"; Description = "D"
        RepoUrl = "https://github.com/forteddyt/goodglam"; Tags = @("glamour", "loot")
        ApplicableVersion = "any"; DalamudApiLevel = $ApiLevel; IconUrl = ""
        InternalName = "GoodGlam"; AssemblyVersion = $AssemblyVersion
    }
    [System.IO.File]::WriteAllText($path, (ConvertTo-Json $manifest -Depth 5), [System.Text.UTF8Encoding]::new($false))
    return $path
}

# Write a fake csproj carrying <Version> (the base/seed version compute-version.ps1 reads).
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

        $stdout = & $PwshExe -NoProfile -File $ScriptPath @Arguments 2>&1
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
