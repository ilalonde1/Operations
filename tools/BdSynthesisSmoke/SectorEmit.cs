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
    public sealed record HoningDetail(List<object> Actions, string? Schedule, List<object> KeyPeople, List<object> Signals);

    // Pull the rich honing detail per project (actions + schedule + key people +
    // signals) - the density the shared PursuitBriefRow doesn't carry. Keyed by MpiId.
    private static async Task<Dictionary<long, HoningDetail>> LoadHoningDetailAsync(
        string cs, IReadOnlyList<long> ids, CancellationToken ct)
    {
        var map = new Dictionary<long, HoningDetail>();
        if (ids.Count == 0) return map;
        var idCsv = string.Join(",", ids);
        var sql = $@"
SELECT e.MajorProjectsInventoryId AS Id, e.ResultJson
FROM opportunities.MajorProjectEnrichment e
JOIN (SELECT MajorProjectsInventoryId, MAX(Id) MaxId FROM opportunities.MajorProjectEnrichment
      WHERE ProviderName=N'ProjectBriefHoning' GROUP BY MajorProjectsInventoryId) l ON l.MaxId=e.Id
WHERE e.MajorProjectsInventoryId IN ({idCsv});";
        await using var con = new SqlConnection(cs);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 90 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (r.IsDBNull(1)) continue;
            try
            {
                var hp = JsonNode.Parse(r.GetString(1))?["honingPass"];
                if (hp is null) continue;
                var actions = new List<object>();
                foreach (var a in hp["actions"]?.AsArray() ?? new JsonArray())
                    actions.Add(new { type = a?["type"]?.GetValue<string>(), recommendation = a?["recommendation"]?.GetValue<string>(), targetPerson = a?["targetPerson"]?.GetValue<string>(), timingNotes = a?["timingNotes"]?.GetValue<string>() });
                var people = new List<object>();
                foreach (var kp in hp["keyPeople"]?.AsArray() ?? new JsonArray())
                    people.Add(new { name = kp?["name"]?.GetValue<string>(), title = kp?["title"]?.GetValue<string>(), side = kp?["side"]?.GetValue<string>() });
                var signals = new List<object>();
                foreach (var s in hp["signals"]?.AsArray() ?? new JsonArray())
                    signals.Add(new { subject = s?["subject"]?.GetValue<string>(), detail = s?["detail"]?.GetValue<string>(), occurredAt = s?["occurredAt"]?.GetValue<string>() });
                map[r.GetInt64(0)] = new HoningDetail(actions, hp["schedule"]?.GetValue<string>(), people, signals);
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
        var detail = await LoadHoningDetailAsync(cs, rows.Select(r => r.MpiId).ToList(), CancellationToken.None);
        var projects = rows.Select(r =>
        {
            detail.TryGetValue(r.MpiId, out var d);
            return (object)new
            {
                id = r.MpiId,
                name = r.ProjectName,
                province = r.Province,
                city = r.MunicipalityName,
                stage = r.Stage,
                proponent = r.ProponentName,
                cost = r.EstimatedCostText ?? (r.EstimatedCostCad is { } c ? c.ToString("C0") : null),
                costNum = r.EstimatedCostCad,
                verdict = r.Verdict,
                korAngle = r.KorAngle,
                status = r.HoningStatus,
                description = r.Description,
                schedule = d?.Schedule,
                actions = d?.Actions ?? new List<object>(),
                keyPeople = d?.KeyPeople ?? new List<object>(),
                signals = d?.Signals ?? new List<object>(),
                honedAtUtc = r.LastRefreshAtUtc?.ToString("o"),
            };
        }).ToList();

        List<object> market = new();
        try
        {
            var sig = await svc.GetSectorIntelSignalsAsync(sectorKey, CancellationToken.None);
            market = sig.Select(s => (object)new { s.DisplayName, s.SignalType, s.Subject, s.Detail, s.OccurredAtApprox }).ToList();
        }
        catch { }

        var outObj = new { projects, market };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(outObj, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"emit: sector={sectorKey} rows={rows.Count} market={market.Count} -> {outFile}");
        return 0;
    }
}
