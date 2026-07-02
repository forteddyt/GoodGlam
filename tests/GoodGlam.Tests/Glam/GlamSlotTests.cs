using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class GlamSlotTests
{
    [Theory]
    [InlineData("head", "headPiece")]
    [InlineData("body", "bodyPiece")]
    [InlineData("hands", "handsPiece")]
    [InlineData("weapon", "weaponPiece")]
    public void FilterParam_appends_piece_suffix(string key, string expected)
        => new GlamSlot(key).FilterParam.Should().Be(expected);

    public static IEnumerable<object[]> SlotMappings()
    {
        // (slot value array index, expected slot) — exactly one flag set positive.
        yield return new object[] { 0, GlamSlot.Head };
        yield return new object[] { 1, GlamSlot.Body };
        yield return new object[] { 2, GlamSlot.Hands };
        yield return new object[] { 3, GlamSlot.Legs };
        yield return new object[] { 4, GlamSlot.Feet };
        yield return new object[] { 5, GlamSlot.Weapon };
        yield return new object[] { 6, GlamSlot.Offhand };
        yield return new object[] { 7, GlamSlot.Earrings };
        yield return new object[] { 8, GlamSlot.Necklace };
        yield return new object[] { 9, GlamSlot.Bracelets };
        yield return new object[] { 10, GlamSlot.Ring }; // FingerL
        yield return new object[] { 11, GlamSlot.Ring }; // FingerR
    }

    [Theory]
    [MemberData(nameof(SlotMappings))]
    public void FromSlotFlags_maps_each_slot(int index, GlamSlot expected)
    {
        var f = new sbyte[12];
        f[index] = 1;
        GlamSlot.FromSlotFlags(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11])
            .Should().Be(expected);
    }

    [Fact]
    public void FromSlotFlags_returns_null_for_non_gear()
        => GlamSlot.FromSlotFlags(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).Should().BeNull();

    [Fact]
    public void FromSlotFlags_prioritises_head_when_multiple_set()
        => GlamSlot.FromSlotFlags(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).Should().Be(GlamSlot.Head);

    [Fact]
    public void FromSlotFlags_treats_either_finger_as_ring()
        => GlamSlot.FromSlotFlags(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1).Should().Be(GlamSlot.Ring);

    [Fact]
    public void All_lists_the_eleven_slots_in_grid_reading_order()
    {
        // Reading order of the equipment grid: each row's left cell then its right cell.
        GlamSlot.All.Should().Equal(
            GlamSlot.Weapon, GlamSlot.Head, GlamSlot.Offhand, GlamSlot.Body, GlamSlot.Earrings,
            GlamSlot.Hands, GlamSlot.Necklace, GlamSlot.Legs, GlamSlot.Bracelets, GlamSlot.Feet,
            GlamSlot.Ring);
    }

    [Fact]
    public void Grid_mirrors_the_in_game_equipment_layout()
    {
        GlamSlot.Grid.Should().Equal(
            new GlamSlot.GridRow(GlamSlot.Weapon, null),
            new GlamSlot.GridRow(GlamSlot.Head, GlamSlot.Offhand),
            new GlamSlot.GridRow(GlamSlot.Body, GlamSlot.Earrings),
            new GlamSlot.GridRow(GlamSlot.Hands, GlamSlot.Necklace),
            new GlamSlot.GridRow(GlamSlot.Legs, GlamSlot.Bracelets),
            new GlamSlot.GridRow(GlamSlot.Feet, GlamSlot.Ring));
    }

    [Fact]
    public void All_is_derived_from_the_grid_with_no_duplicates_or_gaps()
    {
        // Every grid cell (both columns, skipping the empty Main Hand partner) appears exactly once.
        var fromGrid = GlamSlot.Grid
            .SelectMany(row => row.Right is null ? new[] { row.Left } : [row.Left, row.Right])
            .ToList();

        GlamSlot.All.Should().BeEquivalentTo(fromGrid);
        GlamSlot.All.Should().OnlyHaveUniqueItems();
        GlamSlot.All.Should().HaveCount(11);
    }

    [Theory]
    [InlineData("weapon", "Main Hand")]
    [InlineData("offhand", "Off Hand")]
    [InlineData("head", "Head")]
    [InlineData("body", "Body")]
    [InlineData("hands", "Hands")]
    [InlineData("legs", "Legs")]
    [InlineData("feet", "Feet")]
    [InlineData("earrings", "Ears")]
    [InlineData("necklace", "Neck")]
    [InlineData("bracelets", "Wrists")]
    [InlineData("ring", "Rings")]
    public void Label_maps_each_key_to_its_human_name(string key, string expected)
        => new GlamSlot(key).Label.Should().Be(expected);

    [Fact]
    public void Label_stays_out_of_value_equality()
    {
        // Label is computed, so two slots are equal iff their Key matches — a freshly-constructed
        // slot must equal the corresponding static even though Label is derived.
        new GlamSlot("head").Should().Be(GlamSlot.Head);
    }
}
