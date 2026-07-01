using System.Text.Json;
using GoodGlam.Diagnostics;
using GoodGlam.History;

namespace GoodGlam;

/// <summary>
/// Owns the per-character storage layout and swaps the live <see cref="Configuration"/>,
/// <see cref="NotificationHistoryStore"/>, and unseen-drop glow over to whichever character is
/// logged in. Each character gets an isolated folder under
/// <c>ConfigDirectory/characters/&lt;contentId&gt;/</c> holding its own <c>config.json</c>,
/// <c>history.json</c> and a readability-only <c>meta.json</c>.
///
/// Rather than rebuilding the window/service object graph on every login, the single live config
/// and history instances are re-bound in place (<see cref="Configuration.CopyFrom"/> /
/// <see cref="NotificationHistoryStore.Rebind"/>), so every holder keeps its existing reference.
///
/// Deliberately free of <see cref="Services"/> lookups in its core logic (the paths and the live
/// instances are injected) so it can be unit-tested without the Dalamud framework.
/// </summary>
public sealed class CharacterDataManager
{
    private const string ConfigFileName = "config.json";
    private const string HistoryFileName = "history.json";
    private const string MetaFileName = "meta.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string charactersRoot;
    private readonly Configuration liveConfig;
    private readonly NotificationHistoryStore liveHistory;
    private readonly NotificationState notificationState;
    private readonly ITraceLogger<CharacterDataManager> log;

    private ulong activeContentId;

    /// <param name="charactersRoot">The <c>characters/</c> directory under the plugin config dir.</param>
    /// <param name="liveConfig">The single configuration instance shared by every window/service.</param>
    /// <param name="liveHistory">The single history store shared by the history window and notifier.</param>
    /// <param name="notificationState">The shared unseen-drop glow latch, reset on deactivate.</param>
    /// <param name="log">Component logger; defaults to a real one when not supplied (tests can fake it).</param>
    public CharacterDataManager(
        string charactersRoot,
        Configuration liveConfig,
        NotificationHistoryStore liveHistory,
        NotificationState notificationState,
        ITraceLogger<CharacterDataManager>? log = null)
    {
        this.charactersRoot = charactersRoot;
        this.liveConfig = liveConfig;
        this.liveHistory = liveHistory;
        this.notificationState = notificationState;
        this.log = log ?? new TraceLogger<CharacterDataManager>();
    }

    /// <summary>Content id of the character whose data is currently loaded; 0 when none.</summary>
    public ulong ActiveContentId => this.activeContentId;

    /// <summary>True while a character's data is loaded (i.e. logged in).</summary>
    public bool IsActive => this.activeContentId != 0;

    /// <summary>
    /// Loads <paramref name="contentId"/>'s config + history into the live instances, seeding a
    /// fresh per-character folder on first sight. A zero content id is treated as "not logged in"
    /// and deactivates instead.
    /// </summary>
    public void Activate(ulong contentId, string? name, string? world)
    {
        if (contentId == 0)
        {
            this.Deactivate();
            return;
        }

        var folder = Path.Combine(this.charactersRoot, contentId.ToString("x16"));
        var configStore = new ConfigurationStore(Path.Combine(folder, ConfigFileName));
        var historyPath = Path.Combine(folder, HistoryFileName);

        var hadConfig = configStore.Exists();
        var loaded = configStore.Load();
        this.liveConfig.CopyFrom(loaded);
        this.liveConfig.SaveSink = configStore.Save;

        // Persist immediately so a brand-new character's folder + config.json exist from the start.
        configStore.Save(this.liveConfig);

        this.liveHistory.Rebind(historyPath);
        this.WriteMeta(folder, contentId, name, world);

        this.activeContentId = contentId;
        this.log.Debug(
            $"activated contentId={contentId:x16} (name={name ?? "(unknown)"}, world={world ?? "(unknown)"}) " +
            $"from '{folder}' — {(hadConfig ? "loaded existing config" : "seeded a new config")}.");
    }

    /// <summary>
    /// Detaches from the active character: resets the live config to defaults with saving disabled
    /// (title-screen edits become no-ops), empties the live history, and clears the unseen-drop glow
    /// so a pending drop from the character logging out can't carry over to the next one (whose
    /// history doesn't contain it). Idempotent.
    /// </summary>
    public void Deactivate()
    {
        this.liveConfig.CopyFrom(new Configuration());
        this.liveConfig.SaveSink = null;
        this.liveHistory.Rebind(null);
        this.notificationState.Clear();
        if (this.activeContentId != 0)
            this.log.Debug($"deactivated contentId={this.activeContentId:x16}; reset to defaults.");
        this.activeContentId = 0;
    }

    private void WriteMeta(string folder, ulong contentId, string? name, string? world)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var meta = new CharacterMeta(contentId, name, world);
            File.WriteAllText(Path.Combine(folder, MetaFileName), JsonSerializer.Serialize(meta, Json));
        }
        catch (Exception ex)
        {
            this.log.Warning("failed to write character meta.json.", ex);
        }
    }

    /// <summary>Readability-only sidecar so a character folder is identifiable without the game running.</summary>
    private sealed record CharacterMeta(ulong ContentId, string? Name, string? World);
}
