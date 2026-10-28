using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class ClockSpotsWindow : Window, IDisposable
{
    private const int SlotCount = 8;
    private const uint JobIconBaseId = 62100;
    private const string DragDropPayloadType = "FaeClockSpotMember";

    // Row-major order of a 3x3 grid, skipping the center cell (-1).
    // Slot indices run clockwise from the top: 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW.
    private static readonly int[] GridSlots = { 7, 0, 1, 6, -1, 2, 5, 4, 3 };

    // Default light-party (4 or fewer members) cardinal assignments: tank north, healer west,
    // the first-listed DPS south, and the second-listed DPS east.
    private const int LightPartySize = 4;
    private const int TankSlot = 0;
    private const int FirstDpsSlot = 4;
    private const int SecondDpsSlot = 2;
    private const int HealerSlot = 6;

    // ClassJob row ids for the current tank and healer jobs; anything else counts as DPS.
    private static readonly HashSet<uint> TankJobIds = new() { 19, 21, 32, 37 };
    private static readonly HashSet<uint> HealerJobIds = new() { 24, 28, 33, 40 };

    private readonly ClockSpotMember?[] members = new ClockSpotMember?[SlotCount];

    private int draggedSlot = -1;

    public ClockSpotsWindow()
        : base("Party Clock Spots###FAE Clock Spots Window",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
    }

    public void Dispose() { }

    public override void OnOpen() => SyncPartyMembers();

    public override void Draw()
    {
        var iconSize = new Vector2(64, 64) * ImGuiHelpers.GlobalScale;
        var nameAreaWidth = 36f * ImGuiHelpers.GlobalScale;
        var cellGap = 12f * ImGuiHelpers.GlobalScale;
        var colStride = iconSize.X + ImGui.GetStyle().ItemSpacing.X + nameAreaWidth + cellGap;
        var rowStride = iconSize.Y + cellGap;
        var origin = ImGui.GetCursorPos();

        for (var i = 0; i < GridSlots.Length; i++)
        {
            var row = i / 3;
            var col = i % 3;
            var pos = origin + new Vector2(col * colStride, row * rowStride);
            var slotIndex = GridSlots[i];

            if (slotIndex < 0)
            {
                // Center of the square is intentionally left empty.
                continue;
            }

            DrawSlot(slotIndex, pos, iconSize);
        }

        ImGui.SetCursorPos(origin + new Vector2(0, rowStride * 3));
    }

    private void DrawSlot(int slotIndex, Vector2 pos, Vector2 iconSize)
    {
        ImGui.SetCursorPos(pos);
        ImGui.PushID($"ClockSpot{slotIndex}");

        var member = members[slotIndex];

        if (member is { } occupied)
        {
            var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(JobIconBaseId + occupied.ClassJobId)).GetWrapOrEmpty();
            ImGui.ImageButton(icon.Handle, iconSize);
        }
        else
        {
            ImGui.Button("##empty", iconSize);
        }

        if (ImGui.BeginDragDropSource())
        {
            draggedSlot = slotIndex;
            ImGui.SetDragDropPayload(DragDropPayloadType, ReadOnlySpan<byte>.Empty, ImGuiCond.None);

            if (member is { } dragged)
            {
                ImGui.Text(dragged.Abbreviation);
            }

            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            if (!ImGui.AcceptDragDropPayload(DragDropPayloadType, ImGuiDragDropFlags.None).IsNull && draggedSlot >= 0)
            {
                SwapSlots(draggedSlot, slotIndex);
                draggedSlot = -1;
            }

            ImGui.EndDragDropTarget();
        }

        // Job icon on the left, name initials to the right of it.
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();

        if (member is { } labeled)
        {
            ImGui.Text(labeled.Abbreviation);
        }

        ImGui.PopID();
    }

    private void SwapSlots(int a, int b)
    {
        if (a == b)
        {
            return;
        }

        (members[a], members[b]) = (members[b], members[a]);
    }

    public void SyncPartyMembers()
    {
        var current = GetCurrentMembers().ToList();

        // Drop anyone who has left the party.
        for (var i = 0; i < members.Length; i++)
        {
            if (members[i] is { } existing && current.All(c => c.Id != existing.Id))
            {
                members[i] = null;
            }
        }

        var newcomers = current.Where(c => members.All(m => m?.Id != c.Id)).ToList();
        if (newcomers.Count == 0)
        {
            return;
        }

        if (current.Count <= LightPartySize)
        {
            SeatLightParty(current, newcomers);
        }
        else
        {
            SeatGeneric(newcomers);
        }
    }

    private void SeatGeneric(List<ClockSpotMember> newcomers)
    {
        foreach (var member in newcomers)
        {
            var slot = FindFirstEmptySlot();
            if (slot < 0)
            {
                break;
            }

            members[slot] = member;
        }
    }

    // One tank and one healer (the "supports") always take their cardinal slot when present.
    // Everyone else ("the rest" - extra supports plus all DPS) fills the remaining slots as
    // best as possible, in party-list order, with the last of them always reserved for East.
    private void SeatLightParty(List<ClockSpotMember> current, List<ClockSpotMember> newcomers)
    {
        // Within light-party seating, the local player is always treated as first in
        // party-list order for priority purposes, regardless of where IPartyList places them.
        current = WithLocalPlayerFirst(current);

        var newcomerIds = newcomers.Select(m => m.Id).ToHashSet();

        if (current.Count == 1)
        {
            // Solo in an instance: always seat north, regardless of role.
            var solo = current[0];
            if (newcomerIds.Contains(solo.Id))
            {
                var slot = members[TankSlot] is null ? TankSlot : FindFirstEmptySlot();
                if (slot >= 0)
                {
                    members[slot] = solo;
                }
            }

            return;
        }

        ClockSpotMember? tank = null;
        ClockSpotMember? healer = null;
        var rest = new List<ClockSpotMember>();

        foreach (var member in current)
        {
            switch (GetRole(member.ClassJobId))
            {
                case Role.Tank when tank is null:
                    tank = member;
                    break;
                case Role.Healer when healer is null:
                    healer = member;
                    break;
                default:
                    rest.Add(member);
                    break;
            }
        }

        SeatSupport(tank, TankSlot, newcomerIds);
        SeatSupport(healer, HealerSlot, newcomerIds);

        var lastRestId = rest.Count > 0 ? rest[^1].Id : (ulong?)null;

        foreach (var member in rest)
        {
            if (!newcomerIds.Contains(member.Id))
            {
                continue;
            }

            int slot;
            if (member.Id == lastRestId)
            {
                // The last of "the rest" is always reserved for East.
                slot = members[SecondDpsSlot] is null ? SecondDpsSlot : FindFirstEmptySlot();
            }
            else
            {
                slot = members[FirstDpsSlot] is null ? FirstDpsSlot : FindFirstEmptySlot(SecondDpsSlot);
            }

            if (slot < 0)
            {
                continue;
            }

            members[slot] = member;
        }
    }

    private void SeatSupport(ClockSpotMember? support, int cardinalSlot, HashSet<ulong> newcomerIds)
    {
        if (support is null || !newcomerIds.Contains(support.Id))
        {
            return;
        }

        var slot = members[cardinalSlot] is null ? cardinalSlot : FindFirstEmptySlot();
        if (slot >= 0)
        {
            members[slot] = support;
        }
    }

    private static List<ClockSpotMember> WithLocalPlayerFirst(List<ClockSpotMember> current)
    {
        if (!Plugin.PlayerState.IsLoaded)
        {
            return current;
        }

        var localIndex = current.FindIndex(m => m.Id == Plugin.PlayerState.ContentId);
        if (localIndex <= 0)
        {
            return current;
        }

        var reordered = new List<ClockSpotMember>(current);
        var local = reordered[localIndex];
        reordered.RemoveAt(localIndex);
        reordered.Insert(0, local);
        return reordered;
    }

    private int FindFirstEmptySlot(int? excludeSlot = null)
    {
        for (var i = 0; i < members.Length; i++)
        {
            if (i != excludeSlot && members[i] is null)
            {
                return i;
            }
        }

        return -1;
    }

    private enum Role
    {
        Tank,
        Healer,
        Dps,
    }

    private static Role GetRole(uint classJobId)
    {
        if (TankJobIds.Contains(classJobId))
        {
            return Role.Tank;
        }

        if (HealerJobIds.Contains(classJobId))
        {
            return Role.Healer;
        }

        return Role.Dps;
    }

    private static IEnumerable<ClockSpotMember> GetCurrentMembers()
    {
        if (Plugin.PartyList.Length > 0)
        {
            foreach (var partyMember in Plugin.PartyList)
            {
                yield return new ClockSpotMember(partyMember.ContentId, GetAbbreviation(partyMember.Name.TextValue), partyMember.ClassJob.RowId);
            }

            yield break;
        }

        // Not in a party: fall back to just the local player, so the window is still useful solo.
        if (Plugin.PlayerState.IsLoaded)
        {
            yield return new ClockSpotMember(Plugin.PlayerState.ContentId, GetAbbreviation(Plugin.PlayerState.CharacterName), Plugin.PlayerState.ClassJob.RowId);
        }
    }

    private static string GetAbbreviation(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => "??",
            1 => parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }

    private sealed record ClockSpotMember(ulong Id, string Abbreviation, uint ClassJobId);
}
