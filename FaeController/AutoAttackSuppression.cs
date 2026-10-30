using System;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FaeController;

public unsafe class AutoAttackSuppression : IDisposable
{
    private readonly Configuration configuration;

    // RVA of the generic "SetByteFlag(byte* ptr, byte value, byte p3, byte p4)" helper that
    // actually writes IsAutoAttacking - found by watching the live write via an in-process
    // guard-page probe (AutoAttackMemoryProbe), then reading backward from the write to the
    // function's real entry. It early-outs if *ptr already equals value (no write, no side
    // effects at all), and only on a false->true transition does it also reset a pair of
    // globals (a flag + a float, almost certainly the swing-delay timer default). Every prior
    // attempt fought the flag or the timer after the fact and lost the race; this blocks the
    // actual decision point instead - specifically, the call that would enable auto-attack for
    // this flag while still out of combat - by suppressing the call itself.
    private const long SetAutoAttackFlagRva = 0xBDE3B0;

    private delegate byte SetByteFlagDelegate(byte* ptr, byte value, byte param3, byte param4);

    private readonly Hook<SetByteFlagDelegate> setByteFlagHook;

    public AutoAttackSuppression(Configuration configuration, IGameInteropProvider gameInterop)
    {
        this.configuration = configuration;

        var moduleBase = Process.GetCurrentProcess().MainModule!.BaseAddress;
        var hookAddress = moduleBase + (nint)SetAutoAttackFlagRva;

        setByteFlagHook = gameInterop.HookFromAddress<SetByteFlagDelegate>(hookAddress, SetByteFlagDetour);
        setByteFlagHook.Enable();
    }

    public void Dispose()
    {
        setByteFlagHook.Dispose();
    }

    private byte SetByteFlagDetour(byte* ptr, byte value, byte param3, byte param4)
    {
        if (configuration.EnableDisableAutoAttackOnPull && value == 1 &&
            !Plugin.Condition[ConditionFlag.InCombat] && IsAutoAttackFlag(ptr))
        {
            Plugin.Log.Debug("[AutoAttackSuppression] Blocking auto-attack enable while out of combat.");
            return 0;
        }

        return setByteFlagHook.Original(ptr, value, param3, param4);
    }

    private static bool IsAutoAttackFlag(byte* ptr)
    {
        var uiState = UIState.Instance();
        return uiState != null && ptr == &uiState->WeaponState.AutoAttackState.IsAutoAttacking;
    }
}
