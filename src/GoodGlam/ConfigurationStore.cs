using System.Text.Json;
using GoodGlam.Diagnostics;

namespace GoodGlam;

/// <summary>
/// Reads and writes a single character's <see cref="Configuration"/> as JSON in that character's
/// folder. Mirrors <see cref="GoodGlam.History.NotificationHistoryStore"/>: per-character config
/// lives under the plugin's own layout rather than Dalamud's single global config file, so each
/// character keeps an isolated settings blob. Tolerant of a missing or corrupt file (falls back to
/// defaults) so a first run or a hand-mangled file never throws on load.
/// </summary>
public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string filePath;
    private readonly ITraceLogger<ConfigurationStore> log;

    public ConfigurationStore(string filePath, ITraceLogger<ConfigurationStore>? log = null)
    {
        this.filePath = filePath;
        this.log = log ?? new TraceLogger<ConfigurationStore>();
    }

    /// <summary>The JSON file this store reads from and writes to.</summary>
    public string FilePath => this.filePath;

    /// <summary>True when this character already has a persisted config on disk.</summary>
    public bool Exists() => File.Exists(this.filePath);

    /// <summary>
    /// Loads the character's settings, returning a defaults instance when the file is absent or
    /// unreadable. The returned object has no <see cref="Configuration.SaveSink"/> wired; callers
    /// own that binding.
    /// </summary>
    public Configuration Load()
    {
        try
        {
            if (!File.Exists(this.filePath))
                return new Configuration();

            var json = File.ReadAllText(this.filePath);
            var config = JsonSerializer.Deserialize<Configuration>(json, Json);
            if (config is null)
                return new Configuration();

            config.Filters ??= new();
            config.Slots ??= new();
            return config;
        }
        catch (Exception ex)
        {
            this.log.Warning($"failed to load character config from '{this.filePath}'; using defaults.", ex);
            return new Configuration();
        }
    }

    /// <summary>Persists the given settings to this character's config file.</summary>
    public void Save(Configuration config)
    {
        try
        {
            var dir = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(this.filePath, JsonSerializer.Serialize(config, Json));
        }
        catch (Exception ex)
        {
            this.log.Warning($"failed to save character config to '{this.filePath}'.", ex);
        }
    }
}
