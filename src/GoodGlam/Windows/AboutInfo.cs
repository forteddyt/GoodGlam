namespace GoodGlam.Windows;

/// <summary>
/// The pure, ImGui-free logic behind the About tab (<see cref="AboutTab"/>): turn the plugin's
/// assembly <see cref="Version"/> into a friendly <c>v</c>-prefixed string, and open the repository
/// URL through the shared <see cref="ILinkOpener"/> seam. Split out so the "what the user sees" and
/// "which URL opens" decisions are unit-testable without a live ImGui context, matching the pure-logic
/// split used by <see cref="Feedback"/> and <see cref="HistoryTabFocus"/>.
/// </summary>
internal static class AboutInfo
{
    /// <summary>
    /// Formats the plugin version for display as <c>v</c> followed by the full version, keeping every
    /// component the assembly version defines (e.g. <c>0.1.0.0</c> renders as <c>v0.1.0.0</c>). A null
    /// version yields <c>v(unknown)</c>.
    /// </summary>
    internal static string FormatVersion(Version? version)
        => version is null ? "v(unknown)" : $"v{version}";

    /// <summary>Opens the plugin's GitHub repository page. Split from the link widget so the URL choice is testable.</summary>
    internal static void OpenRepo(ILinkOpener linkOpener, string repoUrl) => linkOpener.Open(repoUrl);
}
