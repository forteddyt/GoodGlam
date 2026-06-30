using Dalamud.Bindings.ImGui;
using Dalamud.Utility;

namespace GoodGlam.Windows;

/// <summary>
/// Shared "give us feedback" UI. Today this is a single <b>Report Bug</b> button that opens GitHub's
/// new-issue page with the structured <c>bug_report.yml</c> form pre-selected, so reports arrive with
/// the environment details that matter for this plugin (notably the native-Windows <c>curl.exe</c> vs
/// Wine/Linux in-process HTTP transport split).
/// </summary>
/// <remarks>
/// The button lives in a "Feedback" section at the bottom of the Settings tab
/// (<see cref="SettingsTab"/>). Keeping the URL here (rather than inline) lets it be unit-tested
/// without a running ImGui context, mirroring the pure-logic split used by
/// <see cref="LogoInteraction"/> and <see cref="HistoryTabFocus"/>.
/// </remarks>
internal static class Feedback
{
    /// <summary>
    /// GitHub new-issue URL with the bug-report issue form pre-selected. The
    /// <c>?template=bug_report.yml</c> query points at
    /// <c>.github/ISSUE_TEMPLATE/bug_report.yml</c>.
    /// </summary>
    internal const string BugReportUrl =
        "https://github.com/forteddyt/GoodGlam/issues/new?template=bug_report.yml";

    /// <summary>
    /// Draws the <b>Report Bug</b> button. On click it opens <see cref="BugReportUrl"/> in the user's
    /// browser via <see cref="Util.OpenLink"/> (the same mechanism the History tab uses for EC links).
    /// </summary>
    internal static void DrawReportBugButton()
    {
        if (ImGui.Button("Report Bug"))
            Util.OpenLink(BugReportUrl);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens GitHub's new-issue page with the bug-report form pre-selected.");
    }
}
