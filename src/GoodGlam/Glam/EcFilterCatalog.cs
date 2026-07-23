using System.Text.Json;
using System.Text.Json.Serialization;
using GoodGlam.Localization;

namespace GoodGlam.Glam;

/// <summary>
/// Loaded catalog of the Eorzea Collection glamour filter dimensions, mirroring the dropdowns/inputs
/// on EC's /glamours page. Each dimension is a list of <see cref="EcFilterOption"/> pairs: a fixed EC
/// API <see cref="EcFilterOption.Value"/> and its display <see cref="EcFilterOption.Label"/>.
///
/// The two halves live in two separate resources by design. The <b>values</b> — which are transmitted
/// to Eorzea Collection as <c>filter[name]=value</c> (race is the array form <c>filter[race][]=v</c>)
/// and must never be translated — stay in the embedded <c>EcFilterOptions.json</c>. The <b>labels</b>
/// are user-facing copy and live in the string catalog (<see cref="FilterOptionStrings"/>).
/// <see cref="LoadEmbedded"/> zips them by index, so the two files stay aligned or the load fails loudly.
///
/// The first option in every list is the inert "any/none" default; selecting it leaves the parameter
/// off the request, keeping the unfiltered default equivalent to today's behavior. Behavioral
/// constants (level bounds and the inert sentinels) stay in <see cref="EcFilterOptions"/>.
/// </summary>
public sealed class EcFilterCatalog
{
    /// <summary>Embedded resource holding the fixed EC API values; see <c>GoodGlam.csproj</c>.</summary>
    internal const string ResourceName = "GoodGlam.Glam.EcFilterOptions.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public EcFilterCatalog(
        IReadOnlyList<EcFilterOption> genders,
        IReadOnlyList<EcFilterOption> races,
        IReadOnlyList<EcFilterOption> datePeriods,
        IReadOnlyList<EcFilterOption> classifications,
        IReadOnlyList<EcFilterOption> styles,
        IReadOnlyList<EcFilterOption> themes,
        IReadOnlyList<EcFilterOption> colors,
        IReadOnlyList<EcFilterOption> jobs)
    {
        this.Genders = genders;
        this.Races = races;
        this.DatePeriods = datePeriods;
        this.Classifications = classifications;
        this.Styles = styles;
        this.Themes = themes;
        this.Colors = colors;
        this.Jobs = jobs;
    }

    public IReadOnlyList<EcFilterOption> Genders { get; }

    public IReadOnlyList<EcFilterOption> Races { get; }

    public IReadOnlyList<EcFilterOption> DatePeriods { get; }

    public IReadOnlyList<EcFilterOption> Classifications { get; }

    public IReadOnlyList<EcFilterOption> Styles { get; }

    public IReadOnlyList<EcFilterOption> Themes { get; }

    public IReadOnlyList<EcFilterOption> Colors { get; }

    public IReadOnlyList<EcFilterOption> Jobs { get; }

    /// <summary>
    /// Loads the catalog by zipping the fixed EC API values (from the embedded <see cref="ResourceName"/>
    /// JSON) with their display labels (from the string catalog). Both ship with the assembly, so a
    /// missing/empty/invalid values payload or a values/labels count mismatch is a build error rather
    /// than a runtime condition; this surfaces those as a clear <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="strings">The label source; defaults to the active catalog (<see cref="Loc.Strings"/>).</param>
    public static EcFilterCatalog LoadEmbedded(StringCatalog? strings = null)
    {
        var labels = (strings ?? Loc.Strings).FilterOptions;

        var assembly = typeof(EcFilterCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded filter-options resource '{ResourceName}' was not found in {assembly.FullName}.");

        var values = JsonSerializer.Deserialize<EcFilterValues>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Embedded filter-options resource '{ResourceName}' deserialized to null.");

        return new EcFilterCatalog(
            Zip("genders", values.Genders, labels.Genders),
            Zip("races", values.Races, labels.Races),
            Zip("datePeriods", values.DatePeriods, labels.DatePeriods),
            Zip("classifications", values.Classifications, labels.Classifications),
            Zip("styles", values.Styles, labels.Styles),
            Zip("themes", values.Themes, labels.Themes),
            Zip("colors", values.Colors, labels.Colors),
            Zip("jobs", values.Jobs, labels.Jobs));
    }

    /// <summary>
    /// Pairs each fixed EC API value with its display label by index. Throws when a dimension's value
    /// and label counts differ, so <c>EcFilterOptions.json</c> and the string catalog can never drift
    /// out of alignment unnoticed.
    /// </summary>
    private static IReadOnlyList<EcFilterOption> Zip(
        string dimension, IReadOnlyList<string> values, IReadOnlyList<string> labels)
    {
        if (values.Count != labels.Count)
            throw new InvalidOperationException(
                $"EC filter dimension '{dimension}' has {values.Count} value(s) but {labels.Count} label(s); " +
                "the values in EcFilterOptions.json and the labels in the string catalog must be index-aligned.");

        var options = new EcFilterOption[values.Count];
        for (var i = 0; i < values.Count; i++)
            options[i] = new EcFilterOption(values[i], labels[i]);

        return options;
    }

    /// <summary>DTO for the values-only <see cref="ResourceName"/> payload (fixed EC API tokens per dimension).</summary>
    private sealed class EcFilterValues
    {
        [JsonConstructor]
        public EcFilterValues(
            IReadOnlyList<string> genders,
            IReadOnlyList<string> races,
            IReadOnlyList<string> datePeriods,
            IReadOnlyList<string> classifications,
            IReadOnlyList<string> styles,
            IReadOnlyList<string> themes,
            IReadOnlyList<string> colors,
            IReadOnlyList<string> jobs)
        {
            this.Genders = genders;
            this.Races = races;
            this.DatePeriods = datePeriods;
            this.Classifications = classifications;
            this.Styles = styles;
            this.Themes = themes;
            this.Colors = colors;
            this.Jobs = jobs;
        }

        public IReadOnlyList<string> Genders { get; }

        public IReadOnlyList<string> Races { get; }

        public IReadOnlyList<string> DatePeriods { get; }

        public IReadOnlyList<string> Classifications { get; }

        public IReadOnlyList<string> Styles { get; }

        public IReadOnlyList<string> Themes { get; }

        public IReadOnlyList<string> Colors { get; }

        public IReadOnlyList<string> Jobs { get; }
    }
}
