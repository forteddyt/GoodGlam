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

        this.config = Services.PluginInterface.GetPluginConfig() as Configuration ?? CreateDefaultConfig();

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
            HelpMessage = "Open settings. Debug: /goodglam dump (log live loot window), /goodglam check <itemId> (run the full pipeline on demand).",
        });

        Services.Log.Information("GoodGlam loaded.");
    }

    private static Configuration CreateDefaultConfig()
    {
        // First run: persist defaults so later loads reuse them and migrations have a baseline.
        var config = new Configuration();
        config.Save();
        return config;
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            this.ToggleConfig();
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "dump":
                this.lootWatcher.DumpCurrentLoot();
                break;

            case "check":
                if (parts.Length > 1 && uint.TryParse(parts[1].Trim(), out var itemId))
                    this.lootWatcher.SimulateDrop(itemId);
                else
                    Services.Log.Information("GoodGlam: usage is /goodglam check <itemId> (a numeric game item ID).");
                break;

            default:
                this.ToggleConfig();
                break;
        }
    }

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
