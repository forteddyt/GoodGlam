namespace GoodGlam.Glam;

/// <summary>
/// Global, EC-parity filter selections applied to every popularity lookup. Defaults mirror
/// EC's "any/none" state so an unconfigured plugin behaves exactly like the unfiltered check.
///
/// Values are stored as EC's own raw query values (see <see cref="EcFilterOptions"/>), so the
/// model maps straight onto <c>filter[...]</c> params with no translation table at request time.
/// </summary>
[Serializable]
public sealed class PopularityFilters
{
    public string Gender { get; set; } = EcFilterOptions.AnyGender;

    public List<string> Races { get; set; } = [];

    public string Classification { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string Theme { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string Job { get; set; } = EcFilterOptions.AnyJob;

    public int MinLevel { get; set; } = EcFilterOptions.MinLevel;

    public int MaxLevel { get; set; } = EcFilterOptions.MaxLevel;

    public string DatePeriod { get; set; } = EcFilterOptions.AnyDate;

    public bool ExcludeMogstation { get; set; }

    public bool ExcludeSeasonal { get; set; }

    /// <summary>
    /// Yields the active EC <c>filter[...]</c> params (name, value), omitting any left at its
    /// inert default so the request stays equivalent to today's unfiltered call when nothing is set.
    /// Race is emitted once per selection using EC's array form (<c>race[]</c>).
    /// </summary>
    public IEnumerable<(string Name, string Value)> ActiveParams()
    {
        if (!string.IsNullOrEmpty(this.Gender) && this.Gender != EcFilterOptions.AnyGender)
            yield return ("gender", this.Gender);

        foreach (var race in this.Races)
        {
            if (!string.IsNullOrWhiteSpace(race))
                yield return ("race[]", race);
        }

        if (!string.IsNullOrEmpty(this.Classification))
            yield return ("class", this.Classification);

        if (!string.IsNullOrEmpty(this.Style))
            yield return ("style", this.Style);

        if (!string.IsNullOrEmpty(this.Theme))
            yield return ("theme", this.Theme);

        if (!string.IsNullOrEmpty(this.Color))
            yield return ("color", this.Color);

        if (!string.IsNullOrEmpty(this.Job) && this.Job != EcFilterOptions.AnyJob)
            yield return ("job", this.Job);

        if (this.MinLevel > EcFilterOptions.MinLevel)
            yield return ("minimumLvl", this.MinLevel.ToString());

        if (this.MaxLevel < EcFilterOptions.MaxLevel)
            yield return ("maximumLvl", this.MaxLevel.ToString());

        if (!string.IsNullOrEmpty(this.DatePeriod) && this.DatePeriod != EcFilterOptions.AnyDate)
            yield return ("datePeriod", this.DatePeriod);

        if (this.ExcludeMogstation)
            yield return ("excludeMogstation", "1");

        if (this.ExcludeSeasonal)
            yield return ("excludeSeasonal", "1");
    }

    /// <summary>
    /// Stable string identifying the active filter set, used to key the popularity cache so a
    /// filter change is treated as a distinct lookup rather than reusing a stale result.
    /// </summary>
    public string Signature()
        => string.Join("&", this.ActiveParams().Select(p => $"{p.Name}={p.Value}"));
}
