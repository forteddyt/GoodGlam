using System.Numerics;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>
/// The About tab of the unified <see cref="MainWindow"/>: the plugin <b>version</b>, the GoodGlam
/// <b>logo</b>, a clickable link to the <b>GitHub repository</b>, and the <b>Report Bug</b> feedback
/// button. Version and repository URL are sourced from the Dalamud plugin manifest
/// (<see cref="Dalamud.Plugin.IDalamudPluginInterface.Manifest"/>), the single source of truth for
/// both (the packaged <c>GoodGlam.json</c>), so nothing here has to be kept in sync by hand.
/// </summary>
/// <remarks>
/// Rendering only. The version string and the "open repo" effect live in the pure, unit-tested
/// <see cref="AboutInfo"/>, and the bug-report URL/effect in <see cref="Feedback"/>; this class is
/// thin wiring over those, so it is excluded from coverage while the logic behind it is tested.
/// </remarks>
internal sealed class AboutTab
{
    /// <summary>Logical (unscaled) edge length of the logo shown on this tab, in pixels.</summary>
    private const float LogoDisplaySize = 96f;

    private static readonly Assembly OwnAssembly = typeof(AboutTab).Assembly;

    private readonly ILinkOpener linkOpener;
    private readonly ITraceLogger<AboutTab> log = new TraceLogger<AboutTab>();

    internal AboutTab()
        : this(new DalamudLinkOpener())
    {
    }

    internal AboutTab(ILinkOpener linkOpener)
    {
        this.linkOpener = linkOpener;
    }

    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering + thin wiring; the version string (AboutInfo), the open-repo/bug effects (AboutInfo/Feedback) are tested, and a live ImGui context can't run in CI.")]
    internal void Draw()
    {
        var manifest = Services.PluginInterface.Manifest;

        // Logo, centered, drawn from the same embedded resource the floating button uses.
        var wrap = Services.TextureProvider.GetFromManifestResource(OwnAssembly, LogoWindow.LogoResourceName).GetWrapOrEmpty();
        var size = new Vector2(LogoDisplaySize, LogoDisplaySize) * ImGuiHelpers.GlobalScale;
        var offset = (ImGui.GetContentRegionAvail().X - size.X) * 0.5f;
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.Image(wrap.Handle, size);

        ImGui.Spacing();
        ImGui.TextUnformatted("GoodGlam");
        ImGui.SameLine();
        ImGui.TextDisabled(AboutInfo.FormatVersion(manifest.AssemblyVersion));

        if (!string.IsNullOrWhiteSpace(manifest.Punchline))
            ImGui.TextWrapped(manifest.Punchline);

        ImGui.Separator();

        this.DrawRepoLink(manifest.RepoUrl);

        ImGui.Spacing();
        ImGui.TextDisabled("Feedback");
        Feedback.DrawReportBugButton(this.linkOpener);
    }

    /// <summary>
    /// Draws the repository URL as a clickable violet link (same treatment as the History tab's EC
    /// links), opening it in the browser via <see cref="AboutInfo.OpenRepo"/>. Renders nothing when
    /// the manifest has no repo URL.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the open effect (AboutInfo.OpenRepo) is tested and a live ImGui context can't run in CI.")]
    private void DrawRepoLink(string? repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            return;

        ImGui.TextUnformatted("GitHub:");
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudViolet);
        ImGui.TextUnformatted(repoUrl);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Open the GoodGlam repository on GitHub.");
        }

        if (ImGui.IsItemClicked())
        {
            this.log.Debug($"opening the GitHub repository {repoUrl}.");
            AboutInfo.OpenRepo(this.linkOpener, repoUrl);
        }
    }
}
