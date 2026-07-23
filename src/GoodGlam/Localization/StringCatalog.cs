using System.Text.Json;

namespace GoodGlam.Localization;

/// <summary>
/// The full set of user-facing strings for one language, deserialized from the embedded
/// <c>Strings.&lt;lang&gt;.jsonc</c> template at startup. Call sites read the copy through the typed
/// tree (e.g. <c>Loc.Strings.History.EmptyState</c>) instead of holding literals, so a missing or
/// mistyped key is a compile error — important because the plugin's ImGui UI can't run in CI.
///
/// <b>The template is the single source of truth.</b> The typed property tree and the nested
/// <c>XxxStrings</c> group classes are <b>generated</b> from <c>Strings.en.jsonc</c> by the
/// <c>GoodGlam.SourceGen</c> Roslyn source generator (see <c>StringCatalog.g.cs</c> in the build
/// output), so contributors edit only the template (JSONC — comments and trailing commas allowed) and
/// the accessors regenerate on build. This hand-written partial holds just the loader and the members
/// that aren't a plain 1:1 map of the JSON shape.
///
/// Only <b>display text</b> lives in the template. Values transmitted to Eorzea Collection (filter
/// values, slot keys, URL fragments) are fixed API tokens that stay in code and never appear in a
/// template — see <see cref="Glam.EcFilterCatalog"/> and <see cref="Glam.GlamSlot"/>.
/// </summary>
public sealed partial class StringCatalog
{
    /// <summary>Resource-name format for the embedded per-language template; <c>{0}</c> is the language code.</summary>
    internal const string ResourceNameFormat = "GoodGlam.Localization.Strings.{0}.jsonc";

    /// <summary>The default language code.</summary>
    internal const string DefaultLanguage = "en";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,

        // The template is JSONC (Strings.<lang>.jsonc): tolerate // and /* */ comments and trailing
        // commas so contributors can annotate the copy. Kept in sync with the build-time generator,
        // which parses the same file with the equivalent JsonDocumentOptions.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Display labels for each equipment slot, keyed by the fixed Eorzea Collection slot key
    /// (<see cref="Glam.GlamSlot.Key"/>). Hand-authored here (rather than generated) so it can be looked
    /// up by key via <see cref="SlotLabel"/>; only the label is translatable, the key stays in code.
    /// </summary>
    public IReadOnlyDictionary<string, string> Slots { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The human-friendly label for a slot key, falling back to the key itself when the catalog has no
    /// entry (matching the historical behavior of <see cref="Glam.GlamSlot.Label"/>).
    /// </summary>
    public string SlotLabel(string slotKey)
        => this.Slots.TryGetValue(slotKey, out var label) && !string.IsNullOrEmpty(label) ? label : slotKey;

    /// <summary>
    /// Loads the string catalog for the given language from its embedded <c>Strings.&lt;lang&gt;.json</c>
    /// resource. The template ships in the assembly, so a missing/empty/invalid payload is a build error
    /// rather than a runtime condition; this surfaces that as a clear <see cref="InvalidOperationException"/>
    /// (mirroring <see cref="Glam.EcFilterCatalog.LoadEmbedded"/>).
    /// </summary>
    /// <param name="language">The language code (e.g. <c>en</c>). Defaults to <see cref="DefaultLanguage"/>.</param>
    public static StringCatalog LoadEmbedded(string language = DefaultLanguage)
    {
        var resourceName = string.Format(ResourceNameFormat, language);
        var assembly = typeof(StringCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded string catalog '{resourceName}' was not found in {assembly.FullName}.");

        return JsonSerializer.Deserialize<StringCatalog>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Embedded string catalog '{resourceName}' deserialized to null.");
    }
}
