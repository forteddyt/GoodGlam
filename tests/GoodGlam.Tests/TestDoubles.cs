using System.Reflection;
using Dalamud.Plugin.Services;
using GoodGlam.Glam;
using GoodGlam.Loot;
using GoodGlam.Windows;

namespace GoodGlam.Tests;

/// <summary>
/// In-memory <see cref="IGlamSource"/> for deterministic popularity tests. Records calls so
/// tests can assert caching (the source should not be hit twice for a cached item).
/// </summary>
internal sealed class FakeGlamSource : IGlamSource
{
    public int ResolveCalls;
    public int PopularityCalls;
    public EcItem? EcItem = new(14930, "X", 25430);
    public GlamPopularity Popularity = new(0, null);
    public Exception? Throw;
    public PopularityFilters? LastFilters;

    public Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct)
    {
        this.ResolveCalls++;
        if (this.Throw is not null) throw this.Throw;
        return Task.FromResult(this.EcItem);
    }

    public Task<GlamPopularity> GetTopPopularityAsync(GlamSlot slot, int ecId, PopularityFilters filters, CancellationToken ct)
    {
        this.PopularityCalls++;
        this.LastFilters = filters;
        if (this.Throw is not null) throw this.Throw;
        return Task.FromResult(this.Popularity);
    }
}

/// <summary>Captures the most recent notification so threshold tests can assert it fired.</summary>
internal sealed class FakeNotifier : INotifier, INotificationTarget
{
    public int Count;
    public int CaptureCalls;
    public DropItem? LastDrop;
    public GlamPopularity? LastPopularity;

    public INotificationTarget CaptureTarget()
    {
        this.CaptureCalls++;
        return this;
    }

    public void NotifyPopular(DropItem drop, GlamPopularity popularity)
    {
        this.Count++;
        this.LastDrop = drop;
        this.LastPopularity = popularity;
    }
}

/// <summary>Scripted <see cref="ILootReader"/>: returns a fixed snapshot and counts reads.</summary>
internal sealed class StubLootReader : ILootReader
{
    public int ReadCalls;
    public LootSnapshot? Snapshot;

    public StubLootReader(LootSnapshot? snapshot = null) => this.Snapshot = snapshot;

    public LootSnapshot? Read()
    {
        this.ReadCalls++;
        return this.Snapshot;
    }
}

/// <summary>Records URLs passed to <see cref="ILinkOpener.Open"/> so link-open effects are assertable.</summary>
internal sealed class FakeLinkOpener : ILinkOpener
{
    public readonly List<string> Opened = new();

    public void Open(string url) => this.Opened.Add(url);
}

/// <summary>Scripted <see cref="IEcTransport"/> returning queued bodies and recording requests.</summary>
internal sealed class FakeTransport : IEcTransport
{
    public readonly List<string> PostUrls = new();
    public readonly List<string> PostBodies = new();
    public readonly List<string> GetUrls = new();
    public string? PostResult;
    public string? GetResult;

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        this.PostUrls.Add(url);
        this.PostBodies.Add(jsonBody);
        return Task.FromResult(this.PostResult);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        this.GetUrls.Add(url);
        return Task.FromResult(this.GetResult);
    }
}

/// <summary>One-shot transport: returns a fixed value and counts calls (for fallback tests).</summary>
internal sealed class CountingTransport(string? result) : IEcTransport
{
    public int Calls;

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        this.Calls++;
        return Task.FromResult(result);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        this.Calls++;
        return Task.FromResult(result);
    }
}

/// <summary>
/// Installs no-op or fake Dalamud services into the static <c>Services</c> holder so code paths
/// that touch <c>Services.*</c> don't dereference null statics under test.
/// </summary>
internal static class TestServices
{
    private static bool initialized;

    public static void EnsureLog()
    {
        if (initialized) return;
        var log = DispatchProxy.Create<IPluginLog, NoopLog>();
        Install("Log", log);
        initialized = true;
    }

    /// <summary>Sets a static property on <c>GoodGlam.Services</c> (e.g. Framework, Notifications).</summary>
    public static void Install<T>(string property, T value)
    {
        typeof(GoodGlam.Services).GetProperty(property, BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, value);
    }

    /// <summary>
    /// Clears every installed Dalamud service except <c>Log</c> from the static holder. The holder is
    /// process-global with no per-class isolation, so a class that installs a broad set of fakes (e.g.
    /// <c>PluginTests</c>) should reset here in teardown to avoid leaking them into later-running
    /// classes and making outcomes depend on xUnit's execution order. <c>Log</c> stays installed
    /// because <see cref="EnsureLog"/> seeds it once behind an idempotency guard.
    /// </summary>
    public static void ResetServices()
    {
        string[] services =
        [
            "PluginInterface", "Commands", "DataManager", "ClientState", "Condition", "GameGui",
            "PlayerState", "AddonLifecycle", "Notifications", "Framework", "TextureProvider",
        ];

        foreach (var name in services)
            Install<object?>(name, null);
    }
}

internal class NoopLog : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}
