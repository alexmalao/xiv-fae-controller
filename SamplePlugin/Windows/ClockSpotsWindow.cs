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

    private void SyncPartyMembers()
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

        // Keep already-placed members where they are; seat newcomers in the first open slot.
        foreach (var member in current)
        {
            if (members.Any(m => m?.Id == member.Id))
            {
                continue;
            }

            var emptySlot = Array.IndexOf(members, null);
            if (emptySlot < 0)
            {
                break;
            }

            members[emptySlot] = member;
        }
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
