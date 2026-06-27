using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using GoodGlam.Glam;
using GoodGlam.Loot;
using GoodGlam.Windows;

namespace GoodGlam;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/goodglam";

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("GoodGlam");
    private readonly ConfigWindow configWindow;
    private readonly EorzeaCollectionClient ecClient;
    private readonly LootWatcher lootWatcher;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();

        this.config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        this.ecClient = new EorzeaCollectionClient();
        var popularity = new GlamPopularityService(this.config, this.ecClient);
        this.lootWatcher = new LootWatcher(new ItemResolver(), popularity, this.config);

        this.configWindow = new ConfigWindow(this.config);
        this.windowSystem.AddWindow(this.configWindow);

        Services.PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi += this.ToggleConfig;

        Services.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the GoodGlam settings window.",
        });

        Services.Log.Information("GoodGlam loaded.");
    }

    private void OnCommand(string command, string args) => this.ToggleConfig();

    private void ToggleConfig() => this.configWindow.Toggle();

    public void Dispose()
    {
        Services.Commands.RemoveHandler(CommandName);

        Services.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi -= this.ToggleConfig;
        this.windowSystem.RemoveAllWindows();

        this.lootWatcher.Dispose();
    }
}
