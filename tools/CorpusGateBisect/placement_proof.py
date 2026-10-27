r"""placement_proof.py <gate-work-dir> [job ...] : sheets.csv's placement column, proved against the model it describes.

For every placed view with a placement ("rot=R;dx=X;dy=Y;scale=S;ox=OX;oy=OY"): each column outline on its DXF's KOR_V_COL layer
is carried into the model as the column says - scale by S, turn R degrees, shift (X, Y), add (OX, OY) - and counted ON when a
column of the written model stands within 50 mm of it, ON ANY STOREY. Any storey, because a member carries the storey at its TOP:
a foundation plan's columns are written on the storey above it, and matching on the view's own storeys read those views 0 of N
(31130's P3 foundation plans, 31183's P2 parkade) while their placement was right - measured 2026-10-27: own storey 60-64%, any
storey 90-100% on 31048, 31130, 31183. The own-storey share is printed beside it, for reading, not judged.

A WRONG PLACEMENT reads as a view carrying almost none of its columns onto the model; a column a later step dropped or merged
(the building cut, a twin) lowers the total a little. So: exit 1 when any view with 5 or more columns carries under half.

COVERS: the transform's columns, by position. DOES NOT: walls or plates (the same frame carries them; not checked here); which
storey a column was written on. SAME-CLASS FAULT IT MISSES: a placement off by exactly one bay on a regular grid - every column
lands on a neighbour's place and reads ON.
"""
import csv, math, os, sys
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
    every = list({q for v in cols.values() for q in v["column"]})
    on = own = carried = 0; weak = []
    for row in csv.DictReader(open(sc, encoding="utf-8-sig")):
        if not row.get("placement") or not row["dxf_files"]: continue
        for f, pl in zip(row["dxf_files"].split(" | "), row["placement"].split(" | ")):
            if not pl: continue
            p = {k: float(v) for k, v in (kv.split("=") for kv in pl.split(";"))}
            r = math.radians(p["rot"]); c, s = math.cos(r), math.sin(r)
            mine = [q for st in (row["storeys"] or "").split(",") for q in cols.get(norm(st), {"column": []})["column"]]
            try: doc = ezdxf.readfile(os.path.join(work, job, "dxf", f))
            except Exception: continue
            n = hit = hit_own = 0
            for e in doc.modelspace().query('*[layer=="KOR_V_COL"]'):
                if e.dxftype() == "CIRCLE": x, y = e.dxf.center.x, e.dxf.center.y
                elif e.dxftype() in ("LWPOLYLINE", "POLYLINE"):
                    pts = [(v[0], v[1]) for v in e.get_points()] if e.dxftype() == "LWPOLYLINE" else [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
                    x, y = (min(a for a, _ in pts) + max(a for a, _ in pts)) / 2, (min(b for _, b in pts) + max(b for _, b in pts)) / 2
                else: continue
                x, y = x * p["scale"], y * p["scale"]
                mx, my = x * c - y * s + p["dx"] + p["ox"], x * s + y * c + p["dy"] + p["oy"]
                n += 1
                if every and min(math.hypot(mx - a, my - b) for a, b in every) <= 50: hit += 1
                if mine and min(math.hypot(mx - a, my - b) for a, b in mine) <= 50: hit_own += 1
            on += hit; own += hit_own; carried += n
            if n >= 5 and hit * 2 < n: weak.append(f"{f[:40]} {hit}/{n}")
    if not carried: continue
    if weak: bad += 1
    print(f"{job}: {on} of {carried} columns carried onto a model column ({on / carried:.0%}); on the view's own storey {own} ({own / carried:.0%})"
          + (f"; VIEWS UNDER HALF: {'; '.join(weak)}" if weak else ""))
sys.exit(1 if bad else 0)
