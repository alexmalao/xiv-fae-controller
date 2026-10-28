using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/faecontrols";
    private const string ClockSpotsCommandName = "/faeclocks";

    // Arbitrary id used to register our "clockspots" chat link handler with Dalamud.
    private const uint ClockSpotsLinkCommandId = 1;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private ClockSpotsWindow ClockSpotsWindow { get; init; }

    private readonly DalamudLinkPayload clockSpotsLinkPayload;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        ClockSpotsWindow = new ClockSpotsWindow();

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ClockSpotsWindow);

        clockSpotsLinkPayload = ChatGui.AddChatLinkHandler(ClockSpotsLinkCommandId, OnClockSpotsLinkClicked);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the FAE Controller settings menu."
        });

        CommandManager.AddHandler(ClockSpotsCommandName, new CommandInfo(OnClockSpotsCommand)
        {
            HelpMessage = "Opens the party clock spots window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Prompt the party to set up clock spots whenever we zone into an instance with others
        ClientState.TerritoryChanged += OnTerritoryChanged;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        ClockSpotsWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ClockSpotsCommandName);

        ChatGui.RemoveChatLinkHandler(ClockSpotsLinkCommandId);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of the settings menu
        ConfigWindow.Toggle();
    }

    private void OnClockSpotsCommand(string command, string args)
    {
        // Always open (rather than toggle) so this is a reliable way to bring the window back up
        ClockSpotsWindow.IsOpen = true;
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        // Only prompt when we've actually zoned into an instanced duty with at least one other party member
        if (!Condition[ConditionFlag.BoundByDuty] || PartyList.Length < 2)
        {
            return;
        }

        var message = new SeStringBuilder()
            .AddText("Setup party member ")
            .Add(clockSpotsLinkPayload)
            .AddUiForeground(500)
            .AddText("clockspots")
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .AddText(".")
            .Build();

        ChatGui.Print(new XivChatEntry { Message = message });
    }

    private void OnClockSpotsLinkClicked(uint commandId, SeString args) => ClockSpotsWindow.IsOpen = true;

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
