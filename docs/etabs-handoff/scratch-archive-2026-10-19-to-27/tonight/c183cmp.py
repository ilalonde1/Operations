r"""Step 183 on vs off on the six sets: e2k identical or not; the floors the report says were zoned; and for every storey whose
floor plates differ, area by thickness off and on (sq ft) and volume (m3). COVERS: AREA ... FLOOR objects and their slab
section thickness. DOES NOT: judge against the engineer's model (storey names differ per set - done by hand per storey)."""
import collections, glob, os, re, sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sixrepair")
TO_MM = {"MM": 1.0, "IN": 25.4, "FT": 304.8, "M": 1000.0}


def plates(path):
    u = 1.0; pts = {}; areas = {}; thick = {}; by = collections.defaultdict(lambda: collections.defaultdict(float))
    for line in open(path, encoding="utf-8", errors="replace"):
        s = line.strip()
        if s.startswith("UNITS"): u = TO_MM.get(re.findall(r'"([^"]+)"', s)[1].upper(), 1.0); continue
        m = re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s)
        if m: thick[m.group(1)] = float(m.group(2)) * u; continue
        m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
        if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u); continue
        m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s)
        if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
        m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
        if m and m.group(1) in areas:
            p = [pts[q] for q in areas[m.group(1)] if q in pts]
            a = abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 1e6
            by[m.group(2)][round(thick.get(m.group(3), 0))] += a
    return by


for job in sorted(os.listdir(OUT)):
    on, off = (os.path.join(OUT, job, t) for t in ("c183-on", "c183-off"))
    if not (os.path.isdir(on) and os.path.isdir(off)): continue
    e = [max(glob.glob(os.path.join(d, "*.e2k")), key=os.path.getsize) for d in (on, off)]
    same = open(e[0], "rb").read() == open(e[1], "rb").read()
    rep = open(os.path.join(on, "report.txt"), encoding="utf-8", errors="replace").read()
    zoned = re.findall(r"[^\n]*the drawing zones by thickness[^\n]*", rep)
    kept = re.findall(r"[^\n]*the floor was kept whole[^\n]*", rep)
    print(f"== {job}: e2k {'IDENTICAL' if same else 'DIFFERS'}; floors zoned {len(zoned)}, zones refused (did not tile) {len(kept)}")
    for z in zoned + kept: print("    " + z.strip()[:330])
    if same: continue
    a, b = plates(e[0]), plates(e[1])
    for st in sorted(set(a) | set(b)):
        if a.get(st) == b.get(st): continue
        fmt = lambda d: ", ".join(f"{t}mm {v * 10.7639:,.0f}" for t, v in sorted(d.items())) if d else "-"
        vol = lambda d: sum(t / 1000 * v for t, v in d.items()) if d else 0
        print(f"    {st:10} off: {fmt(b.get(st))}  [{vol(b.get(st)):,.0f} m3]")
        print(f"    {'':10} on:  {fmt(a.get(st))}  [{vol(a.get(st)):,.0f} m3]")
