r"""Step 183 prototype, on the PDF page itself (page points): the heavy pen's lines plus the dashed pen's dashes joined into
lines, cut into faces; each face filled by the thickness call-out(s) standing in it. To LOOK at whether the faces are the
drawing's zones before any C# is written. COVERS: straight strokes of the two black pens, «N SLAB» words. DOES NOT: curves,
the reader's own classification, the slab pass's bridges; the dash join is the footing reader's rule (collinear, gap <=)."""
import re, sys, collections, math
import fitz
from shapely.geometry import LineString, Point, box
from shapely.ops import polygonize, unary_union
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon as MPoly

pdf, pageno, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
clip = box(*map(float, sys.argv[4].split(","))) if len(sys.argv) > 4 and sys.argv[4] else None
GAP = float(sys.argv[5]) if len(sys.argv) > 5 else 10.0   # gap in page pt (~355 mm at 1:100)
HEAVY = float(sys.argv[6]) if len(sys.argv) > 6 else 0.96
DASH = float(sys.argv[7]) if len(sys.argv) > 7 else 0.6
COL = 0.6
pg = fitz.open(pdf)[pageno - 1]
heavy, dashes = [], []
for g in pg.get_drawings():
    w = round(g.get("width") or 0, 2); c = tuple(round(x, 2) for x in (g.get("color") or ()))
    if c != (0, 0, 0) or w not in (HEAVY, DASH): continue
    for it in g["items"]:
        if it[0] != "l": continue
        a, b = (it[1].x, it[1].y), (it[2].x, it[2].y)
        if math.dist(a, b) < 0.3: continue
        if clip is not None and not clip.intersects(LineString([a, b])): continue
        (heavy if w == HEAVY else dashes).append((a, b))

# join dashes: same direction (1 deg), same offset (COL pt), gaps <= GAP; a run of >= 3 pieces is a dashed line
def key(a, b):
    ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0])) % 180
    ux, uy = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    off = -a[0] * uy + a[1] * ux
    t0, t1 = sorted((a[0] * ux + a[1] * uy, b[0] * ux + b[1] * uy))
    return ang, off, t0, t1, ux, uy
pieces = sorted((key(a, b) for a, b in dashes if math.dist(a, b) < 40), key=lambda k: (round(k[0]), round(k[1])))
groups = collections.defaultdict(list)
for k in pieces: groups[(round(k[0] / 1.0) % 180, round(k[1] / COL))].append(k)
chains = []
for g in groups.values():
    g.sort(key=lambda k: k[2]); run = [g[0]]
    for k in g[1:]:
        if k[2] - run[-1][3] <= GAP and k[2] - run[-1][3] >= -1: run.append(k)
        else:
            if len(run) >= 3: chains.append(run)
            run = [k]
    if len(run) >= 3: chains.append(run)
dlines = []
for run in chains:
    ang, off, ux, uy = run[0][0], run[0][1], run[0][4], run[0][5]
    t0, t1 = run[0][2], max(k[3] for k in run)
    p = lambda t: (t * ux - off * uy, t * uy + off * ux)
    dlines.append(LineString([p(t0 - GAP / 2), p(t1 + GAP / 2)]))
lines = [LineString(s) for s in heavy] + dlines
faces = [f for f in polygonize(unary_union(lines)) if f.area > 50]
calls = []
for w in pg.get_text("words"):
    pass
txt = pg.get_text("dict")
for b in txt["blocks"]:
    for l in b.get("lines", []):
        s = " ".join(sp["text"] for sp in l["spans"]).strip()
        m = re.match(r"^(\d{3,4}|\d{1,2}(?=\s*[\"”″]))\s*[\"”″]?\s*(?:DP\.?\s+)?SLAB", s)
        if m:
            x0, y0, x1, y1 = l["bbox"]; calls.append(((x0 + x1) / 2, (y0 + y1) / 2, int(m.group(1)), s))
cmap = {150: "violet", 200: "pink", 300: "tab:blue", 500: "tab:green", 600: "tab:orange", 900: "tab:red",
        8: "violet", 10: "tab:blue", 12: "tab:green", 14: "tab:orange", 18: "gold", 42: "tab:red", 48: "brown"}
fig, ax = plt.subplots(figsize=(18, 13))
stat = collections.Counter()
for f in faces:
    inside = sorted({c[2] for c in calls if f.contains(Point(c[0], c[1]))})
    col = cmap.get(inside[0], "k") if len(inside) == 1 else ("0.5" if inside else "white")
    stat["one" if len(inside) == 1 else "several" if inside else "none"] += 1
    ax.add_patch(MPoly(list(f.exterior.coords), closed=True, fc=col, alpha=0.35, ec="none"))
for s in heavy: ax.plot([s[0][0], s[1][0]], [s[0][1], s[1][1]], "r-", lw=0.7)
for d in dlines: ax.plot(*d.xy, "b-", lw=0.7)
for x, y, v, s in calls: ax.text(x, y, str(v), fontsize=6, color=cmap.get(v, "k"), ha="center", fontweight="bold")
if clip is not None: ax.set_xlim(clip.bounds[0], clip.bounds[2]); ax.set_ylim(clip.bounds[3], clip.bounds[1])
else: ax.invert_yaxis()
ax.set_aspect("equal"); plt.savefig(out, dpi=80, bbox_inches="tight")
print(f"heavy {len(heavy)} dash pieces {len(dashes)} -> {len(dlines)} dashed lines; faces {len(faces)}: {dict(stat)}; call-outs {len(calls)}")
