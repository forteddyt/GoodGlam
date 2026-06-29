using FluentAssertions;
using Xunit;

namespace GoodGlam.Tests;

/// <summary>Guards the default values of the logo-related configuration flags.</summary>
public class ConfigurationTests
{
    [Fact]
    public void Logo_flags_default_to_shown_and_unlocked()
    {
        var config = new Configuration();

        config.ShowLogo.Should().BeTrue();
        config.LockLogo.Should().BeFalse();
    }
}
