// Renders the Indigenous sector report through the SAME path the app uses
// (SqlBdReportService -> SectorReportProseCatalog -> SectorReportGenerator.Build)
// and confirms the re-honed Gwaii section actually surfaces in the document.
using System.IO;
using System.Text.Json;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB not set");

var store = new SqlBdReportService(cs);

// "docx <key> [outpath]": render a sector report to .docx via the app's own
// DocxBuilder — byte-for-byte what the app's Export-DOCX produces (same live
// data + prose). Proves the deliverable is not a separate data source.
if (args.Length >= 2 && args[0] == "docx")
{
    var k = args[1];
    var def0 = SectorReportDefinitionCatalog.All.Single(d => d.Key == k);
    var prose0 = SectorReportProseCatalog.For(k);
    var rows0 = await store.GetSectorPursuitsAsync(k, CancellationToken.None);
    var sig0 = await store.GetSectorIntelSignalsAsync(k, CancellationToken.None);
    var doc0 = SectorReportGenerator.Build(def0, prose0, rows0, DateTimeOffset.UtcNow, sig0);
    var bytes = DocxBuilder.Render(doc0);
    var outPath = args.Length >= 3 ? args[2]
        : $@"C:\VIsual Studio Projects\Operations\docs\KOR-{k}-Sector-Report-2026-06-23.docx";
    File.WriteAllBytes(outPath, bytes);
    Console.WriteLine($"Wrote {outPath}");
    Console.WriteLine($"  {bytes.Length:N0} bytes | {doc0.Blocks.Count} blocks | {rows0.Count(x => x.Verdict is not null)} honed of {rows0.Count} | title: {doc0.Title}");
    return 0;
}

// "all" mode: sweep every sector, one summary line each, flag thin/broken.
if (args.Length > 0 && args[0] == "all")
{
    Console.WriteLine($"{"sector",-15} {"pursuits",8} {"honed",6} {"blocks",7} {"synth",6} {"prose?",7}  status");
    foreach (var def in SectorReportDefinitionCatalog.All)
    {
        try
        {
            var pr = SectorReportProseCatalog.For(def.Key);
            var rw = await store.GetSectorPursuitsAsync(def.Key, CancellationToken.None);
            var sg = await store.GetSectorIntelSignalsAsync(def.Key, CancellationToken.None);
            var d = SectorReportGenerator.Build(def, pr, rw, DateTimeOffset.UtcNow, sg);
            var honed = rw.Count(x => x.Verdict is not null);
            var proseOk = !string.IsNullOrWhiteSpace(pr.IntroNote) || pr.Synthesis.Count > 0;
            var thin = honed == 0 || !proseOk ? "  <-- THIN" : "";
            Console.WriteLine($"{def.Key,-15} {rw.Count,8} {honed,6} {d.Blocks.Count,7} {pr.Synthesis.Count,6} {(proseOk ? "yes" : "NO"),7}{thin}");
        }
        catch (Exception ex) { Console.WriteLine($"{def.Key,-15}  ERROR: {ex.Message}"); }
    }
    return 0;
}

// "pursuit": build the pursuit-dossier report and report how many pursuits got
// project-named live signals attached (read-time), + confirm the section renders.
if (args.Length > 0 && args[0] == "pursuit")
{
    var pursuits = await store.GetPursuitDossiersAsync(CancellationToken.None);
    var withSig = pursuits.Where(p => p.LiveSignals.Count > 0).ToList();
    Console.WriteLine($"Pursuits: {pursuits.Count}; with project-named live signals: {withSig.Count}");
    foreach (var p in withSig.Take(20))
    {
        Console.WriteLine($"  {p.ProjectName}");
        foreach (var s in p.LiveSignals) Console.WriteLine($"      - {(s.Length > 90 ? s[..90] : s)}");
    }
    var pdoc = PursuitDossierReportGenerator.Build(pursuits, DateTimeOffset.UtcNow);
    var hasSection = pdoc.Blocks.Any(b => b is HeadingBlock h && h.Text.Contains("Live signals", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"\ndoc blocks: {pdoc.Blocks.Count}; 'Live signals on active pursuits' section rendered: {hasSection}");
    return 0;
}

var key = args.Length > 0 ? args[0] : "indigenous";
var definition = SectorReportDefinitionCatalog.All.Single(d => d.Key == key);
var prose = SectorReportProseCatalog.For(key);
var rows = await store.GetSectorPursuitsAsync(key, CancellationToken.None);
var signals = await store.GetSectorIntelSignalsAsync(key, CancellationToken.None);

var doc = SectorReportGenerator.Build(definition, prose, rows, DateTimeOffset.UtcNow, signals);

Console.WriteLine($"Sector       : {key}  ({definition.Title})");
Console.WriteLine($"Report title : {doc.Title}");
Console.WriteLine($"Blocks       : {doc.Blocks.Count}");
Console.WriteLine($"Pursuit rows : {rows.Count}");

static string BlockText(BdReportBlock b) => b switch
{
    HeadingBlock h => h.Text,
    ParagraphBlock p => p.Text,
    LabelValueBlock lv => lv.Label + " " + lv.Value,
    ItalicNoteBlock i => i.Text,
    TableBlock t => string.Join(" ", t.Headers) + " " + string.Join(" ", t.Rows.SelectMany(r => r)),
    ChipRowBlock c => string.Join(" ", c.Chips.Select(x => x.Text)),
    KpiStripBlock k => string.Join(" ", k.Items.Select(x => x.Value + " " + x.Label)),
    _ => ""
};

var allText = string.Join("\n", doc.Blocks.Select(BlockText));
foreach (var needle in new[] { "Gwaii", "Indigenous-owned engineering partner", "Corey Brown", "CCAB" })
{
    var hits = allText.Split(needle).Length - 1;
    Console.WriteLine($"  '{needle}' in rendered report: {(hits > 0 ? $"YES ({hits}x)" : "NO")}");
}

Console.WriteLine("\nStrategic Synthesis section headings rendered:");
foreach (var b in doc.Blocks)
{
    if (b is HeadingBlock { Level: 3 } h) Console.WriteLine("  H3: " + h.Text);
}
Console.WriteLine("\nBlocks mentioning Gwaii:");
foreach (var b in doc.Blocks)
{
    var t = BlockText(b);
    if (t.Contains("Gwaii", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine("  - " + (t.Length > 200 ? t[..200] + "…" : t));
}
return 0;
