using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GoodGlam.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;

    public ConfigWindow(Configuration config)
        : base("GoodGlam Settings###GoodGlamConfig")
    {
        this.config = config;
        this.Size = new Vector2(440, 240);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var enabled = this.config.Enabled;
        if (ImGui.Checkbox("Enable drop notifications", ref enabled))
        {
            this.config.Enabled = enabled;
            this.config.Save();
        }

        ImGui.Separator();
        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.SliderInt("Loves threshold", ref threshold, 0, 1000, "%d", default))
        {
            this.config.LovesThreshold = Math.Max(0, threshold);
            this.config.Save();
        }

        var ttl = this.config.CacheTtlHours;
        if (ImGui.SliderInt("Cache lifetime (hours)", ref ttl, 1, 72, "%d", default))
        {
            this.config.CacheTtlHours = Math.Clamp(ttl, 1, 72);
            this.config.Save();
        }
    }
}
