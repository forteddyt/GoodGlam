using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the pure effects behind the Settings controls (<see cref="SettingsActions"/>): each applies
/// a value to the live <see cref="Configuration"/> — with clamping and coupled-bound ordering — and
/// persists via <see cref="Configuration.Save"/>. A <c>SaveSink</c> counts saves; the logo callback is
/// observed directly. No ImGui context is needed.
/// </summary>
public class SettingsActionsTests
{
    private int saves;
    private readonly Configuration config;

    public SettingsActionsTests()
    {
        this.config = new Configuration { Filters = new PopularityFilters(), SaveSink = _ => this.saves++ };
    }

    private SettingsActions New(Action<bool>? setLogoVisible = null)
        => new(this.config, setLogoVisible ?? (_ => { }));

    [Fact]
    public void SetShowLogo_invokes_the_callback_and_does_not_save_here()
    {
        bool? seen = null;
        var actions = this.New(v => seen = v);

        actions.SetShowLogo(true);

        seen.Should().BeTrue();
        // Persistence for the logo is owned by the callback (Plugin.SetLogoVisible), not this action.
        this.saves.Should().Be(0);
    }

    [Fact]
    public void SetEnabled_writes_and_saves()
    {
        this.New().SetEnabled(false);

        this.config.Enabled.Should().BeFalse();
        this.saves.Should().Be(1);
    }

    [Theory]
    [InlineData(250, 250)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void SetLovesThreshold_floors_at_zero(int input, int expected)
    {
        this.New().SetLovesThreshold(input);

        this.config.LovesThreshold.Should().Be(expected);
        this.saves.Should().Be(1);
    }

    [Theory]
    [InlineData(12, 12)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(100, 72)]
    public void SetCacheTtlHours_clamps_to_1_through_72(int input, int expected)
    {
        this.New().SetCacheTtlHours(input);

        this.config.CacheTtlHours.Should().Be(expected);
        this.saves.Should().Be(1);
    }

    [Fact]
    public void SelectFilter_writes_the_matching_property_and_saves()
    {
        var defaults = new PopularityFilters();

        foreach (var field in Enum.GetValues<FilterField>())
        {
            // Fresh config per field so the save count and "only this field changed" checks are clean.
            var saves = 0;
            var config = new Configuration { Filters = new PopularityFilters(), SaveSink = _ => saves++ };
            new SettingsActions(config, _ => { }).SelectFilter(field, "picked");

            foreach (var other in Enum.GetValues<FilterField>())
            {
                var expected = other == field ? "picked" : Value(defaults, other);
                Value(config.Filters, other).Should().Be(expected, $"field {other} after selecting {field}");
            }

            saves.Should().Be(1, $"selecting {field} should persist once");
        }
    }

    [Fact]
    public void GetFilter_reads_back_what_SelectFilter_wrote()
    {
        foreach (var field in Enum.GetValues<FilterField>())
        {
            var config = new Configuration { Filters = new PopularityFilters(), SaveSink = _ => { } };
            var actions = new SettingsActions(config, _ => { });

            actions.SelectFilter(field, "round-trip");

            actions.GetFilter(field).Should().Be("round-trip", $"GetFilter({field}) should mirror SelectFilter");
        }
    }

    [Fact]
    public void GetFilter_and_SelectFilter_reject_an_undefined_field()
    {
        var actions = this.New();
        const FilterField bogus = (FilterField)999;

        actions.Invoking(a => a.GetFilter(bogus)).Should().Throw<ArgumentOutOfRangeException>();
        actions.Invoking(a => a.SelectFilter(bogus, "x")).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Reads a combo field straight off the model (independent of GetFilter) to prove the
    /// action wrote the property the test expects, catching a mis-mapped case in the switch.</summary>
    private static string Value(PopularityFilters filters, FilterField field) => field switch
    {
        FilterField.Gender => filters.Gender,
        FilterField.Job => filters.Job,
        FilterField.DatePeriod => filters.DatePeriod,
        FilterField.Classification => filters.Classification,
        FilterField.Style => filters.Style,
        FilterField.Theme => filters.Theme,
        FilterField.Color => filters.Color,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    [Fact]
    public void ToggleRace_adds_then_removes_the_same_race()
    {
        var actions = this.New();

        actions.ToggleRace("miqote");
        this.config.Filters.Races.Should().ContainSingle().Which.Should().Be("miqote");

        actions.ToggleRace("miqote");
        this.config.Filters.Races.Should().BeEmpty();

        this.saves.Should().Be(2);
    }

    [Fact]
    public void SetExcludeMogstation_and_SetExcludeSeasonal_write_and_save()
    {
        var actions = this.New();

        actions.SetExcludeMogstation(true);
        actions.SetExcludeSeasonal(true);

        this.config.Filters.ExcludeMogstation.Should().BeTrue();
        this.config.Filters.ExcludeSeasonal.Should().BeTrue();
        this.saves.Should().Be(2);
    }

    [Fact]
    public void SetMinLevel_clamps_and_pushes_max_up_to_stay_ordered()
    {
        this.config.Filters.MaxLevel = 20;

        this.New().SetMinLevel(50);

        this.config.Filters.MinLevel.Should().Be(50);
        this.config.Filters.MaxLevel.Should().Be(50); // max was below the new min, so it follows up
        this.saves.Should().Be(1);
    }

    [Theory]
    [InlineData(0, EcFilterOptions.MinLevel)]
    [InlineData(999, EcFilterOptions.MaxLevel)]
    public void SetMinLevel_clamps_to_the_ec_bounds(int input, int expectedMin)
    {
        this.config.Filters.MaxLevel = EcFilterOptions.MaxLevel;

        this.New().SetMinLevel(input);

        this.config.Filters.MinLevel.Should().Be(expectedMin);
    }

    [Fact]
    public void SetMaxLevel_clamps_and_pulls_min_down_to_stay_ordered()
    {
        this.config.Filters.MinLevel = 60;

        this.New().SetMaxLevel(30);

        this.config.Filters.MaxLevel.Should().Be(30);
        this.config.Filters.MinLevel.Should().Be(30); // min was above the new max, so it follows down
        this.saves.Should().Be(1);
    }

    [Fact]
    public void RestoreDefaults_resets_settings_and_filters_and_saves()
    {
        this.config.Enabled = false;
        this.config.LovesThreshold = 999;
        this.config.CacheTtlHours = 70;
        this.config.Filters.Job = "tanks";

        this.New().RestoreDefaults();

        var defaults = new Configuration();
        this.config.Enabled.Should().Be(defaults.Enabled);
        this.config.LovesThreshold.Should().Be(defaults.LovesThreshold);
        this.config.CacheTtlHours.Should().Be(defaults.CacheTtlHours);
        this.config.Filters.Job.Should().Be(new PopularityFilters().Job);
        this.saves.Should().Be(1);
    }

    [Fact]
    public void ResetFilters_clears_filters_but_keeps_other_settings()
    {
        this.config.LovesThreshold = 250;
        this.config.Filters.Job = "tanks";
        this.config.Filters.Races.Add("miqote");

        this.New().ResetFilters();

        this.config.Filters.Job.Should().Be(new PopularityFilters().Job);
        this.config.Filters.Races.Should().BeEmpty();
        // Non-filter settings are untouched.
        this.config.LovesThreshold.Should().Be(250);
        this.saves.Should().Be(1);
    }
}
