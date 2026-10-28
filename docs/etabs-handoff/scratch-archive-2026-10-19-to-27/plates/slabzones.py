import sys, re, collections
# slabzones.py <model.e2k> <storey> : the storey's floor plates (not openings) by slab thickness - area and volume per
# thickness, in metric, read from the e2k's own SHELLPROP / SLAB section thicknesses
TO_MM = {'MM': 1.0, 'IN': 25.4, 'FT': 304.8, 'M': 1000.0}
u = 1.0; pts = {}; areas = {}; thick = {}; rows = collections.defaultdict(float); n = collections.Counter()
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
        a = abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 1e6
        t = thick.get(m.group(3))
        key = f"{m.group(3)} ({t:.0f} mm)" if t else m.group(3)
        rows[key] += a; n[key] += 1
        rows['__vol__'] += a * (t or 0) / 1000
for k, a in sorted(rows.items()):
    if k != '__vol__': print(f"  {k:36} {n[k]:3} plate(s) {a:9.1f} m2 {a * 10.7639:10,.0f} sq ft")
print(f"  volume {rows['__vol__']:.0f} m3 (plates only, openings not taken off)")
