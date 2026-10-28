r"""placement_proof.py <gate-work-dir> [job ...] : sheets.csv's placement column, proved against the model it describes.

For every placed view with a placement ("rot=R;dx=X;dy=Y;scale=S;ox=OX;oy=OY"): each column outline on its DXF's KOR_V_COL layer
is carried into the model as the column says - scale by S, turn R degrees, shift (X, Y), add (OX, OY) - and counted ON when a
column of the written model on one of the view's storeys stands within 50 mm (model mm). Prints, per set, columns ON of columns
carried, and the views whose share is under half (named). Exit 1 when any set carries fewer than 90% of its columns onto the model.

COVERS: the transform's columns, by position. DOES NOT: walls or plates (the same transform carries them; not checked here);
columns a later step dropped or merged (the stack merge, the building cut) - they count as not ON, so the share is a floor,
not an exact match. SAME-CLASS FAULT IT MISSES: a placement right for the columns and wrong for a view's walls - impossible by
construction (one frame per view), but not tested here.
"""
import csv, math, os, re, sys
import ezdxf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import members, norm   # noqa: E402

work = sys.argv[1]; only = set(sys.argv[2:])
bad = 0
for job in sorted(os.listdir(work)):
    if only and job not in only: continue
    sc, e2k = os.path.join(work, job, "sheets.csv"), os.path.join(work, job, "out.e2k")
    if not (os.path.isfile(sc) and os.path.isfile(e2k)): continue
    cols = members(e2k)
    on = carried = 0; weak = []
    for row in csv.DictReader(open(sc, encoding="utf-8-sig")):
        if not row.get("placement") or not row["dxf_files"]: continue
        for f, pl in zip(row["dxf_files"].split(" | "), row["placement"].split(" | ")):
            if not pl: continue
            p = dict(kv.split("=") for kv in pl.split(";")); p = {k: float(v) for k, v in p.items()}
            r = math.radians(p["rot"]); c, s = math.cos(r), math.sin(r)
            mine = [q for v in cols.values() for q in v["column"]]
            try: doc = ezdxf.readfile(os.path.join(work, job, "dxf", f))
            except Exception: continue
            n = hit = 0
            for e in doc.modelspace().query('*[layer=="KOR_V_COL"]'):
                if e.dxftype() == "CIRCLE": x, y = e.dxf.center.x, e.dxf.center.y
                elif e.dxftype() in ("LWPOLYLINE", "POLYLINE"):
                    pts = [(v[0], v[1]) for v in e.get_points()] if e.dxftype() == "LWPOLYLINE" else [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
                    x, y = (min(a for a, _ in pts) + max(a for a, _ in pts)) / 2, (min(b for _, b in pts) + max(b for _, b in pts)) / 2
                else: continue
                x, y = x * p["scale"], y * p["scale"]
                mx, my = x * c - y * s + p["dx"] + p["ox"], x * s + y * c + p["dy"] + p["oy"]
                n += 1
                if mine and min(math.hypot(mx - a, my - b) for a, b in mine) <= 50: hit += 1
            on += hit; carried += n
            if n and hit * 2 < n: weak.append(f"{f[:40]} {hit}/{n}")
    if not carried: continue
    share = on / carried
    if share < 0.9: bad += 1
    print(f"{job}: {on} of {carried} columns carried onto a model column ({share:.0%})" + (f"; views under half: {'; '.join(weak)}" if weak else ""))
sys.exit(1 if bad else 0)
