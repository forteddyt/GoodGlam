#requires -Version 7.0
<#
    Tests for scripts/build-repo-json.ps1 - the two-channel Dalamud plugin-master (repo.json)
    generator: static fields from the built manifest, per-channel versions from each channel's
    uploaded manifest asset, bootstrap handling, the testing-link fallback, and JSON-array output.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot "TestHelpers.ps1")
    Initialize-FakeGh | Out-Null
    $script:Script = $BuildRepoJsonScript

    function Invoke-Generator {
        param([string]$FixtureDir, [string]$ManifestPath)
        $out = Join-Path ([System.IO.Path]::GetTempPath()) "gg-repo-$([guid]::NewGuid()).json"
        $r = Invoke-ScriptUnderTest -ScriptPath $Script -FixtureDir $FixtureDir `
            -Arguments @("-ManifestPath", $ManifestPath, "-OutputPath", $out, "-Repo", "forteddyt/GoodGlam")
        $entry = $null
        if (Test-Path $out) {
            $parsed = Get-Content $out -Raw | ConvertFrom-Json
            $r | Add-Member -NotePropertyName Raw -NotePropertyValue (Get-Content $out -Raw)
            $r | Add-Member -NotePropertyName Parsed -NotePropertyValue $parsed
        }
        Remove-Item $out -ErrorAction SilentlyContinue
        return $r
    }
}

Describe "build-repo-json.ps1" {

    Context "output shape" {
        It "emits a JSON array (not a bare object) even with a single plugin entry" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.5"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.5"
            $r = Invoke-Generator -FixtureDir $fx -ManifestPath $local

            $r.ExitCode | Should -Be 0
            # ConvertFrom-Json unwraps a single-element array to a scalar, so assert on the raw text:
            # a bare-object regression would start with '{'. @(...) then normalizes for the count.
            $r.Raw.TrimStart()[0] | Should -Be "["
            $r.Raw.TrimEnd()[-1]  | Should -Be "]"
            @($r.Parsed).Count | Should -Be 1
        }

        It "includes every field a Dalamud plugin master requires, with https download links" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.9"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.0"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.InternalName | Should -Be "GoodGlam"
            foreach ($k in "Name","Author","Description","DalamudApiLevel","AssemblyVersion",
                           "TestingAssemblyVersion","DownloadLinkInstall","DownloadLinkUpdate","DownloadLinkTesting") {
                $e.PSObject.Properties.Name | Should -Contain $k
            }
            $e.DownloadLinkInstall  | Should -Match "^https://"
            $e.DownloadLinkUpdate   | Should -Match "^https://"
            $e.DownloadLinkTesting  | Should -Match "^https://"
        }

        It "preserves installer presentation and feedback metadata from the built manifest" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.5"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.5"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.RepoUrl | Should -Be "https://github.com/forteddyt/GoodGlam"
            @($e.CategoryTags) | Should -Be @("inventory", "utility")
            $e.IconUrl | Should -Be "https://raw.githubusercontent.com/forteddyt/GoodGlam/main/src/GoodGlam/Assets/icon.png"
            $e.AcceptsFeedback | Should -BeTrue
            $e.FeedbackMessage | Should -Be "Use the About tab to report bugs or suggest features."
        }

        It "uses safe defaults when optional installer metadata is absent" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.5"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.5" -OmitOptionalInstallerMetadata
            $r = Invoke-Generator -FixtureDir $fx -ManifestPath $local

            $r.ExitCode | Should -Be 0
            if ($r.ExitCode -eq 0) {
                $e = $r.Parsed[0]
                $e.AcceptsFeedback | Should -BeTrue
                $e.PSObject.Properties.Name | Should -Not -Contain "CategoryTags"
                $e.PSObject.Properties.Name | Should -Not -Contain "FeedbackMessage"
            }
        }

        It "propagates preview image URLs when the built manifest defines them" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.5"
            $imageUrls = @(
                "https://example.com/image1.png",
                "https://example.com/image2.png"
            )
            $local = New-LocalManifest -AssemblyVersion "0.1.0.5" -ImageUrls $imageUrls
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            @($e.ImageUrls) | Should -Be $imageUrls
        }

        It "omits preview image URLs when the built manifest does not define them" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.5"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.5"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.PSObject.Properties.Name | Should -Not -Contain "ImageUrls"
        }
    }

    Context "full repo (stable + testing)" {
        It "advertises each channel's version from its own uploaded manifest and points links at the right zips" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.47"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.0"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.AssemblyVersion        | Should -Be "0.1.0.0"
            $e.TestingAssemblyVersion | Should -Be "0.1.0.47"
            $e.IsTestingExclusive     | Should -BeFalse
            $e.DownloadLinkInstall    | Should -Be "https://github.com/forteddyt/GoodGlam/releases/latest/download/latest.zip"
            $e.DownloadLinkTesting    | Should -Be "https://github.com/forteddyt/GoodGlam/releases/download/testing/latest.zip"
        }
    }

    Context "bootstrap (testing only, no stable release yet)" {
        It "marks the entry testing-exclusive and falls the stable links back to the testing zip" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "testing" -AssemblyVersion "0.1.0.3"
            $local = New-LocalManifest -AssemblyVersion "0.1.0.3"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.IsTestingExclusive  | Should -BeTrue
            $e.AssemblyVersion     | Should -Be "0.1.0.3"
            $e.DownloadLinkInstall | Should -Be "https://github.com/forteddyt/GoodGlam/releases/download/testing/latest.zip"
            $e.DownloadLinkTesting | Should -Be "https://github.com/forteddyt/GoodGlam/releases/download/testing/latest.zip"
        }
    }

    Context "stable only (no testing release yet)" {
        It "falls the testing link back to the stable zip so it never advertises a 404" {
            $fx = New-GhFixture
            Add-FakeRelease -FixtureDir $fx -Tag "v0.1.0" -AssemblyVersion "0.1.0.0" -Latest
            $local = New-LocalManifest -AssemblyVersion "0.1.0.0"
            $e = (Invoke-Generator -FixtureDir $fx -ManifestPath $local).Parsed[0]

            $e.IsTestingExclusive     | Should -BeFalse
            $e.TestingAssemblyVersion | Should -Be "0.1.0.0"
            $e.DownloadLinkTesting    | Should -Be "https://github.com/forteddyt/GoodGlam/releases/latest/download/latest.zip"
        }
    }

    Context "failure handling" {
        It "fails when neither a stable nor a testing release exists" {
            $fx = New-GhFixture   # nothing registered
            $local = New-LocalManifest -AssemblyVersion "0.1.0.0"
            $r = Invoke-Generator -FixtureDir $fx -ManifestPath $local

            $r.ExitCode | Should -Not -Be 0
            # Match a single word - PowerShell's error rendering line-wraps the message.
            $r.StdOut | Should -Match "Publish"
        }
    }
}
