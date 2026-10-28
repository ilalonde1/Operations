r"""walk_attribute.py [final-tag] : from walk.py's builds, (1) every storey of the six banked sets that differs between the
73458eec build (rb-7345 - the bank's geometry) and the final build, with the commit where it FIRST moves and what moved there;
(2) per commit, what it changed against the commit before, beside the engineer's plate on each storey - the evidence for a
fix / regression / bank's-error verdict.

A storey's state: plate (total sq ft), thickness (sq ft by thickness, when the total holds within 1%), openings (count and sq
ft of OPENING areas), members (takeoff model-diff's lost/gained walls and columns against the comparison build). COVERS: the
storey names the e2k carries, AREA FLOOR plates and OPENING areas by assign, model-diff's per-storey member counts.
DOES NOT: positions of plates (a plate moved but the same size reads unchanged); section changes of members; a storey renamed
between builds (it reads as one gone and one new - reported)."""
import collections, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SIX = os.path.join(HERE, "sixrepair")
TAKEOFF = r"C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"
YARD = os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks")
JOBS = ("31065-01", "31130-01", "31138-01", "31168-01", "31170-01-arch", "31202-01")
COMMITS = [c.strip() for c in open(os.path.join(HERE, "walk-commits.txt")) if c.strip()]
TO_MM = {"MM": 1.0, "IN": 25.4, "FT": 304.8, "M": 1000.0}
SUBJ = {}
for c in COMMITS:
    s = subprocess.run(["git", "-C", r"C:\VIsual Studio Projects\Operations", "log", "-1", "--format=%s", c], capture_output=True, text=True).stdout.strip()
    SUBJ[c] = s[:90]


def state(path):
    """storey -> {'plate': sqft, 'thick': {in: sqft}, 'open': (n, sqft)}"""
    u = 1.0; pts = {}; areas = {}; thick = {}
    st = collections.defaultdict(lambda: {"plate": 0.0, "thick": collections.Counter(), "open": [0, 0.0]})
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for s in lines:
        s = s.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
        elif m := re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s): thick[m.group(1)] = float(m.group(2)) * u
        elif m := re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s): pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u)
        elif m := re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s): areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]
    def sqft(obj):
        p = [pts[q] for q in areas.get(obj, []) if q in pts]
        return abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 92903.04 if len(p) >= 3 else 0.0
    for s in lines:
        if m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+(SECTION\s+"([^"]+)"|OPENING\s+"Yes")', s):
            obj, storey = m.group(1), m.group(2)
            if obj not in areas: continue                       # a wall panel, not a floor area
            a = sqft(obj)
            if m.group(4) is not None:
                st[storey]["plate"] += a; st[storey]["thick"][round(thick.get(m.group(4), 0) / 25.4, 1)] += a
            else:
                st[storey]["open"][0] += 1; st[storey]["open"][1] += a
    return st


def members(a, b):
    """storey -> (lost+gained walls, lost+gained columns) by takeoff model-diff a -> b"""
    out = subprocess.run([TAKEOFF, "model-diff", a, b], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    res = {}
    for line in out.splitlines():
        if m := re.match(r"^(\S+): columns .*\[lost columns (\d+), gained columns (\d+), lost walls (\d+), gained walls (\d+)\]", line):
            res[m.group(1)] = (int(m.group(4)) + int(m.group(5)), int(m.group(2)) + int(m.group(3)))
    return res


def what(sa, sb, mem):
    """what moved between two storey states (plate / thickness / openings / members)"""
    out = []
    pa, pb = sa["plate"], sb["plate"]
    if abs(pa - pb) > max(1.0, 0.01 * max(pa, pb)): out.append(f"plate {pa:,.0f}->{pb:,.0f}")
    elif {k: round(v) for k, v in sa["thick"].items() if v > 1} != {k: round(v) for k, v in sb["thick"].items() if v > 1}:
        out.append("thickness " + "/".join(f'{k:g}"' for k, v in sorted(sa["thick"].items(), key=lambda kv: -kv[1])[:2]) + "->"
                   + "/".join(f'{k:g}"' for k, v in sorted(sb["thick"].items(), key=lambda kv: -kv[1])[:2]))
    if sa["open"][0] != sb["open"][0] or abs(sa["open"][1] - sb["open"][1]) > 1: out.append(f"openings {sa['open'][0]}->{sb['open'][0]}")
    if mem and (mem[0] or mem[1]): out.append(f"members walls {mem[0]} cols {mem[1]}")
    return out


def build(job, tag): return os.path.join(SIX, job, tag, "out.e2k")


final = sys.argv[1] if len(sys.argv) > 1 else f"walk-{COMMITS[-1]}"
table, per_commit = [], collections.defaultdict(list)
for job in JOBS:
    base = build(job, "rb-7345")
    yard = os.path.join(YARD, job.split("-arch")[0] + ".e2k")
    def norm(n):                                                    # LEVEL 3 / L-3 / L03 -> L3, prefix kept (C-LEVEL 3 -> C-L3)
        n = re.sub(r"LEVEL\s*", "L", n.upper().strip()); n = re.sub(r"\bL-(\d)", r"L\1", n); return re.sub(r"\bL0+(\d)", r"L\1", n)
    hers = {norm(k): v for k, v in (state(yard) if os.path.exists(yard) else {}).items()}
    hers = type("H", (dict,), {"get": lambda self, k, d=None: dict.get(self, norm(k), d)})(hers)
    s0 = state(base)
    seq = [(c, build(job, f"walk-{c}")) for c in COMMITS if os.path.exists(build(job, f"walk-{c}"))]
    states = {c: state(p) for c, p in seq}
    mem0 = {c: members(base, p) for c, p in seq}                    # each build against the bank's geometry
    sf = state(build(job, final)); memf = members(base, build(job, final))
    moved = sorted({st for st in set(s0) | set(sf) if what(s0.get(st, {"plate": 0, "thick": {}, "open": [0, 0]}), sf.get(st, {"plate": 0, "thick": {}, "open": [0, 0]}), memf.get(st))},
                   key=lambda s: (len(s), s))
    empty = {"plate": 0, "thick": {}, "open": [0, 0]}
    for st in moved:
        first = next((c for c, _ in seq if what(s0.get(st, empty), states[c].get(st, empty), mem0[c].get(st))), None)
        w = what(s0.get(st, empty), states[first].get(st, empty), mem0[first].get(st)) if first else []
        chain, pv = [], s0
        for c, _ in seq:
            if what(pv.get(st, empty), states[c].get(st, empty), None): chain.append(c[:8])
            pv = states[c]
        net = "; ".join(what(s0.get(st, empty), sf.get(st, empty), memf.get(st)))
        table.append((job, st, first or "NOT FOUND", "; ".join(w), hers.get(st, {}).get("plate"), net, ",".join(chain)))
    prev, prevp = s0, base
    for c, p in seq:
        mem = members(prevp, p)
        for st in sorted(set(prev) | set(states[c]), key=lambda s: (len(s), s)):
            w = what(prev.get(st, empty), states[c].get(st, empty), mem.get(st))
            if w: per_commit[c].append(f"{job} {st}: {'; '.join(w)} | hers {hers.get(st, {}).get('plate', 0):,.0f}")
        prev, prevp = states[c], p

print("TABLE - set | storey | first commit it moves at | what moved there (vs the bank's geometry) | the engineer's plate there")
for row in table:
    print(f"{row[0]:<14} {row[1]:<10} {row[2]:<9} {row[3]:<60} hers {('-' if row[4] is None else f'{row[4]:,.0f}')}")
    print(f"{'':<25} NET bank->final: {row[5]}   plate/thickness/openings moved at: {row[6] or '(members only)'}")
nf = sum(1 for r in table if r[2] == "NOT FOUND")
print(f"\n{len(table)} storeys moved bank -> {final}; {len(table) - nf} with a first commit, {nf} NOT FOUND")
print("\nPER COMMIT - what it changed against the commit before (the evidence for fix / regression / bank's error):")
for c in COMMITS:
    print(f"{c} {SUBJ[c]}: {len(per_commit[c])} storey change(s)")
    for line in per_commit[c]: print("    " + line)
