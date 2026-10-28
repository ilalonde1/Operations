r"""member_verdicts.py : for the member moves the drift record left unjudged, split each commit's lost/gained walls and columns
into SHIFTS (a lost and a gained member of one kind within 300 mm, lengths within 600 mm - the same member re-placed) and REAL
losses/gains, and hold each real one against the engineer's model: is there a member of that kind within 300 mm (column) /
500 mm of its midpoint (wall) on her storey of that name, after the frame offset model-yardstick registered for the set?
A real LOSS of a member she has = regression; a real GAIN of one she has = fix; a loss of one she lacks = fix; a gain of one she
lacks = regression. COVERS: plan position of columns and wall midpoints. DOES NOT: sections, wall ends, heights; a member she
models on a differently named storey reads as 'she lacks'."""
import math, os, re, subprocess, collections
HERE = os.path.dirname(os.path.abspath(__file__))
SIX = os.path.join(HERE, "sixrepair")
TAKEOFF = r"C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"
YARD = os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks")
TO_MM = {"MM": 1.0, "IN": 25.4, "FT": 304.8, "M": 1000.0}
COMMITS = [c.strip() for c in open(os.path.join(HERE, "walk-commits.txt")) if c.strip()]
ITEMS = [("31170-01-arch", "8ed10d03", "L3,L4,L5,L6,L7"), ("31138-01", "8ed10d03", "L2"), ("31065-01", "8ed10d03", "L2"),
         ("31202-01", "d2cec177", "ROOF"), ("31065-01", "8fa0d9d6", "L1,P1,P2"), ("31130-01", "8fa0d9d6", "L1,P2,L0/P1"),
         ("31065-01", "674b2890", "L1,P1"), ("31138-01", "674b2890", "L2,L20")]


def norm(n):
    n = re.sub(r"LEVEL\s*", "L", n.upper().strip()); n = re.sub(r"\bL-(\d)", r"L\1", n); return re.sub(r"\bL0+(\d)", r"L\1", n)


def members(path):
    """norm(storey) -> {'column': [(x,y)], 'wall': [(x,y)]} in mm"""
    u = 1.0; pts = {}; kind = {}; obj_pts = {}
    out = collections.defaultdict(lambda: {"column": [], "wall": []})
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for s in lines:
        s = s.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
        elif m := re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s): pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u)
        elif m := re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s): kind[("L", m.group(1))] = "column"; obj_pts[("L", m.group(1))] = [m.group(2)]
        elif m := re.match(r'AREA\s+"([^"]+)"\s+PANEL\s+(\d+)\s+(.*)', s):
            kind[("A", m.group(1))] = "wall"; obj_pts[("A", m.group(1))] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]
    for s in lines:
        m = re.match(r'\s*(LINEASSIGN|AREAASSIGN)\s+"([^"]+)"\s+"([^"]+)"', s)
        if not m: continue
        key = ("L" if m.group(1) == "LINEASSIGN" else "A", m.group(2))
        if key not in kind: continue
        p = [pts[q] for q in obj_pts[key] if q in pts]
        if not p: continue
        xy = sorted(set((round(a), round(b)) for a, b in p))
        cx = sum(a for a, _ in xy) / len(xy); cy = sum(b for _, b in xy) / len(xy)
        n = norm(m.group(3))
        out[n][kind[key]].append((cx, cy))
        if (bare := re.sub(r"^L\d+\s+", "", n)) != n: out[bare][kind[key]].append((cx, cy))   # "L14 ROOF" also answers to ROOF
    return out


def her_storey(hers, st):
    """her storey for one of ours: by name, or a part of a slashed name (L0/P1 -> P1)"""
    for n in [norm(st)] + [norm(p) for p in st.split("/")]:
        if n in hers: return n
    return None


def diff(a, b):
    """storey -> [(LOST|GAINED, kind, x, y, length)]"""
    txt = subprocess.run([TAKEOFF, "model-diff", a, b], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    res, cur = collections.defaultdict(list), None
    for line in txt.splitlines():
        if m := re.match(r"^(\S+): columns ", line): cur = m.group(1); continue
        if line.startswith("where the members went"): cur = None
        if cur and (m := re.match(r"^\s+(LOST|GAINED) (column|wall)\s+at \(([-\d,]+), ([-\d,]+)\)(?: ([\d,]+) long)?", line)):
            res[cur].append((m.group(1), m.group(2), float(m.group(3).replace(",", "")), float(m.group(4).replace(",", "")),
                             float(m.group(5).replace(",", "")) if m.group(5) else 0.0))
    return res


def offset(job):
    t = open(os.path.join(SIX, job, "walk-86a2e160", "yardstick.txt"), encoding="utf-8", errors="replace").read()
    m = re.search(r"column registration \(([-\d,]+), ([-\d,]+)\) mm", t)
    return float(m.group(1).replace(",", "")), float(m.group(2).replace(",", ""))


def near(p, cands, tol):
    return min((math.hypot(p[0] - q[0], p[1] - q[1]) for q in cands), default=1e12) <= tol


for job, c, sts in ITEMS:
    i = COMMITS.index(c); prev = f"walk-{COMMITS[i - 1]}" if i else "rb-7345"
    a, b = os.path.join(SIX, job, prev, "out.e2k"), os.path.join(SIX, job, f"walk-{c}", "out.e2k")
    d = diff(a, b)
    hers = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k"))
    ours = members(b)
    ox, oy = offset(job)
    # the sign of the registration: the one that lands more of our columns on hers, over every storey
    def score(sg):
        return sum(near((x + sg * ox, y + sg * oy), hers[st]["column"], 100) for st in ours for x, y in ours[st]["column"] if st in hers)
    sg = 1 if score(1) >= score(-1) else -1
    for st in sts.split(","):
        moves = d.get(st, [])
        lost = [m for m in moves if m[0] == "LOST"]; gained = [m for m in moves if m[0] == "GAINED"]
        shifts, used = [], set()
        for l in lost:
            for k, g in enumerate(gained):
                if k in used or g[1] != l[1]: continue
                dist = math.hypot(l[2] - g[2], l[3] - g[3])
                if dist <= 300 and abs(l[4] - g[4]) <= 600:
                    shifts.append(dist); used.add(k); break
            else:
                continue
        paired_lost = len(shifts)
        real_lost = [l for l in lost]
        # drop the lost members that were paired (first match each)
        rl, used2 = [], set()
        for l in lost:
            hit = next((k for k, g in enumerate(gained) if k not in used2 and g[1] == l[1] and math.hypot(l[2] - g[2], l[3] - g[3]) <= 300 and abs(l[4] - g[4]) <= 600), None)
            if hit is None: rl.append(l)
            else: used2.add(hit)
        rg = [g for k, g in enumerate(gained) if k not in used2]
        hs = her_storey(hers, st); h = hers.get(hs, {"column": [], "wall": []})
        def has(m): return near((m[2] + sg * ox, m[3] + sg * oy), h[m[1]], 300 if m[1] == "column" else 500)
        lh = sum(has(m) for m in rl); gh = sum(has(m) for m in rg)
        print(f"{job} {st} @ {c}: {len(moves)} moves = {len(shifts)} shift pairs (max {max(shifts, default=0):.0f} mm)"
              f" + {len(rl)} real lost ({lh} she has -> regression, {len(rl) - lh} she lacks -> fix)"
              f" + {len(rg)} real gained ({gh} she has -> fix, {len(rg) - gh} she lacks -> regression)"
              f"   [her storey: {hs or 'NONE'}: {len(h['column'])} cols {len(h['wall'])} walls]")
        for m in rl + rg:
            print(f"      {m[0]:<6} {m[1]:<6} at ({m[2]:,.0f}, {m[3]:,.0f}) {m[4]:,.0f} long  hers: {'YES' if has(m) else 'no'}")
