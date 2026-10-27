r"""twin_look.py <gate-work-dir> <job> <x> <y> <view-substring> [<view-substring> ...] : renders, around one model point, the
linework of the named views carried into the model through their sheets.csv placement - so a column drawn in two places can be
LOOKED at on both drawings at once. Writes <gate-work-dir>/<job>/look-<x>-<y>.png, a 3 m square, one colour per view.

COVERS: LINE, LWPOLYLINE, POLYLINE, CIRCLE, ARC on every layer of the named views (layer KOR_V_COL drawn heavier). DOES NOT:
text, hatches, blocks; views without a placement. Read it as a picture, not a measurement - twin_trace.py has the numbers.
"""
import csv, math, os, sys
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

work, job, cx, cy, wants = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5:]
half = 1500.0
fig, ax = plt.subplots(figsize=(8, 8))
colours = ["tab:blue", "tab:red", "tab:green", "tab:purple"]
for row in csv.DictReader(open(os.path.join(work, job, "sheets.csv"), encoding="utf-8-sig")):
    if not row["dxf_files"]: continue
    for f, pl in zip(row["dxf_files"].split(" | "), (row.get("placement") or "").split(" | ")):
        hit = [w for w in wants if w in f]
        if not pl or not hit: continue
        colour = colours[wants.index(hit[0]) % len(colours)]
        p = {k: float(v) for k, v in (kv.split("=") for kv in pl.split(";"))}
        r = math.radians(p["rot"]); c, s = math.cos(r), math.sin(r)
        def T(x, y):
            x, y = x * p["scale"], y * p["scale"]
            return x * c - y * s + p["dx"] + p["ox"], x * s + y * c + p["dy"] + p["oy"]
        for e in ezdxf.readfile(os.path.join(work, job, "dxf", f)).modelspace():
            t = e.dxftype(); heavy = e.dxf.layer == "KOR_V_COL"
            if t == "LINE": pts = [T(e.dxf.start.x, e.dxf.start.y), T(e.dxf.end.x, e.dxf.end.y)]
            elif t == "LWPOLYLINE": pts = [T(v[0], v[1]) for v in e.get_points()] + ([T(*e.get_points()[0][:2])] if e.closed else [])
            elif t == "POLYLINE": pts = [T(v.dxf.location.x, v.dxf.location.y) for v in e.vertices] + ([T(e.vertices[0].dxf.location.x, e.vertices[0].dxf.location.y)] if e.is_closed else [])
            elif t in ("CIRCLE", "ARC"):
                a0, a1 = (0, 360) if t == "CIRCLE" else (e.dxf.start_angle, e.dxf.end_angle % 360 + (360 if e.dxf.end_angle % 360 <= e.dxf.start_angle else 0))
                pts = [T(e.dxf.center.x + e.dxf.radius * math.cos(math.radians(a)), e.dxf.center.y + e.dxf.radius * math.sin(math.radians(a))) for a in [a0 + (a1 - a0) * k / 24 for k in range(25)]]
            else: continue
            if not any(abs(x - cx) <= half and abs(y - cy) <= half for x, y in pts): continue
            ax.plot([q[0] for q in pts], [q[1] for q in pts], color=colour, lw=2.2 if heavy else 0.6, alpha=0.9 if heavy else 0.5)
        ax.plot([], [], color=colour, label=f"{f[:55]} [{row['storeys'][:20]}]")
ax.plot([cx], [cy], "k+", ms=14)
ax.set_xlim(cx - half, cx + half); ax.set_ylim(cy - half, cy + half); ax.set_aspect("equal")
ax.legend(fontsize=7, loc="upper left"); ax.set_title(f"{job} around ({cx:.0f}, {cy:.0f}) mm, model frame; + = the twin")
out = os.path.join(work, job, f"look-{cx:.0f}-{cy:.0f}.png")
fig.savefig(out, dpi=110); print(out)
