using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FaeController;

public unsafe class AutoAttackSuppression : IDisposable
{
    private readonly Configuration configuration;

    private const uint MeleeAutoAttackToggleId = 7;
    private const uint RangedAutoAttackToggleId = 8;

    private bool wasInCombat;
    private bool? lastIsAutoAttacking;
    private bool loggedAddress;

    // The logged timeline (toggling true 0.9s before InCombat flips, with no writes of our
    // own in play at the time) confirms the engine itself force-enables auto-attack the moment
    // you engage a hostile target with an action - independent of whatever state we last set.
    // Pre-emptively toggling off every frame (tried before) always loses that race because the
    // engine's enable-on-engage happens later in the same frame our Update() tick already ran.
    // Instead, hook every UseAction call directly and force it back off *synchronously*, right
    // after the opener's own call returns - same callstack, same frame, as late as possible.
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
        var wasInCombatBeforeCall = Plugin.Condition[ConditionFlag.InCombat];

        var result = useActionHook.Original(actionManager, actionType, actionId,
            targetId, extraParam, useType, comboRouteId, outOptAreaTargeted);

        var isAutoAttackToggle = actionType == ActionType.Action &&
            (actionId == MeleeAutoAttackToggleId || actionId == RangedAutoAttackToggleId);

        if (configuration.EnableDisableAutoAttackOnPull && result &&
            !wasInCombatBeforeCall && !isAutoAttackToggle)
        {
            var uiState = UIState.Instance();
            if (uiState != null && uiState->WeaponState.AutoAttackState.IsAutoAttacking)
            {
                Plugin.Log.Debug(
                    $"[AutoAttackSuppression] action={actionId} engaged out of combat and flipped " +
                    "auto-attack on - forcing it back off in the same call");
                uiState->WeaponState.AutoAttackState.IsAutoAttacking = false;
            }
        }

        return result;
    }

    public void Update()
    {
        if (!configuration.EnableDisableAutoAttackOnPull)
            return;

        var uiState = UIState.Instance();
        if (uiState == null) return;

        if (!loggedAddress)
        {
            loggedAddress = true;
            var fieldAddress = (nint)(&uiState->WeaponState.AutoAttackState.IsAutoAttacking);
            // This is a heap address inside the UIState singleton, not module-relative, so it's
            // only valid for this game process instance - re-read it each session before setting
            // a hardware breakpoint in the debugger.
            Plugin.Log.Debug($"[AutoAttackSuppression] IsAutoAttacking live address=0x{fieldAddress:X}");
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var isAutoAttacking = uiState->WeaponState.AutoAttackState.IsAutoAttacking;

        if (lastIsAutoAttacking != isAutoAttacking || wasInCombat != inCombat)
        {
            Plugin.Log.Debug(
                $"[AutoAttackSuppression] inCombat={inCombat} isAutoAttacking={isAutoAttacking}");
            lastIsAutoAttacking = isAutoAttacking;
        }

        wasInCombat = inCombat;
    }
}
