#requires -Version 7.0
<#
    Tests for scripts/compute-version.ps1 - the auto-bump version computation with monotonic,
    SemVer-format, and tag-collision guards, plus the "exit 0 on success" behavior that keeps a
    tolerated gh 404 from failing the release step.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot "TestHelpers.ps1")
    Initialize-FakeGh | Out-Null
    $script:Script = $ComputeVersionScript
}

Describe "compute-version.ps1" {

    Context "dev channel" {
        It "on a fresh repo (no stable release) seeds from the csproj base and appends the run number" {
            $fx = New-GhFixture   # no latest_tag => releases/latest 404
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "7", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.ExitCode | Should -Be 0
            $r.Outputs["version"] | Should -Be "0.1.0.7"
            $r.StdOut | Should -Match "0\.1\.0\.7"
        }

        It "bases the dev version on the current stable X.Y.Z so it stays just ahead of stable" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.2.0" -AssemblyVersion "0.2.0.0" -Latest
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "42", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.ExitCode | Should -Be 0
            $r.Outputs["version"] | Should -Be "0.2.0.42"
        }

        It "does not emit a prod-only tag output" {
            $fx = New-GhFixture
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "1", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.Outputs.ContainsKey("tag") | Should -BeFalse
        }

        It "immediately after a fresh stable X.Y.Z yields X.Y.Z.<run> that stays >= the new stable (issue #67)" {
            # Regression guard for #67: the release workflow now runs the dev publish AFTER a prod
            # release creates the --latest stable. Computing the dev version at that point must read
            # the freshly-cut stable base so the testing channel never trails stable.
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.3.0" -AssemblyVersion "0.3.0.0" -Latest
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "128", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.ExitCode | Should -Be 0
            $r.Outputs["version"] | Should -Be "0.3.0.128"
            # Testing (X.Y.Z.<run>) must be >= the new stable (X.Y.Z) so the "dev >= stable" invariant holds.
            ([version]$r.Outputs["version"]) | Should -BeGreaterThan ([version]"0.3.0")
        }

        It "strictly increases across builds as run number advances against the same stable base (monotonic)" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.3.0" -AssemblyVersion "0.3.0.0" -Latest
            $csproj = New-Csproj -Version "0.1.0.0"

            $earlier = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "128", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $later = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "129", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            ([version]$later.Outputs["version"]) | Should -BeGreaterThan ([version]$earlier.Outputs["version"])
        }
    }

    Context "prod channel - first release" {
        It "ships the csproj base version as-is when no stable release exists yet (bump ignored)" {
            $fx = New-GhFixture
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "major", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.ExitCode | Should -Be 0
            $r.Outputs["version"] | Should -Be "0.1.0"
            $r.Outputs["tag"] | Should -Be "v0.1.0"
        }
    }

    Context "prod channel - bumping from an existing stable release" {
        BeforeEach {
            $script:fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            $script:csproj = New-Csproj -Version "0.1.0.0"
        }

        It "increments the patch component" {
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "patch", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $r.ExitCode | Should -Be 0
            $r.Outputs["version"] | Should -Be "0.1.1"
            $r.Outputs["tag"] | Should -Be "v0.1.1"
        }

        It "increments the minor component and resets patch" {
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "minor", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $r.Outputs["version"] | Should -Be "0.2.0"
        }

        It "increments the major component and resets minor and patch" {
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "major", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $r.Outputs["version"] | Should -Be "1.0.0"
        }
    }

    Context "prod channel - guardrails" {
        It "fails when the computed tag already exists (collision guard), rather than clobbering it" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            # v0.1.1 also already exists as a tag -> patch bump would collide.
            New-Item -ItemType File -Force -Path (Join-Path $fx "tags" "v0.1.1") | Out-Null
            $csproj = New-Csproj -Version "0.1.0.0"

            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "patch", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)

            $r.ExitCode | Should -Not -Be 0
            # Single word - the error message may be line-wrapped in the rendered output.
            $r.StdOut | Should -Match "already"
        }
    }

    Context "exit code hygiene" {
        It "exits 0 even though the underlying gh 'releases/latest' returned non-zero (404)" {
            # This is the regression guard for the failed first dev run: a tolerated non-zero gh exit
            # must not leave a stale LASTEXITCODE that fails the step.
            $fx = New-GhFixture   # no latest_tag => gh api returns 404 / exit 1
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "dev", "-RunNumber", "3", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $r.ExitCode | Should -Be 0
        }

        It "exits 0 on a collision-free prod release even though the collision-check gh call returned non-zero" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            $csproj = New-Csproj -Version "0.1.0.0"
            $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $fx `
                -Arguments @("-Channel", "prod", "-Bump", "patch", "-Repo", "forteddyt/GoodGlam", "-CsprojPath", $csproj)
            $r.ExitCode | Should -Be 0
        }
    }
}
