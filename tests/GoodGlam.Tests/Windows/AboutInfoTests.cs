using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the pure logic behind the About tab (<see cref="AboutInfo"/>): formatting the plugin
/// version as <c>vMAJOR.MINOR.PATCH</c> (the assembly's 4-part <see cref="Version"/> drops its
/// build/revision component) and the "open the repo" effect. The tab's ImGui rendering can't run in
/// CI, so these guard the parts that decide what the user sees and where the link goes.
/// </summary>
public class AboutInfoTests
{
    [Theory]
    [InlineData(0, 1, 0, 0, "v0.1.0")]
    [InlineData(1, 2, 3, 4, "v1.2.3")]
    [InlineData(10, 0, 0, 0, "v10.0.0")]
    public void FormatVersion_renders_major_minor_patch(int major, int minor, int build, int revision, string expected)
    {
        AboutInfo.FormatVersion(new Version(major, minor, build, revision)).Should().Be(expected);
    }

    [Fact]
    public void FormatVersion_treats_missing_components_as_zero()
    {
        // A two-part Version reports -1 for the absent build; it must still render a clean patch of 0.
        AboutInfo.FormatVersion(new Version(2, 5)).Should().Be("v2.5.0");
    }

    [Fact]
    public void FormatVersion_of_a_null_version_is_unknown()
    {
        AboutInfo.FormatVersion(null).Should().Be("v(unknown)");
    }

    [Fact]
    public void OpenRepo_opens_exactly_the_given_url()
    {
        var opener = new FakeLinkOpener();

        AboutInfo.OpenRepo(opener, "https://github.com/forteddyt/goodglam");

        opener.Opened.Should().ContainSingle().Which.Should().Be("https://github.com/forteddyt/goodglam");
    }
}
