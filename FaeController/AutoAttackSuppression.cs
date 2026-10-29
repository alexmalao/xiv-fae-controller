using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FaeController;

public unsafe class AutoAttackSuppression : IDisposable
{
    private readonly Configuration configuration;

    private const uint MeleeAutoAttackToggleId = 7;
    private const uint RangedAutoAttackToggleId = 8;

    private bool wasInCombat;

    public AutoAttackSuppression(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public void Dispose() { }

    public void Update()
    {
        if (!configuration.EnableDisableAutoAttackOnPull)
            return;

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];

        if (inCombat && !wasInCombat)
        {
            // Just entered combat - resume normal auto-attacking.
            ForceAutoAttack(true);
        }
        else if (!inCombat)
        {
            // Keep it suppressed for the whole out-of-combat period, not just reactively at the
            // moment of the pull - toggling reactively loses the race against the server already
            // having scheduled the "instant" swing by the time our own toggle lands.
            ForceAutoAttack(false);
        }

        wasInCombat = inCombat;
    }

    private static void ForceAutoAttack(bool desired)
    {
        var uiState = UIState.Instance();
        var actionManager = ActionManager.Instance();
        if (uiState == null || actionManager == null) return;

        if (uiState->WeaponState.AutoAttackState.IsAutoAttacking == desired)
            return;

        actionManager->UseAction(ActionType.Action, MeleeAutoAttackToggleId, 0, 0,
            ActionManager.UseActionMode.None, 0, null);

        if (uiState->WeaponState.AutoAttackState.IsAutoAttacking != desired)
        {
            actionManager->UseAction(ActionType.Action, RangedAutoAttackToggleId, 0, 0,
                ActionManager.UseActionMode.None, 0, null);
        }
    }
}
