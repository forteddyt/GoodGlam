using System.Reflection;
using Dalamud.Plugin.Services;
using FluentAssertions;
using Xunit;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// Offline coverage of the diagnostics sink that makes a live-EC failure actionable. Like
/// <see cref="EcProbeDiagnosisTests"/> these need no EC connectivity, so they keep reporting when
/// the live suite cannot run.
/// </summary>
public sealed class LiveEcDiagnosticsTests
{
    [Fact]
    public void Plugin_log_calls_and_harness_notes_are_captured_with_their_level()
    {
        LiveEcDiagnostics.Reset();
        LiveEcDiagnostics.Snapshot().Should().Be("(no log lines were captured)");

        var log = DispatchProxy.Create<IPluginLog, CapturingLog>();
        log.Debug("GoodGlam[ManagedHttpTransport]: POST returned HTTP 403");
        log.Warning(new InvalidOperationException("nope"), "GoodGlam[CurlTransport]: request failed");
        LiveEcDiagnostics.Note("attempt 1/4 threw TaskCanceledException");

        var snapshot = LiveEcDiagnostics.Snapshot();
        snapshot.Should().Contain("[Debug] GoodGlam[ManagedHttpTransport]: POST returned HTTP 403");
        snapshot.Should().Contain("GoodGlam[CurlTransport]: request failed (InvalidOperationException: nope)");
        snapshot.Should().Contain("[test] attempt 1/4 threw TaskCanceledException");
    }

    [Fact]
    public void Buffer_is_bounded_so_a_long_run_cannot_grow_without_limit()
    {
        LiveEcDiagnostics.Reset();
        for (var i = 0; i < 1000; i++)
            LiveEcDiagnostics.Note($"line {i}");

        var lines = LiveEcDiagnostics.Snapshot().Split(Environment.NewLine);
        lines.Should().HaveCount(400);
        lines[^1].Should().Contain("line 999", "the most recent lines are the ones worth keeping");
    }
}
