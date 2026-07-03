using Dalamud.Interface.Textures.TextureWraps;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Diagnostics;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Exercises <see cref="GlamImageLoader"/>: the download → decode → upload orchestration behind the
/// History tab's hover preview. Every collaborator is injected (byte fetch, <see cref="IGlamImageDecoder"/>,
/// upload, logger), so the decision flow — the happy path, the empty-download short-circuit, and the
/// reason-specific handling of download/decode/upload failures — is asserted here without a live
/// network or render device. Each failure is expected to yield <c>null</c> (a missing preview) and
/// log a distinct warning so a field report is diagnosable from the xllogs.
/// </summary>
public class GlamImageLoaderTests
{
    private const string Url = "https://glamours.ec/200/cover-0-9.png";

    private static readonly byte[] SomeBytes = { 1, 2, 3, 4 };
    private static readonly DecodedImage SomeImage = new(2, 1, new byte[2 * 1 * 4]);

    private readonly RecordingLogger log = new();

    /// <summary>
    /// A hand-written recording logger. A FakeItEasy fake can't stand in here because
    /// <c>ITraceLogger&lt;GlamImageLoader&gt;</c> is parameterised with an internal type, which the
    /// dynamic-proxy backend can't proxy without exposing internals to DynamicProxyGenAssembly2.
    /// </summary>
    private sealed class RecordingLogger : ITraceLogger<GlamImageLoader>
    {
        public List<string> Warnings { get; } = new();

        public void Verbose(string message) { }

        public void Debug(string message) { }

        public void Information(string message) { }

        public void Warning(string message) => this.Warnings.Add(message);

        public void Warning(string message, Exception exception) => this.Warnings.Add(message);

        public void Error(string message) { }

        public void Error(string message, Exception exception) { }
    }

    /// <summary>A stub decoder that records call count and either returns a fixed image or throws.</summary>
    private sealed class StubDecoder : IGlamImageDecoder
    {
        private readonly DecodedImage result;
        private readonly Exception? error;

        private StubDecoder(DecodedImage result, Exception? error)
        {
            this.result = result;
            this.error = error;
        }

        public int Calls { get; private set; }

        public static StubDecoder Returning(DecodedImage image) => new(image, null);

        public static StubDecoder Throwing(Exception ex) => new(default, ex);

        public DecodedImage Decode(ReadOnlySpan<byte> bytes, CancellationToken ct)
        {
            this.Calls++;
            if (this.error is not null)
                throw this.error;
            return this.result;
        }
    }

    private GlamImageLoader Build(
        Func<string, CancellationToken, Task<byte[]>> fetch,
        IGlamImageDecoder decoder,
        Func<DecodedImage, CancellationToken, Task<IDalamudTextureWrap?>> upload)
        => new(fetch, decoder, upload, this.log);

    [Fact]
    public async Task Happy_path_returns_the_uploaded_texture()
    {
        var wrap = A.Fake<IDalamudTextureWrap>();
        var loader = this.Build(
            (_, _) => Task.FromResult(SomeBytes),
            StubDecoder.Returning(SomeImage),
            (_, _) => Task.FromResult<IDalamudTextureWrap?>(wrap));

        var result = await loader.LoadAsync(Url, CancellationToken.None);

        result.Should().BeSameAs(wrap);
        this.log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_failure_returns_null_and_logs_without_decoding()
    {
        var decoder = StubDecoder.Returning(SomeImage);
        var loader = this.Build(
            (_, _) => Task.FromException<byte[]>(new HttpRequestException("boom")),
            decoder,
            (_, _) => Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>()));

        var result = await loader.LoadAsync(Url, CancellationToken.None);

        result.Should().BeNull();
        decoder.Calls.Should().Be(0);
        this.log.Warnings.Should().ContainSingle().Which.Should().Contain("download failed");
    }

    [Fact]
    public async Task Empty_download_returns_null_without_decoding()
    {
        var decoder = StubDecoder.Returning(SomeImage);
        var loader = this.Build(
            (_, _) => Task.FromResult(Array.Empty<byte>()),
            decoder,
            (_, _) => Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>()));

        var result = await loader.LoadAsync(Url, CancellationToken.None);

        result.Should().BeNull();
        decoder.Calls.Should().Be(0);
        this.log.Warnings.Should().ContainSingle().Which.Should().Contain("no bytes");
    }

    [Fact]
    public async Task Decode_failure_returns_null_and_logs_without_uploading()
    {
        var uploaded = false;
        var loader = this.Build(
            (_, _) => Task.FromResult(SomeBytes),
            StubDecoder.Throwing(new InvalidOperationException("bad image")),
            (_, _) => { uploaded = true; return Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>()); });

        var result = await loader.LoadAsync(Url, CancellationToken.None);

        result.Should().BeNull();
        uploaded.Should().BeFalse("a decode failure must not reach the GPU upload");
        this.log.Warnings.Should().ContainSingle().Which.Should().Contain("decode failed");
    }

    [Fact]
    public async Task Upload_failure_returns_null_and_logs()
    {
        var loader = this.Build(
            (_, _) => Task.FromResult(SomeBytes),
            StubDecoder.Returning(SomeImage),
            (_, _) => Task.FromException<IDalamudTextureWrap?>(new InvalidOperationException("no device")));

        var result = await loader.LoadAsync(Url, CancellationToken.None);

        result.Should().BeNull();
        this.log.Warnings.Should().ContainSingle().Which.Should().Contain("GPU upload failed");
    }
}
