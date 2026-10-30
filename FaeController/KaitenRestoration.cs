using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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

    // Hagakure's real animation files (ability slot "abl006" for Samurai), redirected the same
    // way to a custom mod that reskins them to look like Kaiten's classic animation. Hagakure
    // itself is otherwise unused by this feature - it's purely a disguise vessel: we force its
    // (now Kaiten-styled) animation to play via PlayActionTimeline right after a real Shinten
    // cast, so the button mechanically executes Shinten but visually looks like Kaiten.
    private const string HagakureTmbGamePath = "chara/action/ability/2kt_samrai/abl006.tmb";
    private const string HagakurePapGamePath =
        "chara/human/c0101/animation/a0001/bt_common/ability/2kt_samrai/abl006.pap";
    private const ushort HagakureAnimationTimelineId = 5126;

    private const string PenumbraAddTemporaryModAllIpc = "Penumbra.AddTemporaryModAll.V5";
    private const string PenumbraRedrawObjectIpc = "Penumbra.RedrawObject.V5";
    private const string PenumbraModTag = "FaeController.Kaiten";
    private const int PenumbraModPriority = 100;
    private const int PenumbraRedrawTypeRedraw = 0;

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

    private delegate void PrepareSlotForRenderDelegate(
        RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot,
        RaptureHotbarModule.HotbarUIIntermediate* intermediate);

    private readonly Hook<UseActionDelegate> useActionHook;
    private readonly Hook<GetActionCostDelegate> getActionCostHook;
    private readonly Hook<GetHotbarCooldownInfoDelegate> getHotbarCooldownInfoHook;
    private readonly Hook<CheckActionResourcesDelegate> checkActionResourcesHook;
    private readonly Hook<PrepareSlotForRenderDelegate> prepareSlotForRenderHook;

    private bool hooksEnabled;

    public KaitenRestoration(
        Configuration configuration, IGameInteropProvider gameInterop, string kaitenIconReplacementPath,
        string hagakureTmbReplacementPath, string hagakurePapReplacementPath)
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

        prepareSlotForRenderHook = gameInterop.HookFromAddress<PrepareSlotForRenderDelegate>(
            (nint)RaptureHotbarModule.MemberFunctionPointers.PrepareSlotForRender,
            PrepareSlotForRenderDetour);

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
        // either issue. PrepareSlotForRenderDetour takes a different, safer approach for the same
        // goal: it never redirects the queried actionId (Original() always runs unmodified), and
        // only overwrites the *output* availability/highlight fields for Kaiten's own slot
        // afterward - so there's no shared input substitution to corrupt other actions' results.
        Update();

        ApplyPenumbraRedirects(kaitenIconReplacementPath, hagakureTmbReplacementPath, hagakurePapReplacementPath);
    }

    public void Dispose()
    {
        useActionHook.Dispose();
        getActionCostHook.Dispose();
        getHotbarCooldownInfoHook.Dispose();
        checkActionResourcesHook.Dispose();
        prepareSlotForRenderHook.Dispose();
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
            prepareSlotForRenderHook.Enable();
        }
        else
        {
            useActionHook.Disable();
            getActionCostHook.Disable();
            getHotbarCooldownInfoHook.Disable();
            checkActionResourcesHook.Disable();
            prepareSlotForRenderHook.Disable();
        }
    }

    // Uses Penumbra's IPC to redirect Kaiten's icon and Hagakure's animation file paths to our
    // own art/animation, since there is no supported Dalamud API for substituting native hotbar
    // icon textures or action animations. This is a no-op (with a logged warning) if Penumbra
    // isn't installed/running.
    private static void ApplyPenumbraRedirects(
        string kaitenIconReplacementPath, string hagakureTmbReplacementPath, string hagakurePapReplacementPath)
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
                [HagakureTmbGamePath] = hagakureTmbReplacementPath,
                [HagakurePapGamePath] = hagakurePapReplacementPath,
            };

            var result = subscriber.InvokeFunc(PenumbraModTag, paths, string.Empty, PenumbraModPriority);
            if (result != 0)
            {
                Plugin.Log.Warning($"[KaitenRestoration] Penumbra.AddTemporaryModAll returned error code {result}.");
            }

            // AddTemporaryModAll only registers the new file mapping - it doesn't force the
            // character model to reload, so already-cached animation data (unlike VFX/SFX, which
            // get scheduled fresh per cast) keeps showing the old Hagakure animation until the
            // model is redrawn once here.
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer != null)
            {
                var redrawSubscriber = Plugin.PluginInterface.GetIpcSubscriber<int, int, object>(PenumbraRedrawObjectIpc);
                redrawSubscriber.InvokeAction(localPlayer.ObjectIndex, PenumbraRedrawTypeRedraw);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[KaitenRestoration] Could not apply Penumbra redirects (is Penumbra installed?).");
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
        var isKaiten = configuration.EnableKaitenRestoration &&
            actionType == ActionType.Action &&
            actionId == KaitenActionId;

        if (isKaiten)
            actionId = ShintenActionId;

        var result = useActionHook.Original(actionManager, actionType, actionId,
            targetId, extraParam, useType, comboRouteId, outOptAreaTargeted);

        // Shinten mechanically executes (and plays its own animation) above; forcing Hagakure's
        // (Kaiten-styled, via the Penumbra redirect) animation on top makes the button visually
        // look like Kaiten while still dealing Shinten's real damage/cost. Calling this before
        // Original() instead was tried to suppress Shinten's own leftover VFX, but Shinten's own
        // native trigger (inside Original()) simply overwrote it again, since it runs later.
        if (isKaiten && result)
            PlayHagakureAnimationOnLocalPlayer();

        return result;
    }

    // Calls the sequencer's own PlayTimeline directly rather than TimelineContainer's higher-level
    // PlayActionTimeline wrapper - this is the same lower-level entry point the Brio posing plugin
    // uses specifically to force-blend an animation over whatever is currently playing, which
    // PlayActionTimeline's normal priority arbitration could otherwise silently drop depending on
    // what GCD animation Kaiten gets weaved after.
    private static void PlayHagakureAnimationOnLocalPlayer()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var character = (Character*)localPlayer.Address;
        character->Timeline.TimelineSequencer.PlayTimeline(HagakureAnimationTimelineId);
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

    // Unlike the other detours, this never substitutes the queried actionId - Original() always
    // evaluates Kaiten's own (always-unavailable, since it's not a real learned action) data
    // unmodified. Only afterward, for Kaiten's own slot specifically, do we overwrite the output
    // availability/highlight fields with Shinten's real status, queried via a single direct call
    // (not a hook) so there's no risk of this leaking into any other action's evaluation.
    private void PrepareSlotForRenderDetour(
        RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot,
        RaptureHotbarModule.HotbarUIIntermediate* intermediate)
    {
        prepareSlotForRenderHook.Original(module, slot, intermediate);

        if (!configuration.EnableKaitenRestoration ||
            slot->CommandType != RaptureHotbarModule.HotbarSlotType.Action ||
            slot->CommandId != KaitenActionId)
        {
            return;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null) return;

        var targetId = Plugin.ObjectTable.LocalPlayer?.GameObjectId ?? 0;
        var status = actionManager->GetActionStatus(ActionType.Action, ShintenActionId, targetId, true, true, null);
        var isReady = status == 0;

        intermediate->ActionAvailable1 = isReady;
        intermediate->ActionAvailable2 = isReady;
        intermediate->DrawAnts = isReady;
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
