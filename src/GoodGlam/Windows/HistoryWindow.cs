using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// Browsable, persistent replacement for the old toast: a scrollable table of every qualifying
/// drop. Each row shows when it dropped, the item, the top loves count, and a clickable glamour
/// name that opens the Eorzea Collection page.
/// </summary>
public sealed class HistoryWindow : Window
{
    private readonly NotificationHistoryStore store;

    public HistoryWindow(NotificationHistoryStore store)
        : base("GoodGlam History###GoodGlamHistory")
    {
        this.store = store;
        this.Size = new Vector2(560, 460);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
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
            DrawLinkCell(record.GlamName ?? record.GlamUrl, record.GlamUrl, "(unknown)");

            ImGui.TableSetColumnIndex(4);
            DrawLinkCell("Browse", record.ListingUrl, "(n/a)");
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Renders a cell as a clickable Eorzea Collection link. Falls back to plain/disabled text when
    /// there's no URL (e.g. older history entries saved before the link was captured).
    /// </summary>
    private static void DrawLinkCell(string? label, string? url, string fallback)
    {
        if (string.IsNullOrEmpty(url))
        {
            if (string.IsNullOrEmpty(label))
                ImGui.TextDisabled(fallback);
            else
                ImGui.TextUnformatted(label);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudViolet);
        ImGui.TextUnformatted(string.IsNullOrEmpty(label) ? url : label);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(url);
        }

        if (ImGui.IsItemClicked())
            Util.OpenLink(url);
    }
}
