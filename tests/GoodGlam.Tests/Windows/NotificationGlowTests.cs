using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers <see cref="NotificationGlow"/>, the pure pulse maths behind the logo's notification glow:
/// the intensity stays within [0, 1], breathes between its extremes over a cycle, and is continuous
/// (no snapping) frame to frame.
/// </summary>
public class NotificationGlowTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.31)]
    [InlineData(0.625)]
    [InlineData(1.0)]
    [InlineData(3.7)]
    [InlineData(42.0)]
    public void Intensity_stays_within_unit_range(double seconds)
    {
        var value = new NotificationGlow().Intensity(seconds);

        value.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public void Intensity_reaches_both_dim_and_bright_extremes_across_a_cycle()
    {
        var glow = new NotificationGlow();

        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i <= 200; i++)
        {
            var value = glow.Intensity(i / 100.0);
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        min.Should().BeLessThan(0.05f);
        max.Should().BeGreaterThan(0.95f);
    }

    [Fact]
    public void Intensity_changes_smoothly_between_nearby_frames()
    {
        var glow = new NotificationGlow();

        var a = glow.Intensity(0.50);
        var b = glow.Intensity(0.50 + (1.0 / 60.0));

        MathF.Abs(b - a).Should().BeLessThan(0.1f);
    }

    [Fact]
    public void BuildGoldSilhouette_recolours_to_gold_and_preserves_alpha()
    {
        // 2x1 RGBA source: a fully-opaque red pixel and a half-transparent blue pixel.
        byte[] source =
        {
            255, 0, 0, 255,
            0, 0, 255, 128,
        };

        var result = NotificationGlow.BuildGoldSilhouette(2, 1, 8, source);

        // Output is tightly-packed BGRA32: every pixel is gold (B,G,R) with the source alpha kept.
        result.Should().HaveCount(8);
        result[0].Should().Be(NotificationGlow.GoldB);
        result[1].Should().Be(NotificationGlow.GoldG);
        result[2].Should().Be(NotificationGlow.GoldR);
        result[3].Should().Be(255);
        result[4].Should().Be(NotificationGlow.GoldB);
        result[5].Should().Be(NotificationGlow.GoldG);
        result[6].Should().Be(NotificationGlow.GoldR);
        result[7].Should().Be(128);
    }

    [Fact]
    public void BuildGoldSilhouette_honours_row_padding_in_the_source_pitch()
    {
        // 1x2 image with a padded pitch of 6 bytes (one 4-byte pixel + 2 padding bytes per row).
        byte[] source =
        {
            10, 20, 30, 40, 0xEE, 0xEE,
            50, 60, 70, 90, 0xEE, 0xEE,
        };

        var result = NotificationGlow.BuildGoldSilhouette(1, 2, 6, source);

        result.Should().HaveCount(8);
        result[3].Should().Be(40);   // row 0 alpha
        result[7].Should().Be(90);   // row 1 alpha — proves padding was skipped
    }

    [Fact]
    public void BuildGoldSilhouette_rejects_a_pitch_too_small_for_the_row()
    {
        var act = () => NotificationGlow.BuildGoldSilhouette(2, 1, 4, new byte[8]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildStamps_lays_out_two_rings_of_radial_offsets()
    {
        var glow = new NotificationGlow();

        var stamps = glow.BuildStamps(seconds: 0.0, scale: 1f);

        // Two concentric rings, each of NotificationGlow.Directions stamps.
        stamps.Should().HaveCount(NotificationGlow.StampCount);

        var radii = stamps.Select(s => s.Offset.Length()).ToList();
        var inner = radii.Where(r => r < 3f).ToList();
        var outer = radii.Where(r => r >= 3f).ToList();

        inner.Should().HaveCount(NotificationGlow.StampCount / 2);
        outer.Should().HaveCount(NotificationGlow.StampCount / 2);

        // Inner ring sits ~2px out, outer ring ~4px out (the InnerRadius/OuterRadius knobs at scale 1).
        inner.Should().AllSatisfy(r => r.Should().BeApproximately(2f, 0.01f));
        outer.Should().AllSatisfy(r => r.Should().BeApproximately(4f, 0.01f));
    }

    [Fact]
    public void BuildStamps_distributes_offsets_evenly_around_the_circle()
    {
        var glow = new NotificationGlow();

        var innerAngles = glow.BuildStamps(0.0, 1f)
            .Where(s => s.Offset.Length() < 3f)
            .Select(s => MathF.Atan2(s.Offset.Y, s.Offset.X))
            .OrderBy(a => a)
            .ToList();

        // Distinct directions, evenly spaced by a full turn / Directions.
        innerAngles.Should().OnlyHaveUniqueItems();
        for (var i = 1; i < innerAngles.Count; i++)
        {
            var gap = innerAngles[i] - innerAngles[i - 1];
            gap.Should().BeApproximately(2f * MathF.PI / innerAngles.Count, 0.001f);
        }
    }

    [Fact]
    public void BuildStamps_scales_offsets_by_the_ui_factor()
    {
        var glow = new NotificationGlow();

        var atOne = glow.BuildStamps(0.0, 1f).Max(s => s.Offset.Length());
        var atTwo = glow.BuildStamps(0.0, 2f).Max(s => s.Offset.Length());

        atTwo.Should().BeApproximately(atOne * 2f, 0.01f);
    }

    [Fact]
    public void BuildStamps_inner_ring_is_stronger_than_the_outer_ring()
    {
        var glow = new NotificationGlow();

        var stamps = glow.BuildStamps(0.0, 1f);
        var innerAlpha = stamps.Where(s => s.Offset.Length() < 3f).Select(s => s.Alpha).Distinct().ToList();
        var outerAlpha = stamps.Where(s => s.Offset.Length() >= 3f).Select(s => s.Alpha).Distinct().ToList();

        // Each ring uses one alpha; the inner ring is the brighter of the two, and all are faint
        // (they accumulate via overlap).
        innerAlpha.Should().ContainSingle();
        outerAlpha.Should().ContainSingle();
        innerAlpha[0].Should().BeGreaterThan(outerAlpha[0]);
        stamps.Should().AllSatisfy(s => s.Alpha.Should().BeInRange(0f, 1f));
    }

    [Fact]
    public void BuildStamps_brightens_with_the_pulse()
    {
        var glow = new NotificationGlow();

        // Intensity peaks a quarter of the way through a pulse, and is lowest three-quarters through.
        var peak = glow.BuildStamps(0.3125, 1f).Max(s => s.Alpha);
        var trough = glow.BuildStamps(0.9375, 1f).Max(s => s.Alpha);

        peak.Should().BeGreaterThan(trough);
    }
}
