using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using FaeController.MitSheet;

namespace FaeController.Windows;

public class MitSheetWindow : Window
{
    private readonly Configuration configuration;
    private readonly MitSheetService mitSheetService;
    private int selectedFightIdx;
    private int selectedPhaseIdx;
    private int selectedSectionIdx;
    private int selectedRoleIdx = -1;
    private int selectedJobIdx;

    private static readonly RolePosition[] Roles =
    [
        new("MT", new Vector4(0.2f, 0.4f, 0.8f, 1f), 62581, ["PLD", "WAR", "DRK", "GNB"]),
        new("OT", new Vector4(0.2f, 0.4f, 0.8f, 1f), 62581, ["PLD", "WAR", "DRK", "GNB"]),
        new("H1", new Vector4(0.2f, 0.7f, 0.3f, 1f), 62582, ["WHM", "SCH", "AST", "SGE"]),
        new("H2", new Vector4(0.2f, 0.7f, 0.3f, 1f), 62582, ["WHM", "SCH", "AST", "SGE"]),
        new("M1", new Vector4(0.8f, 0.2f, 0.2f, 1f), 62583, ["DRG", "MNK", "NIN", "SAM", "RPR", "VPR", "BLM", "SMN", "RDM", "PCT"]),
        new("M2", new Vector4(0.8f, 0.2f, 0.2f, 1f), 62583, ["DRG", "MNK", "NIN", "SAM", "RPR", "VPR", "BLM", "SMN", "RDM", "PCT"]),
        new("R1", new Vector4(0.8f, 0.2f, 0.2f, 1f), 62583, ["BRD", "MCH", "DNC"]),
        new("R2", new Vector4(0.8f, 0.2f, 0.2f, 1f), 62583, ["BLM", "SMN", "RDM", "PCT"]),
    ];

    public MitSheetWindow(Configuration configuration, MitSheetService mitSheetService)
        : base("Mit Sheet Helper###FaeMitSheet")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(700, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.configuration = configuration;
        this.mitSheetService = mitSheetService;
    }

    public void SelectFightForTerritory(uint territoryId)
    {
        var idx = MitSheetService.GetFightIndexForTerritory(territoryId);
        if (idx >= 0)
        {
            selectedFightIdx = idx;
            selectedPhaseIdx = 0;
            selectedSectionIdx = 0;
        }
    }

    public override void OnOpen()
    {
        mitSheetService.EnsureLoaded(selectedFightIdx);
    }

    public override void Draw()
    {
        if (!configuration.EnableMitSheetHelper)
        {
            ImGui.TextWrapped("Mit Sheet Helper is disabled. Enable it in /faeconfig.");
            return;
        }

        DrawFightSelector();
        DrawRoleSelector();

        if (mitSheetService.IsLoading(selectedFightIdx))
        {
            ImGui.Text("Loading mit sheet data...");
            return;
        }

        var data = mitSheetService.GetCachedData(selectedFightIdx);
        if (data == null || data.Phases.Count == 0)
        {
            ImGui.Text("No data loaded.");
            if (ImGui.Button("Load"))
                mitSheetService.EnsureLoaded(selectedFightIdx);
            return;
        }

        DrawPhaseSelector(data);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var phase = data.Phases[selectedPhaseIdx];
        if (phase.Sections.Count == 0)
        {
            ImGui.Text("No sections found in this tab.");
            return;
        }

        var section = phase.Sections[selectedSectionIdx];
        var highlightColumns = selectedRoleIdx >= 0 ? Roles[selectedRoleIdx].SheetColumns : null;
        DrawSection(section, highlightColumns);
    }

    private void DrawFightSelector()
    {
        var fightNames = MitSheetService.Fights.Select(f => f.Name).ToArray();
        if (selectedFightIdx >= fightNames.Length)
            selectedFightIdx = 0;

        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Fight", ref selectedFightIdx, fightNames, fightNames.Length))
        {
            selectedPhaseIdx = 0;
            selectedSectionIdx = 0;
            mitSheetService.EnsureLoaded(selectedFightIdx);
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            mitSheetService.Invalidate(selectedFightIdx);
            mitSheetService.EnsureLoaded(selectedFightIdx);
        }
    }

    private void DrawRoleSelector()
    {
        ImGui.Spacing();

        for (var i = 0; i < Roles.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            var role = Roles[i];
            var isSelected = selectedRoleIdx == i;
            var buttonSize = new Vector2(40, 40);

            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, role.Color);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, role.Color * new Vector4(1.2f, 1.2f, 1.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, role.Color);
            }
            else
            {
                var dimmed = role.Color * new Vector4(0.4f, 0.4f, 0.4f, 0.6f);
                ImGui.PushStyleColor(ImGuiCol.Button, dimmed);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, role.Color * new Vector4(0.7f, 0.7f, 0.7f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, role.Color);
            }

            var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(role.IconId)).GetWrapOrEmpty();
            bool clicked;

            ImGui.PushID($"Role{i}");
            if (icon.Handle != nint.Zero)
            {
                clicked = ImGui.ImageButton(icon.Handle, buttonSize - new Vector2(8, 8));
            }
            else
            {
                clicked = ImGui.Button(role.Label, buttonSize);
            }
            ImGui.PopID();

            ImGui.PopStyleColor(3);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(role.Label);

            if (clicked)
                selectedRoleIdx = isSelected ? -1 : i;
        }

        if (selectedRoleIdx >= 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Roles[selectedRoleIdx].Color, Roles[selectedRoleIdx].Label);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                selectedRoleIdx = -1;
        }

        ImGui.Spacing();
    }

    private void DrawPhaseSelector(MitSheetData data)
    {
        var phaseNames = data.Phases.Select(p => p.Name).ToArray();
        if (selectedPhaseIdx >= phaseNames.Length)
            selectedPhaseIdx = 0;

        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Phase", ref selectedPhaseIdx, phaseNames, phaseNames.Length))
            selectedSectionIdx = 0;

        var phase = data.Phases[selectedPhaseIdx];

        if (phase.Sections.Count > 1)
        {
            ImGui.SameLine();

            var sectionNames = phase.Sections.Select(s => s.Name).ToArray();
            if (selectedSectionIdx >= sectionNames.Length)
                selectedSectionIdx = 0;

            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Section", ref selectedSectionIdx, sectionNames, sectionNames.Length);
        }
        else
        {
            selectedSectionIdx = 0;
        }
    }

    private static void DrawSection(MitSection section, HashSet<string>? highlightColumns)
    {
        if (section.Mechanics.Count == 0)
        {
            ImGui.Text("No mechanics in this section.");
            return;
        }

        var columnCount = section.RoleHeaders.Length + 2;
        if (!ImGui.BeginTable("MitTable", columnCount,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Mechanic", ImGuiTableColumnFlags.WidthFixed, 130);
        foreach (var role in section.RoleHeaders)
            ImGui.TableSetupColumn(role, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var highlightColor = new Vector4(0.15f, 0.35f, 0.15f, 1f);
        var dimColor = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

        foreach (var mech in section.Mechanics)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(mech.Timestamp);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(mech.Name);

            foreach (var role in section.RoleHeaders)
            {
                ImGui.TableNextColumn();

                var isHighlighted = highlightColumns != null && highlightColumns.Contains(role);

                if (highlightColumns != null && isHighlighted)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));

                if (mech.RoleAssignments.TryGetValue(role, out var assignment))
                {
                    if (highlightColumns != null && !isHighlighted)
                        ImGui.TextColored(dimColor, assignment);
                    else
                        ImGui.TextWrapped(assignment);
                }
            }
        }

        ImGui.EndTable();

        if (section.Notes.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.82f, 0.2f, 1f), "Notes:");
            foreach (var note in section.Notes)
                ImGui.TextWrapped(note);
        }
    }

    private record RolePosition(string Label, Vector4 Color, uint IconId, HashSet<string> SheetColumns)
    {
        public RolePosition(string label, Vector4 color, uint iconId, string[] columns)
            : this(label, color, iconId, new HashSet<string>(columns)) { }
    }
}
