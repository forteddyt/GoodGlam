using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using GoodGlam.History;
using GoodGlam.Localization;

namespace GoodGlam.Windows;

internal readonly record struct DropDetailsLayout(Vector2 Position, Vector2 LogicalSize)
{
    public static DropDetailsLayout Compute(Vector2 hostMin, Vector2 hostSize, float globalScale)
    {
        var logicalHostSize = hostSize / globalScale;
        var logicalSize = new Vector2(
            MathF.Min(360f, MathF.Max(0f, logicalHostSize.X - 32f)),
            MathF.Min(170f, MathF.Max(0f, logicalHostSize.Y - 32f)));
        var renderedSize = logicalSize * globalScale;
        return new DropDetailsLayout(hostMin + ((hostSize - renderedSize) / 2f), logicalSize);
    }
}

internal sealed class ClickAwayDismissal
{
    private bool waitingForRelease;

    public void SuppressUntilRelease() => this.waitingForRelease = true;

    public bool Update(bool mouseDown, bool mouseClicked, bool windowHovered)
    {
        if (this.waitingForRelease)
        {
            if (!mouseDown)
                this.waitingForRelease = false;
            return false;
        }

        return mouseClicked && !windowHovered;
    }
}

[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; state, click-away behavior, and standard close-hotkey configuration are tested without a live context.")]
internal sealed class DropDetailsWindow : Window
{
    private readonly HistoryDetailsState state = new();
    private readonly ClickAwayDismissal clickAway = new();
    private Vector2 hostMin;
    private Vector2 hostSize;
    private bool hostVisible;

    internal DropDetailsWindow()
        : base($"{Loc.Strings.DropDetails.Title}###GoodGlamDropDetails")
    {
        this.Flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;
        this.RespectCloseHotkey = true;
    }

    internal PopularDropRecord? Selected => this.state.Selected;

    internal void SetHostBounds(Vector2 min, Vector2 size)
    {
        this.hostMin = min;
        this.hostSize = size;
        this.hostVisible = true;
    }

    internal void BeginHostFrame() => this.hostVisible = false;

    internal void Show(PopularDropRecord record)
    {
        this.state.Open(record);
        this.clickAway.SuppressUntilRelease();
        this.IsOpen = true;
        this.RequestFocus = true;
        this.BringToFront();
    }

    internal void Close()
    {
        this.state.Close();
        this.IsOpen = false;
    }

    public override void OnClose() => this.state.Close();

    public override void Update()
    {
        if (this.IsOpen && !this.hostVisible)
            this.Close();
    }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var layout = DropDetailsLayout.Compute(this.hostMin, this.hostSize, scale);
        this.Position = layout.Position;
        this.PositionCondition = ImGuiCond.Always;
        this.Size = layout.LogicalSize;
        this.SizeCondition = ImGuiCond.Always;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg]);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f * scale));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    public override void Draw()
    {
        if (this.state.Selected is not { } record)
        {
            this.IsOpen = false;
            return;
        }

        if (this.clickAway.Update(
            ImGui.IsMouseDown(ImGuiMouseButton.Left),
            ImGui.IsMouseClicked(ImGuiMouseButton.Left),
            ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)))
        {
            this.Close();
            return;
        }

        ImGui.TextUnformatted(Loc.Strings.DropDetails.Title);
        ImGui.Separator();
        ImGui.TextUnformatted(record.ItemName);
        ImGui.Spacing();

        ImGui.TextDisabled(Loc.Strings.DropDetails.Dropped);
        ImGui.SameLine(90f * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(HistoryRecordPresentation.DroppedAt(record.DroppedAt));

        ImGui.TextDisabled(Loc.Strings.DropDetails.Duty);
        ImGui.SameLine(90f * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(HistoryRecordPresentation.Duty(record.DutyName));

        ImGui.Spacing();
        if (ImGui.Button(Loc.Strings.DropDetails.Close))
            this.Close();
    }
}
