using System.Collections.Generic;

namespace FaeController.MitSheet;

public class MitFightConfig
{
    public required string Name { get; init; }
    public required string SpreadsheetId { get; init; }
    public required long[] TabGids { get; init; }
    public required uint TerritoryId { get; init; }
}

public class MitSheetData
{
    public List<MitPhase> Phases { get; set; } = [];
}

public class MitPhase
{
    public string Name { get; set; } = string.Empty;
    public long Gid { get; set; }
    public List<MitSection> Sections { get; set; } = [];
}

public class MitSection
{
    public string Name { get; set; } = string.Empty;
    public string[] RoleHeaders { get; set; } = [];
    public List<MitMechanic> Mechanics { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public class MitMechanic
{
    public string Timestamp { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> RoleAssignments { get; set; } = new();
}
