using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the pure logic behind the About tab (<see cref="AboutInfo"/>): formatting the plugin
/// version as <c>v</c> followed by the full version (every component the assembly version defines,
/// e.g. <c>v0.1.0.0</c>) plus the local-build label for the sentinel version, and the "open the repo"
/// effect. The tab's ImGui rendering can't run in CI, so these guard the parts that decide what the
/// user sees and where the link goes.
/// </summary>
public class AboutInfoTests
{
    [Theory]
    [InlineData(0, 1, 0, 0, "v0.1.0.0")]
    [InlineData(1, 2, 3, 4, "v1.2.3.4")]
    [InlineData(10, 0, 0, 0, "v10.0.0.0")]
    public void FormatVersion_keeps_every_component(int major, int minor, int build, int revision, string expected)
    {
        AboutInfo.FormatVersion(new Version(major, minor, build, revision)).Should().Be(expected);
    }

    [Fact]
    public void FormatVersion_renders_only_the_components_that_are_defined()
    {
        // A version constructed with just major/minor has no build/revision to show — nothing is
        // padded and nothing is dropped.
        AboutInfo.FormatVersion(new Version(2, 5)).Should().Be("v2.5");
    }

    [Fact]
    public void FormatVersion_of_a_null_version_is_unknown()
    {
        AboutInfo.FormatVersion(null).Should().Be("v(unknown)");
    }

    [Fact]
    public void FormatVersion_of_the_local_sentinel_marks_it_as_a_local_build()
    {
        AboutInfo.FormatVersion(new Version(0, 0, 0, 0)).Should().Be("v0.0.0.0 (local build)");
    }

    [Fact]
    public void OpenRepo_opens_exactly_the_given_url()
    {
        var opener = new FakeLinkOpener();

        AboutInfo.OpenRepo(opener, "https://github.com/forteddyt/goodglam");

        opener.Opened.Should().ContainSingle().Which.Should().Be("https://github.com/forteddyt/goodglam");
    }

    [Fact]
    public void How_it_works_gives_a_concise_ordered_in_plugin_tutorial()
    {
        AboutInfo.HowItWorksSteps.Should().Equal(
            "GoodGlam watches items in the Need/Greed/Pass roll window.",
            "It checks Eorzea Collection for popular glam outfits that use each item, using your threshold and filters.",
            "Qualifying drops light the floating logo and are saved in History, where you can preview and open matching glams.");
    }
}
