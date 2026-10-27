r"""judge_drift_moves.py : hold one commit's member moves, or one storey's thickness zones, against the engineer's model.

  members <outdir> <job> <commit> <prev-commit> <storey[,storey]>   each lost/gained wall and column, judged
  zones   <outdir> <job> <commit> <our storey> <her storey|-> <inches>   where that commit's plates of one thickness sit

<outdir> is an attribute_baseline_drift.py walk directory (<outdir>\<job>\<commit>\*.e2k). The engineer's model is the local
yardstick (<TEMP>\kor-drawings\yardsticks\<job>.e2k); her frame is moved into ours by the column registration model-yardstick
prints into <outdir>\<job>\<commit>\yardstick.txt when it is there, else computed here the same way (the offset that lands the
most of our columns within 100 mm of hers).

WHY (2026-10-26). model-diff counts a member re-placed by 100 mm as one lost and one gained, so the drift record's "walls 6 cols 0"
on 31170 L3-L7 was 3 walls moved 76-127 mm, and the "12 columns lost" on 31138 L2 was 12 twins of columns that stayed. Judging
those by count would have called fixes regressions. This pairs each lost member with the nearest gained one of its kind
(within 1,000 mm: the same member re-placed or re-trimmed) and scores the pair closer to / further from hers; for each unpaired
lost member reports the nearest one of its kind that STANDS AFTER the commit (300 mm away: it was a duplicate, and the one kept
can be judged against hers - nearer or further than the one dropped); and names each unpaired member's distance to hers. zones: whether a plate of a thickness sits inside one of
her plates on that storey and at what thickness she has it there - "outside every plate of hers" means a part of the set she
did not model on that storey, which this cannot judge.

COVERS: plan position - column points, wall and plate midpoints / centroids; storeys matched by name (LEVEL 3 / L-3 / L03 -> L3;
her "L14 ROOF" also answers to ROOF; our "L0/P1" to either part).
DOES NOT: sections; wall ends (a wall's midpoint can sit on hers while one end runs 2 m long); heights - a member re-spanned to
another storey reads as lost here and gained there (read model-diff's "where the members went" for that); nested plates (a
zone inside her 8" ring and her 24" core both read as inside).
SAME-CLASS FAULT IT MISSES: two of her walls 1 m apart and ours between them reads as on hers. Render before the verdict:
takeoff model-render --storey, or this tool's pairs plotted over hers.
Exit 0; 2 on bad arguments.
"""
import math, os, re, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from attribute_baseline_drift import REPO, CLI, MIRROR, TO_MM, e2k_in, norm   # noqa: E402

EXE = os.path.join(REPO, CLI)
YARD = os.path.join(MIRROR, "yardsticks")


def model(path):
    """(units-to-mm, points, LINE columns, AREA panels, AREA floors, slab thickness in inches, lines)"""
    u = 1.0; pts = {}; cols = {}; panels = {}; floors = {}; thick = {}
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for s in lines:
        s = s.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
        elif m := re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s): pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
        elif m := re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s): cols[m.group(1)] = [m.group(2)]
        elif m := re.match(r'AREA\s+"([^"]+)"\s+(PANEL|FLOOR)\s+(\d+)\s+(.*)', s):
            (panels if m.group(2) == "PANEL" else floors)[m.group(1)] = re.findall(r'"([^"]+)"', m.group(4))[:int(m.group(3))]
        elif m := re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s): thick[m.group(1)] = float(m.group(2))
    pts = {k: (x * u, y * u) for k, (x, y) in pts.items()}
    return u, pts, cols, panels, floors, {k: v * u / 25.4 for k, v in thick.items()}, lines


def storey_keys(n):
    n = norm(n); bare = re.sub(r"^L\d+\s+", "", n)
    return {n, bare}


def members(path):
    """storey -> {'column': [(x, y)], 'wall': [(x, y)]}, each storey also under its bare name"""
    _, pts, cols, panels, _, _, lines = model(path)
    out = {}
    for s in lines:
        m = re.match(r'\s*(LINEASSIGN|AREAASSIGN)\s+"([^"]+)"\s+"([^"]+)"', s)
        if not m: continue
        src, kind = (cols, "column") if m.group(1) == "LINEASSIGN" else (panels, "wall")
        if m.group(2) not in src: continue
        p = sorted(set((round(pts[q][0]), round(pts[q][1])) for q in src[m.group(2)] if q in pts))
        if not p: continue
        c = (sum(a for a, _ in p) / len(p), sum(b for _, b in p) / len(p))
        for k in storey_keys(m.group(3)):
            out.setdefault(k, {"column": [], "wall": []})[kind].append(c)
    return out


def her_storey(hers, st):
    for n in [norm(st)] + [norm(p) for p in st.split("/")]:
        if n in hers: return n
    return None


def near(p, cands):
    return min((math.hypot(p[0] - q[0], p[1] - q[1]) for q in cands), default=float("inf"))


def offset(out, job, commit, ours, hers):
    """our frame -> hers: model-yardstick's registration if it printed one, else the best of the column-to-column offsets"""
    ys = os.path.join(out, job, commit, "yardstick.txt")
    if os.path.exists(ys) and (m := re.search(r"column registration \(([-\d,]+), ([-\d,]+)\) mm", open(ys, encoding="utf-8", errors="replace").read())):
        o = (float(m.group(1).replace(",", "")), float(m.group(2).replace(",", "")))
    else:
        votes = {}
        for st in ours:
            for a in ours[st]["column"][:60]:
                for b in hers.get(st, {"column": []})["column"][:60]:
                    k = (round((b[0] - a[0]) / 100), round((b[1] - a[1]) / 100)); votes[k] = votes.get(k, 0) + 1
        k = max(votes, key=votes.get) if votes else (0, 0)
        o = (k[0] * 100.0, k[1] * 100.0)
    score = lambda s: sum(near((x + s * o[0], y + s * o[1]), hers[st]["column"]) <= 100 for st in ours if st in hers for x, y in ours[st]["column"])
    return o if score(1) >= score(-1) else (-o[0], -o[1])


def diff(a, b):
    txt = subprocess.run([EXE, "model-diff", a, b], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    res, cur = {}, None
    for line in txt.splitlines():
        if m := re.match(r"^(\S+): columns ", line): cur = m.group(1); continue
        if line.startswith("where the members went"): cur = None
        if cur and (m := re.match(r"^\s+(LOST|GAINED) (column|wall)\s+at \(([-\d,]+), ([-\d,]+)\)(?: ([\d,]+) long)?", line)):
            res.setdefault(cur, []).append((m.group(1), m.group(2), float(m.group(3).replace(",", "")), float(m.group(4).replace(",", "")),
                                            float(m.group(5).replace(",", "")) if m.group(5) else 0.0))
    return res


def judge_members(out, job, c, prev, storeys):
    a, b = e2k_in(os.path.join(out, job, prev)), e2k_in(os.path.join(out, job, c))
    hm = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k"))
    before, after = members(a), members(b)
    o = offset(out, job, c, after, hm)
    mv = diff(a, b)
    for st in storeys:
        hs = her_storey(hm, st); h = hm.get(hs, {"column": [], "wall": []}); af = after.get(norm(st), {"column": [], "wall": []})
        dh = lambda m: near((m[2] + o[0], m[3] + o[1]), h[m[1]])
        moves = mv.get(st, [])
        lost = [m for m in moves if m[0] == "LOST"]; gained = [m for m in moves if m[0] == "GAINED"]
        pairs = sorted((math.hypot(l[2] - g[2], l[3] - g[3]), i, j) for i, l in enumerate(lost) for j, g in enumerate(gained) if l[1] == g[1])
        ul, ug, score = set(range(len(lost))), set(range(len(gained))), [0, 0, 0]
        for d, i, j in pairs:
            if d > 1000 or i not in ul or j not in ug: continue
            ul.discard(i); ug.discard(j)
            x, y = dh(lost[i]), dh(gained[j])
            score[0 if y < x - 50 else 1 if y > x + 50 else 2] += 1
        print(f"{job} {st} (hers {hs or 'NONE'}) @ {c}: {len(lost)} lost, {len(gained)} gained; {len(lost) - len(ul)} re-placed "
              f"({score[0]} closer to hers, {score[1]} further, {score[2]} same within 50 mm)")
        for i in sorted(ul):
            m = lost[i]
            twin = min(af[m[1]], key=lambda q: math.hypot(q[0] - m[2], q[1] - m[3]), default=None)     # what stands nearest after
            tw = (f"nearest after {math.hypot(twin[0] - m[2], twin[1] - m[3]):,.0f} mm away, {near((twin[0] + o[0], twin[1] + o[1]), h[m[1]]):,.0f} mm from hers"
                  if twin else f"none of its kind after")
            print(f"    LOST   {m[1]:<6} ({m[2]:,.0f}, {m[3]:,.0f}) {m[4]:,.0f}  {dh(m):,.0f} mm from hers; {tw}")
        for j in sorted(ug):
            m = gained[j]
            print(f"    GAINED {m[1]:<6} ({m[2]:,.0f}, {m[3]:,.0f}) {m[4]:,.0f}  {dh(m):,.0f} mm from hers")


def plates(path, storey):
    _, pts, _, _, floors, thick, lines = model(path)
    res = []
    for s in lines:
        if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)) and m.group(1) in floors and norm(storey) in storey_keys(m.group(2)):
            p = [pts[q] for q in floors[m.group(1)] if q in pts]
            if len(p) < 3: continue
            cr = [p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p))]
            a = sum(cr) / 2
            if abs(a) < 1: continue
            cx = sum((p[i][0] + p[(i + 1) % len(p)][0]) * cr[i] for i in range(len(p))) / (6 * a)
            cy = sum((p[i][1] + p[(i + 1) % len(p)][1]) * cr[i] for i in range(len(p))) / (6 * a)
            res.append((round(thick.get(m.group(3), 0), 1), abs(a) / 92903.04, (cx, cy), p))
    return res


def inside(pt, poly):
    x, y = pt; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c


def judge_zones(out, job, c, st, hst, inch):
    ours_path = e2k_in(os.path.join(out, job, c)); yard = os.path.join(YARD, job.split("-arch")[0] + ".e2k")
    o = offset(out, job, c, members(ours_path), members(yard))
    hers = [] if hst == "-" else [(t, a, cc, [(x - o[0], y - o[1]) for x, y in p]) for t, a, cc, p in plates(yard, hst)]
    print(f"{job} {st} (hers {hst}: {len(hers)} plates) @ {c} - {inch:g}\" plates:")
    for t, a, cc, p in plates(ours_path, st):
        if abs(t - inch) > 0.05: continue
        under = [h for h in hers if inside(cc, h[3])]
        print(f"   {a:,.0f} sq ft at ({cc[0]:,.0f}, {cc[1]:,.0f}) -> "
              + (", ".join(f'inside her {h[0]:g}" plate ({h[1]:,.0f} sq ft)' for h in under) or "outside every plate of hers"))


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) == 6 and a[0] == "members":
        judge_members(a[1], a[2], a[3], a[4], a[5].split(","))
    elif len(a) == 7 and a[0] == "zones":
        judge_zones(a[1], a[2], a[3], a[4], a[5], float(a[6]))
    else:
        print(__doc__.split("\n\n")[0]); sys.exit(2)
