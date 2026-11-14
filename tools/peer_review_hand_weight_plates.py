"""Hand seismic weight, step 1: slab plate outlines off WHM's GA plans (2026-09-09 Structural IFT).

Reads ONLY the PDF vector paths (WHM's black 0.84 pt solid slab-edge linework, closed across gaps by the grey
wall/column poche fills it meets) and drawn-openings.csv. Writes plates.json (model-mm polygons per storey view)
and one PNG per distinct plan (outline in red over the page crop) for LOOKING at.

Transform (drawn-walls.csv header, fitted on WHM's drawn grid lines):
  px = 359.760 + ox + 0.0295276*(X - 12183.872); py = 339.840 + oy + 0.0295276*(76273.152 - Y)
S217 view C (ELEVATOR ROOF _ BLDG A) offset fitted here off the grid bubbles 4/5 and D/E: ox 1027.0, oy 868.5.
Run: python -I peer_review_hand_weight_plates.py [plan ids]   (KOR_HW_DIR = output folder; default the job's lateral/independent)
In the repo since JM-LAT-PREP 14 Nov 2026 (was scratchpad hw_slabs.py).
"""
import json, sys, os
import fitz
from shapely.geometry import LineString, Polygon, box, MultiPolygon
from shapely.ops import unary_union
from shapely import affinity

PDF = r"C:\VIsual Studio Projects\KOR.Drafter\work\01819-01-renfrew\inputs\2026-09-09 - Structural IFT.pdf"
OUT = os.environ.get("KOR_HW_DIR", os.path.join(os.path.dirname(os.path.dirname(PDF)), "..", "..", "jobs", "01819-01-renfrew", "review", "lateral", "independent"))   # plates.json and png/ go here (JM-LAT-PREP: was the helper's scratchpad hw/)
K = 0.0295276
R = 1.0  # pt: line buffer that bridges hairline gaps (34 mm); removed again by an inward buffer

# plan id -> (sheet, page0, ox, oy, model clip box X0,Y0,X1,Y1, storeys drawn by this plan)
PLANS = {
    "S205": ("S205", 8, 0.0, 0.0, (9000, 36000, 101000, 79000), ["P1/GF"]),
    "S207": ("S207", 10, 0.0, 0.0, (9000, 36000, 101000, 79000), ["L1"]),
    "S209": ("S209", 12, 0.0, 0.0, (9000, 36000, 101000, 79000), ["L2"]),
    "S211": ("S211", 14, 0.0, 0.0, (9000, 36000, 101000, 79000), ["L3"]),
    "S213": ("S213", 16, 0.0, 0.0, (9000, 36000, 101000, 79000), ["L4"]),
    "S215": ("S215", 18, 0.0, 0.0, (9000, 36000, 101000, 79000), ["L5", "L6", "L7", "L8"]),
    "S217A": ("S217", 20, 0.0, 0.0, (9000, 38000, 101000, 79000), ["L9"]),
    "S217B": ("S217", 20, 40.809, 922.68, (9000, 42000, 61000, 66000), ["L10"]),      # Bldg A pop-up roofs
    "S217C": ("S217", 20, 1027.0, 868.5, (34000, 44000, 50000, 66000), ["L11"]),      # Bldg A elevator roof
    "S219": ("S219", 22, 40.809, 0.0, (61000, 36000, 101000, 79000), ["L10", "L11", "L12", "L13", "L14"]),
    "S220": ("S220", 23, 40.809, 0.0, (61000, 36000, 101000, 79000), [f"L{i}" for i in range(15, 27)]),
    "S221": ("S221", 24, 40.809, 0.0, (61000, 36000, 101000, 79000), ["L27"]),
    "S222": ("S222", 25, 40.809, 0.0, (61000, 36000, 101000, 79000), ["L28"]),
    "S223": ("S223", 26, 40.809, 0.0, (61000, 36000, 101000, 79000), ["L29"]),
    "S224": ("S224", 27, 40.809, 0.0, (61000, 36000, 101000, 79000), ["L30", "L31"]),
    "S225": ("S225", 28, 40.809, 0.0, (61000, 36000, 101000, 79000), ["ROOF AMENITY"]),
    "S226": ("S226", 29, 40.809, 0.0, (61000, 36000, 101000, 79000), ["ROOF"]),
    "S227A": ("S227", 30, 40.809, -294.98, (61000, 36000, 101000, 79000), ["MECHANICAL SERVICE"]),
    "S227B": ("S227", 30, 40.809, 831.60, (61000, 36000, 101000, 79000), ["ELEVATOR PENTHOUSE"]),
}


def to_page(X, Y, ox, oy):
    return 359.760 + ox + K * (X - 12183.872), 339.840 + oy + K * (76273.152 - Y)


def to_model(px, py, ox, oy):
    return 12183.872 + (px - 359.760 - ox) / K, 76273.152 - (py - 339.840 - oy) / K


def black(c):
    return c is not None and max(c) < 0.05


def grey_poche(f):
    return f is not None and all(abs(v - 0.75) < 0.02 for v in f)


def extract(doc, pid, min_m2=8.0, closing_pt=0.0):
    sheet, pg, ox, oy, (X0, Y0, X1, Y1), _ = PLANS[pid]
    page = doc[pg]
    a = to_page(X0, Y1, ox, oy); b = to_page(X1, Y0, ox, oy)
    clip = box(a[0], a[1], b[0], b[1])
    geoms = []
    for g in page.get_drawings():
        dash = g.get("dashes")
        solid = dash in (None, "", "[] 0", "[] 0.0")
        if g["type"] in ("s", "fs") and black(g.get("color")) and abs((g.get("width") or 0) - 0.84) < 0.02 and solid:
            for it in g["items"]:
                if it[0] == "l":
                    geoms.append(LineString([(it[1].x, it[1].y), (it[2].x, it[2].y)]).buffer(R, cap_style=2))
                elif it[0] == "re":
                    r = it[1]; geoms.append(box(r.x0, r.y0, r.x1, r.y1).exterior.buffer(R, cap_style=2))
                elif it[0] == "c":
                    geoms.append(LineString([(p.x, p.y) for p in it[1:]]).buffer(R, cap_style=2))
        if g["type"] in ("f", "fs") and grey_poche(g.get("fill")):
            r = g["rect"]; geoms.append(box(r.x0, r.y0, r.x1, r.y1).buffer(R * 0.5))
    geoms = [x for x in geoms if x.intersects(clip)]
    U = unary_union(geoms).intersection(clip)
    if closing_pt:
        U = U.buffer(closing_pt).buffer(-closing_pt)
    parts = list(U.geoms) if hasattr(U, "geoms") else [U]
    filled = unary_union([Polygon(p.exterior) for p in parts if p.geom_type == "Polygon"])
    filled = filled.buffer(-R)  # back to the line centre
    fp = list(filled.geoms) if hasattr(filled, "geoms") else [filled]
    pt2 = (1 / K) ** 2 / 1e6  # pt^2 -> m2
    keep = [p for p in fp if p.area * pt2 >= min_m2]
    # to model mm
    out = []
    for p in keep:
        ring = [to_model(x, y, ox, oy) for x, y in p.exterior.coords]
        holes = [[to_model(x, y, ox, oy) for x, y in h.coords] for h in p.interiors]
        out.append(Polygon(ring, holes))
    return out, keep, (a, b)


def render(doc, pid, keep, ab, tag=""):
    sheet, pg, ox, oy, *_ = PLANS[pid]
    page = doc[pg]
    (a, b) = ab
    clip = fitz.Rect(a[0] - 20, a[1] - 20, b[0] + 20, b[1] + 20)
    pix = page.get_pixmap(clip=clip, dpi=40)
    # overlay: draw on a pixmap copy via a temp PDF page
    tmp = fitz.open(); tp = tmp.new_page(width=page.rect.width, height=page.rect.height)
    tp.show_pdf_page(tp.rect, doc, pg)
    sh = tp.new_shape()
    for p in keep:
        pts = list(p.exterior.coords)
        sh.draw_polyline([fitz.Point(x, y) for x, y in pts])
        sh.finish(color=(1, 0, 0), width=3, closePath=True)
        for h in p.interiors:
            sh.draw_polyline([fitz.Point(x, y) for x, y in h.coords]); sh.finish(color=(0, 0, 1), width=2)
    sh.commit()
    tp.get_pixmap(clip=clip, dpi=40).save(os.path.join(OUT, "png", f"plate_{pid}{tag}.png"))


if __name__ == "__main__":
    doc = fitz.open(PDF)
    only = sys.argv[1:] or list(PLANS)
    res = {}
    if os.path.exists(os.path.join(OUT, "plates.json")):
        res = json.load(open(os.path.join(OUT, "plates.json")))
    for pid in only:
        closing = 0.0
        if ":" in pid:
            pid, c = pid.split(":"); closing = float(c)
        polys, keep, ab = extract(doc, pid, closing_pt=closing)
        render(doc, pid, keep, ab)
        res[pid] = {"closing_pt": closing, "polys": [{"ext": [list(map(round, c)) for c in p.exterior.coords],
                    "holes": [[list(map(round, c)) for c in h.coords] for h in p.interiors],
                    "area_m2": round(p.area / 1e6, 2)} for p in polys]}
        print(pid, "closing", closing, [round(p.area / 1e6, 1) for p in polys])
    json.dump(res, open(os.path.join(OUT, "plates.json"), "w"), indent=0)
