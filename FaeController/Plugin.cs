using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using FaeController.Windows;

namespace FaeController;

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
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string ConfigCommandName = "/faeconfig";
    private const string ClockSpotsCommandName = "/faeclocks";
    private const string KaitenCommandName = "/faekaiten";
    private const string AutoAttackProbeCommandName = "/faeprobe";

    // Arbitrary id used to register our "clockspots" chat link handler with Dalamud.
    private const uint ClockSpotsLinkCommandId = 1;

    // Numpad1-8, clockwise from North, polled directly so a Steam Controller (or any input
    // remapper) can be bound straight to these keys with no in-game macro/hotbar setup at all.
    private static readonly VirtualKey[] ClockSpotKeys =
    {
        VirtualKey.NUMPAD1, VirtualKey.NUMPAD2, VirtualKey.NUMPAD3, VirtualKey.NUMPAD4,
        VirtualKey.NUMPAD5, VirtualKey.NUMPAD6, VirtualKey.NUMPAD7, VirtualKey.NUMPAD8,
    };

    private readonly bool[] clockSpotKeyWasDown = new bool[ClockSpotKeys.Length];

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("FaeController");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private ClockSpotsWindow ClockSpotsWindow { get; init; }

    private readonly UntargetableBuffering untargetableBuffering;
    private readonly AutoAttackSuppression autoAttackSuppression;
    private readonly AutoAttackMemoryProbe autoAttackMemoryProbe = new();
    private readonly NearestEnemyDistance nearestEnemyDistance;
    private readonly KaitenRestoration kaitenRestoration;
    private readonly DalamudLinkPayload clockSpotsLinkPayload;

    // TerritoryChanged fires before Condition[BoundByDuty] (and the party list) actually update,
    // so the prompt can't be decided right there -- instead flag it as pending and let
    // OnFrameworkUpdate fire it once BoundByDuty actually flips true.
    private bool pendingClockSpotPrompt;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        untargetableBuffering = new UntargetableBuffering(Configuration, GameInterop);
        autoAttackSuppression = new AutoAttackSuppression(Configuration, GameInterop);
        nearestEnemyDistance = new NearestEnemyDistance(Configuration, AddonLifecycle);

        // You might normally want to embed resources and load them from the manifest stream
        var faeImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "fae.png");
        var kaitenImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "kaiten.png");
        var hagakureTmbPath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "abl006.tmb");
        var hagakurePapPath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "abl006.pap");
        kaitenRestoration = new KaitenRestoration(
            Configuration, GameInterop, kaitenImagePath, hagakureTmbPath, hagakurePapPath);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, faeImagePath);
        ClockSpotsWindow = new ClockSpotsWindow(Configuration);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ClockSpotsWindow);

        clockSpotsLinkPayload = ChatGui.AddChatLinkHandler(ClockSpotsLinkCommandId, OnClockSpotsLinkClicked);

        CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the FAE Controller settings menu."
        });

        CommandManager.AddHandler(ClockSpotsCommandName, new CommandInfo(OnClockSpotsCommand)
        {
            HelpMessage = "Opens the party clock spots window."
        });

        CommandManager.AddHandler(KaitenCommandName, new CommandInfo(OnKaitenCommand)
        {
            HelpMessage = "Places Kaiten on the specified hotbar. Usage: /faekaiten <hotbar_number>"
        });

        CommandManager.AddHandler(AutoAttackProbeCommandName, new CommandInfo(OnAutoAttackProbeCommand)
        {
            HelpMessage = "Diagnostic: arms an in-process watch on the auto-attack flag and logs whatever writes it."
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

        // Poll Numpad1-8 directly every frame so a controller bound to those keys soft-targets
        // the matching clock spot with no in-game macro or hotbar slot required.
        Framework.Update += OnFrameworkUpdate;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [FaeController] ===A cool log message from Fae Nightwolf's Controller Additions===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Framework.Update -= OnFrameworkUpdate;

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        ClockSpotsWindow.Dispose();

        untargetableBuffering.Dispose();
        autoAttackSuppression.Dispose();
        autoAttackMemoryProbe.Dispose();
        nearestEnemyDistance.Dispose();
        kaitenRestoration.Dispose();

        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(ClockSpotsCommandName);
        CommandManager.RemoveHandler(KaitenCommandName);
        CommandManager.RemoveHandler(AutoAttackProbeCommandName);

        ChatGui.RemoveChatLinkHandler(ClockSpotsLinkCommandId);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of the settings menu
        ConfigWindow.Toggle();
    }

    private void OnClockSpotsCommand(string command, string args)
    {
        ClockSpotsWindow.Open();
    }

    private void OnKaitenCommand(string command, string args)
    {
        if (!Configuration.EnableKaitenRestoration)
        {
            ChatGui.PrintError("Kaiten restoration is not enabled. Enable it in /faeconfig first.");
            return;
        }

        if (!int.TryParse(args.Trim(), out var hotbarNumber))
        {
            ChatGui.PrintError("Usage: /faekaiten <hotbar_number> (1-10)");
            return;
        }

        KaitenRestoration.PlaceOnHotbar(hotbarNumber);
    }

    private void OnAutoAttackProbeCommand(string command, string args)
    {
        autoAttackMemoryProbe.Arm();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        untargetableBuffering.Update();
        autoAttackSuppression.Update();
        kaitenRestoration.Update();

        if (pendingClockSpotPrompt)
        {
            TryPromptClockSpots();
        }

        if (!Configuration.EnableClockSpotTargeting)
            return;

        for (var i = 0; i < ClockSpotKeys.Length; i++)
        {
            var isDown = KeyState[ClockSpotKeys[i]];

            // Only react on the press edge, not every frame the key is held down.
            if (isDown && !clockSpotKeyWasDown[i])
            {
                var target = ClockSpotsWindow.ResolveSlotTarget(i);
                if (Configuration.HardTargetClockSpots)
                {
                    TargetManager.Target = target;
                }
                else
                {
                    TargetManager.SoftTarget = target;
                }
            }

            clockSpotKeyWasDown[i] = isDown;
        }
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        if (!Configuration.EnableClockSpotTargeting)
            return;

        pendingClockSpotPrompt = true;
    }

    private void TryPromptClockSpots()
    {
        if (!Condition[ConditionFlag.BoundByDuty])
        {
            return;
        }

        pendingClockSpotPrompt = false;

        // Refresh the clock spot board as soon as we zone into any instance, whether solo or partied.
        ClockSpotsWindow.SyncPartyMembers();

        // Only prompt in chat when there's at least one other party member to set clock spots with
        if (PartyList.Length < 2)
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

    private void OnClockSpotsLinkClicked(uint commandId, SeString args) => ClockSpotsWindow.Open();

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
