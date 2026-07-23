using GoodGlam.Localization;

namespace GoodGlam.Windows;

/// <summary>
/// The pure, ImGui-free logic behind the About tab (<see cref="AboutTab"/>): turn the plugin's
/// assembly <see cref="Version"/> into a friendly <c>v</c>-prefixed string, provide the ordered
/// in-plugin tutorial copy, and open the repository URL through the shared <see cref="ILinkOpener"/>
/// seam. Split out so the display decisions are unit-testable without a live ImGui context. Display
/// copy is sourced from the active string catalog (<see cref="Loc.Strings"/>).
/// </summary>
internal static class AboutInfo
{
    private static readonly Version LocalBuildSentinelVersion = new(0, 0, 0, 0);

    internal static IReadOnlyList<string> HowItWorksSteps => Loc.Strings.About.HowItWorksSteps;

    /// <summary>
    /// Formats the plugin version for display as <c>v</c> followed by the full version, keeping every
    /// component the assembly version defines (e.g. <c>0.1.0.0</c> renders as <c>v0.1.0.0</c>). The
    /// local/source-build sentinel <c>0.0.0.0</c> is labeled as <c>(local build)</c>. A null version
    /// yields <c>v(unknown)</c>. The surrounding copy comes from the string catalog.
    /// </summary>
    internal static string FormatVersion(Version? version)
        => version switch
        {
            null => Loc.Strings.About.VersionUnknown,
            _ when version == LocalBuildSentinelVersion
                => string.Format(Loc.Strings.About.VersionLocalBuildFormat, version),
            _ => string.Format(Loc.Strings.About.VersionFormat, version),
        };

    /// <summary>Opens the plugin's GitHub repository page. Split from the link widget so the URL choice is testable.</summary>
    internal static void OpenRepo(ILinkOpener linkOpener, string repoUrl) => linkOpener.Open(repoUrl);
}
