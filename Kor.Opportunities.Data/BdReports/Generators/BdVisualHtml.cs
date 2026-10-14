#nullable enable
using System.Globalization;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Self-contained HTML for the three BD visual reports (teaming attack-graph,
/// priority treemap, opportunity attack cards). Data is serialized in by the
/// report service from live, CHEAP queries; these methods just template it.
/// Rendered in the app's WebView2 preview (Chromium) and exported to Word/PDF.
///
/// Design (set with Ian 2026-06-23): this is an ATTACK map, so —
///  • HEAT = opportunity value (sum of pursuit cost x urgency), NOT dossier
///    completeness. The thing to chase is the big urgent money, full stop.
///  • WARMTH (do we already know people there) is its OWN channel: a green ring
///    on the graph, green tiles on the treemap, a green pill on the cards.
///  • Light, KOR-branded theme to sit beside the other in-app reports — no more
///    black-on-black.
/// Graph/treemap pull their layout libs from a CDN; the app host has internet.
/// </summary>
public static class BdVisualHtml
{
    public static string Graph(string nodesLinksJson, double maxAttack, int nodes, int edges,
        int? costed = null, int? total = null) => GraphHtml
        .Replace("{{COVERAGE}}", BdReportFormatting.CostCoverage(costed, total))
        .Replace("/*DATA*/", nodesLinksJson)
        .Replace("{{MAXATTACK}}", maxAttack.ToString("F2", CultureInfo.InvariantCulture))
        .Replace("{{NODES}}", nodes.ToString(CultureInfo.InvariantCulture))
        .Replace("{{EDGES}}", edges.ToString(CultureInfo.InvariantCulture));

    public static string Treemap(string rowsJson, int n, int? costed = null, int? total = null) => TreemapHtml
        .Replace("{{COVERAGE}}", BdReportFormatting.CostCoverage(costed, total))
        .Replace("/*DATA*/", rowsJson)
        .Replace("{{N}}", n.ToString(CultureInfo.InvariantCulture));

    private const string CardsDataStart = "<script type=\"application/json\" id=\"attack-cards-data\">";

    public static string Cards(string cardsJson, int n)
    {
        using var data = JsonDocument.Parse(cardsJson);
        if (data.RootElement.GetArrayLength() != n)
            throw new ArgumentException("Card count must match the report snapshot.", nameof(n));
        // Default JSON escaping protects the script boundary, including names containing </script>.
        var safeJson = JsonSerializer.Serialize(data.RootElement);
        return CardsHtml.Replace("{{N}}", n.ToString(CultureInfo.InvariantCulture))
            .Replace("/*DATA*/", "JSON.parse(document.getElementById('attack-cards-data').textContent)")
            .Replace("</head>", CardsDataStart + safeJson + "</script></head>");
    }

    /// <summary>Full Word content from the exact preview snapshot; browser filters/scroll do not limit it.</summary>
    public static BdReportDocument CardsDocument(string html)
    {
        var start = html.IndexOf(CardsDataStart, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Attack Cards snapshot is missing; regenerate the report.");
        start += CardsDataStart.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("Attack Cards snapshot is incomplete.");
        using var data = JsonDocument.Parse(html.Substring(start, end - start));
        var blocks = new List<BdReportBlock>
        {
            new HeadingBlock(1, "Opportunity Attack Cards"),
            new ParagraphBlock($"{data.RootElement.GetArrayLength()} live pursuits — full generated snapshot, including cards hidden by preview filtering. Word uses a text layout."),
            new ItalicNoteBlock("Recorded team links; research contacts do not establish KOR relationships.")
        };
        foreach (var card in data.RootElement.EnumerateArray())
        {
            string Text(string key) => card.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ToString() : "unknown";
            var arch = Text("arch");
            var contacts = card.TryGetProperty("archPeople", out var people) && people.ValueKind == JsonValueKind.Number && people.TryGetInt32(out var count) && count > 0;
            var isKor = card.TryGetProperty("seIsKor", out var kor) && kor.ValueKind == JsonValueKind.True;
            var se = Text("se");
            var cost = card.TryGetProperty("cost", out var amount) && amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var dollars)
                ? dollars.ToString("C0", CultureInfo.GetCultureInfo("en-CA")) : "not stated";
            blocks.Add(new HeadingBlock(2, Text("project")));
            blocks.Add(new ParagraphBlock($"{Text("verdict")} · {Text("province")} · Cost: {cost}"));
            blocks.Add(new LabelValueBlock("Architect: ", arch + (contacts ? " — research contacts recorded; KOR relationship unknown" : " — KOR relationship unknown")));
            blocks.Add(new LabelValueBlock("Structural: ", se + (isKor ? " — recorded KOR" : se == "unknown" ? " — seat status unknown" : " — recorded SE")));
            blocks.Add(new LabelValueBlock("GC: ", Text("gc")));
            blocks.Add(new LabelValueBlock("Owner: ", Text("owner")));
        }
        return new BdReportDocument("Opportunity Attack Cards", blocks);
    }

    // KOR maroon #602640; light surface; dark ink. Shared chrome for all three.
    private const string Css = @"body{margin:0;font-family:Calibri,Segoe UI,sans-serif;background:#ffffff;color:#1f2328}
  #hdr{padding:10px 16px;background:#602640;border-bottom:3px solid #4a1d31}
  #hdr h1{margin:0;font-size:16px;color:#ffffff;font-weight:600} #hdr .sub{font-size:11px;color:#f0d8e2;margin-top:2px}
  .legend{position:fixed;background:#ffffffea;border:1px solid #d0d7de;padding:9px 11px;border-radius:6px;font-size:11px;line-height:1.7;box-shadow:0 1px 4px #0002}
  .sw{display:inline-block;width:11px;height:11px;border-radius:50%;margin-right:6px;vertical-align:middle}
  .ring{display:inline-block;width:10px;height:10px;border-radius:50%;border:2px solid #1a7f37;margin-right:6px;vertical-align:middle}
  input.find{padding:5px 8px;border-radius:4px;border:1px solid #d0d7de;background:#fff;color:#1f2328}";

    // Cool slate -> amber -> KOR red, readable on white. t in [0,1].
    private const string HeatFn = @"function heat(t){t=Math.max(0,Math.min(1,t));if(t<0.5){var u=t/0.5;return `rgb(${Math.round(159+73*u)},${Math.round(179-16*u)},${Math.round(200-139*u)})`;}var u=(t-0.5)/0.5;return `rgb(${Math.round(232-40*u)},${Math.round(163-106*u)},${Math.round(61-18*u)})`;}";

    // HTML-encode DB-sourced strings before DOM insertion. Org/project names
    // originate from scraped external sources — a hostile name would otherwise
    // execute in the WebView2 preview (audit 2026-07-01 M10). JSON serialization
    // protects the /*DATA*/ slot; this protects the innerHTML/template-literal sinks.
    private const string EscFn = @"function esc(s){return s==null?'':String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;');}";

    private const string GraphHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>Teaming Attack-Graph</title>
<script src=""https://unpkg.com/force-graph""></script><style>" + Css + @"
  #legend{bottom:12px;left:12px} #search{position:fixed;top:10px;right:16px;width:220px}
  /* force-graph's hover tooltip defaults to a near-black bg; force it light so the dark label text is readable */
  .graph-tooltip{background:#ffffff !important;color:#1f2328 !important;border:1px solid #c0392b;border-radius:6px;padding:7px 10px !important;box-shadow:0 2px 10px #0004;font:12px Calibri,Segoe UI,sans-serif !important;max-width:280px}
  #g{position:absolute;top:56px;left:0}</style></head><body>
<div id=""hdr""><h1>KOR Structural — BD Teaming Attack-Graph</h1>
<div class=""sub"">Orgs on LIVE pursuits ({{NODES}} orgs, {{EDGES}} teaming links). Color = known-cost heat (pursuit $ &times; urgency), an approximate ranking. Full pursuit pool: {{COVERAGE}}. Unknown costs contribute no heat; orgs overlap. Size = # pursuits. Green ring = research contacts recorded; KOR relationship unknown. Edge = co-on-a-pursuit.</div></div>
<input id=""search"" class=""find"" placeholder=""find an org…"">
<div id=""legend""><div><span class=""sw"" style=""background:rgb(192,57,43)""></span>hot — big urgent money</div>
<div><span class=""sw"" style=""background:rgb(232,163,61)""></span>warm money</div><div><span class=""sw"" style=""background:rgb(159,179,200)""></span>smaller</div>
<div><span class=""ring""></span>green ring = research contacts recorded</div>
<div><span class=""sw"" style=""background:#602640""></span>KOR (us)</div></div>
<div id=""g""></div><script>
const data=/*DATA*/;const maxAttack={{MAXATTACK}}||1;
" + HeatFn + @"
" + EscFn + @"
const el=document.getElementById('g');const G=ForceGraph()(el).backgroundColor('#ffffff').graphData(data).nodeId('id')
.nodeLabel(n=>`<div style='font:12px Calibri;color:#1f2328'><b>${esc(n.name)}</b><br>${esc(n.kind)} · $${n.attack}M known-cost weighted subtotal · ${n.costed??'unknown'}/${n.pursuits} pursuits costed · ${n.warm?'research contacts recorded; KOR relationship unknown':'no research contacts recorded; KOR relationship unknown'}</div>`)
.nodeColor(n=>n.kind==='KorStructural'?'#602640':heat(n.attack/maxAttack)).nodeVal(n=>Math.max(1,n.pursuits)).nodeRelSize(5)
.linkColor(()=>'rgba(120,130,140,0.22)').linkWidth(l=>Math.min(5,l.w))
.nodeCanvasObjectMode(()=>'after')
.nodeCanvasObject((n,ctx,sc)=>{const rad=Math.sqrt(Math.max(1,n.pursuits))*5;
  if(n.warm&&n.kind!=='KorStructural'){ctx.beginPath();ctx.arc(n.x,n.y,rad+1.5,0,2*Math.PI);ctx.lineWidth=2/sc;ctx.strokeStyle='#1a7f37';ctx.stroke();}
  if(sc<1.2&&n.kind!=='KorStructural'&&n.attack<maxAttack*0.5)return;
  ctx.font=`${Math.max(3,11/sc)}px Calibri`;ctx.fillStyle='#1f2328';ctx.textAlign='center';
  ctx.fillText(n.name.length>26?n.name.slice(0,25)+'…':n.name,n.x,n.y-rad-2);})
.onNodeClick(n=>{G.centerAt(n.x,n.y,600);G.zoom(3,600);});
function sz(){G.width(window.innerWidth).height(window.innerHeight-56);}window.addEventListener('resize',sz);sz();
document.getElementById('search').addEventListener('input',e=>{const q=e.target.value.toLowerCase();const h=q?data.nodes.find(n=>n.name.toLowerCase().includes(q)):null;if(h){G.centerAt(h.x,h.y,600);G.zoom(4,600);}});
</script></body></html>";

    private const string TreemapHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>Priority Treemap</title>
<script src=""https://d3js.org/d3.v7.min.js""></script><style>" + Css + @"
  #t{padding:8px} .tile{position:absolute;overflow:hidden;border:1px solid #ffffff;box-sizing:border-box;padding:3px 5px;font-size:10px}
  .tile b{font-size:11px} #legend{bottom:12px;right:12px}
  /* shared, readable tooltip — same light card as the graph's .graph-tooltip */
  #tip{position:fixed;display:none;pointer-events:none;background:#ffffff;color:#1f2328;border:1px solid #c0392b;border-radius:6px;padding:7px 10px;box-shadow:0 2px 10px #0004;font:12px Calibri,Segoe UI,sans-serif;max-width:280px;z-index:9}</style></head><body>
<div id=""tip""></div>
<div id=""hdr""><h1>KOR Structural — BD Priority Treemap (where the money is)</h1>
<div class=""sub"">Top {{N}} orgs on live pursuits (retrieval capped at 160). Tile size = known-cost heat (pursuit $ &times; urgency), an approximate ranking. Full pursuit pool: {{COVERAGE}}. Unknown costs contribute no heat; zero-value tiles have a visibility minimum. Org values overlap. <b style=""color:#caffd5"">Green</b> = research contacts recorded · <b style=""color:#ffd5cf"">red</b> = no research contacts recorded. Grouped by kind. Contact presence does not establish a KOR relationship.</div></div>
<div id=""t""></div><div class=""legend"" id=""legend""><span class=""sw"" style=""background:#2da44e""></span>research contacts &nbsp; <span class=""sw"" style=""background:#c0392b""></span>no research contacts — bigger = higher known-cost weighted subtotal</div><script>
const data=/*DATA*/;
" + EscFn + @"
function draw(){const W=window.innerWidth-16,H=window.innerHeight-72;
const root=d3.hierarchy({children:Array.from(d3.group(data,d=>d.kind),([k,v])=>({name:k,children:v}))}).sum(d=>d.attack||0.01).sort((a,b)=>b.value-a.value);
d3.treemap().size([W,H]).paddingInner(1).paddingTop(15)(root);const t=d3.select('#t');t.selectAll('*').remove();
t.selectAll('div.g').data(root.children).enter().append('div').attr('class','tile').style('left',d=>d.x0+'px').style('top',d=>d.y0+'px').style('width',d=>(d.x1-d.x0)+'px').style('height',d=>(d.y1-d.y0)+'px').style('background','#eaeef2').style('color','#57606a').style('border','1px solid #d0d7de').html(d=>'<b>'+esc(d.data.name)+'</b>');
t.selectAll('div.l').data(root.leaves()).enter().append('div').attr('class','tile').style('left',d=>d.x0+'px').style('top',d=>d.y0+'px').style('width',d=>(d.x1-d.x0)+'px').style('height',d=>(d.y1-d.y0)+'px').style('background',d=>d.data.warm?'#2da44e':'#c0392b').style('color','#fff').on('mousemove',function(ev,d){const tip=document.getElementById('tip');tip.style.display='block';tip.style.left=(ev.clientX+14)+'px';tip.style.top=(ev.clientY+14)+'px';tip.innerHTML='<b>'+esc(d.data.name)+'</b><br>'+esc(d.data.kind)+' · $'+d.data.attack+'M known-cost weighted subtotal · '+(d.data.costed??'unknown')+'/'+d.data.pursuits+' pursuits costed<br>'+(d.data.warm?'research contacts recorded; KOR relationship unknown':'no research contacts recorded; KOR relationship unknown');}).on('mouseleave',function(){document.getElementById('tip').style.display='none';}).html(d=>((d.x1-d.x0>56&&d.y1-d.y0>24)?('<b>'+esc(d.data.name.length>24?d.data.name.slice(0,23)+'…':d.data.name)+'</b><br>$'+d.data.attack+'M'):''));}
window.addEventListener('resize',draw);draw();</script></body></html>";

    private const string CardsHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>Opportunity Attack Cards</title><style>" + Css + @"
  #wrap{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:10px;padding:12px}
  .card{background:#ffffff;border:1px solid #d0d7de;border-radius:8px;padding:10px 12px;border-left:4px solid #602640;box-shadow:0 1px 3px #0001}
  .card.urgent{border-left-color:#c0392b}
  .pname{font-size:13px;font-weight:bold;color:#1f2328;margin-bottom:2px} .meta{font-size:10px;color:#57606a;margin-bottom:7px}
  .row{display:flex;justify-content:space-between;font-size:11px;padding:2px 0;border-top:1px solid #f0f2f5} .role{color:#57606a;width:64px} .who{flex:1;text-align:right;color:#1f2328}
  .pill{display:inline-block;padding:1px 6px;border-radius:9px;font-size:9px;margin-left:6px;color:#fff;font-weight:bold}
  #f{margin:0 12px;width:240px}</style></head><body>
<div id=""hdr""><h1 id=""card-title"">KOR Structural — Opportunity Attack Cards</h1>
<div class=""sub""><span id=""card-count"">{{N}} live pursuits — full generated snapshot</span> (urgent first). PDF includes the displayed cards; Word includes the full snapshot. Per deal: recorded team links; KOR relationships are not established by research contact counts. <b style=""color:#caffd5"">green</b>=research contacts recorded · <b style=""color:#ffd5cf"">red</b>=recorded structural firm · <b style=""color:#ffe8c2"">amber</b>=seat status unknown.</div></div>
<input id=""f"" class=""find"" placeholder=""filter pursuits…""><div id=""wrap""></div><script>
const data=/*DATA*/;
" + EscFn + @"
function cad(v){if(v==null)return '';if(v>=1e9)return '$'+(v/1e9).toFixed(1)+'B';if(v>=1e6)return '$'+(v/1e6).toFixed(0)+'M';return '$'+Math.round(v/1e3)+'K';}
function pill(t,c){return `<span class=""pill"" style=""background:${c}"">${t}</span>`;}
function arch(c){if(!c.arch)return '<span style=""color:#8b949e"">— unknown route</span>';return esc(c.arch)+(c.archPeople>0?pill('research contacts','#2da44e'):pill('relationship unknown','#8b949e'));}
function se(c){if(c.seIsKor)return esc(c.se||'KOR')+pill('recorded KOR','#2da44e');if(c.se)return esc(c.se)+pill('recorded SE','#c0392b');return '<span style=""color:#9a6700"">seat status unknown</span>'+pill('SEAT','#d29922');}
function org(n){return n?esc(n):'<span style=""color:#8b949e"">—</span>';}
function render(list,query){
const title='KOR Structural — Opportunity Attack Cards'+(query?' — filtered view':'');
document.getElementById('card-count').textContent=query?`${list.length} of ${data.length} live pursuits — filtered by: ${query}`:`${list.length} live pursuits — full generated snapshot`;
document.getElementById('card-title').textContent=title;document.title=title;
document.getElementById('wrap').innerHTML=list.map(c=>`
<div class=""card ${c.verdict==='PURSUE_URGENT'?'urgent':''}""><div class=""pname"">${esc(c.project)}</div>
<div class=""meta"">${esc(c.verdict)} · ${esc(c.province)} · ${cad(c.cost)}</div>
<div class=""row""><span class=""role"">Architect</span><span class=""who"">${arch(c)}</span></div>
<div class=""row""><span class=""role"">Structural</span><span class=""who"">${se(c)}</span></div>
<div class=""row""><span class=""role"">GC</span><span class=""who"">${org(c.gc)}</span></div>
<div class=""row""><span class=""role"">Owner</span><span class=""who"">${org(c.owner)}</span></div></div>`).join('');}
render(data,'');document.getElementById('f').addEventListener('input',e=>{const q=e.target.value.trim().toLowerCase();render(q?data.filter(c=>(c.project+' '+(c.arch||'')+' '+(c.owner||'')).toLowerCase().includes(q)):data,q);});
</script></body></html>";
}
