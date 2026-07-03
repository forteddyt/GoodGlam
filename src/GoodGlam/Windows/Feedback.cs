using Dalamud.Bindings.ImGui;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>
/// Shared "give us feedback" UI: a <b>Report Bug</b> button and a <b>Suggest Feature</b> button, each
/// opening GitHub's new-issue page with the matching structured issue form pre-selected — so bug
/// reports arrive with the environment details that matter for this plugin (notably the
/// native-Windows <c>curl.exe</c> vs Wine/Linux in-process HTTP transport split), and enhancement
/// requests arrive as well-formed <c>feature_request.yml</c> submissions.
/// </summary>
/// <remarks>
/// The buttons live in the "Feedback" section of the About tab (<see cref="AboutTab"/>). Each URL and
/// open action lives here (not inline in the draw call) so they can be unit-tested without a running
/// ImGui context, mirroring the pure-logic split used by <see cref="LogoInteraction"/> and
/// <see cref="HistoryTabFocus"/>.
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
    /// <c>.github/ISSUE_TEMPLATE/bug_report.yml</c>. Used as the base for
    /// <see cref="BuildBugReportUrl"/>, which appends the running plugin version.
    /// </summary>
    internal const string BugReportUrl =
        "https://github.com/forteddyt/GoodGlam/issues/new?template=bug_report.yml";

    /// <summary>
    /// Builds the bug-report URL with the <b>GoodGlam version</b> form field pre-filled. GitHub issue
    /// forms fill a text field from a query parameter whose key equals the field's <c>id</c>, and
    /// <c>bug_report.yml</c> defines <c>id: goodglam-version</c>, so appending
    /// <c>&amp;goodglam-version=&lt;version&gt;</c> populates that field. The value uses the same
    /// <see cref="AboutInfo.FormatVersion"/> rendering the About tab shows (e.g. <c>v0.1.0.0</c>, or
    /// <c>v(unknown)</c> for a null version, so the required field is always filled) and is
    /// URL-encoded via <see cref="Uri.EscapeDataString"/>.
    /// </summary>
    internal static string BuildBugReportUrl(Version? version)
        => $"{BugReportUrl}&goodglam-version={Uri.EscapeDataString(AboutInfo.FormatVersion(version))}";

    /// <summary>
    /// Opens the bug-report page with the version pre-filled via <see cref="BuildBugReportUrl"/>. Split
    /// out from the button so the URL choice is testable.
    /// </summary>
    internal static void OpenBugReport(ILinkOpener linkOpener, Version? version)
        => linkOpener.Open(BuildBugReportUrl(version));

    /// <summary>
    /// GitHub new-issue URL with the feature-request issue form pre-selected. The
    /// <c>?template=feature_request.yml</c> query points at
    /// <c>.github/ISSUE_TEMPLATE/feature_request.yml</c>.
    /// </summary>
    internal const string SuggestFeatureUrl =
        "https://github.com/forteddyt/GoodGlam/issues/new?template=feature_request.yml";

    /// <summary>Opens the feature-request page. Split out from the button so the URL choice is testable.</summary>
    internal static void OpenSuggestFeature(ILinkOpener linkOpener) => linkOpener.Open(SuggestFeatureUrl);

    /// <summary>
    /// Draws the <b>Report Bug</b> button. On click it opens the bug-report form via
    /// <see cref="OpenBugReport"/> (the same <see cref="ILinkOpener"/> the History tab uses) with the
    /// running plugin <paramref name="version"/> pre-filled into the form's <b>GoodGlam version</b>
    /// field.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the URL + open effect are extracted into the tested BuildBugReportUrl/OpenBugReport.")]
    internal static void DrawReportBugButton(ILinkOpener linkOpener, Version? version)
    {
        if (ImGui.Button("Report Bug"))
        {
            Log.Debug("Report Bug clicked; opening the GitHub issue form.");
            OpenBugReport(linkOpener, version);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens GitHub's new-issue page with the bug-report form pre-selected.");
    }

    /// <summary>
    /// Draws the <b>Suggest Feature</b> button. On click it opens <see cref="SuggestFeatureUrl"/> in the
    /// user's browser via <see cref="OpenSuggestFeature"/> (the same <see cref="ILinkOpener"/> the History
    /// tab uses).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the URL + open effect are extracted into the tested OpenSuggestFeature.")]
    internal static void DrawSuggestFeatureButton(ILinkOpener linkOpener)
    {
        if (ImGui.Button("Suggest Feature"))
        {
            Log.Debug("Suggest Feature clicked; opening the GitHub issue form.");
            OpenSuggestFeature(linkOpener);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens GitHub's new-issue page with the feature-request form pre-selected.");
    }
}
