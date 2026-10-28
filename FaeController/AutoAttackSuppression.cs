using System;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FaeController;

public unsafe class AutoAttackSuppression : IDisposable
{
    private readonly Configuration configuration;

    private const uint MeleeAutoAttackId = 7;
    private const uint RangedAutoAttackId = 8;
    private const int SuppressionDurationMs = 2000;

    private bool wasOutOfCombat = true;
    private bool suppressionActive;
    private readonly Stopwatch suppressionTimer = new();

    private delegate bool UseActionDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted);

    private readonly Hook<UseActionDelegate> useActionHook;

    public AutoAttackSuppression(Configuration configuration, IGameInteropProvider gameInterop)
    {
        this.configuration = configuration;

        useActionHook = gameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            UseActionDetour);
        useActionHook.Enable();
    }

    public void Dispose()
    {
        useActionHook.Dispose();
    }

    private bool UseActionDetour(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        if (configuration.EnableDisableAutoAttackOnPull &&
            actionType == ActionType.Action &&
            IsAutoAttack(actionId) &&
            suppressionActive)
        {
            return false;
        }

        var result = useActionHook.Original(actionManager, actionType, actionId,
            targetId, extraParam, useType, comboRouteId, outOptAreaTargeted);

        if (result && configuration.EnableDisableAutoAttackOnPull &&
            actionType == ActionType.Action &&
            !IsAutoAttack(actionId) &&
            wasOutOfCombat)
        {
            suppressionActive = true;
            wasOutOfCombat = false;
            suppressionTimer.Restart();
            Plugin.Log.Information("[Auto-Attack Suppression] Suppressing auto-attacks for encounter start.");
        }

        return result;
    }

    public void Update()
    {
        if (!configuration.EnableDisableAutoAttackOnPull)
            return;

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];

        if (!inCombat)
        {
            if (suppressionActive)
                Plugin.Log.Information("[Auto-Attack Suppression] Left combat, clearing suppression.");

            wasOutOfCombat = true;
            suppressionActive = false;
            suppressionTimer.Reset();
        }

        if (suppressionActive && suppressionTimer.ElapsedMilliseconds >= SuppressionDurationMs)
        {
            suppressionActive = false;
            suppressionTimer.Reset();
            Plugin.Log.Information("[Auto-Attack Suppression] Suppression window expired, auto-attacks re-enabled.");
        }
    }

    private static bool IsAutoAttack(uint actionId) =>
        actionId is MeleeAutoAttackId or RangedAutoAttackId;
}
