using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FaeController.MitSheet;

public class MitSheetService : IDisposable
{
    private const string BaseUrl = "https://docs.google.com/spreadsheets/d/{0}/pub?output=csv&gid={1}";

    public static readonly MitFightConfig[] Fights =
    [
        new MitFightConfig
        {
            Name = "Future's Rewritten",
            SpreadsheetId = "1M1LHe4mpb1lyxkLWJxrDwe_JH897nickG3XLTtwnI90",
            TerritoryId = 1238,
            TabGids =
            [
                58668446, 598251485, 370065642, 810381878,
                1274743541, 69044699, 1077717744, 897078869,
                1476839696, 1116075293, 1331714667, 654167500,
                840768364, 835371878, 636237462, 2121918156,
                772187725, 1068337018,
            ],
        },
    ];

    private readonly HttpClient httpClient;
    private readonly Dictionary<string, MitSheetData> cache = new();
    private readonly HashSet<string> loadingFights = [];

    public MitSheetService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    public bool IsLoaded(int fightIdx) =>
        fightIdx >= 0 && fightIdx < Fights.Length && cache.ContainsKey(Fights[fightIdx].SpreadsheetId);

    public bool IsLoading(int fightIdx) =>
        fightIdx >= 0 && fightIdx < Fights.Length && loadingFights.Contains(Fights[fightIdx].SpreadsheetId);

    public MitSheetData? GetCachedData(int fightIdx)
    {
        if (fightIdx < 0 || fightIdx >= Fights.Length) return null;
        cache.TryGetValue(Fights[fightIdx].SpreadsheetId, out var data);
        return data;
    }

    public static int GetFightIndexForTerritory(uint territoryId)
    {
        for (var i = 0; i < Fights.Length; i++)
        {
            if (Fights[i].TerritoryId == territoryId)
                return i;
        }
        return -1;
    }

    public void EnsureLoaded(int fightIdx)
    {
        if (fightIdx < 0 || fightIdx >= Fights.Length) return;
        var fight = Fights[fightIdx];

        if (cache.ContainsKey(fight.SpreadsheetId) || loadingFights.Contains(fight.SpreadsheetId))
            return;

        loadingFights.Add(fight.SpreadsheetId);

        Task.Run(async () =>
        {
            try
            {
                var data = new MitSheetData();

                foreach (var gid in fight.TabGids)
                {
                    try
                    {
                        var csv = await FetchSheetCsvAsync(fight.SpreadsheetId, gid);
                        var phase = ParseSheet(csv, gid);
                        data.Phases.Add(phase);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning($"[MitSheet] Failed to fetch tab gid={gid}: {ex.Message}");
                    }
                }

                cache[fight.SpreadsheetId] = data;
                Plugin.Log.Information($"[MitSheet] Loaded {data.Phases.Count} tabs for {fight.Name}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[MitSheet] Failed to load {fight.Name}: {ex.Message}");
            }
            finally
            {
                loadingFights.Remove(fight.SpreadsheetId);
            }
        });
    }

    public void Invalidate(int fightIdx)
    {
        if (fightIdx >= 0 && fightIdx < Fights.Length)
            cache.Remove(Fights[fightIdx].SpreadsheetId);
    }

    private async Task<string> FetchSheetCsvAsync(string spreadsheetId, long gid)
    {
        var url = string.Format(BaseUrl, spreadsheetId, gid);
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    internal static MitPhase ParseSheet(string csv, long gid)
    {
        var phase = new MitPhase { Gid = gid };
        var rows = ParseCsvRows(csv);
        if (rows.Count == 0) return phase;

        MitSection? currentSection = null;
        string[] currentRoles = [];
        var foundRoleHeaders = false;

        foreach (var row in rows)
        {
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            if (IsRoleHeaderRow(row))
            {
                foundRoleHeaders = true;
                currentRoles = ExtractRoleHeaders(row);

                if (currentSection == null)
                {
                    currentSection = new MitSection { Name = phase.Name };
                    phase.Sections.Add(currentSection);
                }

                currentSection.RoleHeaders = currentRoles;
                continue;
            }

            if (IsSectionHeader(row))
            {
                var headerText = GetSectionHeaderText(row);

                if (!foundRoleHeaders)
                {
                    if (string.IsNullOrEmpty(phase.Name) && headerText.Contains(':'))
                        phase.Name = headerText;
                    continue;
                }

                currentSection = new MitSection { Name = headerText };
                phase.Sections.Add(currentSection);
                currentRoles = [];
                continue;
            }

            if (IsNotesRow(row))
            {
                var notesText = GetNotesText(row);
                if (currentSection != null && !string.IsNullOrWhiteSpace(notesText))
                    currentSection.Notes.Add(notesText);
                continue;
            }

            if (IsTimestampRow(row))
            {
                if (currentSection is { Mechanics.Count: > 0 })
                    currentSection.Mechanics[^1].Timestamp = row[0].Trim();
                continue;
            }

            if (IsMechanicRow(row) && currentSection != null && currentRoles.Length > 0)
            {
                var mechanic = new MitMechanic
                {
                    Name = row[1].Trim()
                };

                for (var i = 0; i < currentRoles.Length && i + 2 < row.Length; i++)
                {
                    var assignment = row[i + 2].Trim();
                    if (!string.IsNullOrWhiteSpace(assignment))
                        mechanic.RoleAssignments[currentRoles[i]] = assignment;
                }

                currentSection.Mechanics.Add(mechanic);
            }
        }

        return phase;
    }

    private static bool IsSectionHeader(string[] row)
    {
        var nonEmpty = row.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
        if (nonEmpty.Length != 1) return false;
        var text = nonEmpty[0].Trim();
        if (text.Length < 3) return false;
        if (IsRoleHeaderRow(row)) return false;
        if (Regex.IsMatch(text, @"^\d+:\d+$")) return false;
        return !text.StartsWith("Notes", StringComparison.OrdinalIgnoreCase) &&
               !text.StartsWith("*");
    }

    private static string GetSectionHeaderText(string[] row) =>
        row.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;

    private static bool IsRoleHeaderRow(string[] row) =>
        row.Any(c => c.Trim() == "Tank 1") && row.Any(c => c.Trim() == "Scholar");

    private static string[] ExtractRoleHeaders(string[] row)
    {
        var startIdx = Array.FindIndex(row, c => c.Trim() == "Tank 1");
        if (startIdx < 0) return [];

        var headers = new List<string>();
        for (var i = startIdx; i < row.Length; i++)
        {
            var val = row[i].Trim();
            if (string.IsNullOrWhiteSpace(val)) break;
            headers.Add(val);
        }
        return headers.ToArray();
    }

    private static bool IsNotesRow(string[] row) =>
        row.Any(c => c.Trim().StartsWith("Notes", StringComparison.OrdinalIgnoreCase) ||
                      c.Trim().StartsWith("*"));

    private static string GetNotesText(string[] row)
    {
        var notesIdx = Array.FindIndex(row, c =>
            c.Trim().StartsWith("Notes", StringComparison.OrdinalIgnoreCase));
        if (notesIdx >= 0 && notesIdx + 1 < row.Length)
        {
            var combined = string.Join(" ", row.Skip(notesIdx).Where(c => !string.IsNullOrWhiteSpace(c)));
            return combined;
        }
        return string.Join(" ", row.Where(c => !string.IsNullOrWhiteSpace(c)));
    }

    private static bool IsTimestampRow(string[] row)
    {
        if (string.IsNullOrWhiteSpace(row[0])) return false;
        return Regex.IsMatch(row[0].Trim(), @"^\d+:\d+$");
    }

    private static bool IsMechanicRow(string[] row) =>
        !string.IsNullOrWhiteSpace(row.ElementAtOrDefault(1));

    private static List<string[]> ParseCsvRows(string csv)
    {
        var rows = new List<string[]>();
        using var reader = new StringReader(csv);

        string? line;
        var currentRow = new List<string>();
        var inQuote = false;
        var currentField = "";

        while ((line = reader.ReadLine()) != null)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            currentField += '"';
                            i++;
                        }
                        else
                        {
                            inQuote = false;
                        }
                    }
                    else
                    {
                        currentField += c;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuote = true;
                    }
                    else if (c == ',')
                    {
                        currentRow.Add(currentField);
                        currentField = "";
                    }
                    else
                    {
                        currentField += c;
                    }
                }
            }

            if (inQuote)
            {
                currentField += '\n';
            }
            else
            {
                currentRow.Add(currentField);
                currentField = "";
                rows.Add(currentRow.ToArray());
                currentRow.Clear();
            }
        }

        if (currentRow.Count > 0 || currentField.Length > 0)
        {
            currentRow.Add(currentField);
            rows.Add(currentRow.ToArray());
        }

        return rows;
    }
}
