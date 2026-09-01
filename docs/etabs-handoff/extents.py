"""Plan extents of an e2k's joints, and of its grid lines, to compare orientation."""
import re
import sys

POINT = re.compile(r'^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)')
GRID = re.compile(r'^\s*GRID\s+"([^"]+)"\s+"([^"]+)"\s+(\w+)\s+(-?[\d.]+)')


def span(vals):
    return (min(vals), max(vals), max(vals) - min(vals)) if vals else (0, 0, 0)


for path in sys.argv[1:]:
    xs, ys = [], []
    gx, gy = [], []
    for line in open(path, encoding="utf-8", errors="replace"):
        m = POINT.match(line)
        if m:
            xs.append(float(m.group(2)))
            ys.append(float(m.group(3)))
            continue
        g = GRID.match(line)
        if g:
            (gx if g.group(3).upper().startswith("X") else gy).append(float(g.group(4)))

    name = path.replace("\\", "/").split("/")[-1]
    x0, x1, w = span(xs)
    y0, y1, h = span(ys)
    print(f"{name}")
    print(f"  joints {len(xs):5d}  x {x0:10.0f}..{x1:<10.0f} ({w:8.0f})   "
          f"y {y0:10.0f}..{y1:<10.0f} ({h:8.0f})   w/h {w / h:.3f}" if h else "  no joints")
    if gx or gy:
        gxs = span(gx)
        gys = span(gy)
        print(f"  grids  X={len(gx)} Y={len(gy)}  x span {gxs[2]:8.0f}   y span {gys[2]:8.0f}   "
              f"w/h {gxs[2] / gys[2]:.3f}" if gys[2] else "  grids: one axis only")
    print()
