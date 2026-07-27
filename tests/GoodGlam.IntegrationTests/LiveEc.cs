using System.Reflection;
using System.Text.Json;
using Dalamud.Plugin.Services;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// A known Eorzea Collection gear piece: the game item ID the loot window would report, the
/// EC slot, and the EC item ID the bridge is expected to resolve it to. Used to assert the
/// real <c>game ID -&gt; EC ID</c> bridge against live EC.
/// </summary>
public sealed record EcFixture(string Name, GlamSlot Slot, uint GameItemId, int EcId);

/// <summary>Stable, reliably-popular fixtures verified against live EC.</summary>
public static class EcFixtures
{
    public static readonly EcFixture CavalryGauntlets = new("Cavalry Gauntlets", GlamSlot.Hands, 3610, 1870);
    public static readonly EcFixture ScionJacket = new("Scion Adventurer's Jacket", GlamSlot.Body, 17492, 8912);
    public static readonly EcFixture ScionBottoms = new("Scion Adventurer's Bottoms", GlamSlot.Legs, 17493, 8941);

    /// <summary>The piece used for the reachability probe — chosen for a large, stable loves count.</summary>
    public static readonly EcFixture Reachability = ScionJacket;

    /// <summary>Primitive rows for <c>[Theory]</c> data (xUnit-serializable): name, slot key, game ID, EC ID.</summary>
    public static IEnumerable<object[]> KnownItems =>
    [
        ["Cavalry Gauntlets", "hands", 3610u, 1870],
        ["Scion Adventurer's Jacket", "body", 17492u, 8912],
        ["Scion Adventurer's Bottoms", "legs", 17493u, 8941],
    ];
}

/// <summary>
/// Shared helpers for the live Eorzea Collection tests: a capturing Dalamud log bootstrap and a
/// bounded-retry wrapper that smooths over transient network blips / rate limiting.
/// </summary>
public static class LiveEc
{
    /// <summary>All live-EC tests share this collection so they run serially (politeness + determinism).</summary>
    public const string Collection = "LiveEc";

    private const int MaxAttempts = 4;
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(30);

    private static bool logInstalled;

    /// <summary>
    /// Installs an <see cref="IPluginLog"/> into the plugin's static <c>Services.Log</c> so the EC
    /// client and transport (which log freely) don't dereference a null static under test. It records
    /// rather than discards: those log lines carry the HTTP status codes, curl exit codes and record
    /// counts that say *why* a live call came back empty, and they are the only such evidence CI gets.
    /// </summary>
    public static void EnsureLog()
    {
        if (logInstalled)
            return;

        var log = DispatchProxy.Create<IPluginLog, CapturingLog>();
        typeof(GoodGlam.Services).GetProperty("Log", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, log);
        logInstalled = true;
    }

    /// <summary>
    /// Runs a live EC call with bounded exponential backoff, retrying until <paramref name="isGood"/>
    /// accepts the result or attempts are exhausted, so only a sustained failure reaches the assertion.
    /// Each attempt is hard-bounded by <see cref="PerAttemptTimeout"/>. The final (possibly "bad")
    /// result is returned so the caller's assertion produces the failure message; every attempt's
    /// outcome is recorded to <see cref="LiveEcDiagnostics"/> so a swallowed transient isn't invisible.
    /// </summary>
    public static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> call, Func<T, bool> isGood)
    {
        T result = default!;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(PerAttemptTimeout);
            try
            {
                result = await call(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
                if (isGood(result))
                    return result;

                LiveEcDiagnostics.Note($"attempt {attempt}/{MaxAttempts} completed but produced an unusable result.");
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                // Transient failure — record it, then fall through to the backoff delay and try again.
                LiveEcDiagnostics.Note($"attempt {attempt}/{MaxAttempts} threw {ex.GetType().Name}: {ex.Message}");
            }

            if (attempt < MaxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt)).ConfigureAwait(false);
        }

        return result;
    }
}

/// <summary>
/// A bounded, in-memory sink for everything the EC client and transport log during the live tests,
/// plus the retry harness's per-attempt notes. Nothing is printed during a green run; the buffer is
/// only rendered into a failure message, where it is the difference between "EC unreachable" and an
/// actual HTTP status code.
/// </summary>
public static class LiveEcDiagnostics
{
    /// <summary>Enough to cover a full four-attempt probe without letting a long run grow unbounded.</summary>
    private const int Capacity = 400;

    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();

    /// <summary>Adds a line from the test harness itself (as opposed to the plugin's logger).</summary>
    public static void Note(string message) => Append($"[test] {message}");

    /// <summary>Drops everything captured so far, so a failure message covers only the run of interest.</summary>
    public static void Reset()
    {
        lock (Gate)
            Lines.Clear();
    }

    /// <summary>The captured lines, oldest first, ready to embed in an assertion message.</summary>
    public static string Snapshot()
    {
        lock (Gate)
            return Lines.Count == 0 ? "(no log lines were captured)" : string.Join(Environment.NewLine, Lines);
    }

    /// <summary>Records a <see cref="IPluginLog"/> call, rendering its level, message and any exception.</summary>
    internal static void Record(string level, object?[]? args)
    {
        var message = args?.OfType<string>().FirstOrDefault();
        if (message is null)
            return;

        var exception = args?.OfType<Exception>().FirstOrDefault();
        Append(exception is null
            ? $"[{level}] {message}"
            : $"[{level}] {message} ({exception.GetType().Name}: {exception.Message})");
    }

    private static void Append(string line)
    {
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();
        }
    }
}

/// <summary>
/// <see cref="IPluginLog"/> stand-in that forwards every call into <see cref="LiveEcDiagnostics"/>.
/// Returns <c>null</c> for everything, exactly as the previous no-op proxy did.
/// </summary>
internal class CapturingLog : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod?.Name;
        if (name is not null && !name.StartsWith("get_", StringComparison.Ordinal) &&
            !name.StartsWith("set_", StringComparison.Ordinal))
        {
            LiveEcDiagnostics.Record(name, args);
        }

        return null;
    }
}

/// <summary>
/// Captures the most recent popularity notification so the end-to-end check-flow test can assert
/// the threshold path fired.
/// </summary>
internal sealed class CapturingNotifier : INotifier, INotificationTarget
{
    public int Count;
    public DropOccurrence? LastDrop;
    public GlamPopularity? LastPopularity;

    public INotificationTarget CaptureTarget() => this;

    public void NotifyPopular(DropOccurrence drop, GlamPopularity popularity)
    {
        this.Count++;
        this.LastDrop = drop;
        this.LastPopularity = popularity;
    }
}

/// <summary>
/// Collection fixture for the live-EC tests: bootstraps the log and verifies EC is actually
/// reachable before any test runs. A sustained failure (after retries) fails fast — these are
/// blocking, highest-value end-to-end tests, so an unreachable EC is a hard failure, not a skip —
/// but it fails *diagnostically*: the probe response is classified (blocked vs. reachable-but-drifted)
/// and the captured client/transport log is attached, because every distinct cause otherwise
/// produces the same unactionable message.
/// </summary>
public sealed class LiveEcFixture
{
    private const string BaseUrl = "https://ffxiv.eorzeacollection.com";

    private const string UnreachableMessage =
        "The live Eorzea Collection probe failed after retries. The GoodGlam integration tests are " +
        "blocking, end-to-end tests that require live EC connectivity.";

    public LiveEcFixture()
    {
        LiveEc.EnsureLog();
        EnsureEcReachable();
    }

    private static void EnsureEcReachable()
    {
        var probe = EcFixtures.Reachability;
        var client = new EorzeaCollectionClient();

        LiveEcDiagnostics.Reset();

        EcItem? resolved = null;
        Exception? failure = null;
        try
        {
            // Probe the real transport (managed HTTP under Wine/Linux, curl.exe on native Windows)
            // exactly the way the plugin reaches EC.
            resolved = LiveEc.RetryAsync(
                ct => client.ResolveEcItemAsync(probe.Slot, probe.Name, probe.GameItemId, ct),
                result => result is not null).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (resolved is not null)
            return;

        throw new InvalidOperationException(BuildFailureMessage(probe), failure);
    }

    /// <summary>
    /// Assembles the actionable failure: the classified probe outcome plus the captured transport
    /// log (HTTP statuses, curl exit codes, record counts) that explains how it got there.
    /// </summary>
    private static string BuildFailureMessage(EcFixture probe)
    {
        var diagnosis = Diagnose(probe);
        return $"""
            {UnreachableMessage}

            Diagnosis: {diagnosis}

            EC client/transport log:
            {LiveEcDiagnostics.Snapshot()}
            """;
    }

    /// <summary>
    /// Re-runs the search once against the raw transport (bypassing the client's uniform <c>null</c>)
    /// so the response body can be classified. Only ever runs on the failure path, so the extra
    /// request costs nothing in a green run.
    /// </summary>
    private static string Diagnose(EcFixture probe)
    {
        try
        {
            var transport = EcTransportFactory.Create();

            // Mirrors EorzeaCollectionClient.ResolveEcItemAsync's request; kept local because the
            // point is to see the raw body the client throws away.
            var url = $"{BaseUrl}/gear/{probe.Slot.Key}/search";
            var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["search"] = probe.Name });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = transport.PostJsonAsync(url, body, cts.Token).GetAwaiter().GetResult();

            return EcProbeDiagnosis.Classify(response, probe.GameItemId).ToString();
        }
        catch (Exception ex)
        {
            return $"the diagnostic probe itself failed with {ex.GetType().Name}: {ex.Message}";
        }
    }
}

[CollectionDefinition(LiveEc.Collection)]
public sealed class LiveEcCollection : ICollectionFixture<LiveEcFixture>
{
}
