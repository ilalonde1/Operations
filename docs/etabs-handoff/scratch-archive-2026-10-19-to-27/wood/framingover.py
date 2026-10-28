r"""framingover.py : every plan sheet titled "... FRAMING OVER" in the gate's sets, with the storey it was given, our plate area
on that storey (by thickness, from out.e2k), the engineer's plate area on the storey of the same name (from her e2k), and the
other plan sheets of that set giving the same storey.

COVERS: the sets in %TEMP%\kor-drawings\corpus\gate-sets.csv; title read from the DXF name (sheets.csv's title column is
empty); storeys matched by exact name, case-blind. DOES NOT: a wood plan whose title lacks "FRAMING OVER" (the stud-wall
detector is not in sheets.csv); storeys the yardstick pairs under a different name ("L2+L3"); whether her plate there is
concrete or a wood WFL slab (by thickness only, printed)."""
import collections, csv, os, re, sys

ROOT = os.path.join(os.environ["TEMP"], "kor-drawings", "corpus")
TO_MM = {"MM": 1.0, "IN": 25.4, "FT": 304.8, "M": 1000.0}
FRAMING = re.compile(r"FRAMING\s+OVER", re.I)


def plates(path):
    """storey (upper) -> {thickness in: sq ft}"""
    u = 1.0; pts = {}; areas = {}; thick = {}; by = collections.defaultdict(lambda: collections.defaultdict(float))
    if not path or not os.path.exists(path): return None
    for line in open(path, encoding="utf-8", errors="replace"):
        s = line.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
            continue
        m = re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s)
        if m: thick[m.group(1)] = float(m.group(2)) * u; continue
        m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
        if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u); continue
        m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s)
        if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
        m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
        if m and m.group(1) in areas:
            p = [pts[q] for q in areas[m.group(1)] if q in pts]
            a = abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 92903.04
            by[m.group(2).upper()][round(thick.get(m.group(3), 0) / 25.4, 1)] += a
    return by


def fmt(d):
    if not d: return "none"
    return " + ".join(f'{v:,.0f}@{t:g}"' for t, v in sorted(d.items(), key=lambda kv: -kv[1]))


ORD = {"1ST": 1, "2ND": 2, "3RD": 3, "4TH": 4, "5TH": 5, "6TH": 6}


def norm(name):
    """L01, L-1, Level 1, 1st, 01 -> L1; anything else as is, upper"""
    s = name.strip().upper()
    if s in ORD: return f"L{ORD[s]}"
    m = re.fullmatch(r"(?:L|LEVEL)\s*-?\s*0*(\d+)", s)
    return f"L{int(m.group(1))}" if m else s


def normed(by):
    if by is None: return None
    out = collections.defaultdict(lambda: collections.defaultdict(float))
    for st, d in by.items():
        for t, v in d.items(): out[norm(st)][t] += v
    return out


def storeys_of(path):
    return {norm(m.group(1)) for line in open(path, encoding="utf-8", errors="replace") if (m := re.match(r'\s*STORY\s+"([^"]+)"', line))}


TALLY = collections.Counter()
PRED = collections.Counter()
PRED_ROWS = []
OVER = re.compile(r"SHOWING\s+(.+?)\s*FRAMING\s+OVER", re.I)
BUILDING = re.compile(r"\b(?:BUILDING|BLDG\.?)\s*(\d+)|\b(MID-RISE|TOWNHOME)", re.I)


def target(title):
    """the storey a title names as the framing over: "LEVEL 3", "4TH FLOOR", "ROOF", "LEVEL 2" -> L3, L4, ROOF, L2"""
    m = OVER.search(title)
    if not m: return None
    t = m.group(1).upper()
    if "ROOF" in t: return "ROOF"
    n = re.search(r"(?:LEVEL|LVL)\s*(\d+)|(\d+)\s*(?:ST|ND|RD|TH)\b", t)
    return f"L{int(n.group(1) or n.group(2))}" if n else None


def building(title):
    m = BUILDING.search(title)
    return (m.group(1) or m.group(2)).upper() if m else ""
jobs = [r["job"] for r in csv.DictReader(open(os.path.join(ROOT, "gate-sets.csv"), encoding="utf-8-sig"))]
sets_with = 0; sheets_n = 0
for job in sorted(set(jobs)):
    d = os.path.join(ROOT, job)
    sp = os.path.join(d, "sheets.csv")
    if not os.path.exists(sp): continue
    rows = list(csv.DictReader(open(sp, encoding="utf-8-sig")))
    plans = [r for r in rows if r["sheet_type"] == "plan"]
    fo = [r for r in plans if FRAMING.search(r["dxf_files"] or "")]
    if not fo: continue
    sets_with += 1; sheets_n += len(fo)
    ours = normed(plates(os.path.join(d, "out.e2k"))) or {}
    yard = next(csv.DictReader(open(os.path.join(d, "set.csv"), encoding="utf-8-sig"))).get("yardstick")
    hers = normed(plates(yard))
    her_storeys = storeys_of(yard) if hers is not None else set()
    def title_of(r): return " ".join(re.sub(r"^[^_]*_\d+_", "", f).removesuffix(".dxf") for f in (r["dxf_files"] or "").split(" | "))
    framed = collections.defaultdict(set)                       # building -> storeys some sheet draws as the framing over
    for r in fo:
        if (t := target(title_of(r))): framed[building(title_of(r))].add(t)
    print(f"{job}: {len(fo)} of {len(plans)} plan sheets FRAMING OVER; her model {'none' if hers is None else str(len(hers)) + ' storeys with plates'}")
    for r in fo:
        st = norm(r["storeys"] or "")
        others = [o["sheet_number"] for o in plans if o is not r and norm(o["storeys"] or "") == st and st and not FRAMING.search(o["dxf_files"] or "")]
        # the class of this sheet's storey in her model: she plates it / she names it and plates nothing / she has no such storey
        h = (hers or {}).get(st)
        cls = "HER PLATE" if h else ("her storey, no plate" if st in her_storeys else "not in her model")
        if "," not in st and st:
            TALLY[(cls, "other plan sheet" if others else "framing-over only", "ours plated" if ours.get(st) else "ours none")] += 1
            # THE RULE UNDER TEST: this sheet's own floor is wood when its building frames this storey over the one below
            drop = st in framed[building(title_of(r))]
            if drop and h and not others: verdict = "LOSS: dropped, she plates it, no other sheet gives it"
            elif drop and h: verdict = "dropped, she plates it, another plan sheet gives it"
            elif drop: verdict = "RIGHT: dropped, she plates nothing there"
            elif h: verdict = "RIGHT: kept, she plates it"
            else: verdict = "OVER: kept, she plates nothing there"
            PRED[verdict] += 1
            if verdict.startswith(("LOSS", "OVER")): PRED_ROWS.append(f"{job} {r['sheet_number']} {st} bldg '{building(title_of(r))}' framed {sorted(framed[building(title_of(r))])}: {verdict}")
        title = re.sub(r"^[^_]*_\d+_", "", (r["dxf_files"] or "").split(";")[0]).removesuffix(".dxf")
        print(f"   {r['sheet_number']:<9}{st or '-':<6} slabs {r['slabs']:>4}  ours {fmt(ours.get(st)):<28} hers {('-' if hers is None else fmt(hers.get(st))):<28}"
              f" other sheets of {st or '-'}: {','.join(others) or 'none'}   | {title[:70]}")
print(f"\n{sets_with} of {len(set(jobs))} gate sets carry a FRAMING OVER plan sheet; {sheets_n} sheets")
print(f"tally over the {sum(TALLY.values())} of those sheets given ONE storey (her storey class, the storey's other plan sheets, ours):")
for k, n in sorted(TALLY.items(), key=lambda kv: -kv[1]): print(f"   {n:4}  {' | '.join(k)}")
print(f"the rule 'a framing-over sheet's own floor is wood when its building frames that storey over the one below', on the same {sum(PRED.values())}:")
for k, n in sorted(PRED.items(), key=lambda kv: -kv[1]): print(f"   {n:4}  {k}")
for row in PRED_ROWS: print("   " + row)
