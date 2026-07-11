// emit mode: run a sector's pursuit read through the wired SqlBdReportService
// (ensures on-demand freshness) and write the fresh rows to JSON for the generic
// canonical-template report builder to render.
// Usage: dotnet run --project tools/BdSynthesisSmoke -- emit <sectorKey> <outFile>
using System.Net.Http;
using System.Text.Json;
using Kor.Opportunities.Data.BdReports;

internal static class SectorEmit
{
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
            honedAtUtc = r.LastRefreshAtUtc?.ToString("o"),
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(outObj, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"emit: sector={sectorKey} rows={rows.Count} -> {outFile}");
        return 0;
    }
}
