"""List one storey's wall panels: name, axis ends (mm), length, section, pier."""
import re, sys, math
pts, panels, assign = {}, {}, {}
for raw in open(sys.argv[1], encoding="latin-1"):
    s = raw.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+PANEL\s+\d+\s+(.*)', s)
    if m: panels[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m and m[2] == sys.argv[2] and m[1] in panels: assign[m[1]] = m[3]
for w, rest in assign.items():
    a, b = pts[panels[w][0]], pts[panels[w][1]]
    sec = re.search(r'SECTION\s+"([^"]+)"', rest); pier = re.search(r'PIER\s+"([^"]+)"', rest)
    print(f"{w:7} ({a[0]:8.0f},{a[1]:8.0f})-({b[0]:8.0f},{b[1]:8.0f}) {math.dist(a, b):6.0f} mm  {sec[1] if sec else '-':14} {pier[1] if pier else 'NO PIER'}")
print(len(assign), "panels")
