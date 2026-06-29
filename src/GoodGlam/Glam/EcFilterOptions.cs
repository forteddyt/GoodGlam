namespace GoodGlam.Glam;

/// <summary>A selectable Eorzea Collection filter value paired with its display label.</summary>
public sealed record EcFilterOption(string Value, string Label);

/// <summary>
/// Single source of truth for the Eorzea Collection glamour filter dimensions, mirroring the
/// dropdowns/inputs on EC's /glamours page. Both the query builder (<see cref="EorzeaCollectionClient"/>)
/// and the settings UI (<c>ConfigWindow</c>) read these tables so they never drift apart.
///
/// EC sends each filter as <c>filter[name]=value</c> (race is the array form <c>filter[race][]=v</c>).
/// The first option in every list is the inert "any/none" default; selecting it leaves the
/// parameter off the request, keeping the unfiltered default equivalent to today's behavior.
/// </summary>
public static class EcFilterOptions
{
    public const int MinLevel = 1;
    public const int MaxLevel = 100;

    /// <summary>Lowest selectable EC tag/job value; first entry is the unfiltered default.</summary>
    public const string AnyGender = "any";
    public const string AnyDate = "any";
    public const string AnyJob = "all";

    public static readonly IReadOnlyList<EcFilterOption> Genders = new[]
    {
        new EcFilterOption("any", "All genders"),
        new EcFilterOption("male", "Male"),
        new EcFilterOption("female", "Female"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Races = new[]
    {
        new EcFilterOption("hyur", "Hyur"),
        new EcFilterOption("highlander", "Highlander"),
        new EcFilterOption("elezen", "Elezen"),
        new EcFilterOption("miqote", "Miqo'te"),
        new EcFilterOption("lalafell", "Lalafell"),
        new EcFilterOption("roegadyn", "Roegadyn"),
        new EcFilterOption("aura", "Au Ra"),
        new EcFilterOption("hrothgar", "Hrothgar"),
        new EcFilterOption("viera", "Viera"),
    };

    public static readonly IReadOnlyList<EcFilterOption> DatePeriods = new[]
    {
        new EcFilterOption("any", "All-time"),
        new EcFilterOption("today", "Today"),
        new EcFilterOption("yesterday", "Yesterday"),
        new EcFilterOption("this-week", "This Week"),
        new EcFilterOption("past-week", "Last Week"),
        new EcFilterOption("this-month", "This Month"),
        new EcFilterOption("past-month", "Last Month"),
        new EcFilterOption("this-year", "This Year"),
        new EcFilterOption("past-year", "Last Year"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Classifications = new[]
    {
        new EcFilterOption(string.Empty, "Any Classification"),
        new EcFilterOption("1", "Athletic"),
        new EcFilterOption("2", "Cool"),
        new EcFilterOption("3", "Cute"),
        new EcFilterOption("4", "Divine"),
        new EcFilterOption("5", "Elegant"),
        new EcFilterOption("6", "Fashionable"),
        new EcFilterOption("7", "Glamorous"),
        new EcFilterOption("11", "Heroic"),
        new EcFilterOption("8", "Sexy"),
        new EcFilterOption("13", "Strong"),
        new EcFilterOption("9", "Sweet"),
        new EcFilterOption("12", "Villainous"),
        new EcFilterOption("10", "Youthful"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Styles = new[]
    {
        new EcFilterOption(string.Empty, "Any Style"),
        new EcFilterOption("108", "Ancient"),
        new EcFilterOption("80", "Bohemian"),
        new EcFilterOption("81", "Casual"),
        new EcFilterOption("82", "Chic"),
        new EcFilterOption("83", "Cosplay"),
        new EcFilterOption("15", "Dark"),
        new EcFilterOption("115", "Earth"),
        new EcFilterOption("92", "Eastern"),
        new EcFilterOption("84", "Fantasy"),
        new EcFilterOption("112", "Fire"),
        new EcFilterOption("85", "Formal"),
        new EcFilterOption("95", "Futuristic"),
        new EcFilterOption("86", "Goth"),
        new EcFilterOption("93", "Grunge"),
        new EcFilterOption("109", "Historical"),
        new EcFilterOption("14", "Holy"),
        new EcFilterOption("113", "Ice"),
        new EcFilterOption("117", "Lightning"),
        new EcFilterOption("110", "Magical"),
        new EcFilterOption("87", "Medieval"),
        new EcFilterOption("88", "Modern"),
        new EcFilterOption("96", "Retro"),
        new EcFilterOption("111", "Spiritual"),
        new EcFilterOption("89", "Steampunk"),
        new EcFilterOption("94", "Tribal"),
        new EcFilterOption("90", "Vacation"),
        new EcFilterOption("91", "Vintage"),
        new EcFilterOption("114", "Water"),
        new EcFilterOption("116", "Wind"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Themes = new[]
    {
        new EcFilterOption(string.Empty, "Any Theme"),
        new EcFilterOption("32", "Academic Clothes"),
        new EcFilterOption("101", "Adventurer"),
        new EcFilterOption("35", "Autumn Outfit"),
        new EcFilterOption("20", "Battle Gear"),
        new EcFilterOption("22", "Crafter"),
        new EcFilterOption("106", "Cyber Outfit"),
        new EcFilterOption("104", "Desert Outfit"),
        new EcFilterOption("99", "Eternal Bonding"),
        new EcFilterOption("30", "Final Fantasy"),
        new EcFilterOption("37", "Forest Outfit"),
        new EcFilterOption("23", "Gatherer"),
        new EcFilterOption("24", "House Wear"),
        new EcFilterOption("21", "Idle Clothes"),
        new EcFilterOption("102", "Military Gear"),
        new EcFilterOption("105", "Nature Outfit"),
        new EcFilterOption("31", "Ninja"),
        new EcFilterOption("26", "Party Outfit"),
        new EcFilterOption("27", "Pirate"),
        new EcFilterOption("28", "Royalty"),
        new EcFilterOption("25", "Seasonal Event"),
        new EcFilterOption("100", "Special Occasions"),
        new EcFilterOption("97", "Sportswear"),
        new EcFilterOption("34", "Spring Outfit"),
        new EcFilterOption("33", "Summer Outfit"),
        new EcFilterOption("103", "Swimwear"),
        new EcFilterOption("107", "Wild West Outfit"),
        new EcFilterOption("36", "Winter Outfit"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Colors = new[]
    {
        new EcFilterOption(string.Empty, "Any Color"),
        new EcFilterOption("40", "Beige"),
        new EcFilterOption("41", "Black"),
        new EcFilterOption("42", "Blue"),
        new EcFilterOption("43", "Brown"),
        new EcFilterOption("53", "Gold"),
        new EcFilterOption("51", "Green"),
        new EcFilterOption("44", "Grey"),
        new EcFilterOption("63", "Metallic"),
        new EcFilterOption("62", "Monochromatic"),
        new EcFilterOption("66", "Neon"),
        new EcFilterOption("45", "Orange"),
        new EcFilterOption("64", "Pastel"),
        new EcFilterOption("46", "Pink"),
        new EcFilterOption("47", "Purple"),
        new EcFilterOption("48", "Red"),
        new EcFilterOption("52", "Silver"),
        new EcFilterOption("65", "Turquoise"),
        new EcFilterOption("67", "Vibrant"),
        new EcFilterOption("49", "White"),
        new EcFilterOption("50", "Yellow"),
    };

    public static readonly IReadOnlyList<EcFilterOption> Jobs = new[]
    {
        new EcFilterOption("all", "All Classes"),
        new EcFilterOption("tanks", "Tanks"),
        new EcFilterOption("healers", "Healers"),
        new EcFilterOption("melee", "Melee DPS"),
        new EcFilterOption("ranged", "Ranged DPS"),
        new EcFilterOption("casters", "Caster DPS"),
        new EcFilterOption("d-war", "Disciples of War"),
        new EcFilterOption("d-magic", "Disciples of Magic"),
        new EcFilterOption("d-war-mag", "Disciples of War and Magic"),
        new EcFilterOption("crafters", "Disciples of the Hand"),
        new EcFilterOption("gatherers", "Disciples of the Land"),
        new EcFilterOption("d-hnd-lnd", "Disciples of the Hand and Land"),
        new EcFilterOption("pld", "Paladin"),
        new EcFilterOption("war", "Warrior"),
        new EcFilterOption("drk", "Dark Knight"),
        new EcFilterOption("gnb", "Gunbreaker"),
        new EcFilterOption("whm", "White Mage"),
        new EcFilterOption("sch", "Scholar"),
        new EcFilterOption("ast", "Astrologian"),
        new EcFilterOption("sge", "Sage"),
        new EcFilterOption("mnk", "Monk"),
        new EcFilterOption("drg", "Dragoon"),
        new EcFilterOption("nin", "Ninja"),
        new EcFilterOption("sam", "Samurai"),
        new EcFilterOption("rpr", "Reaper"),
        new EcFilterOption("vpr", "Viper"),
        new EcFilterOption("brd", "Bard"),
        new EcFilterOption("mch", "Machinist"),
        new EcFilterOption("dnc", "Dancer"),
        new EcFilterOption("smn", "Summoner"),
        new EcFilterOption("blm", "Black Mage"),
        new EcFilterOption("rdm", "Red Mage"),
        new EcFilterOption("pct", "Pictomancer"),
        new EcFilterOption("blu", "Blue Mage"),
        new EcFilterOption("crp", "Carpenter"),
        new EcFilterOption("arm", "Armorer"),
        new EcFilterOption("ltw", "Leatherworker"),
        new EcFilterOption("alc", "Alchemist"),
        new EcFilterOption("bsm", "Blacksmith"),
        new EcFilterOption("gsm", "Goldsmith"),
        new EcFilterOption("wvr", "Weaver"),
        new EcFilterOption("cul", "Culinarian"),
        new EcFilterOption("btn", "Botanist"),
        new EcFilterOption("fsh", "Fisher"),
        new EcFilterOption("min", "Miner"),
    };
}
