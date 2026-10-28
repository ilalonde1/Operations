r"""platemap.py <model.e2k> <storey> <out.png> : draw one storey's floor plates filled by thickness, labelled, so the
zoning can be looked at. COVERS: AREA ... FLOOR objects and their SHELLPROP/SLAB thickness. DOES NOT: openings, walls."""
import re, sys, collections
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon

TO_MM = {'MM': 1.0, 'IN': 25.4, 'FT': 304.8, 'M': 1000.0}
u = 1.0; pts = {}; areas = {}; thick = {}; plates = []
for line in open(sys.argv[1], encoding='utf-8', errors='replace'):
    s = line.strip()
    if s.startswith('UNITS'): u = TO_MM.get(re.findall(r'"([^"]+)"', s)[1].upper(), 1.0); continue
    m = re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s)
    if m: thick[m.group(1)] = float(m.group(2)) * u; continue
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s)
    if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
    if m and m.group(2) == sys.argv[2] and m.group(1) in areas:
        p = [pts[q] for q in areas[m.group(1)] if q in pts]
        if len(p) >= 3: plates.append((thick.get(m.group(3), 0), p))
fig, ax = plt.subplots(figsize=(16, 12))
ts = sorted({t for t, _ in plates}); cmap = plt.get_cmap('viridis')
col = {t: cmap(i / max(1, len(ts) - 1)) for i, t in enumerate(ts)}
for t, p in sorted(plates, key=lambda x: x[0]):
    ax.add_patch(Polygon(p, closed=True, fc=(*col[t][:3], 0.45), ec='k', lw=0.5))
    cx = sum(q[0] for q in p) / len(p); cy = sum(q[1] for q in p) / len(p)
    if abs(max(q[0] for q in p) - min(q[0] for q in p)) > 3000: ax.text(cx, cy, f"{t:.0f}", fontsize=8, ha='center')
ax.autoscale(); ax.set_aspect('equal')
ax.legend(handles=[Polygon([(0, 0)], fc=col[t]) for t in ts], labels=[f"{t:.0f} mm" for t in ts], fontsize=7, loc='upper right')
ax.set_title(f"{sys.argv[1].split(chr(92))[-1]} {sys.argv[2]}: {len(plates)} plates")
plt.savefig(sys.argv[3], dpi=90, bbox_inches='tight')
print(len(plates), "plates", collections.Counter(round(t) for t, _ in plates))
