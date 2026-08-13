"""Render the two map figures for the Namdar fee proposal.

A screenshot of /projects/ would carry the site's navigation and dark hero into
a client document, so these are drawn standalone from the same live GeoJSON the
website uses -- same data, no chrome, and sized for print rather than a browser.

Figure 1: KOR's US portfolio.
Figure 2: downtown San Diego, where the two proposal sites are, with 1355
          Broadway and 901 Park Blvd marked so Namdar can see our work sits
          around their blocks.
"""
import io, json, html, subprocess, sys, urllib.request

S = r"C:/Users/ilalonde/AppData/Local/Temp/claude/C--VIsual-Studio-Projects-Operations/912461f4-d333-42a6-8a2a-c879ddd0d90b/scratchpad"
OUT = S + "/namdar"
DATA = "https://www.korstructural.com/wp-content/uploads/kor-map-data.json"
EDGE = r"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"

# The two proposal sites, from the RFP.
# Geocoded 2026-08-07, not estimated -- the two blocks sit about 90 m apart in
# East Village, so the labels are pushed to opposite sides to stay legible.
SITES = [
    ("1355 Broadway", 32.71550, -117.15234, "right"),
    ("901 Park Blvd", 32.71508, -117.15332, "left"),
]

PAGE = """<meta charset="utf-8">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<style>
  html,body{margin:0;padding:0;background:#fff;font-family:Segoe UI,Arial,sans-serif}
  #map{width:__W__px;height:__H__px}
  .leaflet-container{background:#f2f2f0}
  .kor-cap{background:rgba(255,255,255,.95);white-space:nowrap;
           border-left:4px solid #ff5c35;padding:9px 16px;font-size:15px;color:#2f3338;
           box-shadow:0 1px 6px rgba(0,0,0,.18)}
  .kor-cap b{color:#ff5c35}
  .kor-key{margin-top:5px;font-size:12.5px;color:#4a4f55}
  .kor-key i{display:inline-block;width:10px;height:10px;border-radius:50%;margin:0 5px 0 12px;
             vertical-align:middle}
  .kor-key i:first-child{margin-left:0}
  .kor-site{background:#1f2e3c!important;color:#fff!important;font-size:13px;font-weight:700;
            border:0!important;padding:4px 9px;border-radius:3px;box-shadow:0 1px 4px rgba(0,0,0,.35)}
  .kor-site:before{display:none!important}
</style>
<div id="map"></div>
<script>
var DATA = __DATA__, SITES = __SITES__, CAPTION = "__CAP__";
var map = L.map('map', {zoomControl:false, attributionControl:true, preferCanvas:true});
L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
  {maxZoom:19, attribution:'&copy; OpenStreetMap, &copy; CARTO'}).addTo(map);
var pts = [];
// Orange is KOR's own work; green is Jim DesRoches' pre-KOR experience. They
// are NOT the same claim and must not share a colour on a fee proposal.
DATA.forEach(function(f){
  var c = f.geometry.coordinates;
  pts.push([c[1], c[0]]);
  L.circleMarker([c[1], c[0]], {radius:__R__, color:'#ffffff', weight:1.2,
      fillColor: f.era === 'PRIOR' ? '#2fa36b' : '#ff5c35', fillOpacity:.85}).addTo(map);
});
SITES.forEach(function(s){
  L.circleMarker([s[1], s[2]], {radius:11, color:'#ffffff', weight:2.5,
      fillColor:'#1f2e3c', fillOpacity:1}).addTo(map);
  L.marker([s[1], s[2]], {opacity:0})
    .addTo(map)
    .bindTooltip(s[0], {permanent:true, direction:s[3], offset:s[3]==='left'?[-14,0]:[14,0],
                        className:'kor-site'})
    .openTooltip();
  pts.push([s[1], s[2]]);
});
map.fitBounds(pts, {padding:[__P__,__P__], maxZoom:__MZ__});
var cap = L.control({position:'bottomleft'});
cap.onAdd = function(){ var d=L.DomUtil.create('div','kor-cap'); d.innerHTML=CAPTION; return d; };
cap.addTo(map);
document.title = 'ready';
</script>"""


def build(name, feats, width, height, radius, pad, maxzoom, caption, sites):
    page = (PAGE.replace("__W__", str(width)).replace("__H__", str(height))
                .replace("__R__", str(radius)).replace("__P__", str(pad))
                .replace("__MZ__", str(maxzoom)).replace("__CAP__", caption)
                .replace("__SITES__", json.dumps(sites))
                .replace("__DATA__", json.dumps(feats)))
    src = OUT + "/" + name + ".html"
    io.open(src, "w", encoding="utf-8").write(page)
    png = OUT + "/" + name + ".png"
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--no-sandbox", "--hide-scrollbars",
                    "--force-device-scale-factor=2",
                    "--user-data-dir=" + S + "/edge_fig_" + name,
                    "--window-size=%d,%d" % (width, height),
                    "--virtual-time-budget=45000", "--screenshot=" + png,
                    "file:///" + src.replace("\\", "/")],
                   capture_output=True)
    return png


if __name__ == "__main__":
    # Jim's own Keep/Delete decisions (KOR-CA-Portfolio-Projects JD.xlsx,
    # 10 Aug) are the source, not the live map and not Deltek. He went through
    # all 75 rows and marked which projects KOR actually got. Nothing derivable
    # reproduces that: Deltek has no won/lost flag, a lost pursuit still carries
    # proposal time, and we invoice proposal work -- so both the billable-labour
    # floor and an invoice test kept jobs he says we never got.
    jim = json.loads(io.open(OUT + "/jim_list.json", encoding="utf-8").read())

    # The downtown core, exactly as Jim asked: "zoom right in on downtown.
    # Don't worry about the outlying ones." Reaching north to Bankers Hill for
    # The Quince and 6th & Palm was tried and rejected -- it drags Balboa Park
    # into frame and shrinks the cluster around Namdar's blocks, which is the
    # whole point of the figure. Those projects are carried in the written list
    # and in the Greater San Diego count instead.
    BOX = (32.703, 32.728, -117.176, -117.145)

    def inbox(r):
        return (r.get("lat") is not None and r.get("lng") is not None
                and BOX[0] <= r["lat"] <= BOX[1] and BOX[2] <= r["lng"] <= BOX[3])

    kor = [r for r in jim["kor_san_diego"] if inbox(r)]
    prior = [r for r in jim["prior_downtown"] if inbox(r)]
    print("Jim's keeps in frame: %d KOR, %d prior to KOR" % (len(kor), len(prior)))

    def pins(rows, era):
        return [{"geometry": {"type": "Point", "coordinates": [r["lng"], r["lat"]]},
                 "era": era} for r in rows]

    caption = ("<b>%d</b> KOR projects in downtown San Diego" % len(kor)
               + "<div class='kor-key'><i style='background:#ff5c35'></i>KOR"
               + "<i style='background:#2fa36b'></i>Jim DesRoches, prior to KOR (%d)" % len(prior)
               + "<i style='background:#1f2e3c'></i>Namdar &mdash; Park &amp; Broadway (proposed)</div>")

    p2 = build("fig_sd", pins(kor, "KOR") + pins(prior, "PRIOR"), 1150, 900, 11, 40, 16,
               caption, [[n, la, lo, side] for n, la, lo, side in SITES])
    print("wrote", p2)
