using System.Reflection;
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
/// Shared helpers for the live Eorzea Collection tests: a no-op Dalamud log bootstrap and a
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
    /// Installs a no-op <see cref="IPluginLog"/> into the plugin's static <c>Services.Log</c> so the
    /// EC client and transport (which log freely) don't dereference a null static under test.
    /// </summary>
    public static void EnsureLog()
    {
        if (logInstalled)
            return;

        var log = DispatchProxy.Create<IPluginLog, NoopLog>();
        typeof(GoodGlam.Services).GetProperty("Log", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, log);
        logInstalled = true;
    }

    /// <summary>
    /// Runs a live EC call with bounded exponential backoff, retrying until <paramref name="isGood"/>
    /// accepts the result or attempts are exhausted, so only a sustained failure reaches the assertion.
    /// Each attempt is hard-bounded by <see cref="PerAttemptTimeout"/>. The final (possibly "bad")
    /// result is returned so the caller's assertion produces the failure message.
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
            }
            catch (Exception) when (attempt < MaxAttempts)
            {
                // Transient failure — fall through to the backoff delay and try again.
            }

            if (attempt < MaxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt)).ConfigureAwait(false);
        }

        return result;
    }
}

internal class NoopLog : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}

/// <summary>
/// Captures the most recent popularity notification so the end-to-end check-flow test can assert
/// the threshold path fired.
/// </summary>
internal sealed class CapturingNotifier : INotifier
{
    public int Count;
    public DropItem? LastDrop;
    public GlamPopularity? LastPopularity;

    public void NotifyPopular(DropItem drop, GlamPopularity popularity)
    {
        this.Count++;
        this.LastDrop = drop;
        this.LastPopularity = popularity;
    }
}

/// <summary>
/// Collection fixture for the live-EC tests: bootstraps the log and verifies EC is actually
/// reachable before any test runs. A sustained outage (after retries) fails fast with a clear
/// message — these are blocking, highest-value end-to-end tests, so an unreachable EC is a hard
/// failure, not a skip.
/// </summary>
public sealed class LiveEcFixture
{
    private const string UnreachableMessage =
        "Eorzea Collection was unreachable after retries. The GoodGlam integration tests are " +
        "blocking, end-to-end tests that require live EC connectivity. Check network/Cloudflare " +
        "access and re-run (or run them manually if EC itself is down).";

    public LiveEcFixture()
    {
        LiveEc.EnsureLog();
        EnsureEcReachable();
    }

    private static void EnsureEcReachable()
    {
        var probe = EcFixtures.Reachability;
        var client = new EorzeaCollectionClient();

        EcItem? resolved;
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
            throw new InvalidOperationException(UnreachableMessage, ex);
        }

        if (resolved is null)
            throw new InvalidOperationException(UnreachableMessage);
    }
}

[CollectionDefinition(LiveEc.Collection)]
public sealed class LiveEcCollection : ICollectionFixture<LiveEcFixture>
{
}
