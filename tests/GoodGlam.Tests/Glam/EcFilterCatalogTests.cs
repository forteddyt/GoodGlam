using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.Localization;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class EcFilterCatalogTests
{
    private static readonly EcFilterCatalog Catalog = EcFilterCatalog.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_populates_every_list()
    {
        Catalog.Genders.Should().NotBeEmpty();
        Catalog.Races.Should().NotBeEmpty();
        Catalog.DatePeriods.Should().NotBeEmpty();
        Catalog.Classifications.Should().NotBeEmpty();
        Catalog.Styles.Should().NotBeEmpty();
        Catalog.Themes.Should().NotBeEmpty();
        Catalog.Colors.Should().NotBeEmpty();
        Catalog.Jobs.Should().NotBeEmpty();
    }

    [Fact]
    public void Counts_match_the_eorzea_collection_tables()
    {
        Catalog.Genders.Should().HaveCount(3);
        Catalog.Races.Should().HaveCount(9);
        Catalog.DatePeriods.Should().HaveCount(9);
        Catalog.Classifications.Should().HaveCount(14);
        Catalog.Styles.Should().HaveCount(30);
        Catalog.Themes.Should().HaveCount(28);
        Catalog.Colors.Should().HaveCount(21);
        Catalog.Jobs.Should().HaveCount(45);
    }

    [Fact]
    public void Inert_defaults_lead_each_list_and_match_the_constants()
    {
        Catalog.Genders[0].Should().Be(new EcFilterOption(EcFilterOptions.AnyGender, "All genders"));
        Catalog.DatePeriods[0].Should().Be(new EcFilterOption(EcFilterOptions.AnyDate, "All-time"));
        Catalog.Jobs[0].Should().Be(new EcFilterOption(EcFilterOptions.AnyJob, "All Classes"));

        Catalog.Classifications[0].Should().Be(new EcFilterOption(string.Empty, "Any Classification"));
        Catalog.Styles[0].Should().Be(new EcFilterOption(string.Empty, "Any Style"));
        Catalog.Themes[0].Should().Be(new EcFilterOption(string.Empty, "Any Theme"));
        Catalog.Colors[0].Should().Be(new EcFilterOption(string.Empty, "Any Color"));
    }

    [Fact]
    public void Known_value_label_pairs_are_preserved()
    {
        Catalog.Races.Should().Contain(new EcFilterOption("miqote", "Miqo'te"));
        Catalog.Styles.Should().Contain(new EcFilterOption("84", "Fantasy"));
        Catalog.Colors.Should().Contain(new EcFilterOption("41", "Black"));
        Catalog.Jobs.Should().Contain(new EcFilterOption("pct", "Pictomancer"));
    }

    [Fact]
    public void LoadEmbedded_throws_when_labels_are_not_aligned_with_values()
    {
        // The fixed EC API values and their display labels live in two files; a count mismatch must fail
        // loudly rather than silently mispair a label with the wrong value.
        var misaligned = new StringCatalog
        {
            FilterOptions = new FilterOptionsStrings { Genders = ["only one label"] },
        };

        var act = () => EcFilterCatalog.LoadEmbedded(misaligned);

        act.Should().Throw<InvalidOperationException>().WithMessage("*genders*");
    }
}
