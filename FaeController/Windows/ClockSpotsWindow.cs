using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace FaeController.Windows;

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

    // Full-party (5-8 members) cardinal assignments.
    private const int FullPartySize = 8;
    private const int MainTankSlot = 0; // North
    private const int CasterSlot = 1; // NE
    private const int ShieldHealerSlot = 2; // East
    private const int MeleeTwoSlot = 3; // SE
    private const int OffTankSlot = 4; // South
    private const int MeleeOneSlot = 5; // SW
    private const int PureHealerSlot = 6; // West
    private const int PhysRangedSlot = 7; // NW

    // Encounter-specific full-party layout overrides, keyed by TerritoryType.
    // FRU: shield healer south, off tank east (swapped from the default layout).
    // TOP: physical ranged northeast, caster northwest (swapped from the default layout).
    private const uint FuturesRewrittenTerritoryId = 1238;
    private const uint OmegaProtocolTerritoryId = 1122;

    // Main Tank vs. Off Tank priority: Warrior, then Paladin, then Gunbreaker, then Dark Knight.
    private static readonly uint[] TankPriority = { 21, 19, 37, 32 };

    // Pure Healer vs. Shield Healer priority: White Mage, then Astrologian, then Sage, then
    // Scholar. A flat priority list (rather than grouping by pure/shield archetype) naturally
    // covers double-shield-healer comps too -- e.g. Sage ranks above Scholar, so a Sage+Scholar
    // pair seats Sage as the "pure" slot and Scholar as the "shield" slot, matching how parties
    // usually run Sage as the stand-in pure healer in that comp.
    private static readonly uint[] HealerPriority = { 24, 33, 40, 28 };

    // ClassJob row ids for the current tank and healer jobs; anything else counts as DPS.
    private static readonly HashSet<uint> TankJobIds = new() { 19, 21, 32, 37 };
    private static readonly HashSet<uint> HealerJobIds = new() { 24, 28, 33, 40 };
    private static readonly HashSet<uint> MeleeJobIds = new() { 20, 22, 30, 34, 39, 41 };
    private static readonly HashSet<uint> PhysicalRangedJobIds = new() { 23, 31, 38 };
    private static readonly HashSet<uint> CasterJobIds = new() { 25, 27, 35, 36, 42 };

    private readonly Configuration configuration;
    private readonly ClockSpotMember?[] members = new ClockSpotMember?[SlotCount];

    private int draggedSlot = -1;
    private bool isLocked;

    public ClockSpotsWindow(Configuration configuration)
        : base("Clockspots###FAE Clock Spots Window",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.configuration = configuration;
    }

    public void Dispose() { }

    // Reopens the window as a fully interactive window, clearing any locked/passthrough state.
    // SyncPartyMembers() is a no-op if nothing has changed, so this won't disturb manual
    // drag-and-drop rearranging unless the party composition or someone's job actually changed.
    public void Open()
    {
        isLocked = false;
        IsOpen = true;
        SyncPartyMembers();
    }

    public override void OnClose()
    {
        // If enabled, closing instead locks the window in place as a passthrough overlay
        // rather than actually hiding it.
        if (configuration.LockClockSpotsWindowOnClose)
        {
            isLocked = true;
            IsOpen = true;
        }
        else
        {
            isLocked = false;
        }
    }

    public override void PreDraw()
    {
        IsClickthrough = isLocked;
        BgAlpha = isLocked ? configuration.ClockSpotsLockedOpacity : null;
    }

    // Room reserved below the icon for the name initials plus a small gap, added on top of the
    // icon's own size to get the (always square) slot pitch -- so the grid stays proportional
    // to the icon instead of under- or over-shooting it.
    private const float NameAndGapAllowance = 20f;

    public override void Draw()
    {
        var iconDimension = ImGui.GetFontSize() * 2f * (2f / 3f) * 2f * (2f / 3f);
        var iconSize = new Vector2(iconDimension, iconDimension) * ImGuiHelpers.GlobalScale;
        var stride = (iconDimension + NameAndGapAllowance) * ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorPos();

        for (var i = 0; i < GridSlots.Length; i++)
        {
            var row = i / 3;
            var col = i % 3;
            var pos = origin + new Vector2(col * stride, row * stride);
            var slotIndex = GridSlots[i];

            if (slotIndex < 0)
            {
                // Center of the square is intentionally left empty.
                continue;
            }

            DrawSlot(slotIndex, pos, iconSize);
        }

        ImGui.SetCursorPos(origin + new Vector2(0, stride * 3));
    }

    private void DrawSlot(int slotIndex, Vector2 pos, Vector2 iconSize)
    {
        ImGui.SetCursorPos(pos);
        ImGui.PushID($"ClockSpot{slotIndex}");

        var member = members[slotIndex];

        if (member is { } occupied)
        {
            var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(JobIconBaseId + occupied.ClassJobId)).GetWrapOrEmpty();

            // Zero frame padding so the button's bounding box is exactly iconSize -- otherwise
            // the default padding inflates it past the empty-slot size and the grid looks uneven.
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.ImageButton(icon.Handle, iconSize);
            ImGui.PopStyleVar();
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

        // Name initials centered below the icon.
        if (member is { } labeled)
        {
            var textSize = ImGui.CalcTextSize(labeled.Abbreviation);
            ImGui.SetCursorPos(new Vector2(pos.X + (iconSize.X - textSize.X) / 2f, pos.Y + iconSize.Y + 2f));
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

        if (!HasCompositionOrJobChanged(current))
        {
            return;
        }

        // Something changed -- a join/leave or a job swap among people already seated. Clear
        // everyone and run the full automatic seating fresh, same as a brand new instance entry,
        // rather than just patching in deltas (which wouldn't catch job swaps at all, since no
        // one's Id changes when players swap jobs around).
        Array.Clear(members);

        if (current.Count <= LightPartySize)
        {
            SeatLightParty(current, current);
        }
        else if (current.Count <= FullPartySize)
        {
            SeatFullParty(current, current);
        }
        else
        {
            SeatGeneric(current);
        }
    }

    private bool HasCompositionOrJobChanged(List<ClockSpotMember> current)
    {
        var existingById = members
            .Where(m => m is not null)
            .ToDictionary(m => m!.Id, m => m!);

        if (existingById.Count != current.Count)
        {
            return true;
        }

        foreach (var member in current)
        {
            if (!existingById.TryGetValue(member.Id, out var existing) ||
                existing.ClassJobId != member.ClassJobId)
            {
                return true;
            }
        }

        return false;
    }

    // Looks up whoever currently occupies the given slot and resolves them to a live game
    // object, so a soft-target command always reflects whatever the board currently shows --
    // there's no separate "remap" step needed, since this is read live at call time.
    public IGameObject? ResolveSlotTarget(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= members.Length)
        {
            return null;
        }

        var member = members[slotIndex];
        if (member is null)
        {
            return null;
        }

        if (Plugin.PartyList.Length > 0)
        {
            foreach (var partyMember in Plugin.PartyList)
            {
                if (partyMember.ContentId == member.Id)
                {
                    return partyMember.GameObject;
                }
            }

            return null;
        }

        return Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ContentId == member.Id
            ? Plugin.ObjectTable.LocalPlayer
            : null;
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

    // Main tank, off tank, two healers, two melee, one physical ranged, and one caster each take
    // a fixed cardinal slot. Anyone extra (nonstandard comps) falls back to the first open slot.
    private void SeatFullParty(List<ClockSpotMember> current, List<ClockSpotMember> newcomers)
    {
        var newcomerIds = newcomers.Select(m => m.Id).ToHashSet();

        var tanks = current.Where(m => GetRole(m.ClassJobId) == Role.Tank)
            .OrderBy(m => PriorityIndex(TankPriority, m.ClassJobId))
            .ToList();
        var healers = current.Where(m => GetRole(m.ClassJobId) == Role.Healer)
            .OrderBy(m => PriorityIndex(HealerPriority, m.ClassJobId))
            .ToList();
        var melees = current.Where(m => MeleeJobIds.Contains(m.ClassJobId)).ToList();
        var physRanged = current.Where(m => PhysicalRangedJobIds.Contains(m.ClassJobId)).ToList();
        var casters = current.Where(m => CasterJobIds.Contains(m.ClassJobId)).ToList();

        var preferredSlots = new Dictionary<ulong, int>();

        void Reserve(List<ClockSpotMember> roleGroup, int index, int slot)
        {
            if (index < roleGroup.Count)
            {
                preferredSlots[roleGroup[index].Id] = slot;
            }
        }

        var (offTankSlot, shieldHealerSlot, physRangedSlot, casterSlot) = GetFullPartySlots();

        Reserve(tanks, 0, MainTankSlot);
        Reserve(tanks, 1, offTankSlot);
        Reserve(healers, 0, PureHealerSlot);
        Reserve(healers, 1, shieldHealerSlot);
        Reserve(melees, 0, MeleeOneSlot);
        Reserve(melees, 1, MeleeTwoSlot);
        Reserve(physRanged, 0, physRangedSlot);
        Reserve(casters, 0, casterSlot);

        foreach (var member in current)
        {
            if (!newcomerIds.Contains(member.Id))
            {
                continue;
            }

            var slot = preferredSlots.TryGetValue(member.Id, out var preferred) && members[preferred] is null
                ? preferred
                : FindFirstEmptySlot();

            if (slot < 0)
            {
                continue;
            }

            members[slot] = member;
        }
    }

    // Off tank, shield healer, physical ranged, and caster slots for the current encounter,
    // swapped from the default layout for fights with a known preferred arrangement.
    private static (int OffTankSlot, int ShieldHealerSlot, int PhysRangedSlot, int CasterSlot) GetFullPartySlots()
    {
        return Plugin.ClientState.TerritoryType switch
        {
            FuturesRewrittenTerritoryId => (ShieldHealerSlot, OffTankSlot, PhysRangedSlot, CasterSlot),
            OmegaProtocolTerritoryId => (OffTankSlot, ShieldHealerSlot, CasterSlot, PhysRangedSlot),
            _ => (OffTankSlot, ShieldHealerSlot, PhysRangedSlot, CasterSlot),
        };
    }

    private static int PriorityIndex(uint[] priorityOrder, uint classJobId)
    {
        var index = Array.IndexOf(priorityOrder, classJobId);
        return index < 0 ? int.MaxValue : index;
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
