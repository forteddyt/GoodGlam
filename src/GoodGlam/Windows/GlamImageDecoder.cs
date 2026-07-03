using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace GoodGlam.Windows;

/// <summary>
/// A decoded image reduced to what a GPU upload needs: its dimensions and tightly-packed RGBA32
/// pixels (one byte each of R, G, B, A per pixel, row-major, no padding). This byte order matches
/// <see cref="Dalamud.Interface.Textures.RawImageSpecification.Rgba32"/>, so it can be handed
/// straight to <c>ITextureProvider.CreateFromRawAsync</c>.
/// </summary>
internal readonly record struct DecodedImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decodes encoded image bytes (WebP/PNG/JPEG/…) into raw RGBA32. Abstracted so the History-tab
/// texture loader can be tested without a live render device, and so the decoder can be swapped.
/// </summary>
internal interface IGlamImageDecoder
{
    /// <summary>
    /// Decodes <paramref name="bytes"/> into <see cref="DecodedImage"/>. Throws if the bytes are not a
    /// supported/valid image, if the image exceeds the preview dimension cap, or if
    /// <paramref name="ct"/> is cancelled — so callers can distinguish a decode failure from an empty
    /// download and abandon a load promptly on teardown.
    /// </summary>
    DecodedImage Decode(ReadOnlySpan<byte> bytes, CancellationToken ct);
}

/// <summary>
/// The production decoder: a fully-managed <c>SixLabors.ImageSharp</c> decode to RGBA32. Being pure
/// managed (no native binaries), it decodes WebP identically under Wine/Linux and native Windows —
/// unlike Dalamud's WIC-based <c>CreateFromImageAsync</c>, which can't decode the WebP that Cloudflare
/// Polish serves for Eorzea Collection's <c>.png</c> cover URLs (issue #64).
/// </summary>
internal sealed class ImageSharpDecoder : IGlamImageDecoder
{
    /// <summary>
    /// Upper bound on either dimension of a decoded preview. Cover thumbnails are a few hundred px, so
    /// this only exists to reject a crafted/oversized image (a decompression bomb) before we allocate
    /// <c>Width*Height*4</c> bytes — the image bytes come from a scraped, attacker-influenceable EC
    /// URL. 4096 px/side caps the buffer at ~67 MB.
    /// </summary>
    internal const int MaxDimension = 4096;

    // A single frame is all a cover preview needs; this also stops an animated payload from decoding
    // every frame.
    private static readonly DecoderOptions Options = new() { MaxFrames = 1 };

    public DecodedImage Decode(ReadOnlySpan<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Read the header first so we can reject an oversized image before decoding/allocating it.
        var info = Image.Identify(Options, bytes);
        if (info.Width > MaxDimension || info.Height > MaxDimension)
        {
            throw new InvalidOperationException(
                $"image dimensions {info.Width}x{info.Height} exceed the {MaxDimension}px preview cap.");
        }

        ct.ThrowIfCancellationRequested();

        using var image = Image.Load<Rgba32>(Options, bytes);
        var rgba = new byte[checked(image.Width * image.Height * 4)];
        image.CopyPixelDataTo(rgba);
        return new DecodedImage(image.Width, image.Height, rgba);
    }
}
