r"""twin_trace.py <gate-work-dir> <job> <near-miss.out> : each WRONG TWIN column that column_stack_near_miss.py found, traced
back to the sheets that drew it, through sheets.csv's placement column (d92e50b1).

For every WRONG TWIN line ("  L2  (x y) stack misses by N mm  WRONG TWIN (hers M mm)") it prints:
  - our model's columns on the storeys directly above and below within 700 mm (the stack the twin misses);
  - every column outline on every placed view's KOR_V_COL layer that the view's placement carries to within 700 mm of the twin
    - the view's file, the storeys the ledger gives it, where the outline lands and how far from the twin.
So it says which sheet drew the column the build kept, and whether another sheet drew the one the stack agrees with.

The transform is placement_proof.py's (scale by S, turn R degrees, shift (dx, dy), add (ox, oy)), proved there at 90-100% of
columns landing on a model column on 31048, 31130, 31183.
COVERS: column outlines (CIRCLE, LWPOLYLINE, POLYLINE centres) on KOR_V_COL, every placed view of the job. DOES NOT: decide which
twin is right (the near-miss tool's verdict is taken as given); walls; views with no placement (unplaced views are listed by name
only). A same-class fault it would miss: a twin drawn by a view whose placement is itself wrong by about the twin's offset -
the outline lands on the kept twin and the trace reads the sheet as the cause.
"""
import csv, math, os, re, sys
import ezdxf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import members, norm   # noqa: E402

work, job, report = sys.argv[1], sys.argv[2], sys.argv[3]
cols = members(os.path.join(work, job, "out.e2k"))
lines = open(os.path.join(work, job, "out.e2k"), encoding="utf-8", errors="replace").read().splitlines()
order = [norm(m.group(1)) for s in lines if (m := re.match(r'\s*STORY\s+"([^"]+)"', s))]   # top first
twins = [(norm(m.group(1)), float(m.group(2)), float(m.group(3)))
         for m in (re.match(r"\s+(\S+)\s+\((-?\d+) (-?\d+)\).*WRONG TWIN", s) for s in open(report, encoding="utf-8")) if m]

views, unplaced = [], []
for row in csv.DictReader(open(os.path.join(work, job, "sheets.csv"), encoding="utf-8-sig")):
    if not row["dxf_files"]: continue
    for f, pl in zip(row["dxf_files"].split(" | "), (row.get("placement") or "").split(" | ")):
        if not pl: unplaced.append(f); continue
        p = {k: float(v) for k, v in (kv.split("=") for kv in pl.split(";"))}
        r = math.radians(p["rot"]); c, s = math.cos(r), math.sin(r)
        try: doc = ezdxf.readfile(os.path.join(work, job, "dxf", f))
        except Exception as ex: unplaced.append(f"{f} (unreadable: {ex})"); continue
        at = []
        for e in doc.modelspace().query('*[layer=="KOR_V_COL"]'):
            if e.dxftype() == "CIRCLE": x, y = e.dxf.center.x, e.dxf.center.y
            elif e.dxftype() in ("LWPOLYLINE", "POLYLINE"):
                pts = [(v[0], v[1]) for v in e.get_points()] if e.dxftype() == "LWPOLYLINE" else [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
                x, y = (min(a for a, _ in pts) + max(a for a, _ in pts)) / 2, (min(b for _, b in pts) + max(b for _, b in pts)) / 2
            else: continue
            x, y = x * p["scale"], y * p["scale"]
            at.append((x * c - y * s + p["dx"] + p["ox"], x * s + y * c + p["dy"] + p["oy"]))
        views.append((f, row["storeys"], at))

print(f"{job}: {len(twins)} WRONG TWIN column(s); {len(views)} placed view(s), {len(unplaced)} unplaced")
for st, x, y in twins:
    print(f"\n{st} ({x:.0f} {y:.0f})")
    i = order.index(st) if st in order else -1
    for nb in ([order[i - 1]] if i > 0 else []) + ([order[i + 1]] if 0 <= i < len(order) - 1 else []):
        near = sorted((math.hypot(a - x, b - y), a, b) for a, b in cols.get(nb, {"column": []})["column"])
        near = [n for n in near if n[0] <= 700]
        print(f"  model {nb:8}: " + ("; ".join(f"({a:.0f} {b:.0f}) {d:.0f} mm" for d, a, b in near) if near else "nothing within 700 mm"))
    for f, storeys, at in views:
        for a, b in at:
            d = math.hypot(a - x, b - y)
            if d <= 700:
                print(f"  view {f[:60]:60} [{storeys}] draws ({a:.0f} {b:.0f}) {d:.0f} mm from the twin")
if unplaced: print("\nunplaced: " + "; ".join(unplaced))
