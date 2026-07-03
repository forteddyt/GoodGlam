using FluentAssertions;
using GoodGlam.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Exercises <see cref="ImageSharpDecoder"/>: the fully-managed decode seam behind the History tab's
/// hover preview. This is the fix for the bug where Eorzea Collection cover images fail to load —
/// Cloudflare Polish serves WebP for the `.png` URLs and Dalamud's WIC-based CreateFromImageAsync
/// can't decode WebP under Wine. The decoder turns encoded bytes into raw RGBA32 (matching
/// RawImageSpecification.Rgba32) with no render device or network, so it's unit-testable here.
/// Fixtures are generated in-memory with ImageSharp's encoders so no binaries need committing.
/// </summary>
public class GlamImageDecoderTests
{
    private static byte[] Encode(IImageEncoder encoder)
    {
        // A 2x1 image with two distinct, fully-opaque pixels so we can assert byte order (RGBA) and
        // dimensions. Lossless formats let us assert exact colours; JPEG is asserted loosely.
        using var image = new Image<Rgba32>(2, 1);
        image[0, 0] = new Rgba32(10, 20, 30, 255);
        image[1, 0] = new Rgba32(200, 150, 100, 255);

        using var ms = new MemoryStream();
        image.Save(ms, encoder);
        return ms.ToArray();
    }

    [Fact]
    public void Decodes_webp_to_rgba()
    {
        // WebP is the exact format that was failing in the field (issue #64).
        var bytes = Encode(new WebpEncoder { Quality = 100, FileFormat = WebpFileFormatType.Lossless });

        var decoded = new ImageSharpDecoder().Decode(bytes, CancellationToken.None);

        decoded.Width.Should().Be(2);
        decoded.Height.Should().Be(1);
        decoded.Rgba.Should().HaveCount(2 * 1 * 4);
        // First pixel, RGBA byte order.
        decoded.Rgba[0].Should().Be(10);
        decoded.Rgba[1].Should().Be(20);
        decoded.Rgba[2].Should().Be(30);
        decoded.Rgba[3].Should().Be(255);
        // Second pixel.
        decoded.Rgba[4].Should().Be(200);
        decoded.Rgba[5].Should().Be(150);
        decoded.Rgba[6].Should().Be(100);
        decoded.Rgba[7].Should().Be(255);
    }

    [Fact]
    public void Decodes_png_to_rgba()
    {
        var bytes = Encode(new PngEncoder());

        var decoded = new ImageSharpDecoder().Decode(bytes, CancellationToken.None);

        decoded.Width.Should().Be(2);
        decoded.Height.Should().Be(1);
        decoded.Rgba.Should().HaveCount(2 * 1 * 4);
        decoded.Rgba[0].Should().Be(10);
        decoded.Rgba[1].Should().Be(20);
        decoded.Rgba[2].Should().Be(30);
        decoded.Rgba[3].Should().Be(255);
    }

    [Fact]
    public void Decodes_jpeg_to_rgba()
    {
        // JPEG is lossy, so assert the shape and that alpha is opaque rather than exact colours.
        var bytes = Encode(new JpegEncoder { Quality = 100 });

        var decoded = new ImageSharpDecoder().Decode(bytes, CancellationToken.None);

        decoded.Width.Should().Be(2);
        decoded.Height.Should().Be(1);
        decoded.Rgba.Should().HaveCount(2 * 1 * 4);
        decoded.Rgba[3].Should().Be(255);
    }

    [Fact]
    public void Rejects_non_image_bytes()
    {
        var garbage = "not an image"u8.ToArray();

        var decode = () => new ImageSharpDecoder().Decode(garbage, CancellationToken.None);

        decode.Should().Throw<Exception>("undecodable bytes must surface so the loader can log a decode failure");
    }

    [Fact]
    public void Rejects_images_larger_than_the_dimension_cap()
    {
        // One px past the cap on the long edge; kept 1px tall so the fixture stays cheap to encode.
        using var oversized = new Image<Rgba32>(ImageSharpDecoder.MaxDimension + 1, 1);
        using var ms = new MemoryStream();
        oversized.Save(ms, new PngEncoder());
        var bytes = ms.ToArray();

        var decode = () => new ImageSharpDecoder().Decode(bytes, CancellationToken.None);

        decode.Should().Throw<InvalidOperationException>("an oversized image must be rejected before it is decoded/allocated")
            .WithMessage("*exceed*");
    }

    [Fact]
    public void Honours_cancellation()
    {
        var bytes = Encode(new PngEncoder());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var decode = () => new ImageSharpDecoder().Decode(bytes, cts.Token);

        decode.Should().Throw<OperationCanceledException>("a cancelled load (e.g. teardown) must not decode");
    }
}
