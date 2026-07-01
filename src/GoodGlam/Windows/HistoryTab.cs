using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// The History tab of the unified <see cref="MainWindow"/>: a browsable, persistent table of every
/// qualifying drop. Each row shows when it dropped, the item, the top loves count, and a clickable
/// glamour name that opens the Eorzea Collection page. (Formerly the standalone HistoryWindow.)
/// </summary>
/// <remarks>
/// Rendering only. The link-vs-text decision lives in the tested <see cref="HistoryLinkCell"/> and the
/// open effect in the tested <see cref="HistoryActions"/>; this class just draws from those, so it is
/// excluded from coverage while the logic behind it is tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the link decision (HistoryLinkCell) and open effect (HistoryActions) are extracted and tested, and a live ImGui context can't run in CI.")]
internal sealed class HistoryTab
{
    private readonly NotificationHistoryStore store;
    private readonly HistoryActions actions;

    internal HistoryTab(NotificationHistoryStore store)
        : this(store, new DalamudLinkOpener())
    {
    }

    internal HistoryTab(NotificationHistoryStore store, ILinkOpener linkOpener)
    {
        this.store = store;
        this.actions = new HistoryActions(linkOpener);
    }

    internal void Draw()
    {
        var records = this.store.Snapshot();

        ImGui.TextDisabled($"{records.Count} qualifying drop(s) logged.");
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            this.store.Clear();

        ImGui.Separator();

        if (records.Count == 0)
        {
            ImGui.TextWrapped("No popular drops yet. Qualifying drops appear here, and persist across sessions.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;

        if (!ImGui.BeginTable("##history", 5, flags))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Loves", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Top Glam", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("All Glams", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        foreach (var record in records)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(record.Timestamp.ToLocalTime().ToString("g"));

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(record.ItemName);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(record.Loves.ToString());

            ImGui.TableSetColumnIndex(3);
            this.DrawLinkCell(record.GlamName ?? record.GlamUrl, record.GlamUrl, "(unknown)");

            ImGui.TableSetColumnIndex(4);
            this.DrawLinkCell("Browse", record.ListingUrl, "(n/a)");
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Renders a cell as a clickable Eorzea Collection link. Falls back to plain/disabled text when
    /// there's no URL (e.g. older history entries saved before the link was captured). The decision
    /// is made by <see cref="HistoryLinkCell.Resolve"/>; this only paints it.
    /// </summary>
    private void DrawLinkCell(string? label, string? url, string fallback)
    {
        var cell = HistoryLinkCell.Resolve(label, url, fallback);

        if (cell.Kind == HistoryLinkKind.Disabled)
        {
            ImGui.TextDisabled(cell.Text);
            return;
        }

        if (cell.Kind == HistoryLinkKind.PlainText)
        {
            ImGui.TextUnformatted(cell.Text);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudViolet);
        ImGui.TextUnformatted(cell.Text);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(cell.Url!);
        }

        if (ImGui.IsItemClicked())
            this.actions.OpenLink(cell.Url);
    }
}
