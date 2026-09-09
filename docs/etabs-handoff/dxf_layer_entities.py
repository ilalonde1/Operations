"""Entities on a layer of an ASCII DXF: types counted, and each POLYLINE/LWPOLYLINE's box (DXF units).
usage: dxf_layer_entities.py <dxf> <layer> [maxRows]"""
import sys
path, layer = sys.argv[1], sys.argv[2]
max_rows = int(sys.argv[3]) if len(sys.argv) > 3 else 25
L = open(path, encoding="latin-1").read().splitlines()
n = len(L)
# split into entities: each starts at a "0" code line
ents = []
i = 0
while i + 1 < n:
    if L[i].strip() == "0":
        typ = L[i + 1].strip()
        j = i + 2
        d = {}
        pts = []
        while j + 1 < n and L[j].strip() != "0":
            code = L[j].strip(); val = L[j + 1].strip()
            if code == "8": d["layer"] = val
            elif code == "10": pts.append([float(val), None])
            elif code == "20" and pts and pts[-1][1] is None: pts[-1][1] = float(val)
            elif code == "11": d["x2"] = float(val)
            elif code == "21": d["y2"] = float(val)
            elif code == "70": d["flags"] = int(val)
            j += 2
        ents.append((typ, d, pts))
        i = j
    else:
        i += 1
types = {}
polys = []
cur = None
for typ, d, pts in ents:
    if typ == "POLYLINE" and d.get("layer") == layer:
        cur = {"pts": [], "flags": d.get("flags", 0)}
    elif typ == "VERTEX" and cur is not None:
        if pts: cur["pts"].append(tuple(pts[0]))
    elif typ == "SEQEND" and cur is not None:
        polys.append(cur); cur = None
    if d.get("layer") == layer:
        types[typ] = types.get(typ, 0) + 1
        if typ in ("LWPOLYLINE", "HATCH"):
            polys.append({"pts": [tuple(p) for p in pts], "flags": d.get("flags", 0)})
print("types on", layer, ":", ", ".join(f"{k} x{v}" for k, v in sorted(types.items())))
rows = []
for p in polys:
    xs = [q[0] for q in p["pts"] if q[1] is not None]; ys = [q[1] for q in p["pts"] if q[1] is not None]
    if not xs: continue
    w = max(xs) - min(xs); h = max(ys) - min(ys)
    rows.append((w * h, len(xs), w, h, min(xs), min(ys), p["flags"] & 1))
print(f"{len(rows)} polylines")
for area, k, w, h, x, y, closed in sorted(rows, reverse=True)[:max_rows]:
    print(f"  {k:3d} pts  box {w:9.1f} x {h:9.1f}  at ({x:10.1f},{y:10.1f})  {'closed' if closed else 'open'}")
