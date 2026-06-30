using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Loot;
using GoodGlam.Windows;

namespace GoodGlam;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/goodglam";

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("GoodGlam");
    private readonly MainWindow mainWindow;
    private readonly LogoWindow logoWindow;
    private readonly NotificationHistoryStore history;
    private readonly NotificationState notificationState = new();
    private readonly EorzeaCollectionClient ecClient;
    private readonly LootWatcher lootWatcher;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();

        this.config = Services.PluginInterface.GetPluginConfig() as Configuration ?? CreateDefaultConfig();
        this.config.Filters ??= new();

        var historyPath = Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "history.json");
        this.history = new NotificationHistoryStore(historyPath);

        this.ecClient = new EorzeaCollectionClient();
        var notifier = new HistoryNotifier(this.history, this.notificationState);
        var popularity = new GlamPopularityService(this.config, this.ecClient, notifier);
        this.lootWatcher = new LootWatcher(new ItemResolver(), popularity, this.config);

        this.mainWindow = new MainWindow(this.config, EcFilterCatalog.LoadEmbedded(), this.history, this.SetLogoVisible);
        this.logoWindow = new LogoWindow(this.config, this.ToggleMain, this.notificationState)
        {
            IsOpen = this.config.ShowLogo,
        };
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.logoWindow);

        Services.PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += this.ToggleMain;
        Services.PluginInterface.UiBuilder.OpenMainUi += this.ToggleMain;

        Services.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the GoodGlam window (History + Settings tabs). Debug: /goodglam dump, /goodglam check <itemId>, /goodglam glow.",
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
            this.ToggleMain();
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "config":
            case "settings":
            case "history":
                this.ToggleMain();
                break;

            case "dump":
                this.lootWatcher.DumpCurrentLoot();
                break;

            case "check":
                if (parts.Length > 1 && uint.TryParse(parts[1].Trim(), out var itemId))
                    this.lootWatcher.SimulateDrop(itemId);
                else
                    Services.Log.Information("GoodGlam: usage is /goodglam check <itemId> (a numeric game item ID).");
                break;

            case "glow":
                // Debug: light the logo glow directly, bypassing the EC lookup, so the
                // notification animation can be verified without a qualifying drop.
                this.notificationState.Raise();
                Services.Log.Information("GoodGlam[glow]: notification glow raised — open the window (or click the logo) to clear it.");
                break;

            default:
                this.ToggleMain();
                break;
        }
    }

    private void ToggleMain()
    {
        // Opening the window lands on the History tab, which acknowledges any pending popular drop,
        // so clear the logo glow.
        this.notificationState.Clear();
        this.mainWindow.Toggle();
    }

    private void SetLogoVisible(bool visible)
    {
        this.config.ShowLogo = visible;
        this.config.Save();
        this.logoWindow.IsOpen = visible;
    }

    public void Dispose()
    {
        Services.Commands.RemoveHandler(CommandName);

        Services.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleMain;
        Services.PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMain;
        this.windowSystem.RemoveAllWindows();

        this.logoWindow.Dispose();
        this.lootWatcher.Dispose();
    }
}
