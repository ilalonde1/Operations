// BdHeatGraph — DYNAMIC BD visuals. Re-queries the live BD graph every run, so
// every enrichment pass + pursuit/verdict change is reflected on regeneration.
// Three modes (one tool, three views):
//   graph   (default) teaming heat-graph: orgs on live pursuits, heat=Score, edges=teaming
//   treemap           every priority org tiled by Importance, colored by heat = where's the leverage
//   cards             per-opportunity attack cards: each live pursuit's team, colored by KOR's path-in
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB not set");
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "graph";
string? outArg = args.Length > 1 ? args[1] : null;
const string Verdict = "COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),''))";

await using var con = new SqlConnection(cs);
await con.OpenAsync();

switch (mode)
{
    case "treemap": await Treemap(); break;
    case "cards":   await Cards(); break;
    default:        await Graph(); break;
}

// ===================== GRAPH =====================
async Task Graph()
{
    const string hotOrgCte = @"
WITH HotMpi AS (
    SELECT m.Id FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
    WHERE m.RetiredAtUtc IS NULL AND " + Verdict + @" IN (N'PURSUE', N'PURSUE_URGENT')),
HotOrg AS (
    SELECT DISTINCT x.OrgId, m.Id AS MpiId FROM opportunities.MajorProjectsInventory m
    JOIN HotMpi h ON h.Id = m.Id
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId),(m.GeneralContractorCanonicalOrgId),(m.StructuralEngineerCanonicalOrgId),(m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE x.OrgId IS NOT NULL)";
    var nodes = new List<object>(); double maxScore = 1;
    await using (var c = new SqlCommand(hotOrgCte + @"
SELECT co.Id, co.DisplayName, co.Kind, CAST(ISNULL(v.Score,0) AS DECIMAL(9,2)) AS Score, COUNT(DISTINCT ho.MpiId) AS Pursuits
FROM HotOrg ho JOIN opportunities.CanonicalOrg co ON co.Id=ho.OrgId AND co.RetiredAtUtc IS NULL
LEFT JOIN opportunities.vDossierCompleteness v ON v.CanonicalOrgId=co.Id
GROUP BY co.Id, co.DisplayName, co.Kind, v.Score;", con) { CommandTimeout = 300 })
    await using (var r = await c.ExecuteReaderAsync())
        while (await r.ReadAsync()) { var s=(double)r.GetDecimal(3); if(s>maxScore)maxScore=s;
            nodes.Add(new { id=r.GetInt64(0), name=r.GetString(1), kind=r.GetString(2), score=Math.Round(s,2), pursuits=r.GetInt32(4) }); }
    var links = new List<object>();
    await using (var c = new SqlCommand(hotOrgCte + @"
SELECT a.OrgId AS Src, b.OrgId AS Dst, COUNT(DISTINCT a.MpiId) AS W FROM HotOrg a
JOIN HotOrg b ON a.MpiId=b.MpiId AND a.OrgId<b.OrgId GROUP BY a.OrgId,b.OrgId;", con) { CommandTimeout = 300 })
    await using (var r = await c.ExecuteReaderAsync())
        while (await r.ReadAsync()) links.Add(new { source=r.GetInt64(0), target=r.GetInt64(1), w=r.GetInt32(2) });
    Console.WriteLine($"graph: {nodes.Count} nodes, {links.Count} edges, maxScore {maxScore:N1}");
    Write(outArg ?? Def("Teaming-HeatGraph"), GraphHtml
        .Replace("/*DATA*/", JsonSerializer.Serialize(new { nodes, links }))
        .Replace("{{MAXSCORE}}", maxScore.ToString("F2", CultureInfo.InvariantCulture))
        .Replace("{{NODES}}", nodes.Count.ToString()).Replace("{{EDGES}}", links.Count.ToString()));
}

// ===================== TREEMAP =====================
async Task Treemap()
{
    var rows = new List<object>(); double maxScore = 1;
    await using (var c = new SqlCommand(@"
SELECT TOP 160 DisplayName, Kind, CAST(Importance AS DECIMAL(9,2)) Imp, CAST(Score AS DECIMAL(9,2)) Score, PursuitLinks
FROM opportunities.vDossierCompleteness WHERE Score>0 ORDER BY Score DESC;", con) { CommandTimeout = 300 })
    await using (var r = await c.ExecuteReaderAsync())
        while (await r.ReadAsync()) { var s=(double)r.GetDecimal(3); if(s>maxScore)maxScore=s;
            rows.Add(new { name=r.GetString(0), kind=r.GetString(1), imp=Math.Round((double)r.GetDecimal(2),2), score=Math.Round(s,2), pursuits=r.GetInt32(4) }); }
    Console.WriteLine($"treemap: {rows.Count} orgs, maxScore {maxScore:N1}");
    Write(outArg ?? Def("Priority-Treemap"), TreemapHtml
        .Replace("/*DATA*/", JsonSerializer.Serialize(rows))
        .Replace("{{MAXSCORE}}", maxScore.ToString("F2", CultureInfo.InvariantCulture))
        .Replace("{{N}}", rows.Count.ToString()));
}

// ===================== CARDS =====================
async Task Cards()
{
    var cards = new List<object>();
    var sql = $@"
SELECT m.Id, m.ProjectName, m.Province, {Verdict} AS Verdict, m.EstimatedCostCad,
  arch.DisplayName AS Arch,
  (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId=arch.Id AND a.RetiredAtUtc IS NULL) AS ArchPeople,
  se.DisplayName AS Se, CASE WHEN se.Kind=N'KorStructural' THEN 1 ELSE 0 END AS SeIsKor,
  gc.DisplayName AS Gc, COALESCE(prop.DisplayName, m.ProponentName) AS Owner
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
LEFT JOIN opportunities.CanonicalOrg arch ON arch.Id=m.ArchitectCanonicalOrgId AND arch.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg se ON se.Id=m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg gc ON gc.Id=m.GeneralContractorCanonicalOrgId AND gc.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg prop ON prop.Id=m.ProponentCanonicalOrgId AND prop.RetiredAtUtc IS NULL
WHERE m.RetiredAtUtc IS NULL AND {Verdict} IN (N'PURSUE', N'PURSUE_URGENT')
ORDER BY CASE WHEN {Verdict}=N'PURSUE_URGENT' THEN 0 ELSE 1 END, m.EstimatedCostCad DESC;";
    await using (var c = new SqlCommand(sql, con) { CommandTimeout = 300 })
    await using (var r = await c.ExecuteReaderAsync())
        while (await r.ReadAsync())
            cards.Add(new {
                id = r.GetInt64(0), project = r.GetString(1),
                province = r.IsDBNull(2) ? "" : r.GetString(2),
                verdict = r.IsDBNull(3) ? "" : r.GetString(3),
                cost = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                arch = r.IsDBNull(5) ? null : r.GetString(5),
                archPeople = r.GetInt32(6),
                se = r.IsDBNull(7) ? null : r.GetString(7),
                seIsKor = r.GetInt32(8) == 1,
                gc = r.IsDBNull(9) ? null : r.GetString(9),
                owner = r.IsDBNull(10) ? null : r.GetString(10),
            });
    Console.WriteLine($"cards: {cards.Count} live pursuits");
    Write(outArg ?? Def("Opportunity-AttackCards"), CardsHtml
        .Replace("/*DATA*/", JsonSerializer.Serialize(cards)).Replace("{{N}}", cards.Count.ToString()));
}

static string Def(string name) => $@"C:\VIsual Studio Projects\Operations\docs\BD-{name}.html";
static void Write(string path, string html) { File.WriteAllText(path, html, new UTF8Encoding(false));
    Console.WriteLine($"wrote {path} ({new FileInfo(path).Length:N0} bytes) — open in a browser."); }

partial class Program
{
    const string Css = @"body{margin:0;font-family:Calibri,Segoe UI,sans-serif;background:#0d1117;color:#e6edf3}
  #hdr{padding:10px 16px;background:#161b22;border-bottom:2px solid #602640}
  #hdr h1{margin:0;font-size:16px;color:#fff} #hdr .sub{font-size:11px;color:#8b949e}";

    const string GraphHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>KOR BD — Teaming Heat-Graph</title>
<script src=""https://unpkg.com/force-graph""></script>
<style>" + Css + @"
  #legend{position:fixed;bottom:12px;left:12px;background:#161b22cc;padding:10px;border-radius:6px;font-size:11px;line-height:1.7}
  .sw{display:inline-block;width:11px;height:11px;border-radius:50%;margin-right:5px}
  #search{position:fixed;top:10px;right:16px;padding:5px 8px;border-radius:4px;border:1px solid #30363d;background:#0d1117;color:#e6edf3;width:220px}
  #g{position:absolute;top:54px;left:0}</style></head><body>
<div id=""hdr""><h1>KOR Structural — BD Teaming Heat-Graph</h1>
<div class=""sub"">Orgs on LIVE pursuits ({{NODES}} orgs, {{EDGES}} teaming links). Color=heat (Score, max {{MAXSCORE}}). Size=#pursuits. Edge=co-on-a-pursuit. Live DB — regenerate to refresh.</div></div>
<input id=""search"" placeholder=""find an org…"">
<div id=""legend""><div><span class=""sw"" style=""background:#d7191c""></span>hot — attack</div>
<div><span class=""sw"" style=""background:#fdae61""></span>warm</div><div><span class=""sw"" style=""background:#5b8ff9""></span>cool</div>
<div><span class=""sw"" style=""background:#111;border:1px solid #888""></span>KOR (us)</div></div>
<div id=""g""></div><script>
const data=/*DATA*/;const maxScore={{MAXSCORE}}||1;
function heat(s){const t=Math.max(0,Math.min(1,s/maxScore));if(t<0.5){const u=t/0.5;return `rgb(${Math.round(91+164*u)},${Math.round(143+31*u)},${Math.round(249-152*u)})`;}const u=(t-0.5)/0.5;return `rgb(${Math.round(253-38*u)},${Math.round(174-149*u)},${Math.round(97-69*u)})`;}
const el=document.getElementById('g');const G=ForceGraph()(el).backgroundColor('#0d1117').graphData(data).nodeId('id')
.nodelabel(n=>`<b>${n.name}</b><br>${n.kind} · score ${n.score} · ${n.pursuits} live pursuit(s)`)
.nodeColor(n=>n.kind==='KorStructural'?'#111':heat(n.score)).nodeVal(n=>Math.max(1,n.pursuits)).nodeRelSize(4)
.linkColor(()=>'rgba(140,160,180,0.25)').linkWidth(l=>Math.min(5,l.w))
.nodeCanvasObjectMode(n=>(n.kind==='KorStructural'||n.score>maxScore*0.6)?'after':undefined)
.nodeCanvasObject((n,ctx,sc)=>{if(sc<1.3&&n.kind!=='KorStructural')return;ctx.font=`${Math.max(3,11/sc)}px Calibri`;ctx.fillStyle='#e6edf3';ctx.textAlign='center';ctx.fillText(n.name.length>26?n.name.slice(0,25)+'…':n.name,n.x,n.y-Math.max(3,Math.sqrt(Math.max(1,n.pursuits))*4)-2);})
.onNodeClick(n=>{G.centerAt(n.x,n.y,600);G.zoom(3,600);});
function sz(){G.width(window.innerWidth).height(window.innerHeight-54);}window.addEventListener('resize',sz);sz();
document.getElementById('search').addEventListener('input',e=>{const q=e.target.value.toLowerCase();const h=q?data.nodes.find(n=>n.name.toLowerCase().includes(q)):null;if(h){G.centerAt(h.x,h.y,600);G.zoom(4,600);}});
</script></body></html>";

    const string TreemapHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>KOR BD — Priority Treemap</title>
<script src=""https://d3js.org/d3.v7.min.js""></script><style>" + Css + @"
  #t{padding:8px} .tile{position:absolute;overflow:hidden;border:1px solid #0d1117;box-sizing:border-box;padding:3px 5px;font-size:10px;color:#0d1117;cursor:default}
  .tile b{font-size:11px} #legend{position:fixed;bottom:10px;right:12px;font-size:11px;color:#8b949e}</style></head><body>
<div id=""hdr""><h1>KOR Structural — BD Priority Treemap (where's the leverage)</h1>
<div class=""sub"">Top {{N}} orgs by priority. Tile size = Importance (kind + live-pursuit weight). Color = heat (Score, max {{MAXSCORE}}). Grouped by kind. Live DB — regenerate to refresh.</div></div>
<div id=""t""></div><div id=""legend"">cool ▢▢▢ hot — bigger+redder = more leverage</div><script>
const data=/*DATA*/;const maxScore={{MAXSCORE}}||1;
function heat(s){const t=Math.max(0,Math.min(1,s/maxScore));const r=Math.round(60+195*t),g=Math.round(130-70*t),b=Math.round(200-160*t);return `rgb(${r},${g},${b})`;}
function draw(){const W=window.innerWidth-16,H=window.innerHeight-70;
const root=d3.hierarchy({children:Array.from(d3.group(data,d=>d.kind),([k,v])=>({name:k,children:v}))}).sum(d=>d.imp||0).sort((a,b)=>b.value-a.value);
d3.treemap().size([W,H]).paddingInner(1).paddingTop(14)(root);
const t=d3.select('#t');t.selectAll('*').remove();
t.selectAll('div.grp').data(root.children).enter().append('div').attr('class','tile').style('left',d=>d.x0+'px').style('top',d=>d.y0+'px').style('width',d=>(d.x1-d.x0)+'px').style('height',d=>(d.y1-d.y0)+'px').style('background','#161b22').style('color','#8b949e').style('border','1px solid #30363d').html(d=>'<b>'+d.data.name+'</b>');
t.selectAll('div.leaf').data(root.leaves()).enter().append('div').attr('class','tile').style('left',d=>d.x0+'px').style('top',d=>d.y0+'px').style('width',d=>(d.x1-d.x0)+'px').style('height',d=>(d.y1-d.y0)+'px').style('background',d=>heat(d.data.score)).attr('title',d=>d.data.name+' — '+d.data.kind+' · score '+d.data.score+' · '+d.data.pursuits+' pursuits').html(d=>((d.x1-d.x0>54&&d.y1-d.y0>22)?('<b>'+(d.data.name.length>24?d.data.name.slice(0,23)+'…':d.data.name)+'</b><br>'+d.data.score):''));}
window.addEventListener('resize',draw);draw();</script></body></html>";

    const string CardsHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>KOR BD — Opportunity Attack Cards</title>
<style>" + Css + @"
  #wrap{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:10px;padding:12px}
  .card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:10px 12px;border-left:4px solid #5b8ff9}
  .card.urgent{border-left-color:#d7191c}
  .pname{font-size:13px;font-weight:bold;color:#fff;margin-bottom:2px} .meta{font-size:10px;color:#8b949e;margin-bottom:7px}
  .row{display:flex;justify-content:space-between;align-items:center;font-size:11px;padding:2px 0}
  .role{color:#8b949e;width:64px} .who{flex:1;text-align:right}
  .pill{display:inline-block;padding:1px 6px;border-radius:9px;font-size:9px;margin-left:6px;color:#0d1117;font-weight:bold}
  #f{margin:0 12px;padding:5px 8px;border-radius:4px;border:1px solid #30363d;background:#0d1117;color:#e6edf3;width:240px}</style></head><body>
<div id=""hdr""><h1>KOR Structural — Opportunity Attack Cards</h1>
<div class=""sub"">{{N}} live pursuits (PURSUE_URGENT first). Per deal: the team + KOR's path-in. <b style=""color:#3fb950"">green</b>=warm (we know them) · <b style=""color:#d7191c"">red</b>=incumbent SE to displace · <b style=""color:#d29922"">amber</b>=open seat · grey=unknown. Live DB.</div></div>
<input id=""f"" placeholder=""filter pursuits…""><div id=""wrap""></div><script>
const data=/*DATA*/;
function cad(v){if(v==null)return '';if(v>=1e9)return '$'+(v/1e9).toFixed(1)+'B';if(v>=1e6)return '$'+(v/1e6).toFixed(0)+'M';return '$'+Math.round(v/1e3)+'K';}
function pill(t,c){return `<span class=""pill"" style=""background:${c}"">${t}</span>`;}
function arch(c){if(!c.arch)return '<span style=""color:#6e7681"">— unknown route</span>';return c.arch+(c.archPeople>0?pill('warm','#3fb950'):pill('cold','#6e7681'));}
function se(c){if(c.seIsKor)return (c.se||'KOR')+pill('OURS','#3fb950');if(c.se)return c.se+pill('displace','#d7191c');return '<span style=""color:#d29922"">open seat</span>'+pill('SEAT','#d29922');}
function org(n){return n?n:'<span style=""color:#6e7681"">—</span>';}
function render(list){const w=document.getElementById('wrap');w.innerHTML=list.map(c=>`
<div class=""card ${c.verdict==='PURSUE_URGENT'?'urgent':''}"">
  <div class=""pname"">${c.project}</div>
  <div class=""meta"">${c.verdict} · ${c.province} · ${cad(c.cost)}</div>
  <div class=""row""><span class=""role"">Architect</span><span class=""who"">${arch(c)}</span></div>
  <div class=""row""><span class=""role"">Structural</span><span class=""who"">${se(c)}</span></div>
  <div class=""row""><span class=""role"">GC</span><span class=""who"">${org(c.gc)}</span></div>
  <div class=""row""><span class=""role"">Owner</span><span class=""who"">${org(c.owner)}</span></div>
</div>`).join('');}
render(data);
document.getElementById('f').addEventListener('input',e=>{const q=e.target.value.toLowerCase();render(q?data.filter(c=>(c.project+' '+(c.arch||'')+' '+(c.owner||'')).toLowerCase().includes(q)):data);});
</script></body></html>";
}
