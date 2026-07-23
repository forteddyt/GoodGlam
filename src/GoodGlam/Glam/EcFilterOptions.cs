namespace GoodGlam.Glam;

/// <summary>A selectable Eorzea Collection filter value paired with its display label.</summary>
public sealed record EcFilterOption(string Value, string Label);

/// <summary>
/// Behavioral constants for the Eorzea Collection glamour filters: the level bounds and the inert
/// "any/none" sentinel values. <see cref="PopularityFilters"/> uses these for its defaults and to
/// decide which <c>filter[...]</c> params to omit, so they live in code where they are referenced at
/// compile time.
///
/// The selectable option <b>values</b> (genders, races, styles, ...) are fixed EC API tokens and live
/// in the embedded <c>EcFilterOptions.json</c> resource; their display <b>labels</b> live in the string
/// catalog. <see cref="EcFilterCatalog"/> zips the two and surfaces the combined options. The first
/// entry of each list is the inert default below.
/// </summary>
public static class EcFilterOptions
{
    public const int MinLevel = 1;
    public const int MaxLevel = 100;

    /// <summary>Inert "any/none" defaults; selecting them leaves the EC parameter off the request.</summary>
    public const string AnyGender = "any";
    public const string AnyDate = "any";
    public const string AnyJob = "all";
}
