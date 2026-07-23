using FluentAssertions;
using GoodGlam.Localization;
using Xunit;

namespace GoodGlam.Tests.Localization;

/// <summary>
/// Covers the <see cref="Loc"/> accessor: it lazily provides the default catalog without any setup,
/// <see cref="Loc.Initialize"/> makes a language active, and the test override seam swaps and resets the
/// active catalog. Resets the global state after each test so the (parallelization-disabled) suite sees
/// the normal lazy default afterwards.
/// </summary>
public sealed class LocTests : IDisposable
{
    public void Dispose() => Loc.OverrideForTest(null);

    [Fact]
    public void Strings_lazily_provides_the_default_catalog()
    {
        Loc.OverrideForTest(null);

        Loc.Strings.Should().NotBeNull();
        Loc.Strings.Common.AppName.Should().Be("GoodGlam");
    }

    [Fact]
    public void Initialize_makes_the_requested_language_active()
    {
        Loc.Initialize("en");

        Loc.Strings.Tabs.History.Should().Be("History");
    }

    [Fact]
    public void Initialize_throws_for_a_language_that_does_not_ship()
    {
        var act = () => Loc.Initialize("zz");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OverrideForTest_swaps_the_active_catalog_then_resets_to_the_lazy_default()
    {
        var crafted = new StringCatalog { Common = new CommonStrings { AppName = "Override" } };

        Loc.OverrideForTest(crafted);
        Loc.Strings.Common.AppName.Should().Be("Override");

        Loc.OverrideForTest(null);
        Loc.Strings.Common.AppName.Should().Be("GoodGlam");
    }
}
