// emit mode: run a sector's pursuit read through the wired SqlBdReportService
// (ensures on-demand freshness) and write the fresh rows to JSON for the generic
// canonical-template report builder to render.
// Usage: dotnet run --project tools/BdSynthesisSmoke -- emit <sectorKey> <outFile>
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Kor.Opportunities.Data.BdReports;

internal static class SectorEmit
{
    // Pull the structured actions (recommendation/target/timing) from each
    // project's latest honing row - the richest "do this" content, which the
    // shared PursuitBriefRow doesn't carry. Keyed by MpiId.
    private static async Task<Dictionary<long, List<object>>> LoadActionsAsync(
        string cs, IReadOnlyList<long> ids, CancellationToken ct)
    {
        var map = new Dictionary<long, List<object>>();
        if (ids.Count == 0) return map;
        var idCsv = string.Join(",", ids);
        var sql = $@"
SELECT e.MajorProjectsInventoryId AS Id, JSON_QUERY(e.ResultJson, '$.honingPass.actions') AS Actions
FROM opportunities.MajorProjectEnrichment e
JOIN (SELECT MajorProjectsInventoryId, MAX(Id) MaxId FROM opportunities.MajorProjectEnrichment
      WHERE ProviderName=N'ProjectBriefHoning' GROUP BY MajorProjectsInventoryId) l ON l.MaxId=e.Id
WHERE e.MajorProjectsInventoryId IN ({idCsv});";
        await using var con = new SqlConnection(cs);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (r.IsDBNull(1)) continue;
            try
            {
                var arr = JsonNode.Parse(r.GetString(1))?.AsArray();
                if (arr is null) continue;
                var list = new List<object>();
                foreach (var a in arr)
                {
                    list.Add(new
                    {
                        type = a?["type"]?.GetValue<string>(),
                        recommendation = a?["recommendation"]?.GetValue<string>(),
                        targetPerson = a?["targetPerson"]?.GetValue<string>(),
                        timingNotes = a?["timingNotes"]?.GetValue<string>(),
                    });
                }
                if (list.Count > 0) map[r.GetInt64(0)] = list;
            }
            catch { }
        }
        return map;
    }

    public static async Task<int> RunAsync(string sectorKey, string outFile)
    {
        var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
              ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.Machine);
        var apiKey = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
              ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.Machine) ?? "";
        if (string.IsNullOrWhiteSpace(cs)) { Console.Error.WriteLine("no db cs"); return 2; }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        var synth = string.IsNullOrWhiteSpace(apiKey) ? null : new OnDemandHoningSynthesizer(cs, http, apiKey);
        var svc = new SqlBdReportService(cs, synth);   // same wiring as the app

        var rows = await svc.GetSectorPursuitsAsync(sectorKey, CancellationToken.None);
        var actions = await LoadActionsAsync(cs, rows.Select(r => r.MpiId).ToList(), CancellationToken.None);
        var outObj = rows.Select(r => new
        {
            id = r.MpiId,
            name = r.ProjectName,
            province = r.Province,
            city = r.MunicipalityName,
            stage = r.Stage,
            proponent = r.ProponentName,
            cost = r.EstimatedCostText ?? (r.EstimatedCostCad is { } c ? c.ToString("C0") : null),
            verdict = r.Verdict,
            korAngle = r.KorAngle,
            status = r.HoningStatus,
            actions = actions.TryGetValue(r.MpiId, out var a) ? a : new List<object>(),
            honedAtUtc = r.LastRefreshAtUtc?.ToString("o"),
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(outObj, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"emit: sector={sectorKey} rows={rows.Count} -> {outFile}");
        return 0;
    }
}
