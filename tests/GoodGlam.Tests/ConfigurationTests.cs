using FluentAssertions;
using GoodGlam.Glam;
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

    [Fact]
    public void CopyFrom_adopts_every_persisted_field()
    {
        var live = new Configuration();
        var other = new Configuration
        {
            Version = 99,
            Enabled = false,
            ShowLogo = false,
            LockLogo = true,
            LovesThreshold = 321,
            CacheTtlHours = 7,
            Filters = new PopularityFilters { Job = "healer", ExcludeMogstation = true },
        };

        live.CopyFrom(other);

        live.Version.Should().Be(99);
        live.Enabled.Should().BeFalse();
        live.ShowLogo.Should().BeFalse();
        live.LockLogo.Should().BeTrue();
        live.LovesThreshold.Should().Be(321);
        live.CacheTtlHours.Should().Be(7);
        live.Filters.Job.Should().Be("healer");
        live.Filters.ExcludeMogstation.Should().BeTrue();
    }

    [Fact]
    public void CopyFrom_substitutes_default_filters_when_source_is_null()
    {
        var live = new Configuration();
        var other = new Configuration { Filters = null! };

        live.CopyFrom(other);

        live.Filters.Should().NotBeNull();
    }

    [Fact]
    public void Save_routes_to_the_sink()
    {
        var config = new Configuration();
        Configuration? saved = null;
        config.SaveSink = c => saved = c;

        config.Save();

        saved.Should().BeSameAs(config);
    }

    [Fact]
    public void Save_is_a_no_op_when_no_sink_is_bound()
    {
        var config = new Configuration { SaveSink = null };

        var act = config.Save;

        act.Should().NotThrow();
    }
}

