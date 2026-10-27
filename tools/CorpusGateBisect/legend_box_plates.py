r"""legend_box_plates.py [work-dir | model.e2k ...] : FLOOR plates that are a sheet's LEGEND FRAME read as a floor.

The class, in one sentence (2026-10-27): a bare rectangle drawn beyond the building - the frame of a sheet's key or legend,
holding at most one sample stroke and no column - is closed by the slab pass and written as a storey's floor. Found looking at
the 21 storeys of the 57-set gate (run fff31290, develop c03a0e07) whose members stand 80%+ off every plate: 12 of them, on 8
sets, carry one (31053 L2, 31066 L1 L3 ROOF, 31085 L3 L4, 31093 L2, 31108 L3, 31158 L1 L2, 31171 L3); the other 9 are a floor
read in part (a core, a wing) or a plate off its walls - a different fault, not this one.

A plate is a LEGEND BOX when: (a) it is a rectangle - four corners after collinear points are dropped, every corner square
within 1 deg - of at least MIN_SQFT; (b) no column and no opening of its storey stands inside it, and at most one wall
midpoint does (31053 L17's whole floor, read square beside its walls, holds its stair opening); (c) the box round every
OTHER member of the storey (walls by midpoint, columns) does not touch it - it lies wholly beyond the building. A storey with
no member outside the plate is not judged (there is no building for it to stand beyond).
Exit 1 when any plate is a legend box, so it fails until the build stops writing them.

COVERS: the written model (FLOOR areas, LINE COLUMN, AREA PANEL walls, by AREAASSIGN/LINEASSIGN storey), our own models only.
DOES NOT: which sheet drew the box (the e2k does not say; the build report does); a legend frame that overlaps the building's
box (a key plan inset inside the drawing's extent); a legend frame that is not a rectangle (rounded or notched); a real floor
that is a bare rectangle with no member on it at all beyond the building - a detached slab the drawings frame but hold nothing
on, which this would name as a legend box (none seen in the 21 storeys looked at).
SAME-CLASS FAULT IT MISSES: a schedule or title-block cell closed as a plate INSIDE the building's box (31183 P1's edge boxes,
crossed by its walls, are that shape and are not flagged here).

⚠ IT JUDGES THE WRITTEN MODEL, NOT THE BUILD'S STATE WHEN STEP 185 RUNS - AND OVER- AND UNDER-PREDICTS THE STEP BECAUSE OF
IT (measured 2026-10-27, gate 8acd14bc against fff31290). In the build, a storey still carries walls a later gate drops
before writing - legend linework read as walls, strays - so a frame this tool sees holding 0-1 strokes can hold several in the
build, and the step rightly leaves it (7 of the 20 sets named here did not change: 31093 L2, 31162, 31053, 30864, 31039,
31048, 31098). And the build takes frames this tool never sees, because a later gate had already dropped them before writing
(30990 P3, 31017, 31037, 70057 - the written models identical up to point names), or because the members' box differed
(31064 L1, 31183 P1). Use it to find the class in written models; use the build report's "legend frame" lines to see what
the step did.
Exit 0 none; 1 any; 2 bad arguments.
"""
import glob, math, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import model, members, plates, inside, storey_keys, MIRROR   # noqa: E402

# The smallest legend frame looked at is 653 sq ft (31053 L1); rectangles of 2-100 sq ft beyond the building are pads and
# symbols nobody has looked at yet, and are not named as this class.
MIN_SQFT = 500


def corners(p):
    """the polygon with collinear (and repeated) points dropped"""
    out = []
    n = len(p)
    for i in range(n):
        a, b, c = p[i - 1], p[i], p[(i + 1) % n]
        ux, uy, vx, vy = b[0] - a[0], b[1] - a[1], c[0] - b[0], c[1] - b[1]
        lu, lv = math.hypot(ux, uy), math.hypot(vx, vy)
        if lu < 1 or lv < 1: continue
        if abs(ux * vy - uy * vx) / (lu * lv) < 0.01: continue   # straight on
        out.append(b)
    return out


def rectangle(p):
    c = corners(p)
    if len(c) != 4: return False
    for i in range(4):
        a, b, d = c[i - 1], c[i], c[(i + 1) % 4]
        ux, uy, vx, vy = b[0] - a[0], b[1] - a[1], d[0] - b[0], d[1] - b[1]
        if abs(ux * vx + uy * vy) / (math.hypot(ux, uy) * math.hypot(vx, vy)) > math.sin(math.radians(1)): return False
    return True


def storeys_of(path):
    return sorted({m.group(1) for s in open(path, encoding="utf-8", errors="replace")
                   if (m := re.match(r'\s*AREAASSIGN\s+"[^"]+"\s+"([^"]+)"\s+SECTION', s))})


def openings(path):
    """storey -> opening centres: areas written AREA "KO.." AREA n <points>, assigned OPENING "Yes" """
    _, pts, _, _, _, _, lines = model(path)
    shapes = {m.group(1): re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]
              for s in lines if (m := re.match(r'\s*AREA\s+"([^"]+)"\s+AREA\s+(\d+)\s+(.*)', s))}
    out = {}
    for s in lines:
        if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+OPENING\s+"Yes"', s)) and m.group(1) in shapes:
            p = [pts[q] for q in shapes[m.group(1)] if q in pts]
            if p: out.setdefault(m.group(2), []).append((sum(a for a, _ in p) / len(p), sum(b for _, b in p) / len(p)))
    return out


def legend_boxes(path):
    found = []
    mem = members(path)
    holes = openings(path)
    for st in storeys_of(path):
        here = mem.get(next(iter(storey_keys(st))), {"column": [], "wall": []})
        for th, sqft, centre, poly in plates(path, st):
            if sqft < MIN_SQFT or not rectangle(poly): continue
            if any(inside(o, poly) for o in holes.get(st, [])): continue   # a floor with a stair cut in it is a floor
            cols_in = [c for c in here["column"] if inside(c, poly)]
            walls_in = [w for w in here["wall"] if inside(w, poly)]
            if cols_in or len(walls_in) > 1: continue
            rest = [q for q in here["column"] + here["wall"] if q not in walls_in]
            if not rest: continue
            x0, x1 = min(q[0] for q in rest), max(q[0] for q in rest)
            y0, y1 = min(q[1] for q in rest), max(q[1] for q in rest)
            px0, px1 = min(q[0] for q in poly), max(q[0] for q in poly)
            py0, py1 = min(q[1] for q in poly), max(q[1] for q in poly)
            if px1 < x0 or px0 > x1 or py1 < y0 or py0 > y1:
                found.append((st, sqft, centre, len(walls_in)))
    return found


args = sys.argv[1:] or [os.path.join(MIRROR, "corpus")]
models = []
for a in args:
    if os.path.isdir(a): models += sorted(glob.glob(os.path.join(a, "*", "out.e2k")))
    elif os.path.isfile(a): models.append(a)
    else: print(__doc__); sys.exit(2)
total = sets = 0
for m in models:
    boxes = legend_boxes(m)
    if not boxes: continue
    sets += 1; total += len(boxes)
    job = os.path.basename(os.path.dirname(m))
    print(f"{job}: " + "; ".join(f"{st} {sqft:,.0f} sq ft at ({c[0]:,.0f}, {c[1]:,.0f}), {w} wall sample(s)" for st, sqft, c, w in boxes))
print(f"\n{total} legend box(es) written as floors, on {sets} of {len(models)} models")
sys.exit(1 if total else 0)
