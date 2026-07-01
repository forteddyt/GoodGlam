using Dalamud.Bindings.ImGui;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>
/// Shared "give us feedback" UI. Today this is a single <b>Report Bug</b> button that opens GitHub's
/// new-issue page with the structured <c>bug_report.yml</c> form pre-selected, so reports arrive with
/// the environment details that matter for this plugin (notably the native-Windows <c>curl.exe</c> vs
/// Wine/Linux in-process HTTP transport split).
/// </summary>
/// <remarks>
/// The button lives in a "Feedback" section at the bottom of the Settings tab
/// (<see cref="SettingsTab"/>). The URL and the open action live here (not inline in the draw call)
/// so they can be unit-tested without a running ImGui context, mirroring the pure-logic split used by
/// <see cref="LogoInteraction"/> and <see cref="HistoryTabFocus"/>.
/// </remarks>
internal sealed class Feedback
{
    private static readonly ITraceLogger<Feedback> Log = new TraceLogger<Feedback>();

    private Feedback()
    {
    }

    /// <summary>
    /// GitHub new-issue URL with the bug-report issue form pre-selected. The
    /// <c>?template=bug_report.yml</c> query points at
    /// <c>.github/ISSUE_TEMPLATE/bug_report.yml</c>.
    /// </summary>
    internal const string BugReportUrl =
        "https://github.com/forteddyt/GoodGlam/issues/new?template=bug_report.yml";

    /// <summary>Opens the bug-report page. Split out from the button so the URL choice is testable.</summary>
    internal static void OpenBugReport(ILinkOpener linkOpener) => linkOpener.Open(BugReportUrl);

    /// <summary>
    /// Draws the <b>Report Bug</b> button. On click it opens <see cref="BugReportUrl"/> in the user's
    /// browser via <see cref="OpenBugReport"/> (the same <see cref="ILinkOpener"/> the History tab uses).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the URL + open effect are extracted into the tested OpenBugReport.")]
    internal static void DrawReportBugButton(ILinkOpener linkOpener)
    {
        if (ImGui.Button("Report Bug"))
        {
            Log.Debug("Report Bug clicked; opening the GitHub issue form.");
            OpenBugReport(linkOpener);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens GitHub's new-issue page with the bug-report form pre-selected.");
    }
}
