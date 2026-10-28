using System;
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

    private readonly AtkTextNode*[] customNodes = new AtkTextNode*[MaxNameplates];

    public NearestEnemyDistance(Configuration configuration, IAddonLifecycle addonLifecycle)
    {
        this.configuration = configuration;
        this.addonLifecycle = addonLifecycle;

        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "NamePlate", OnNamePlatePreDraw);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PreDraw, "NamePlate", OnNamePlatePreDraw);
        CleanupAllNodes();
    }

    private void OnNamePlatePreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonNamePlate*)args.Addon;
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

        var nearestDistance = CalculateNearestEnemyDistance(target);

        var raptureModule = RaptureAtkModule.Instance();
        if (raptureModule == null)
        {
            HideAllNodes();
            return;
        }

        for (var i = 0; i < addon->ActiveNamePlateCount && i < MaxNameplates; i++)
        {
            var info = &raptureModule->NamePlateInfoEntries[i];
            var isTarget = info->ObjectId.ObjectId == (uint)target.GameObjectId;

            if (isTarget && nearestDistance < float.MaxValue)
            {
                ShowDistanceOnNameplate(addon, i, nearestDistance);
            }
            else
            {
                HideNode(i);
            }
        }

        for (var i = addon->ActiveNamePlateCount; i < MaxNameplates; i++)
            HideNode(i);
    }

    private static float CalculateNearestEnemyDistance(IGameObject currentTarget)
    {
        var nearest = float.MaxValue;
        var targetPos = currentTarget.Position;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.GameObjectId == currentTarget.GameObjectId)
                continue;

            if (obj is not IBattleNpc npc || npc.BattleNpcKind != BattleNpcKind.Enemy)
                continue;

            var dx = targetPos.X - obj.Position.X;
            var dz = targetPos.Z - obj.Position.Z;
            var centerToCenter = MathF.Sqrt(dx * dx + dz * dz);
            var distanceToRing = centerToCenter - obj.HitboxRadius;
            if (distanceToRing < 0) distanceToRing = 0;

            if (distanceToRing < nearest)
                nearest = distanceToRing;
        }

        return nearest;
    }

    private void ShowDistanceOnNameplate(AddonNamePlate* addon, int index, float distance)
    {
        var node = GetOrCreateNode(addon, index);
        if (node == null) return;

        var text = $"{distance:F1}y";
        node->SetText(text);

        if (distance <= AoeThreshold)
        {
            node->TextColor.R = 100;
            node->TextColor.G = 255;
            node->TextColor.B = 100;
            node->TextColor.A = 255;
        }
        else if (distance <= 6f)
        {
            node->TextColor.R = 255;
            node->TextColor.G = 255;
            node->TextColor.B = 0;
            node->TextColor.A = 255;
        }
        else
        {
            node->TextColor.R = 255;
            node->TextColor.G = 80;
            node->TextColor.B = 80;
            node->TextColor.A = 255;
        }

        node->AtkResNode.ToggleVisibility(true);
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
        textNode->AtkResNode.SetPositionFloat(0, -22);
        textNode->AtkResNode.SetScale(1f, 1f);

        textNode->FontSize = 14;
        textNode->AlignmentType = AlignmentType.Center;
        textNode->FontType = FontType.Axis;
        textNode->EdgeColor.R = 0;
        textNode->EdgeColor.G = 0;
        textNode->EdgeColor.B = 0;
        textNode->EdgeColor.A = 200;
        textNode->TextFlags = (byte)TextFlags.Edge;

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
