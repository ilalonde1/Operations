r"""header_over_column.py <gate-work-dir> [<gate-work-dir> ...] : every header (a KS* PANEL) whose doorway holds a column - a
column on the header's storey whose centre lies inside the header's span (more than END_CLEAR from either end) and within
REACH of the header's line. Per set and in total, as X of Y headers; the drawing unit of each set from its first DXF's
$INSUNITS. Up to three hits per set are listed; `--all` lists every one.

MEASURED 2026-10-27 (docs/etabs-handoff/corpus/headers-over-columns-2026-10-27-*): 79 of 2,833 headers before 0f3e4ee4, 98 of
3,313 on it, 84 of 3,177 on d4352fc0, all 57 sets drawn in mm; spans 41 at <= 900 mm (a column's width), 29 at 901-1,300,
14 above. Rendered (31053 L2, 31085 L3, 30972 P3 twice): a wall run the drawing breaks for a column, often where a cross wall
meets it, read as a doorway - the engineer's model (30972) runs one wall through.

WHY (2026-10-27, board 52): 31183 P1's new 18 deg header spans a gap that holds a column; the opening finder pairs two wall ends
"in line, a doorway apart" and never asks what stands between them. VD asked: one instance or a class, before any fix.

COVERS: KS panels and COLUMN lines in the composed e2k, storey by storey (a header and a column on the same AREAASSIGN /
LINEASSIGN storey), column CENTRES only. DOES NOT: column size (a wide column whose centre is just outside REACH is missed);
openings that got no header (they are not in the e2k); walls in the gap. SAME-CLASS FAULT IT MISSES: a column standing in the
doorway but more than REACH off the line between the jambs (a column set back from the wall face), and a doorway the finder
closed with a header whose ends moved onto the column itself.
Exit 0 always (a report)."""
import collections, glob, math, os, re, sys

END_CLEAR, REACH = 150.0, 300.0      # mm: clear of each jamb end; off the header's line


def unit_mm(t):
    m = re.search(r'^\s*UNITS\s+"[^"]*"\s+"([^"]+)"', t, re.M)
    return {"MM": 1.0, "M": 1000.0, "CM": 10.0, "IN": 25.4, "INCHES": 25.4, "FT": 304.8, "FEET": 304.8}.get(m.group(1).upper(), 1.0) if m else 1.0


def model(path):
    t = open(path, encoding="utf-8", errors="replace").read(); u = unit_mm(t)
    P = {m[0]: (float(m[1]) * u, float(m[2]) * u) for m in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    heads = {}
    for m in re.finditer(r'^\s+AREA\s+"(KS\d+)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        xy = sorted({P[q] for q in re.findall(r'"(\S+)"', m.group(2)) if q in P})
        if len(xy) >= 2: heads[m.group(1)] = max(((a, b) for a in xy for b in xy), key=lambda ab: math.dist(*ab))
    cols = {m[0]: P.get(m[1]) for m in re.findall(r'^\s+LINE\s+"(\S+)"\s+COLUMN\s+"(\S+)"', t, re.M)}
    hs, cs = collections.defaultdict(list), collections.defaultdict(list)
    for k, st in re.findall(r'^\s+AREAASSIGN\s+"(\S+)"\s+"([^"]+)"', t, re.M):
        if k in heads: hs[" ".join(st.split())].append((k, heads[k]))
    for k, st in re.findall(r'^\s+LINEASSIGN\s+"(\S+)"\s+"([^"]+)"', t, re.M):
        if cols.get(k): cs[" ".join(st.split())].append(cols[k])
    return hs, cs


def dxf_unit(job_dir):
    files = sorted(glob.glob(os.path.join(job_dir, "dxf", "*.dxf")))
    if not files: return "?"
    lines = open(files[0], encoding="utf-8", errors="replace").read(20000).splitlines()
    for i, s in enumerate(lines):
        if s.strip() == "$INSUNITS" and i + 2 < len(lines): return {"1": "in", "2": "ft", "4": "mm", "5": "cm", "6": "m"}.get(lines[i + 2].strip(), lines[i + 2].strip())
    return "?"


for work in [w for w in sys.argv[1:] if w != "--all"]:
    tot_h = tot_hit = 0; by_unit = collections.Counter(); by_unit_h = collections.Counter(); per_set = []
    for job in sorted(os.listdir(work)):
        e2k = os.path.join(work, job, "out.e2k")
        if not os.path.isfile(e2k): continue
        hs, cs = model(e2k); unit = dxf_unit(os.path.join(work, job))
        n = sum(len(v) for v in hs.values()); hits = []
        for st, heads in hs.items():
            for k, (a, b) in heads:
                L = math.dist(a, b)
                if L < 1e-6: continue
                ux, uy = (b[0] - a[0]) / L, (b[1] - a[1]) / L
                for c in cs.get(st, []):
                    t = (c[0] - a[0]) * ux + (c[1] - a[1]) * uy; off = abs((c[0] - a[0]) * -uy + (c[1] - a[1]) * ux)
                    if END_CLEAR < t < L - END_CLEAR and off <= REACH:
                        hits.append((st, k, a, b, c, off)); break
        tot_h += n; tot_hit += len(hits); by_unit[unit] += len(hits); by_unit_h[unit] += n
        if hits: per_set.append((job, unit, len(hits), n, hits))
    print(f"== {work}")
    print(f"headers over a column: {tot_hit} of {tot_h}; by drawing unit: " + ", ".join(f"{u} {by_unit[u]} of {by_unit_h[u]}" for u in sorted(by_unit_h)))
    for job, unit, k, n, hits in sorted(per_set, key=lambda r: -r[2]):
        print(f"  {job} ({unit}): {k} of {n}")
        for st, name, a, b, c, off in (hits if "--all" in sys.argv else hits[:3]):
            print(f"      {st:10s} {name:6s} ({a[0]:.0f},{a[1]:.0f})-({b[0]:.0f},{b[1]:.0f}) span {math.dist(a, b):.0f}  column ({c[0]:.0f},{c[1]:.0f}) {off:.0f} off the line")
