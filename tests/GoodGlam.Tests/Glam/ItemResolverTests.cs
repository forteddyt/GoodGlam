using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class ItemResolverTests
{
    [Theory]
    [InlineData(3610u, 3610u)]            // normal-quality id unchanged
    [InlineData(1_003_610u, 3610u)]       // HQ id maps to its base
    [InlineData(1_000_000u, 0u)]          // lower HQ bound
    [InlineData(1_999_999u, 999_999u)]    // upper HQ bound
    [InlineData(2_000_000u, 2_000_000u)]  // collectables sit above HQ, untouched
    [InlineData(0u, 0u)]
    public void NormalizeItemId_strips_hq_offset(uint input, uint expected)
        => ItemResolver.NormalizeItemId(input).Should().Be(expected);

    [Fact]
    public void Resolve_returns_null_when_the_item_sheet_is_unavailable()
    {
        // The Lumina success path needs real game data, but the guard for a missing sheet is pure
        // logic: a faked IDataManager whose GetExcelSheet<Item>() yields null must resolve to null.
        // An un-configured fake returns null for GetExcelSheet by default.
        var data = A.Fake<IDataManager>();
        TestServices.EnsureLog();
        TestServices.Install("DataManager", data);

        new ItemResolver().Resolve(3610).Should().BeNull();
    }
}

