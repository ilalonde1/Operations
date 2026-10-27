r"""step185_replay_diff.py <off-work> <atmost1-work> <exactly1-work> : step 185's replay of migration 126 (2026-10-27), exact,
one PASS/FAIL line per set, then the totals. The three are corpus-gate --work dirs: the step off (run fff31290), the first
replay "at most one stroke" (8acd14bc), the second "exactly one stroke" (f98c7cf2).

TAKES: the exactly-one build's take list (its report's legend-frame lines) must equal the at-most-one build's ONE-STROKE takes.
PLATES AND COLUMNS, EXACT: the exactly-one model = the step-off model MINUS each one-stroke rectangle the at-most-one build
took (found in the off model by storey and lower-left corner within 2 units) - compared names-free, plates as (storey, sorted
vertices rounded to 1 mm), columns as (storey, point).
WALLS, BOUNDED - NOT PAIRED, AND THAT IS A LIMIT: a wall carries the storey at its TOP, so a frame's sample drawn on L2's sheet
is written on L3, and a sample may already have been dropped downstream in the off build; so walls cannot be paired one to one
with the frames. The check is: no wall added; every wall lost stands inside a taken frame's box; at most one lost per frame. A
wall rewritten in place (31158 L4-L6, 1 mm off as a stack recomposed) reads as one lost and one added and FAILS here - read the
set with where126-style listing before calling it.
BYTES: a set with no take in either build must be byte-identical to both; 30864, 31168, 31048 byte-identical to off.
COVERS: FLOOR plates, PANEL walls, LINE columns, by storey. DOES NOT: openings, sections, thickness, assigns other than storey.
Exit 0 always (a report).
"""
import collections, glob, hashlib, os, re, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import model   # noqa: E402
off, a1, e1 = sys.argv[1:4]
TAKE = re.compile(r"^\s*-\s+(\S+): a ([\d,]+) sq ft rectangle on (.+?)\.dxf at \(([-\d,]+), ([-\d,]+)\).*?(no stroke|one sample stroke|exactly one stroke)")
num = lambda s: float(s.replace(",", ""))


def takes(work, job):
    rep = os.path.join(work, job, "report.txt")
    out = set()
    if os.path.isfile(rep):
        for line in open(rep, encoding="utf-8", errors="replace"):
            if (m := TAKE.match(line)):
                out.add((m.group(1), m.group(2), m.group(3), num(m.group(4)), num(m.group(5)), "none" if m.group(6) == "no stroke" else "one"))
    return out


def geometry(path):
    _, pts, cols, panels, floors, _, lines = model(path)
    raw = {k: (x, y) for k, (x, y) in pts.items()}   # model() returns mm; corners in the report are in model units - see unit
    g = collections.Counter()
    for s in lines:
        m = re.match(r'\s*(LINEASSIGN|AREAASSIGN)\s+"([^"]+)"\s+"([^"]+)"', s)
        if not m or "OPENING" in s: continue
        name, st = m.group(2), m.group(3)
        if m.group(1) == "LINEASSIGN" and name in cols:
            g[("column", st, tuple(sorted({(round(raw[q][0]), round(raw[q][1])) for q in cols[name] if q in raw})))] += 1
        elif name in panels:
            g[("wall", st, tuple(sorted((round(raw[q][0]), round(raw[q][1])) for q in panels[name] if q in raw)))] += 1
        elif name in floors:
            g[("plate", st, tuple(sorted((round(raw[q][0]), round(raw[q][1])) for q in floors[name] if q in raw)))] += 1
    return g


def unit_mm(path):
    u, *_ = model(path)
    return u


h = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()[:16] if os.path.isfile(p) else None
passed = failed = 0
for job in sorted(os.listdir(e1)):
    pe, po, pa = (os.path.join(w, job, "out.e2k") for w in (e1, off, a1))
    if not os.path.isfile(pe): continue
    t_a1, t_e1 = takes(a1, job), takes(e1, job)
    one = {t for t in t_a1 if t[5] == "one"}
    problems = []
    if {t[:5] for t in t_e1} != {t[:5] for t in one}:
        problems.append(f"takes {len(t_e1)} vs expected {len(one)}")
    he, ho, ha = h(pe), h(po), h(pa)
    if not t_a1 and not t_e1 and not (he == ho == ha): problems.append("untouched set not byte-identical to both")
    if job in ("30864-01", "31168-01", "31048-01") and he != ho: problems.append("boundary set not byte-identical to off")
    if he != ho:
        go, ge = geometry(po), geometry(pe)
        u = unit_mm(po)
        # PLATES AND COLUMNS EXACT: the off model's minus each one-stroke frame taken.
        expected = collections.Counter({k: v for k, v in go.items() if k[0] != "wall"})
        boxes = []
        for st, _, _, x, y, _ in one:
            frame = [k for k in go if k[0] == "plate" and k[1] == st and k[2]
                     and abs(min(p[0] for p in k[2]) - x * u) <= 2 * u and abs(min(p[1] for p in k[2]) - y * u) <= 2 * u]
            if not frame: problems.append(f"{st} frame at ({x:,.0f}, {y:,.0f}) not found in the off model"); continue
            f = frame[0]; expected[f] -= 1
            boxes.append((min(p[0] for p in f[2]), max(p[0] for p in f[2]), min(p[1] for p in f[2]), max(p[1] for p in f[2])))
        expected += collections.Counter()
        got = collections.Counter({k: v for k, v in ge.items() if k[0] != "wall"})
        if expected != got:
            lost, added = expected - got, got - expected
            problems.append(f"plates/columns differ: {sum(lost.values())} expected-but-absent, {sum(added.values())} unexpected: "
                            + "; ".join(f"{k[0]} {k[1]}" for k in list(lost)[:3] + list(added)[:3]))
        # WALLS BOUNDED (a wall carries the storey at its top, and a sample may already be gone downstream, so they cannot be
        # paired one to one): none added; each one lost stands inside a taken frame; at most one lost per frame.
        wo = collections.Counter({k: v for k, v in go.items() if k[0] == "wall"})
        we = collections.Counter({k: v for k, v in ge.items() if k[0] == "wall"})
        wl, wa = wo - we, we - wo
        mid = lambda k: (sum(p[0] for p in k[2]) / len(k[2]), sum(p[1] for p in k[2]) / len(k[2]))
        stray = [k for k in wl.elements() if not any(b[0] <= mid(k)[0] <= b[1] and b[2] <= mid(k)[1] <= b[3] for b in boxes)]
        if wa: problems.append(f"{sum(wa.values())} wall(s) added")
        if stray: problems.append(f"{len(stray)} wall(s) lost outside every taken frame ({', '.join(k[1] for k in stray[:4])})")
        if sum(wl.values()) > len(boxes): problems.append(f"{sum(wl.values())} walls lost for {len(boxes)} frame(s)")
    status = "PASS" if not problems else "FAIL"
    passed += status == "PASS"; failed += status == "FAIL"
    print(f"{status} {job}: takes {len(t_e1)} (8acd14bc {len(t_a1)}, {len(one)} one-stroke); bytes = off {he == ho}, = 8acd14bc {he == ha}"
          + ("" if not problems else " | " + " | ".join(problems)))
print(f"\n{passed} PASS, {failed} FAIL of {passed + failed} sets")
