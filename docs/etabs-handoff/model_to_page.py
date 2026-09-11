"""Where on the SHEET is this model point? Map e2k model coordinates back to a sheet's page millimetres,
by the grid: the e2k's GRID lines carry each axis's model coordinate, the sheet's DXF carries the same
axis labels at page coordinates, so the shift between the two frames is the median difference over the
shared labels — the same alignment members_diff.py uses between two models.

    python docs/etabs-handoff/model_to_page.py <e2k> <sheet.dxf> [--census <walls.txt>] <xmm> <ymm> [<xmm> <ymm> ...]

Prints the shift it found (and how many labels agreed) and each point in the DXF's frame. The scratch
DXF is recentred on its own content (DxfExporter), so to reach the SHEET's frame — what crop_mm.py wants
on a pdf-overlay PNG — pass the overlay's own wall census (`takeoff pdf-overlay ... --walls > walls.txt`):
its filled walls carry page-millimetre centres, and the same walls are the DXF's KOR_V-WALL boxes, so
matching them by size gives the second shift. A point's page coordinates are only as good as the grid
placement the model made for that sheet; a sheet the model placed by a different reference plan (a
second building, an unplaced sheet) gets the WRONG shift, and this prints the label residuals so that
shows.

Built 2026-09-11 to look at the walls the audit fixes returned to 31138's L1/L2 (four 1,414 mm
diagonals in a row): the model says where they are, the sheet says what they are, and nothing joined
the two frames.
"""
import re
import statistics
import sys


def e2k_grids(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s*GRID\s+"[^"]*"\s+LABEL\s+"([^"]+)"\s+DIR\s+"([XY])"\s+COORD\s+(-?[\d.]+)', line)
        if m:
            out[(m.group(1), m.group(2))] = float(m.group(3))
    return out


def dxf_grid_labels(path):
    """GRID-layer TEXT entities: label -> (x, y) in the DXF's units (mm for the scratch DXFs); the GRID
    lines; and every KOR_V-WALL polyline's box as (w, h, cx, cy)."""
    lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, out, grid_lines, walls = 0, {}, [], []
    while i + 1 < len(lines):
        if lines[i].strip() == "0" and lines[i + 1].strip() in ("TEXT", "MTEXT", "LINE", "POLYLINE", "LWPOLYLINE", "VERTEX"):
            kind = lines[i + 1].strip()
            j = i + 2
            fields, xs, ys = {}, [], []
            while j + 1 < len(lines) and lines[j].strip() != "0":
                code, value = lines[j].strip(), lines[j + 1].strip()
                fields.setdefault(code, value)
                if code in ("10", "11"):
                    xs.append(float(value))
                if code in ("20", "21"):
                    ys.append(float(value))
                j += 2
            layer = fields.get("8", "").upper()
            if layer.startswith("GRID"):
                if kind == "LINE":
                    grid_lines.append((float(fields["10"]), float(fields["20"]), float(fields["11"]), float(fields["21"])))
                elif "1" in fields:
                    out[fields["1"].strip()] = (float(fields["10"]), float(fields["20"]))
            elif kind in ("POLYLINE", "LWPOLYLINE") and layer == "KOR_V-WALL":
                walls.append([xs, ys] if kind == "LWPOLYLINE" else [[], []])   # a POLYLINE's own 10/20 is a dummy 0,0; its VERTEXes follow
            elif kind == "VERTEX" and walls and layer == "KOR_V-WALL":
                walls[-1][0].extend(xs)
                walls[-1][1].extend(ys)
            i = j
        else:
            i += 1
    boxes = [(max(xs) - min(xs), max(ys) - min(ys), (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2) for xs, ys in walls if xs]
    return out, grid_lines, boxes


def page_shift(census_path, boxes):
    """dxf -> page: the median centre difference over overlay walls whose inch size matches a DXF box."""
    dx, dy = [], []
    for line in open(census_path, encoding="utf-8", errors="replace"):
        m = re.match(r"\s*wall\s+([\d.]+) x\s+([\d.]+) in\s+centre \(\s*(-?\d+),\s*(-?\d+)\) mm", line)
        if not m:
            continue
        w, h, cx, cy = float(m.group(1)) * 25.4, float(m.group(2)) * 25.4, float(m.group(3)), float(m.group(4))
        hits = [b for b in boxes if (abs(b[0] - w) < 15 and abs(b[1] - h) < 15) or (abs(b[0] - h) < 15 and abs(b[1] - w) < 15)]
        if len(hits) == 1:
            dx.append(cx - hits[0][2])
            dy.append(cy - hits[0][3])
    if not dx:
        return None
    return statistics.median(dx), statistics.median(dy), len(dx)


def main():
    argv = list(sys.argv[1:])
    census = None
    if "--census" in argv:
        k = argv.index("--census")
        census = argv[k + 1]
        del argv[k:k + 2]
    e2k, dxf = argv[0], argv[1]
    pts = [(float(argv[i]), float(argv[i + 1])) for i in range(2, len(argv) - 1, 2)]
    grids = e2k_grids(e2k)
    labels, grid_lines, boxes = dxf_grid_labels(dxf)
    page = page_shift(census, boxes) if census else None
    if census and page is None:
        print("no overlay wall matched a KOR_V-WALL box; page coordinates not available")
    elif page:
        print(f"page = dxf + ({page[0]:.0f}, {page[1]:.0f}) mm from {page[2]} matched wall(s)")
    dx, dy = [], []
    for (label, d), coord in grids.items():
        if label not in labels:
            continue
        x, y = labels[label]
        # the label sits at the axis's end: its X is the axis's X for an X-direction axis, likewise Y
        if d == "X":
            dx.append((label, coord - x))
        else:
            dy.append((label, coord - y))
    if not dx or not dy:
        print(f"no shared labels: e2k has {len(grids)} axes, dxf has {len(labels)} labels {sorted(labels)[:12]}")
        sys.exit(1)
    sx = statistics.median(v for _, v in dx)
    sy = statistics.median(v for _, v in dy)
    print(f"shift model = page + ({sx:.0f}, {sy:.0f}) mm from {len(dx)} X and {len(dy)} Y labels")
    for label, v in dx + dy:
        r = v - (sx if (label, "X") in grids else sy)
        if abs(r) > 50:
            print(f"   label {label} disagrees by {r:.0f} mm")
    for x, y in pts:
        line = f"model ({x:.0f}, {y:.0f}) -> dxf ({x - sx:.0f}, {y - sy:.0f})"
        if page:
            line += f" -> page ({x - sx + page[0]:.0f}, {y - sy + page[1]:.0f}) mm"
        print(line)


if __name__ == "__main__":
    main()
