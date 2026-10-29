using System;
using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FaeController;

public unsafe class UntargetableBuffering : IDisposable
{
    private readonly Configuration configuration;

    private bool hasBufferedAction;
    private uint bufferedActionId;
    private ulong bufferedTargetId;
    private readonly Stopwatch bufferTimer = new();

    private delegate bool UseActionDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted);

    private readonly Hook<UseActionDelegate> useActionHook;
    private bool isRetrying;

    public UntargetableBuffering(Configuration configuration, IGameInteropProvider gameInterop)
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
        var result = useActionHook.Original(actionManager, actionType, actionId,
            targetId, extraParam, useType, comboRouteId, outOptAreaTargeted);

        if (isRetrying)
            return result;

        if (hasBufferedAction)
        {
            hasBufferedAction = false;
            bufferTimer.Reset();
        }

        if (!result && configuration.EnableUntargetableBuffering &&
            actionType == ActionType.Action && targetId != 0)
        {
            bufferedActionId = actionId;
            bufferedTargetId = targetId;
            hasBufferedAction = true;
            bufferTimer.Restart();
        }

        return result;
    }

    public void Update()
    {
        if (!configuration.EnableUntargetableBuffering || !hasBufferedAction)
            return;

        if (bufferTimer.ElapsedMilliseconds >= configuration.UntargetableBufferMs)
        {
            hasBufferedAction = false;
            bufferTimer.Reset();
            return;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        isRetrying = true;
        var result = useActionHook.Original(actionManager, ActionType.Action,
            bufferedActionId, bufferedTargetId, 0, 0, 0, null);
        isRetrying = false;

        if (result)
        {
            hasBufferedAction = false;
            bufferTimer.Reset();
        }
    }
}
