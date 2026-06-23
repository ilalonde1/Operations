// BdHeatGraph — DYNAMIC teaming heat-graph. Re-queries the live BD graph every
// run, so each enrichment pass + pursuit/verdict change is reflected on the next
// generation (regenerate = current; nothing frozen). Emits a self-contained
// interactive force-graph HTML: nodes = orgs on LIVE pursuits, color = priority
// heat (vDossierCompleteness.Score), size = # live pursuits, edges = teaming
// (two orgs on the same pursuit). KOR Structural is the black hub.
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB not set");

const string hotOrgCte = @"
WITH HotMpi AS (
    SELECT m.Id
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
    WHERE m.RetiredAtUtc IS NULL
      AND COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),''))
          IN (N'PURSUE', N'PURSUE_URGENT')
),
HotOrg AS (
    SELECT DISTINCT x.OrgId, m.Id AS MpiId
    FROM opportunities.MajorProjectsInventory m
    JOIN HotMpi h ON h.Id = m.Id
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId),(m.GeneralContractorCanonicalOrgId),
                        (m.StructuralEngineerCanonicalOrgId),(m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE x.OrgId IS NOT NULL
)";

await using var con = new SqlConnection(cs);
await con.OpenAsync();

// nodes
var nodes = new List<object>();
double maxScore = 1;
await using (var cmd = new SqlCommand(hotOrgCte + @"
SELECT co.Id, co.DisplayName, co.Kind,
       CAST(ISNULL(v.Score, 0) AS DECIMAL(9,2)) AS Score,
       COUNT(DISTINCT ho.MpiId) AS Pursuits
FROM HotOrg ho
JOIN opportunities.CanonicalOrg co ON co.Id = ho.OrgId AND co.RetiredAtUtc IS NULL
LEFT JOIN opportunities.vDossierCompleteness v ON v.CanonicalOrgId = co.Id
GROUP BY co.Id, co.DisplayName, co.Kind, v.Score;", con) { CommandTimeout = 300 })
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
    {
        var score = (double)r.GetDecimal(3);
        if (score > maxScore) maxScore = score;
        nodes.Add(new
        {
            id = r.GetInt64(0),
            name = r.GetString(1),
            kind = r.GetString(2),
            score = Math.Round(score, 2),
            pursuits = r.GetInt32(4),
        });
    }
}

// edges (teaming co-occurrence on a live pursuit)
var links = new List<object>();
await using (var cmd = new SqlCommand(hotOrgCte + @"
SELECT a.OrgId AS Src, b.OrgId AS Dst, COUNT(DISTINCT a.MpiId) AS W
FROM HotOrg a
JOIN HotOrg b ON a.MpiId = b.MpiId AND a.OrgId < b.OrgId
GROUP BY a.OrgId, b.OrgId;", con) { CommandTimeout = 300 })
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        links.Add(new { source = r.GetInt64(0), target = r.GetInt64(1), w = r.GetInt32(2) });
}

Console.WriteLine($"nodes: {nodes.Count}  edges: {links.Count}  maxScore: {maxScore:N1}");

var json = JsonSerializer.Serialize(new { nodes, links });
var generated = "live KorOpportunitiesDb";
var html = HtmlTemplate
    .Replace("/*DATA*/", json)
    .Replace("{{MAXSCORE}}", maxScore.ToString("F2", CultureInfo.InvariantCulture))
    .Replace("{{NODES}}", nodes.Count.ToString())
    .Replace("{{EDGES}}", links.Count.ToString())
    .Replace("{{SRC}}", generated);

var outPath = args.Length > 0 ? args[0]
    : @"C:\VIsual Studio Projects\Operations\docs\BD-Teaming-HeatGraph.html";
File.WriteAllText(outPath, html, new UTF8Encoding(false));
Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length:N0} bytes) — open in a browser.");

static class Holder { }

partial class Program
{
    const string HtmlTemplate = @"<!DOCTYPE html>
<html><head><meta charset=""utf-8""><title>KOR BD — Teaming Heat-Graph</title>
<script src=""https://unpkg.com/force-graph""></script>
<style>
  body{margin:0;font-family:Calibri,Segoe UI,sans-serif;background:#0d1117;color:#e6edf3}
  #hdr{position:fixed;top:0;left:0;right:0;padding:10px 16px;background:#161b22;border-bottom:2px solid #602640;z-index:10}
  #hdr h1{margin:0;font-size:16px;color:#fff}
  #hdr .sub{font-size:11px;color:#8b949e}
  #legend{position:fixed;bottom:12px;left:12px;background:#161b22cc;padding:10px 12px;border-radius:6px;font-size:11px;z-index:10;line-height:1.7}
  .sw{display:inline-block;width:11px;height:11px;border-radius:50%;margin-right:5px;vertical-align:middle}
  #search{position:fixed;top:10px;right:16px;z-index:11;padding:5px 8px;border-radius:4px;border:1px solid #30363d;background:#0d1117;color:#e6edf3;width:220px}
  #g{position:absolute;top:0;left:0}
</style></head>
<body>
<div id=""hdr""><h1>KOR Structural — BD Teaming Heat-Graph</h1>
<div class=""sub"">Orgs on LIVE pursuits ({{NODES}} orgs, {{EDGES}} teaming links). Color = priority heat (Score, max {{MAXSCORE}}). Size = # live pursuits. Edge = co-on-a-pursuit. Source: {{SRC}} — regenerate to refresh.</div></div>
<input id=""search"" placeholder=""find an org…"">
<div id=""legend"">
  <div><span class=""sw"" style=""background:#d7191c""></span>hot — high priority + exposed (attack)</div>
  <div><span class=""sw"" style=""background:#fdae61""></span>warm</div>
  <div><span class=""sw"" style=""background:#5b8ff9""></span>cool — covered / lower leverage</div>
  <div><span class=""sw"" style=""background:#111;border:1px solid #888""></span>KOR Structural (us)</div>
  <div style=""margin-top:4px;color:#8b949e"">hover = detail · drag = pin · scroll = zoom</div>
</div>
<div id=""g""></div>
<script>
const data = /*DATA*/;
const maxScore = {{MAXSCORE}} || 1;
function heat(s){ const t=Math.max(0,Math.min(1, s/maxScore));
  // cool blue -> warm orange -> hot red
  if(t<0.5){ const u=t/0.5; return `rgb(${Math.round(91+164*u)},${Math.round(143+31*u)},${Math.round(249-152*u)})`; }
  const u=(t-0.5)/0.5; return `rgb(${Math.round(253-38*u)},${Math.round(174-149*u)},${Math.round(97-69*u)})`; }
const elem = document.getElementById('g');
const Graph = ForceGraph()(elem)
  .backgroundColor('#0d1117')
  .graphData(data)
  .nodeId('id')
  .nodelabel(n=>`<b>${n.name}</b><br>${n.kind} · score ${n.score} · ${n.pursuits} live pursuit(s)`)
  .nodeColor(n=> n.kind==='KorStructural' ? '#111' : heat(n.score))
  .nodeVal(n=> Math.max(1, n.pursuits))
  .nodeRelSize(4)
  .linkColor(()=> 'rgba(140,160,180,0.25)')
  .linkWidth(l=> Math.min(5, l.w))
  .nodeCanvasObjectMode(n=> (n.kind==='KorStructural'||n.score>maxScore*0.6) ? 'after':undefined)
  .nodeCanvasObject((n,ctx,scale)=>{ if(scale<1.3 && !(n.kind==='KorStructural')) return;
    ctx.font=`${Math.max(3,11/scale)}px Calibri`; ctx.fillStyle='#e6edf3'; ctx.textAlign='center';
    ctx.fillText(n.name.length>26?n.name.slice(0,25)+'…':n.name, n.x, n.y-Math.max(3,Math.sqrt(Math.max(1,n.pursuits))*4)-2); })
  .onNodeClick(n=>{ Graph.centerAt(n.x,n.y,600); Graph.zoom(3,600); });
function size(){ Graph.width(window.innerWidth).height(window.innerHeight); }
window.addEventListener('resize', size); size();
document.getElementById('search').addEventListener('input', e=>{ const q=e.target.value.toLowerCase();
  const hit=q?data.nodes.find(n=>n.name.toLowerCase().includes(q)):null;
  if(hit){ Graph.centerAt(hit.x,hit.y,600); Graph.zoom(4,600); } });
</script></body></html>";
}
