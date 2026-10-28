r"""zone_where.py <job> <our storey> <her storey|-> <inches> : where today's plates of that thickness sit - each one's area, centroid,
and whether its centroid falls inside one of the engineer's floor plates on her storey (registered into our frame), and if so
that plate's thickness. A zone outside every one of her plates is on a part of the set she did not model there."""
import os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "member_verdicts.py"), encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])


def plates(path, storey):
    u = 1.0; pts = {}; areas = {}; thick = {}; res = []
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for s in lines:
        s = s.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
        elif m := re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s): thick[m.group(1)] = float(m.group(2)) * u / 25.4
        elif m := re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s): pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u)
        elif m := re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s): areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]
    for s in lines:
        if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)) and m.group(1) in areas and norm(m.group(2)) == norm(storey):
            p = [pts[q] for q in areas[m.group(1)] if q in pts]
            if len(p) < 3: continue
            a = sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p))) / 2
            if abs(a) < 1: continue
            cx = sum((p[i][0] + p[(i + 1) % len(p)][0]) * (p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1]) for i in range(len(p))) / (6 * a)
            cy = sum((p[i][1] + p[(i + 1) % len(p)][1]) * (p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1]) for i in range(len(p))) / (6 * a)
            res.append((round(thick.get(m.group(3), 0), 1), abs(a) / 92903.04, (cx, cy), p))
    return res


def inside(pt, poly):
    x, y = pt; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c


job, st, hst, inch = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
ox, oy = offset(job)
ours = plates(os.path.join(SIX, job, "walk-86a2e160", "out.e2k"), st)
hers = [] if hst == "-" else [(t, a, c, [(x - ox, y - oy) for x, y in p]) for t, a, c, p in plates(os.path.join(YARD, job.split("-arch")[0] + ".e2k"), hst)]
print(f"{job} {st} (hers {hst}: {len(hers)} plates) - today's {inch:g}\" plates:")
for t, a, c, p in ours:
    if abs(t - inch) > 0.05: continue
    under = [h for h in hers if inside(c, h[3])]
    print(f"   {a:,.0f} sq ft at ({c[0]:,.0f}, {c[1]:,.0f}) -> " + (", ".join(f'inside her {h[0]:g}" plate ({h[1]:,.0f} sq ft)' for h in under) or "outside every plate of hers"))
