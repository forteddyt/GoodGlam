using System.Numerics;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Proves the notification glow is derived from the input art's alpha mask rather than hardcoded geometry: two
/// genuinely different synthetic silhouettes are rasterized to alpha masks, baked through
/// <see cref="NotificationGlow.BuildGoldSilhouette"/>, and the resulting gold halo sprite is asserted
/// to match each shape exactly (and to differ between shapes). So if the logo ever changes shape,
/// the glow follows it — these tests would catch a regression that re-hardcoded a single silhouette.
/// </summary>
public class NotificationGlowShapeTests
{
    private const int Size = 64;

    /// <summary>A synthetic split pyramid made from two triangles.</summary>
    private static readonly Vector2[][] SplitPyramid =
    {
        new[] { new Vector2(48, 16), new Vector2(12, 82), new Vector2(44, 82) },
        new[] { new Vector2(52, 16), new Vector2(88, 82), new Vector2(56, 82) },
    };

    /// <summary>A single centered diamond with a clearly different silhouette.</summary>
    private static readonly Vector2[][] Diamond =
    {
        new[] { new Vector2(50, 10), new Vector2(90, 50), new Vector2(50, 90), new Vector2(10, 50) },
    };

    [Fact]
    public void Glow_sprite_matches_the_split_pyramid_silhouette()
    {
        AssertGlowMatchesShape(SplitPyramid);
    }

    [Fact]
    public void Glow_sprite_matches_the_diamond_silhouette()
    {
        AssertGlowMatchesShape(Diamond);
    }

    [Fact]
    public void Glow_sprite_differs_between_two_different_shapes()
    {
        var pyramid = BakeGlowAlpha(SplitPyramid);
        var diamond = BakeGlowAlpha(Diamond);

        // Both shapes actually cover some pixels...
        pyramid.Should().Contain((byte)255);
        diamond.Should().Contain((byte)255);

        // ...and the glow they produce is genuinely different (it tracks the art, not a fixed shape).
        diamond.Should().NotEqual(pyramid);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-1, 8)]
    [InlineData(8, 0)]
    public void BuildGoldSilhouette_rejects_non_positive_dimensions(int width, int height)
    {
        var source = new byte[Math.Max(1, width) * Math.Max(1, height) * 4];

        var act = () => NotificationGlow.BuildGoldSilhouette(width, height, Math.Max(1, width) * 4, source);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildGoldSilhouette_rejects_a_pitch_too_small_for_a_row()
    {
        var act = () => NotificationGlow.BuildGoldSilhouette(8, 8, 8 * 4 - 1, new byte[8 * 8 * 4]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildGoldSilhouette_rejects_a_source_buffer_smaller_than_the_image()
    {
        // Pitch is valid, but the buffer is one row short of width * height * 4.
        var act = () => NotificationGlow.BuildGoldSilhouette(8, 8, 8 * 4, new byte[8 * 7 * 4]);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Bakes the glow sprite for a shape and asserts every pixel is gold with alpha exactly equal to
    /// the shape's own coverage mask — i.e. the halo silhouette is the logo silhouette.
    /// </summary>
    private static void AssertGlowMatchesShape(Vector2[][] polygons)
    {
        var mask = Rasterize(polygons);
        var source = ToRgba(mask, coveredColor: (R: 251, G: 75, B: 78)); // arbitrary EC-coral fill

        var baked = NotificationGlow.BuildGoldSilhouette(Size, Size, Size * 4, source);

        for (var i = 0; i < Size * Size; i++)
        {
            var b = baked[(i * 4) + 0];
            var g = baked[(i * 4) + 1];
            var r = baked[(i * 4) + 2];
            var a = baked[(i * 4) + 3];

            // Recoloured to gold regardless of the source RGB...
            b.Should().Be(NotificationGlow.GoldB);
            g.Should().Be(NotificationGlow.GoldG);
            r.Should().Be(NotificationGlow.GoldR);

            // ...with the alpha exactly tracking the shape's coverage (255 inside, 0 outside).
            a.Should().Be(mask[i] ? (byte)255 : (byte)0);
        }
    }

    /// <summary>Bakes a shape and returns just the glow sprite's alpha channel (its visible silhouette).</summary>
    private static byte[] BakeGlowAlpha(Vector2[][] polygons)
    {
        var source = ToRgba(Rasterize(polygons), coveredColor: (255, 255, 255));
        var baked = NotificationGlow.BuildGoldSilhouette(Size, Size, Size * 4, source);

        var alpha = new byte[Size * Size];
        for (var i = 0; i < alpha.Length; i++)
            alpha[i] = baked[(i * 4) + 3];

        return alpha;
    }

    /// <summary>Wraps a coverage mask into a 32-bpp RGBA buffer: covered pixels opaque, others clear.</summary>
    private static byte[] ToRgba(bool[] mask, (byte R, byte G, byte B) coveredColor)
    {
        var rgba = new byte[Size * Size * 4];
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;

            rgba[(i * 4) + 0] = coveredColor.R;
            rgba[(i * 4) + 1] = coveredColor.G;
            rgba[(i * 4) + 2] = coveredColor.B;
            rgba[(i * 4) + 3] = 255;
        }

        return rgba;
    }

    /// <summary>
    /// Tiny polygon rasterizer: maps a 100x100 coordinate space onto a <see cref="Size"/>-square grid
    /// and marks a pixel covered if its center falls inside any polygon (even-odd rule). Enough to turn
    /// the synthetic shape definitions above into representative PNG alpha masks.
    /// </summary>
    private static bool[] Rasterize(Vector2[][] polygons)
    {
        var mask = new bool[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // Pixel centre, mapped from the grid back into the 0–100 viewBox space.
                var px = (x + 0.5f) / Size * 100f;
                var py = (y + 0.5f) / Size * 100f;

                var inside = false;
                foreach (var poly in polygons)
                {
                    if (PointInPolygon(px, py, poly))
                        inside = !inside;
                }

                mask[(y * Size) + x] = inside;
            }
        }

        return mask;
    }

    private static bool PointInPolygon(float px, float py, Vector2[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if (((a.Y > py) != (b.Y > py)) &&
                (px < ((b.X - a.X) * (py - a.Y) / (b.Y - a.Y)) + a.X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
