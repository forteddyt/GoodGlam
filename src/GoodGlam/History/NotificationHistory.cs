using System.Text.Json;

namespace GoodGlam.History;

/// <summary>
/// A single qualifying drop, captured for the browsable history window. Stored verbatim so the
/// row can be rendered (and its glamour reopened) long after the drop, across game sessions.
/// <see cref="ListingUrl"/> is the EC glamours listing for the item with the filters that were
/// active when the drop was logged, frozen so the row keeps linking to that same filtered view.
/// </summary>
public sealed record PopularDropRecord(
    uint ItemId,
    string ItemName,
    string Slot,
    int Loves,
    string? GlamName,
    string? GlamUrl,
    DateTimeOffset Timestamp,
    string? ListingUrl = null);

/// <summary>
/// Persists qualifying drops to a JSON file in the plugin config directory so history survives
/// across game sessions. Newest entries are kept at the front and the log is capped to avoid
/// unbounded growth. All access is synchronized so the loot/popularity threads and the UI thread
/// can read/write safely.
/// </summary>
public sealed class NotificationHistoryStore
{
    /// <summary>Maximum entries retained; older ones are pruned as new drops are logged.</summary>
    public const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object gate = new();
    private string filePath;
    private List<PopularDropRecord> records;

    public NotificationHistoryStore(string filePath)
    {
        this.filePath = filePath;
        this.records = Load(filePath);
    }

    /// <summary>
    /// Repoints this store at a different character's history file and reloads its records, so a
    /// single live store can follow the active character across logins without callers (the history
    /// window) needing a new reference. A null/empty path detaches to an empty, non-persisting state
    /// (used on the title screen where no character owns the history).
    /// </summary>
    public void Rebind(string? filePath)
    {
        lock (this.gate)
        {
            this.filePath = filePath ?? string.Empty;
            this.records = string.IsNullOrEmpty(this.filePath) ? [] : Load(this.filePath);
        }
    }

    /// <summary>Logs a qualifying drop (newest first), prunes to the cap, and persists.</summary>
    public void Add(PopularDropRecord record)
    {
        lock (this.gate)
        {
            this.records.Insert(0, record);
            if (this.records.Count > MaxEntries)
                this.records.RemoveRange(MaxEntries, this.records.Count - MaxEntries);
            this.Save();
        }
    }

    /// <summary>
    /// Identifies the character a queued drop belongs to (its history file at dispatch time), so a
    /// lookup that completes after a character switch still lands on the right character. An empty
    /// path means "detached" (title screen) and any drop captured then is dropped.
    /// </summary>
    public readonly record struct Binding(string? Path);

    /// <summary>Snapshots the currently bound character so an in-flight lookup can target it later.</summary>
    public Binding CaptureBinding()
    {
        lock (this.gate)
            return new Binding(string.IsNullOrEmpty(this.filePath) ? null : this.filePath);
    }

    /// <summary>
    /// Records a drop against the character captured in <paramref name="binding"/>, regardless of who
    /// is logged in now. Returns true when the drop landed on the still-active character (so the
    /// caller may raise the logo glow), false when it was written to a since-switched character's
    /// file or dropped because the originating character had detached.
    /// </summary>
    public bool AddTo(Binding binding, PopularDropRecord record)
    {
        if (string.IsNullOrEmpty(binding.Path))
            return false;

        lock (this.gate)
        {
            // Same character still active: update the live in-memory list (and any open window).
            if (binding.Path == this.filePath)
            {
                this.records.Insert(0, record);
                if (this.records.Count > MaxEntries)
                    this.records.RemoveRange(MaxEntries, this.records.Count - MaxEntries);
                this.Save();
                return true;
            }

            // Character switched since dispatch: append straight to the originating file so the entry
            // is still saved to its rightful owner, without disturbing the now-active character.
            AppendDirect(binding.Path, record);
            return false;
        }
    }

    /// <summary>Returns a point-in-time copy of the history (newest first) for rendering.</summary>
    public IReadOnlyList<PopularDropRecord> Snapshot()
    {
        lock (this.gate)
            return this.records.ToArray();
    }

    /// <summary>Empties the history and persists the cleared state.</summary>
    public void Clear()
    {
        lock (this.gate)
        {
            this.records.Clear();
            this.Save();
        }
    }

    private static List<PopularDropRecord> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<PopularDropRecord>>(json, Json) ?? [];
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "GoodGlam: failed to load notification history; starting empty.");
            return [];
        }
    }

    /// <summary>
    /// Appends a single record to a specific (not currently-bound) character's history file by
    /// loading it, inserting newest-first, pruning to the cap, and writing it back. Used when a
    /// lookup completes after the player has switched away from the character that saw the drop.
    /// </summary>
    private static void AppendDirect(string path, PopularDropRecord record)
    {
        try
        {
            var records = Load(path);
            records.Insert(0, record);
            if (records.Count > MaxEntries)
                records.RemoveRange(MaxEntries, records.Count - MaxEntries);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(records, Json));
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "GoodGlam: failed to append a late drop to its character's history.");
        }
    }

    private void Save()
    {
        // Detached (no active character / title screen): keep everything in memory only.
        if (string.IsNullOrEmpty(this.filePath))
            return;

        try
        {
            var dir = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(this.filePath, JsonSerializer.Serialize(this.records, Json));
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "GoodGlam: failed to save notification history.");
        }
    }
}
