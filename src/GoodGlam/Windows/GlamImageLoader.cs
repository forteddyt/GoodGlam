using Dalamud.Interface.Textures.TextureWraps;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>
/// Orchestrates loading one glamour cover preview: download the bytes, decode them to raw RGBA with
/// an <see cref="IGlamImageDecoder"/>, then upload them to a GPU texture. Each collaborator is
/// injected — the byte fetch, the decoder, and the upload — so the decision flow (empty-download
/// short-circuit and the reason-specific failure handling for download vs decode vs upload) is
/// unit-tested with fakes, without a live network or render device. The production wiring in
/// <see cref="HistoryTab"/> supplies the real <c>HttpClient</c>, <see cref="ImageSharpDecoder"/>, and
/// <c>ITextureProvider.CreateFromRawAsync</c>.
/// </summary>
internal sealed class GlamImageLoader
{
    private readonly Func<string, CancellationToken, Task<byte[]>> fetch;
    private readonly IGlamImageDecoder decoder;
    private readonly Func<DecodedImage, CancellationToken, Task<IDalamudTextureWrap?>> upload;
    private readonly ITraceLogger<GlamImageLoader> log;

    internal GlamImageLoader(
        Func<string, CancellationToken, Task<byte[]>> fetch,
        IGlamImageDecoder decoder,
        Func<DecodedImage, CancellationToken, Task<IDalamudTextureWrap?>> upload,
        ITraceLogger<GlamImageLoader>? log = null)
    {
        this.fetch = fetch;
        this.decoder = decoder;
        this.upload = upload;
        this.log = log ?? new TraceLogger<GlamImageLoader>();
    }

    /// <summary>
    /// Loads the cover image at <paramref name="url"/>, returning its GPU texture or <c>null</c> on any
    /// failure (network, empty/blocked response, decode, or GPU upload). The cache treats <c>null</c>
    /// as a missing preview — the row still renders, just without an image. Each failure logs its
    /// distinct reason so a future report is diagnosable from the xllogs alone, instead of a silent
    /// null.
    /// </summary>
    public async Task<IDalamudTextureWrap?> LoadAsync(string url, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await this.fetch(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log.Warning($"preview image download failed for '{url}'.", ex);
            return null;
        }

        if (bytes.Length == 0)
        {
            this.log.Warning($"preview image download for '{url}' returned no bytes (empty/blocked response).");
            return null;
        }

        DecodedImage decoded;
        try
        {
            decoded = this.decoder.Decode(bytes, ct);
        }
        catch (Exception ex)
        {
            this.log.Warning($"preview image decode failed for '{url}' ({bytes.Length} bytes).", ex);
            return null;
        }

        try
        {
            return await this.upload(decoded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log.Warning($"preview image GPU upload failed for '{url}' ({decoded.Width}x{decoded.Height}).", ex);
            return null;
        }
    }
}
