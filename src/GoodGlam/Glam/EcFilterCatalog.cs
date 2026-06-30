using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoodGlam.Glam;

/// <summary>
/// Loaded catalog of the Eorzea Collection glamour filter dimensions, mirroring the dropdowns/inputs
/// on EC's /glamours page. The option lists live in the embedded <c>EcFilterOptions.json</c> resource
/// (the single source of truth for the selectable values), so the settings UI (<c>SettingsTab</c>)
/// reads them from one place and they never drift apart.
///
/// EC sends each filter as <c>filter[name]=value</c> (race is the array form <c>filter[race][]=v</c>).
/// The first option in every list is the inert "any/none" default; selecting it leaves the parameter
/// off the request, keeping the unfiltered default equivalent to today's behavior. Behavioral
/// constants (level bounds and the inert sentinels) stay in <see cref="EcFilterOptions"/>.
/// </summary>
public sealed class EcFilterCatalog
{
    /// <summary>Embedded resource holding the option lists; see <c>GoodGlam.csproj</c>.</summary>
    internal const string ResourceName = "GoodGlam.Glam.EcFilterOptions.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [JsonConstructor]
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
    /// Loads the catalog from the embedded <see cref="ResourceName"/> JSON. The resource ships in the
    /// assembly, so a missing/empty/invalid payload is a build error rather than a runtime condition;
    /// this surfaces that as a clear <see cref="InvalidOperationException"/>.
    /// </summary>
    public static EcFilterCatalog LoadEmbedded()
    {
        var assembly = typeof(EcFilterCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded filter-options resource '{ResourceName}' was not found in {assembly.FullName}.");

        var catalog = JsonSerializer.Deserialize<EcFilterCatalog>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Embedded filter-options resource '{ResourceName}' deserialized to null.");

        return catalog;
    }
}
