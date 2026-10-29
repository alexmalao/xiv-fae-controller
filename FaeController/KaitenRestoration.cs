using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace FaeController;

public unsafe class KaitenRestoration : IDisposable
{
    private readonly Configuration configuration;

    private const uint KaitenActionId = 7494;
    private const uint ShintenActionId = 7490;

    // The game's own icon paths for Kaiten (icon id 786), redirected to our own custom art via
    // Penumbra's IPC since Kaiten's icon no longer resolves after its removal from the game.
    // Both the base and high-resolution variant are redirected, since the UI prefers the hr1
    // version when present.
    private const string KaitenIconGamePath = "ui/icon/000000/000786.tex";
    private const string KaitenIconGamePathHr1 = "ui/icon/000000/000786_hr1.tex";

    private const string PenumbraAddTemporaryModAllIpc = "Penumbra.AddTemporaryModAll.V5";
    private const string PenumbraModTag = "FaeController.Kaiten";
    private const int PenumbraModPriority = 100;

    private delegate bool UseActionDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId,
        ulong targetId, uint extraParam, uint useType, uint comboRouteId,
        bool* outOptAreaTargeted);

    private delegate int GetActionCostDelegate(
        ActionType actionType, uint actionId, byte a3, byte a4, byte a5, byte a6);

    private delegate void GetHotbarCooldownInfoDelegate(
        ActionManager* actionManager, HotbarCooldownInfo* outCooldownInfo, ActionType actionType, uint actionId);

    private delegate uint CheckActionResourcesDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionId, void* actionData);

    private readonly Hook<UseActionDelegate> useActionHook;
    private readonly Hook<GetActionCostDelegate> getActionCostHook;
    private readonly Hook<GetHotbarCooldownInfoDelegate> getHotbarCooldownInfoHook;
    private readonly Hook<CheckActionResourcesDelegate> checkActionResourcesHook;

    private bool hooksEnabled;

    public KaitenRestoration(Configuration configuration, IGameInteropProvider gameInterop, string kaitenIconReplacementPath)
    {
        this.configuration = configuration;

        useActionHook = gameInterop.HookFromAddress<UseActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction,
            UseActionDetour);

        getActionCostHook = gameInterop.HookFromAddress<GetActionCostDelegate>(
            (nint)ActionManager.MemberFunctionPointers.GetActionCost,
            GetActionCostDetour);

        getHotbarCooldownInfoHook = gameInterop.HookFromAddress<GetHotbarCooldownInfoDelegate>(
            (nint)ActionManager.MemberFunctionPointers.GetHotbarCooldownInfo,
            GetHotbarCooldownInfoDetour);

        checkActionResourcesHook = gameInterop.HookFromAddress<CheckActionResourcesDelegate>(
            (nint)ActionManager.MemberFunctionPointers.CheckActionResources,
            CheckActionResourcesDetour);

        // These hooks intercept every action's cost/use/cooldown/resource check, not just
        // Kaiten's, so they must only be live while the feature is actually enabled - leaving them
        // permanently enabled caused every GCD's recast/ready display to go wrong even with the
        // setting off.
        //
        // GetActionStatus and IsActionHighlighted were both tried here too, for the same "light
        // up when usable" goal, but both were confirmed via direct testing to corrupt OTHER
        // actions' display across the whole game (GetActionStatus: every GCD shows grayed, even
        // with Kaiten never placed on a hotbar; IsActionHighlighted: the highlighted/"castable"
        // border started appearing on unrelated GCDs that shouldn't have it) - both were removed.
        // GetHotbarCooldownInfo and CheckActionResources, hooked the same way, did not reproduce
        // either issue.
        Update();

        ApplyPenumbraIconRedirect(kaitenIconReplacementPath);
    }

    public void Dispose()
    {
        useActionHook.Dispose();
        getActionCostHook.Dispose();
        getHotbarCooldownInfoHook.Dispose();
        checkActionResourcesHook.Dispose();
    }

    public void Update()
    {
        if (configuration.EnableKaitenRestoration == hooksEnabled)
            return;

        hooksEnabled = configuration.EnableKaitenRestoration;
        if (hooksEnabled)
        {
            useActionHook.Enable();
            getActionCostHook.Enable();
            getHotbarCooldownInfoHook.Enable();
            checkActionResourcesHook.Enable();
        }
        else
        {
            useActionHook.Disable();
            getActionCostHook.Disable();
            getHotbarCooldownInfoHook.Disable();
            checkActionResourcesHook.Disable();
        }
    }

    // Uses Penumbra's IPC to redirect Kaiten's icon file paths to our own art, since there is no
    // supported Dalamud API for substituting native hotbar icon textures. This is a no-op (with
    // a logged warning) if Penumbra isn't installed/running.
    private static void ApplyPenumbraIconRedirect(string kaitenIconReplacementPath)
    {
        try
        {
            // Penumbra's redirect is a dumb file swap - the game still parses the target file as
            // a native .tex container, so a plain .png (even if correctly sized) renders as blank/
            // transparent rather than erroring. Convert it to a minimal uncompressed .tex once.
            var texPath = ConvertPngToTex(kaitenIconReplacementPath);

            var subscriber = Plugin.PluginInterface
                .GetIpcSubscriber<string, Dictionary<string, string>, string, int, int>(PenumbraAddTemporaryModAllIpc);

            var paths = new Dictionary<string, string>
            {
                [KaitenIconGamePath] = texPath,
                [KaitenIconGamePathHr1] = texPath,
            };

            var result = subscriber.InvokeFunc(PenumbraModTag, paths, string.Empty, PenumbraModPriority);
            if (result != 0)
            {
                Plugin.Log.Warning($"[KaitenRestoration] Penumbra.AddTemporaryModAll returned error code {result}.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[KaitenRestoration] Could not apply Kaiten icon redirect via Penumbra IPC (is Penumbra installed?).");
        }
    }

    // Decodes the given .png and writes it back out as a minimal single-mip, uncompressed
    // B8G8R8A8 FFXIV .tex file (see Lumina's TexFile.TexHeader), since that's the only format
    // the game's native texture loader will actually render through a Penumbra path redirect.
    private static string ConvertPngToTex(string pngPath)
    {
        var texPath = Path.ChangeExtension(pngPath, ".tex");

        using var bitmap = new Bitmap(pngPath);
        var width = (ushort)bitmap.Width;
        var height = (ushort)bitmap.Height;
        var rowBytes = width * 4;

        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        byte[] pixelBytes;
        try
        {
            pixelBytes = new byte[rowBytes * height];
            if (bmpData.Stride == rowBytes)
            {
                Marshal.Copy(bmpData.Scan0, pixelBytes, 0, pixelBytes.Length);
            }
            else
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(bmpData.Scan0 + y * bmpData.Stride, pixelBytes, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        const uint textureType2D = 0x800000;
        const uint formatB8G8R8A8 = 0x1450;
        const int headerSize = 80;

        using var stream = new FileStream(texPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(textureType2D);
        writer.Write(formatB8G8R8A8);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1); // Depth
        writer.Write((byte)1); // MipLevelsCount (MipCount = 1, MipUnknownFlag = false)
        writer.Write((byte)0); // ArraySize
        writer.Write(0u); writer.Write(0u); writer.Write(0u); // LodOffset[3]
        writer.Write((uint)headerSize); // OffsetToSurface[0]
        for (var i = 1; i < 13; i++) writer.Write(0u); // OffsetToSurface[1..12]

        writer.Write(pixelBytes);

        return texPath;
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

    private int GetActionCostDetour(ActionType actionType, uint actionId, byte a3, byte a4, byte a5, byte a6)
    {
        if (configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId)
        {
            actionId = ShintenActionId;
        }

        return getActionCostHook.Original(actionType, actionId, a3, a4, a5, a6);
    }

    private void GetHotbarCooldownInfoDetour(
        ActionManager* actionManager, HotbarCooldownInfo* outCooldownInfo, ActionType actionType, uint actionId)
    {
        if (configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId)
        {
            actionId = ShintenActionId;
        }

        getHotbarCooldownInfoHook.Original(actionManager, outCooldownInfo, actionType, actionId);
    }

    private uint CheckActionResourcesDetour(
        ActionManager* actionManager, ActionType actionType, uint actionId, void* actionData)
    {
        if (configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId)
        {
            actionId = ShintenActionId;
        }

        return checkActionResourcesHook.Original(actionManager, actionType, actionId, actionData);
    }

    public static void PlaceOnHotbar(int hotbarNumber)
    {
        if (hotbarNumber < 1 || hotbarNumber > 10)
        {
            Plugin.ChatGui.PrintError("Hotbar number must be between 1 and 10.");
            return;
        }

        var framework = Framework.Instance();
        var module = framework != null ? framework->GetUIModule()->GetRaptureHotbarModule() : null;
        if (module == null)
        {
            Plugin.ChatGui.PrintError("Could not access hotbar module.");
            return;
        }

        var hotbarId = (uint)(hotbarNumber - 1);
        module->SetAndSaveSlot(hotbarId, 0, RaptureHotbarModule.HotbarSlotType.Action, KaitenActionId, false, false);

        var slot = module->GetSlotById(hotbarId, 0);
        Plugin.Log.Information(
            $"[KaitenRestoration] After SetAndSaveSlot: slot null={slot == null} " +
            $"CommandType={(slot != null ? slot->CommandType.ToString() : "n/a")} " +
            $"CommandId={(slot != null ? slot->CommandId.ToString() : "n/a")}");

        Plugin.ChatGui.Print($"Kaiten placed on hotbar {hotbarNumber}, slot 1.");
    }
}
