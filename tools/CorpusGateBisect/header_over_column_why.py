# For each header over a column that header_over_column.py --all lists, the column's section size from the same e2k, and
# whether the gap is wider than WallNetwork.HoldsColumn allows (largest column side + 6 in = 152.4 mm, the ASSUMED slack).
#   header_over_column_why.py <header_over_column.py --all listing> <gate-work-dir>     (board 52, 2026-10-28)
# Reads: the --all listing and each set's out.e2k in the gate work dir. Does NOT re-measure the gap between the two wall
# pieces (it uses the header's span, which is the gap the finder bridged) or see a column the composer added from another
# sheet differently from one the classifier saw.
import math, re, sys

listing, work = sys.argv[1], sys.argv[2]
SLACK = 152.4
rows = []
job = None
for line in open(listing, encoding="utf-8"):
    m = re.match(r"\s+(\d{5}-\d\d) \(", line)
    if m:
        job = m.group(1)
        continue
    m = re.match(r"\s+(\S+)\s+(KS\d+)\s+\((-?\d+),(-?\d+)\)-\((-?\d+),(-?\d+)\) span (\d+)\s+column \((-?\d+),(-?\d+)\) (\d+) off", line)
    if m and job:
        rows.append((job, m.group(1), m.group(2), int(m.group(7)), float(m.group(8)), float(m.group(9)), int(m.group(10))))

cache = {}
def model(job):
    if job in cache:
        return cache[job]
    t = open(f"{work}/{job}/out.e2k", encoding="utf-8", errors="replace").read()
    unit = re.search(r'^\s*UNITS\s+"[^"]*"\s+"([^"]+)"', t, re.M)
    u = {"MM": 1.0, "M": 1000.0, "IN": 25.4, "FT": 304.8}.get(unit.group(1).upper(), 1.0) if unit else 1.0
    pts = {m[0]: (float(m[1]) * u, float(m[2]) * u) for m in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    cols = {m[0]: m[1] for m in re.findall(r'^\s+LINE\s+"(\S+)"\s+COLUMN\s+"(\S+)"', t, re.M)}
    sec = {}
    for m in re.finditer(r'^\s+FRAMESECTION\s+"([^"]+)"(.*)$', t, re.M):
        d = re.search(r'\bD\s+([\d.]+)', m.group(2)); b = re.search(r'\bB\s+([\d.]+)', m.group(2))
        if d:
            sec[m.group(1)] = (float(d.group(1)) * u, float(b.group(1)) * u if b else float(d.group(1)) * u)
    assign = {}
    for m in re.finditer(r'^\s+LINEASSIGN\s+"(\S+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', t, re.M):
        assign[(m.group(1), " ".join(m.group(2).split()))] = m.group(3)
    cache[job] = (pts, cols, sec, assign)
    return cache[job]

wider = 0
for job, storey, ks, span, cx, cy, off in rows:
    pts, cols, sec, assign = model(job)
    best = None
    for name, p in cols.items():
        if p in pts:
            d = math.dist(pts[p], (cx, cy))
            if best is None or d < best[0]:
                best = (d, name)
    size = None
    if best and best[0] < 5:
        s = assign.get((best[1], storey))
        size = sec.get(s) if s else None
    if size:
        limit = max(size) + SLACK
        verdict = "gap wider than column + 6 in" if span > limit else "WITHIN the join's limit"
        wider += span > limit
        print(f"{job} {storey:6} {ks:6} span {span:5} column {size[0]:.0f}x{size[1]:.0f} limit {limit:.0f}  off {off:3}  {verdict}")
    else:
        print(f"{job} {storey:6} {ks:6} span {span:5} column size not found (nearest {best})")
print(f"gap wider than the largest column side + 6 in: {wider} of {len(rows)}")
