// Console driver that exercises the brief pipeline end-to-end:
// SqlBriefDataStore (which uses IntelReadService) → BriefPdfGenerator → PDF on disk.
// Same code path the WPF Generate-Brief button uses.

using System.IO;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Data.Intel;
using Kor.Operations.App.BusinessDevelopment.Briefs;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
        ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB not set");

var outDir = Path.Combine(Path.GetTempPath(), "r81-brief-smoke");
Directory.CreateDirectory(outDir);
Console.WriteLine($"Output directory: {outDir}");

var intelRead = new IntelReadService(cs);
var store = new SqlBriefDataStore(cs, intelRead);
IBriefPdfGenerator pdf = new HtmlBriefPdfGenerator();

// ===== 1. AHS Org Brief =====
const long ahsId = 476;
Console.WriteLine($"\n[1] Loading Org Brief data for CanonicalOrgId={ahsId} (Alberta Health Services)…");
var orgData = await store.GetOrgBriefAsync(ahsId, CancellationToken.None);
if (orgData is null) { Console.WriteLine("  ERROR: Org not found."); return 1; }

Console.WriteLine($"  DisplayName       : {orgData.DisplayName}");
Console.WriteLine($"  Kind              : {orgData.Kind}");
Console.WriteLine($"  Intel populated   : {orgData.Intel is not null}");
if (orgData.Intel is { } iOrg)
{
    Console.WriteLine($"    Synopsis P1     : {(iOrg.SynopsisParagraph1?.Length > 0 ? $"{iOrg.SynopsisParagraph1!.Length} chars" : "(null)")}");
    Console.WriteLine($"    Synopsis P2     : {(iOrg.SynopsisParagraph2?.Length > 0 ? $"{iOrg.SynopsisParagraph2!.Length} chars" : "(null)")}");
    Console.WriteLine($"    Actions         : {iOrg.Actions.Count}");
    Console.WriteLine($"    People          : {iOrg.People.Count}  (current: {iOrg.People.Count(p => p.IsCurrent)}, former: {iOrg.People.Count(p => !p.IsCurrent)})");
    Console.WriteLine($"    Signals         : {iOrg.Signals.Count}");
    Console.WriteLine($"    Works           : {iOrg.Works.Count}");
    Console.WriteLine($"    Risks           : {iOrg.Risks.Count}");
    Console.WriteLine($"    Narratives      : {iOrg.Narratives.Count}");

    // Show the Olmstead departure row specifically (audit gold)
    var olm = iOrg.People.FirstOrDefault(p => p.DisplayName.Contains("Olmstead", StringComparison.OrdinalIgnoreCase));
    if (olm is not null)
        Console.WriteLine($"    Olmstead row    : IsCurrent={olm.IsCurrent}, Title='{olm.Title}'");
    else
        Console.WriteLine($"    Olmstead row    : NOT FOUND in People (may be in a different signal)");

    var depSignal = iOrg.Signals.FirstOrDefault(s => s.SignalType == "LeadershipChange");
    if (depSignal is not null)
        Console.WriteLine($"    1st departure   : '{depSignal.Subject}'");
}

var ahsPdfPath = Path.Combine(outDir, "AHS-OrgBrief.pdf");
pdf.WriteOrgBrief(orgData, ahsPdfPath);
Console.WriteLine($"  PDF written     : {ahsPdfPath} ({new FileInfo(ahsPdfPath).Length:N0} bytes)");

// ===== 2. Calgary Region Brief =====
Console.WriteLine($"\n[2] Loading Region Brief data for AB / Calgary…");
var regionData = await store.GetRegionBriefAsync("AB", "Calgary", CancellationToken.None);
Console.WriteLine($"  Province          : {regionData.Province}");
Console.WriteLine($"  City              : {regionData.City}");
Console.WriteLine($"  LivePrimeRfpCount : {regionData.LivePrimeRfpCount}");
Console.WriteLine($"  ForwardPipelineCount: {regionData.ForwardPipelineCount}");
Console.WriteLine($"  ActiveMpiCount    : {regionData.ActiveMpiCount}");
Console.WriteLine($"  TopArchitects     : {regionData.TopArchitects.Count}");
Console.WriteLine($"  TopOwners         : {regionData.TopOwners.Count}");
Console.WriteLine($"  Intel populated   : {regionData.Intel is not null}");
if (regionData.Intel is { } iReg)
{
    Console.WriteLine($"    TopActions      : {iReg.TopActions.Count}");
    Console.WriteLine($"    LeadershipChanges (90d): {iReg.RecentLeadershipChanges.Count}");
    Console.WriteLine($"    CapacityRisks   : {iReg.TopCapacityRisks.Count}");
}

var calPdfPath = Path.Combine(outDir, "Calgary-RegionBrief.pdf");
pdf.WriteRegionBrief(regionData, calPdfPath);
Console.WriteLine($"  PDF written     : {calPdfPath} ({new FileInfo(calPdfPath).Length:N0} bytes)");

// ===== 3. Vancouver Region Brief (GVRD fix validation) =====
Console.WriteLine($"\n[3] Loading Region Brief data for BC / GVRD (R81 alias-expansion test)…");
var gvrdData = await store.GetRegionBriefAsync("BC", "GVRD", CancellationToken.None);
Console.WriteLine($"  ActiveMpiCount    : {gvrdData.ActiveMpiCount}");
Console.WriteLine($"  TopArchitects     : {gvrdData.TopArchitects.Count}");
if (gvrdData.Intel is { } iGv)
    Console.WriteLine($"  Intel TopActions  : {iGv.TopActions.Count}   <-- pre-R81 was 0; should be hundreds now");
var gvrdPdfPath = Path.Combine(outDir, "GVRD-RegionBrief.pdf");
pdf.WriteRegionBrief(gvrdData, gvrdPdfPath);
Console.WriteLine($"  PDF written     : {gvrdPdfPath} ({new FileInfo(gvrdPdfPath).Length:N0} bytes)");

Console.WriteLine($"\nAll 3 PDFs in: {outDir}");
return 0;
