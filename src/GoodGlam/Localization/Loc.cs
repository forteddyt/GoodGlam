namespace GoodGlam.Localization;

/// <summary>
/// Static accessor for the active <see cref="StringCatalog"/> — the plugin's user-facing copy.
/// Chosen as a static locator to match the existing <see cref="Services"/> pattern: call sites read
/// <c>Loc.Strings.Tabs.History</c> without threading a catalog through every constructor.
///
/// <see cref="Strings"/> lazily loads the default (<c>en</c>) catalog on first use, so pure types and
/// unit tests work without any explicit setup. <see cref="Initialize"/> eagerly loads a language at
/// plugin startup so a broken/missing template surfaces immediately on load rather than at the first
/// draw. Real per-culture language selection is intentionally deferred; today only <c>en</c> ships.
/// </summary>
public static class Loc
{
    private static StringCatalog? active;

    /// <summary>
    /// The active catalog. Lazily loads the default (<c>en</c>) template on first access so callers
    /// never see a null catalog even before <see cref="Initialize"/> runs.
    /// </summary>
    public static StringCatalog Strings => active ??= StringCatalog.LoadEmbedded();

    /// <summary>
    /// Loads the catalog for <paramref name="language"/> and makes it active. Called once at plugin
    /// startup so a missing/invalid template throws on load. Throws
    /// <see cref="InvalidOperationException"/> if the template can't be loaded (see
    /// <see cref="StringCatalog.LoadEmbedded"/>).
    /// </summary>
    /// <param name="language">The language code to load. Defaults to the shipped default.</param>
    public static void Initialize(string language = StringCatalog.DefaultLanguage)
        => active = StringCatalog.LoadEmbedded(language);

    /// <summary>
    /// Test seam: swaps the active catalog (or resets to lazy default with <see langword="null"/>).
    /// Lets tests exercise formatting/fallback logic against a crafted catalog without touching the
    /// embedded resource.
    /// </summary>
    internal static void OverrideForTest(StringCatalog? catalog) => active = catalog;
}
