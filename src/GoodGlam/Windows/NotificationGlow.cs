using System.Numerics;

namespace GoodGlam.Windows;

/// <summary>
/// One halo stamp: a screen-space <paramref name="Offset"/> from the logo rect at which to draw the
/// gold sprite, and the <paramref name="Alpha"/> to draw it with. Pure data so the glow's geometry
/// and opacity can be asserted without a running ImGui context.
/// </summary>
internal readonly record struct GlowStamp(Vector2 Offset, float Alpha);

/// <summary>
/// Pure, ImGui-free brain of the floating logo's notification glow, plus its compile-time tuning
/// knobs (intentionally not exposed in config). Kept separate from <see cref="LogoWindow"/> so the
/// pulse curve, the per-frame stamp layout, and the silhouette recolouring can all be unit tested
/// without a running ImGui/GPU context. <see cref="LogoWindow"/> owns only the ImGui plumbing:
/// it asks for the stamps (<see cref="BuildStamps"/>) and the baked gold sprite
/// (<see cref="BuildGoldSilhouette"/>) and submits the draw calls.
/// </summary>
internal sealed class NotificationGlow
{
    // --- Notification glow tuning (compile-time knobs; intentionally NOT exposed in config) ---

    /// <summary>How many full bright→dim→bright pulses occur per second.</summary>
    private const double PulsesPerSecond = 0.8;

    /// <summary>Opacity of a single halo stamp; many overlapping stamps accumulate into the glow.</summary>
    private const float StampAlpha = 0.18f;

    /// <summary>Resting (dim) halo brightness before the pulse adds to it.</summary>
    private const float BaseAlpha = 0.30f;

    /// <summary>How much brightness the pulse adds on top of <see cref="BaseAlpha"/> at its peak.</summary>
    private const float PulseAlpha = 0.50f;

    /// <summary>Number of radial stamp directions per ring (higher = smoother, rounder halo).</summary>
    private const int Directions = 12;

    /// <summary>Inner ring spread, in logical pixels (scaled by the UI factor).</summary>
    private const float InnerRadius = 2f;

    /// <summary>Outer ring spread, in logical pixels (scaled by the UI factor).</summary>
    private const float OuterRadius = 4f;

    /// <summary>Relative strength of the tight inner ring.</summary>
    private const float InnerWeight = 1.0f;

    /// <summary>Relative strength of the wider, fainter outer ring.</summary>
    private const float OuterWeight = 0.5f;

    /// <summary>Glow colour (warm gold), channels in 0–255.</summary>
    public const byte GoldR = 255;
    public const byte GoldG = 199;
    public const byte GoldB = 56;

    /// <summary>Total number of stamps drawn per frame (both rings).</summary>
    public const int StampCount = Directions * 2;

    /// <summary>
    /// A smooth glow intensity in the inclusive range [0, 1] for the given elapsed time in seconds.
    /// Follows a sine wave so the glow eases between fully dim (0) and fully bright (1) rather than
    /// snapping, giving a gentle "breathing" pulse.
    /// </summary>
    public float Intensity(double seconds)
    {
        var phase = seconds * PulsesPerSecond * 2.0 * Math.PI;
        return (float)((Math.Sin(phase) + 1.0) * 0.5);
    }

    /// <summary>
    /// Lays out the per-frame halo stamps: two concentric rings of <see cref="Directions"/> radial
    /// offsets (a tight, strong inner ring and a wider, fainter outer ring), with the pulse breathing
    /// every stamp's alpha. The offsets are in screen pixels (already multiplied by
    /// <paramref name="scale"/>) and are added to the logo rect by the caller. Pure: no ImGui state.
    /// </summary>
    /// <param name="seconds">Elapsed time, for the pulse.</param>
    /// <param name="scale">UI/DPI scale factor applied to the ring radii.</param>
    public GlowStamp[] BuildStamps(double seconds, float scale)
    {
        var baseAlpha = BaseAlpha + (PulseAlpha * this.Intensity(seconds));

        var stamps = new GlowStamp[StampCount];
        var ringRadii = new[] { InnerRadius * scale, OuterRadius * scale };
        var ringWeights = new[] { InnerWeight, OuterWeight };

        var s = 0;
        for (var r = 0; r < ringRadii.Length; r++)
        {
            var radius = ringRadii[r];
            var alpha = baseAlpha * ringWeights[r] * StampAlpha;
            for (var i = 0; i < Directions; i++)
            {
                var angle = i / (float)Directions * 2f * MathF.PI;
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                stamps[s++] = new GlowStamp(offset, alpha);
            }
        }

        return stamps;
    }

    /// <summary>
    /// Bakes a "gold silhouette" of an image: every pixel becomes solid gold while keeping the
    /// source's alpha, producing a shape-matching halo sprite that works for <em>any</em> logo
    /// (triangles, a square, a circle, curves…) without hardcoding its geometry. Input is a 32-bpp
    /// image (RGBA or BGRA — alpha is the 4th byte either way); output is tightly packed BGRA32
    /// (<c>pitch = width * 4</c>) ready for <c>ITextureProvider.CreateFromRaw</c>.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="sourcePitch">Row stride of <paramref name="source"/> in bytes.</param>
    /// <param name="source">Source pixels (32 bpp, alpha in the 4th byte of each pixel).</param>
    public static byte[] BuildGoldSilhouette(int width, int height, int sourcePitch, ReadOnlySpan<byte> source)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (sourcePitch < width * 4)
            throw new ArgumentOutOfRangeException(nameof(sourcePitch), "Pitch is too small for a 32-bpp row.");
        if (source.Length < sourcePitch * height)
            throw new ArgumentException("Source buffer is smaller than height * pitch.", nameof(source));

        var dst = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * sourcePitch;
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var alpha = source[srcRow + (x * 4) + 3];
                var d = dstRow + (x * 4);
                dst[d] = GoldB;
                dst[d + 1] = GoldG;
                dst[d + 2] = GoldR;
                dst[d + 3] = alpha;
            }
        }

        return dst;
    }
}
