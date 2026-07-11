// Second mode: run a real sector dossier read through SqlBdReportService WITH
// the synthesizer wired exactly as the app DI does - proves on-demand freshness
// fires during an actual dossier generation.
// Usage: dotnet run --project tools/BdSynthesisSmoke -- sector <sectorKey>
using System.Net.Http;
using Kor.Opportunities.Data.BdReports;

internal static class SectorRun
{
    public static async Task<int> RunAsync(string sectorKey)
    {
        var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
              ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.Machine);
        var apiKey = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
              ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.User)
              ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.Machine) ?? "";
        if (string.IsNullOrWhiteSpace(cs)) { Console.Error.WriteLine("no db cs"); return 2; }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var synth = string.IsNullOrWhiteSpace(apiKey) ? null : new OnDemandHoningSynthesizer(cs, http, apiKey);
        // Exactly how the app constructs it (OpportunitiesModule).
        var svc = new SqlBdReportService(cs, synth);

        Console.WriteLine($"Running sector dossier read '{sectorKey}' through SqlBdReportService (on-demand {(synth is null ? "OFF" : "ON")})...\n");
        var t0 = DateTime.UtcNow;
        var rows = await svc.GetSectorPursuitsAsync(sectorKey, CancellationToken.None);
        var ms = (DateTime.UtcNow - t0).TotalMilliseconds;

        var byVerdict = rows.GroupBy(r => r.Verdict ?? "(none)").OrderBy(g => g.Key);
        Console.WriteLine($"  {rows.Count} projects in {ms:N0} ms");
        foreach (var g in byVerdict) Console.WriteLine($"    {g.Key,-16} {g.Count()}");
        Console.WriteLine("\n  Top rows:");
        foreach (var r in rows.Take(8))
            Console.WriteLine($"    {r.MpiId,6}  {r.Verdict,-14}  {Trunc(r.ProjectName, 46)}");
        Console.WriteLine("\n(Synthesis, if any fired, is logged and the verdicts above already reflect it - the read re-queried after refresh.)");
        return 0;
    }

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
}
