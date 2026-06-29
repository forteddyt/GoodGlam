using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class PopularityFiltersTests
{
    [Fact]
    public void Defaults_are_inert_so_unfiltered()
    {
        var filters = new PopularityFilters();

        filters.ActiveParams().Should().BeEmpty();
        filters.Signature().Should().BeEmpty();
    }

    [Fact]
    public void ActiveParams_omits_any_and_default_selections()
    {
        var filters = new PopularityFilters
        {
            Gender = EcFilterOptions.AnyGender,
            Job = EcFilterOptions.AnyJob,
            DatePeriod = EcFilterOptions.AnyDate,
            Classification = string.Empty,
            MinLevel = EcFilterOptions.MinLevel,
            MaxLevel = EcFilterOptions.MaxLevel,
        };

        filters.ActiveParams().Should().BeEmpty();
    }

    [Fact]
    public void ActiveParams_emits_only_set_filters()
    {
        var filters = new PopularityFilters
        {
            Gender = "female",
            Job = "tanks",
            DatePeriod = "this-week",
            Classification = "3",
            Style = "84",
            Theme = "20",
            Color = "41",
            ExcludeMogstation = true,
            ExcludeSeasonal = true,
        };

        filters.ActiveParams().Should().BeEquivalentTo(new[]
        {
            ("gender", "female"),
            ("class", "3"),
            ("style", "84"),
            ("theme", "20"),
            ("color", "41"),
            ("job", "tanks"),
            ("datePeriod", "this-week"),
            ("excludeMogstation", "1"),
            ("excludeSeasonal", "1"),
        });
    }

    [Fact]
    public void ActiveParams_emits_each_race_as_array_param()
    {
        var filters = new PopularityFilters { Races = ["miqote", "aura"] };

        filters.ActiveParams().Should().Contain(("race[]", "miqote"))
            .And.Contain(("race[]", "aura"));
    }

    [Fact]
    public void ActiveParams_emits_only_non_default_level_bounds()
    {
        new PopularityFilters { MinLevel = 50, MaxLevel = 100 }.ActiveParams()
            .Should().ContainSingle().Which.Should().Be(("minimumLvl", "50"));

        new PopularityFilters { MinLevel = 1, MaxLevel = 90 }.ActiveParams()
            .Should().ContainSingle().Which.Should().Be(("maximumLvl", "90"));
    }

    [Fact]
    public void Signature_is_stable_and_changes_with_selection()
    {
        var a = new PopularityFilters { Gender = "female" };
        var b = new PopularityFilters { Gender = "female" };
        var c = new PopularityFilters { Gender = "male" };

        a.Signature().Should().Be(b.Signature());
        a.Signature().Should().NotBe(c.Signature());
    }
}
