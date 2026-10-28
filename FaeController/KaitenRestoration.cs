using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace FaeController;

public unsafe class KaitenRestoration : IDisposable
{
    private readonly Configuration configuration;

    private const uint KaitenActionId = 7494;
    private const uint ShintenActionId = 7490;

    private delegate bool UseActionDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted);

    private delegate uint GetActionStatusDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, bool checkRecastActive, bool checkCastingActive,
        uint* outOptExtraInfo);

    private readonly Hook<UseActionDelegate> useActionHook;
    private readonly Hook<GetActionStatusDelegate> getActionStatusHook;

    public KaitenRestoration(Configuration configuration, IGameInteropProvider gameInterop)
    {
        this.configuration = configuration;

        useActionHook = gameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            UseActionDetour);
        useActionHook.Enable();

        getActionStatusHook = gameInterop.HookFromAddress<GetActionStatusDelegate>(
            (nint)ActionManager.MemberFunctionPointers.GetActionStatus,
            GetActionStatusDetour);
        getActionStatusHook.Enable();
    }

    public void Dispose()
    {
        useActionHook.Dispose();
        getActionStatusHook.Dispose();
    }

    private bool UseActionDetour(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        if (configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId)
        {
            actionId = ShintenActionId;
        }

        return useActionHook.Original(actionManager, actionType, actionId,
            targetId, extraParam, useType, comboRouteId, outOptAreaTargeted);
    }

    private uint GetActionStatusDetour(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, bool checkRecastActive, bool checkCastingActive,
        uint* outOptExtraInfo)
    {
        if (configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId)
        {
            actionId = ShintenActionId;
        }

        return getActionStatusHook.Original(actionManager, actionType, actionId,
            targetId, checkRecastActive, checkCastingActive, outOptExtraInfo);
    }

    public static void PlaceOnHotbar(int hotbarNumber)
    {
        if (hotbarNumber < 1 || hotbarNumber > 10)
        {
            Plugin.ChatGui.PrintError("Hotbar number must be between 1 and 10.");
            return;
        }

        var module = RaptureHotbarModule.Instance();
        if (module == null)
        {
            Plugin.ChatGui.PrintError("Could not access hotbar module.");
            return;
        }

        module->SetAndSaveSlot((uint)(hotbarNumber - 1), 0, RaptureHotbarModule.HotbarSlotType.Action, KaitenActionId);
        Plugin.ChatGui.Print($"Kaiten placed on hotbar {hotbarNumber}, slot 1.");
    }
}
