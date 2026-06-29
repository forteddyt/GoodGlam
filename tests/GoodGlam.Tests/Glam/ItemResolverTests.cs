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
}
