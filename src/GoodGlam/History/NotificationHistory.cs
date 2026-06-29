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

    private readonly string filePath;
    private readonly object gate = new();
    private readonly List<PopularDropRecord> records;

    public NotificationHistoryStore(string filePath)
    {
        this.filePath = filePath;
        this.records = Load(filePath);
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

    private void Save()
    {
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
