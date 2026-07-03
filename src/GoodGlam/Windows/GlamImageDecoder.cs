using SixLabors.ImageSharp;
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
    /// supported/valid image so callers can distinguish a decode failure from an empty download.
    /// </summary>
    DecodedImage Decode(ReadOnlySpan<byte> bytes);
}

/// <summary>
/// The production decoder: a fully-managed <c>SixLabors.ImageSharp</c> decode to RGBA32. Being pure
/// managed (no native binaries), it decodes WebP identically under Wine/Linux and native Windows —
/// unlike Dalamud's WIC-based <c>CreateFromImageAsync</c>, which can't decode the WebP that Cloudflare
/// Polish serves for Eorzea Collection's <c>.png</c> cover URLs (issue #64).
/// </summary>
internal sealed class ImageSharpDecoder : IGlamImageDecoder
{
    public DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        using var image = Image.Load<Rgba32>(bytes);
        var rgba = new byte[checked(image.Width * image.Height * 4)];
        image.CopyPixelDataTo(rgba);
        return new DecodedImage(image.Width, image.Height, rgba);
    }
}
