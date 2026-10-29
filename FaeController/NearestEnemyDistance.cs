using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FaeController;

public unsafe class NearestEnemyDistance : IDisposable
{
    private readonly Configuration configuration;
    private readonly IAddonLifecycle addonLifecycle;

    private const uint CustomNodeIdBase = 99001;
    private const int MaxNameplates = 50;
    private const float AoeThreshold = 5f;
    private const float WarningThreshold = 6f;
    private const float LargeAoeThreshold = 8f;
    private const float MaxDisplayDistance = 30f;

    private const uint HpBarWidgetNodeId = 99099;
    private const ushort HpBarWidgetWidth = 200;
    private const ushort HpBarWidgetHeight = 14;
    private const float HpBarWidgetOffsetX = -200f;
    private const float HpBarWidgetOffsetY = 30f;

    private readonly AtkTextNode*[] customNodes = new AtkTextNode*[MaxNameplates];

    public NearestEnemyDistance(Configuration configuration, IAddonLifecycle addonLifecycle)
    {
        this.configuration = configuration;
        this.addonLifecycle = addonLifecycle;

        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "NamePlate", OnNamePlatePreDraw);
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfo", OnTargetInfoPreDraw);
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfoMainTarget", OnTargetInfoPreDraw);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PreDraw, "NamePlate", OnNamePlatePreDraw);
        addonLifecycle.UnregisterListener(AddonEvent.PreDraw, "_TargetInfo", OnTargetInfoPreDraw);
        addonLifecycle.UnregisterListener(AddonEvent.PreDraw, "_TargetInfoMainTarget", OnTargetInfoPreDraw);
        CleanupAllNodes();
        HideHpBarWidgetNode(Plugin.GameGui.GetAddonByName<AtkUnitBase>("_TargetInfo", 1));
        HideHpBarWidgetNode(Plugin.GameGui.GetAddonByName<AtkUnitBase>("_TargetInfoMainTarget", 1));
    }

    private void OnNamePlatePreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonNamePlate*)args.Addon.Address;
        if (addon == null) return;

        if (!configuration.EnableNearestEnemyDistance)
        {
            HideAllNodes();
            return;
        }

        var target = Plugin.TargetManager.Target;
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc)
        {
            HideAllNodes();
            return;
        }

        var raptureModule = RaptureAtkModule.Instance();
        if (raptureModule == null)
        {
            HideAllNodes();
            return;
        }

        var objectsById = BuildObjectLookup();
        var activeCount = raptureModule->NameplateInfoCount;

        for (var i = 0; i < activeCount && i < MaxNameplates; i++)
        {
            ref var info = ref raptureModule->NamePlateInfoEntries[i];
            var objectId = info.ObjectId.ObjectId;

            if (objectId == target.EntityId)
            {
                HideNode(i);
                continue;
            }

            if (!objectsById.TryGetValue(objectId, out var npcObject) ||
                npcObject is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant ||
                npc.StatusFlags.HasFlag(StatusFlags.PartyMember) ||
                npc.StatusFlags.HasFlag(StatusFlags.AllianceMember) ||
                npc.StatusFlags.HasFlag(StatusFlags.Friend))
            {
                HideNode(i);
                continue;
            }

            var distance = CalculateDistanceToTarget(target, npcObject);
            if (distance > MaxDisplayDistance)
            {
                HideNode(i);
                continue;
            }

            ShowDistanceOnNameplate(addon, i, distance);
        }

        for (var i = activeCount; i < MaxNameplates; i++)
            HideNode(i);
    }

    private void OnTargetInfoPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;

        var otherAddonName = args.AddonName == "_TargetInfoMainTarget" ? "_TargetInfo" : "_TargetInfoMainTarget";
        var otherAddon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(otherAddonName, 1);

        if (!configuration.EnableNearestEnemyDistance)
        {
            HideHpBarWidgetNode(addon);
            HideHpBarWidgetNode(otherAddon);
            return;
        }

        var target = Plugin.TargetManager.Target;
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc)
        {
            HideHpBarWidgetNode(addon);
            HideHpBarWidgetNode(otherAddon);
            return;
        }

        var nearestDistance = CalculateNearestEnemyDistance(target);
        if (nearestDistance > MaxDisplayDistance)
        {
            HideHpBarWidgetNode(addon);
            HideHpBarWidgetNode(otherAddon);
            return;
        }

        var node = FindNodeById(addon, HpBarWidgetNodeId);
        if (node == null) node = CreateAndAttachHpBarWidgetNode(addon);
        if (node != null)
        {
            node->SetText($"{nearestDistance:F1}y");
            ApplyDistanceColor(node, nearestDistance);
            node->AtkResNode.ToggleVisibility(true);
        }

        HideHpBarWidgetNode(otherAddon);
    }

    private static void HideHpBarWidgetNode(AtkUnitBase* addon)
    {
        if (addon == null) return;
        var node = FindNodeById(addon, HpBarWidgetNodeId);
        if (node != null) node->AtkResNode.ToggleVisibility(false);
    }

    private static AtkTextNode* FindNodeById(AtkUnitBase* addon, uint nodeId)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var candidate = addon->UldManager.NodeList[i];
            if (candidate != null && candidate->NodeId == nodeId)
                return (AtkTextNode*)candidate;
        }

        return null;
    }

    private static AtkTextNode* CreateAndAttachHpBarWidgetNode(AtkUnitBase* addon)
    {
        var rootNode = addon->RootNode;
        if (rootNode == null) return null;

        var textNode = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        if (textNode == null) return null;

        textNode->AtkResNode.Type = NodeType.Text;
        textNode->AtkResNode.NodeId = HpBarWidgetNodeId;
        textNode->AtkResNode.SetWidth(HpBarWidgetWidth);
        textNode->AtkResNode.SetHeight(HpBarWidgetHeight);
        textNode->AtkResNode.SetPositionFloat(HpBarWidgetOffsetX, HpBarWidgetOffsetY);
        textNode->AtkResNode.SetScale(1f, 1f);

        textNode->FontSize = 14;
        textNode->AlignmentType = AlignmentType.BottomRight;
        textNode->FontType = FontType.Axis;
        textNode->EdgeColor.R = 0;
        textNode->EdgeColor.G = 0;
        textNode->EdgeColor.B = 0;
        textNode->EdgeColor.A = 200;
        textNode->TextFlags = TextFlags.Edge;

        var resNode = &textNode->AtkResNode;

        // Mirrors the "Distance" plugin's AtkNodeHelpers.AttachTextNode: RootNode->ChildNode is not
        // necessarily the first sibling, so walk PrevSiblingNode back to the true head before splicing in.
        var lastNode = rootNode;
        if (lastNode->ChildNode != null)
        {
            lastNode = lastNode->ChildNode;
            while (lastNode->PrevSiblingNode != null)
                lastNode = lastNode->PrevSiblingNode;

            resNode->NextSiblingNode = lastNode;
            resNode->ParentNode = rootNode;
            lastNode->PrevSiblingNode = resNode;
        }
        else
        {
            rootNode->ChildNode = resNode;
            resNode->ParentNode = rootNode;
        }

        addon->UldManager.UpdateDrawNodeList();

        return textNode;
    }

    private static Dictionary<uint, IGameObject> BuildObjectLookup()
    {
        var lookup = new Dictionary<uint, IGameObject>();
        foreach (var obj in Plugin.ObjectTable)
            lookup[obj.EntityId] = obj;
        return lookup;
    }

    private static float CalculateDistanceToTarget(IGameObject currentTarget, IGameObject other)
    {
        var targetPos = currentTarget.Position;
        var dx = targetPos.X - other.Position.X;
        var dz = targetPos.Z - other.Position.Z;
        var centerToCenter = MathF.Sqrt(dx * dx + dz * dz);
        var centerToRing = centerToCenter - other.HitboxRadius;
        return centerToRing < 0 ? 0 : centerToRing;
    }

    private static float CalculateNearestEnemyDistance(IGameObject currentTarget)
    {
        var nearest = float.MaxValue;
        var targetPos = currentTarget.Position;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.EntityId == currentTarget.EntityId)
                continue;

            if (obj is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcSubKind.Combatant ||
                npc.StatusFlags.HasFlag(StatusFlags.PartyMember) ||
                npc.StatusFlags.HasFlag(StatusFlags.AllianceMember) ||
                npc.StatusFlags.HasFlag(StatusFlags.Friend) ||
                obj.Name.TextValue.Length == 0)
                continue;

            var dx = targetPos.X - obj.Position.X;
            var dz = targetPos.Z - obj.Position.Z;
            var centerToCenter = MathF.Sqrt(dx * dx + dz * dz);
            var centerToRing = centerToCenter - obj.HitboxRadius;
            if (centerToRing < 0) centerToRing = 0;

            if (centerToRing < nearest)
                nearest = centerToRing;
        }

        return nearest;
    }

    private void ShowDistanceOnNameplate(AddonNamePlate* addon, int index, float distance)
    {
        var node = GetOrCreateNode(addon, index);
        if (node == null) return;

        node->SetText($"{distance:F1}y");
        ApplyDistanceColor(node, distance);
        node->AtkResNode.ToggleVisibility(true);
    }

    private static void ApplyDistanceColor(AtkTextNode* node, float distance)
    {
        if (distance <= AoeThreshold)
        {
            node->TextColor.R = 100;
            node->TextColor.G = 255;
            node->TextColor.B = 100;
            node->TextColor.A = 255;
        }
        else if (distance <= WarningThreshold)
        {
            node->TextColor.R = 255;
            node->TextColor.G = 235;
            node->TextColor.B = 140;
            node->TextColor.A = 255;
        }
        else if (distance <= LargeAoeThreshold)
        {
            node->TextColor.R = 255;
            node->TextColor.G = 165;
            node->TextColor.B = 0;
            node->TextColor.A = 255;
        }
        else
        {
            node->TextColor.R = 235;
            node->TextColor.G = 110;
            node->TextColor.B = 110;
            node->TextColor.A = 255;
        }
    }

    private AtkTextNode* GetOrCreateNode(AddonNamePlate* addon, int index)
    {
        if (index < 0 || index >= MaxNameplates) return null;

        if (customNodes[index] != null)
            return customNodes[index];

        var textNode = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        if (textNode == null) return null;

        textNode->AtkResNode.Type = NodeType.Text;
        textNode->AtkResNode.NodeId = CustomNodeIdBase + (uint)index;
        textNode->AtkResNode.SetWidth(200);
        textNode->AtkResNode.SetHeight(22);
        textNode->AtkResNode.SetPositionFloat(45, 70);
        textNode->AtkResNode.SetScale(1f, 1f);

        textNode->FontSize = 14;
        textNode->AlignmentType = AlignmentType.Center;
        textNode->FontType = FontType.Axis;
        textNode->EdgeColor.R = 0;
        textNode->EdgeColor.G = 0;
        textNode->EdgeColor.B = 0;
        textNode->EdgeColor.A = 200;
        textNode->TextFlags = TextFlags.Edge;

        var nameplateObject = &addon->NamePlateObjectArray[index];
        var componentNode = nameplateObject->RootComponentNode;
        if (componentNode == null || componentNode->Component == null)
        {
            textNode->AtkResNode.Destroy(true);
            return null;
        }

        var resNode = &textNode->AtkResNode;
        var lastChild = componentNode->Component->UldManager.RootNode;
        if (lastChild == null)
        {
            textNode->AtkResNode.Destroy(true);
            return null;
        }

        while (lastChild->PrevSiblingNode != null)
            lastChild = lastChild->PrevSiblingNode;

        lastChild->PrevSiblingNode = resNode;
        resNode->NextSiblingNode = lastChild;
        resNode->ParentNode = &componentNode->AtkResNode;

        componentNode->Component->UldManager.UpdateDrawNodeList();

        customNodes[index] = textNode;
        return textNode;
    }

    private void HideNode(int index)
    {
        if (index >= 0 && index < MaxNameplates && customNodes[index] != null)
            customNodes[index]->AtkResNode.ToggleVisibility(false);
    }

    private void HideAllNodes()
    {
        for (var i = 0; i < MaxNameplates; i++)
            HideNode(i);
    }

    private void CleanupAllNodes()
    {
        for (var i = 0; i < MaxNameplates; i++)
        {
            if (customNodes[i] == null) continue;

            var resNode = &customNodes[i]->AtkResNode;

            if (resNode->NextSiblingNode != null)
                resNode->NextSiblingNode->PrevSiblingNode = resNode->PrevSiblingNode;
            if (resNode->PrevSiblingNode != null)
                resNode->PrevSiblingNode->NextSiblingNode = resNode->NextSiblingNode;

            resNode->Destroy(true);
            customNodes[i] = null;
        }
    }
}
