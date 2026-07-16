namespace GoodGlam.Windows;

/// <summary>
/// The pure, ImGui-free logic behind the About tab (<see cref="AboutTab"/>): turn the plugin's
/// assembly <see cref="Version"/> into a friendly <c>v</c>-prefixed string, provide the ordered
/// in-plugin tutorial copy, and open the repository URL through the shared <see cref="ILinkOpener"/>
/// seam. Split out so the display decisions are unit-testable without a live ImGui context.
/// </summary>
internal static class AboutInfo
{
    private static readonly Version LocalBuildSentinelVersion = new(0, 0, 0, 0);

    internal static readonly IReadOnlyList<string> HowItWorksSteps =
    [
        "GoodGlam watches items in the Need/Greed/Pass roll window.",
        "It checks Eorzea Collection for popular glam outfits that use each item, using your threshold and filters.",
        "Qualifying drops light the floating logo and are saved in History, where you can preview and open matching glams.",
    ];

    /// <summary>
    /// Formats the plugin version for display as <c>v</c> followed by the full version, keeping every
    /// component the assembly version defines (e.g. <c>0.1.0.0</c> renders as <c>v0.1.0.0</c>). The
    /// local/source-build sentinel <c>0.0.0.0</c> is labeled as <c>(local build)</c>. A null version
    /// yields <c>v(unknown)</c>.
    /// </summary>
    internal static string FormatVersion(Version? version)
        => version switch
        {
            null => "v(unknown)",
            _ when version == LocalBuildSentinelVersion => $"v{version} (local build)",
            _ => $"v{version}",
        };

    /// <summary>Opens the plugin's GitHub repository page. Split from the link widget so the URL choice is testable.</summary>
    internal static void OpenRepo(ILinkOpener linkOpener, string repoUrl) => linkOpener.Open(repoUrl);
}
