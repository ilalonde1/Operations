"""Chain the slab-edge linework of one DXF sheet and report how close each ring is to closing.

    python docs/etabs-handoff/chains.py <sheet.dxf> [layerSubstring=SLABEDG]

Reads LINE, LWPOLYLINE and POLYLINE/VERTEX. The last of those matters: a Revit export writes loose
LINEs, but the PDF route writes POLYLINE with VERTEX children, so until 2026-09-10 this said
"0 segment(s)" on every DXF the PDF route produced and looked like an answer.
"""
import math
import sys

path = sys.argv[1]
want = sys.argv[2] if len(sys.argv) > 2 else "SLABEDG"

with open(path, encoding="utf-8", errors="replace") as fh:
    raw = [ln.rstrip("\n").rstrip("\r") for ln in fh]

segments = []

# POLYLINE carries its points as following VERTEX entities, so gather those first
i = 0
poly_layer = None
poly_pts = []
while i < len(raw) - 1:
    if raw[i].strip() != "0":
        i += 1
        continue
    kind = raw[i + 1].strip()
    j = i + 2
    codes = []
    while j < len(raw) - 1 and raw[j].strip() != "0":
        codes.append((raw[j].strip(), raw[j + 1].strip()))
        j += 2
    d = {c: v for c, v in codes}
    if kind == "POLYLINE":
        poly_layer, poly_pts = d.get("8", ""), []
    elif kind == "VERTEX" and poly_layer is not None:
        try:
            poly_pts.append((float(d["10"]), float(d["20"])))
        except (KeyError, ValueError):
            pass
    elif kind == "SEQEND":
        if poly_layer is not None and want.upper() in poly_layer.upper():
            for a, b in zip(poly_pts, poly_pts[1:]):
                segments.append((a, b))
        poly_layer, poly_pts = None, []
    i = j

i = 0
while i < len(raw) - 1:
    if raw[i].strip() != "0":
        i += 1
        continue
    kind = raw[i + 1].strip()
    if kind not in ("LINE", "LWPOLYLINE", "POLYLINE", "VERTEX"):
        i += 2
        continue

    j = i + 2
    codes = []
    while j < len(raw) - 1 and raw[j].strip() != "0":
        codes.append((raw[j].strip(), raw[j + 1].strip()))
        j += 2

    layer = next((v for c, v in codes if c == "8"), "")
    if want.upper() in layer.upper():
        if kind == "LINE":
            d = {c: v for c, v in codes}
            try:
                segments.append(((float(d["10"]), float(d["20"])),
                                 (float(d["11"]), float(d["21"]))))
            except (KeyError, ValueError):
                pass
        elif kind == "LWPOLYLINE":
            xs = [float(v) for c, v in codes if c == "10"]
            ys = [float(v) for c, v in codes if c == "20"]
            flag = next((int(v) for c, v in codes if c == "70"), 0)
            pts = list(zip(xs, ys))
            for a, b in zip(pts, pts[1:]):
                segments.append((a, b))
            if flag & 1 and len(pts) > 2:
                segments.append((pts[-1], pts[0]))
    i = j

print(f"{len(segments)} segment(s) on layers containing '{want}'")

TOL = 0.05


def key(p):
    return (round(p[0] / TOL), round(p[1] / TOL))


unused = list(range(len(segments)))
ends = {}
for idx, (a, b) in enumerate(segments):
    ends.setdefault(key(a), []).append(idx)
    ends.setdefault(key(b), []).append(idx)

seen = set()
chains = []
for start in range(len(segments)):
    if start in seen:
        continue
    seen.add(start)
    pts = [segments[start][0], segments[start][1]]
    grew = True
    while grew:
        grew = False
        for end_i, at in ((0, pts[0]), (1, pts[-1])):
            for idx in ends.get(key(at), []):
                if idx in seen:
                    continue
                a, b = segments[idx]
                if key(a) == key(at):
                    nxt = b
                elif key(b) == key(at):
                    nxt = a
                else:
                    continue
                seen.add(idx)
                if end_i == 0:
                    pts.insert(0, nxt)
                else:
                    pts.append(nxt)
                grew = True
                break
            if grew:
                break
    chains.append(pts)


def area(pts):
    s = 0.0
    for a, b in zip(pts, pts[1:] + pts[:1]):
        s += a[0] * b[1] - b[0] * a[1]
    return abs(s) / 2.0 / 144.0


chains.sort(key=lambda c: -area(c))
print(f"\n{len(chains)} chain(s), largest first:\n")
for n, pts in enumerate(chains[:12], 1):
    gap = math.dist(pts[0], pts[-1])
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    state = "CLOSED" if gap <= TOL else f"OPEN, ends {gap:8.1f} in apart"
    print(f"{n:2d}. {len(pts):4d} pts  area-if-closed {area(pts):10,.0f} sq ft  "
          f"bbox {max(xs) - min(xs):7.0f} x {max(ys) - min(ys):7.0f}  {state}")
    if n <= 2:
        for p in pts:
            print(f"        ({p[0]:10.1f}, {p[1]:10.1f})")
        print(f"        gap runs from ({pts[-1][0]:.1f}, {pts[-1][1]:.1f}) "
              f"to ({pts[0][0]:.1f}, {pts[0][1]:.1f})")
