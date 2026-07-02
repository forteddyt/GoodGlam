namespace GoodGlam.Windows;

/// <summary>
/// The pure, ImGui-free logic behind the About tab (<see cref="AboutTab"/>): turn the plugin's
/// 4-part assembly <see cref="Version"/> into a friendly <c>vMAJOR.MINOR.PATCH</c> string, and open
/// the repository URL through the shared <see cref="ILinkOpener"/> seam. Split out so the "what the
/// user sees" and "which URL opens" decisions are unit-testable without a live ImGui context, matching
/// the pure-logic split used by <see cref="Feedback"/> and <see cref="HistoryTabFocus"/>.
/// </summary>
internal static class AboutInfo
{
    /// <summary>
    /// Formats the plugin version as <c>vMAJOR.MINOR.PATCH</c>, dropping the assembly version's 4th
    /// (revision) component. Absent components (a <see cref="Version"/> reports <c>-1</c> for parts it
    /// was not given) render as <c>0</c>, and a null version yields <c>v(unknown)</c>.
    /// </summary>
    internal static string FormatVersion(Version? version)
    {
        if (version is null)
            return "v(unknown)";

        var patch = Math.Max(0, version.Build);
        return $"v{version.Major}.{version.Minor}.{patch}";
    }

    /// <summary>Opens the plugin's GitHub repository page. Split from the link widget so the URL choice is testable.</summary>
    internal static void OpenRepo(ILinkOpener linkOpener, string repoUrl) => linkOpener.Open(repoUrl);
}
