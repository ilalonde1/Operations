"""The axis names a sheet's DXF carries and the names a model's GRIDS carry, side by side — the first
question when a sheet "could NOT be set on the grid by name".

    python docs/etabs-handoff/grid_names.py <model.e2k> <sheet.dxf> [<sheet.dxf> ...]

Prints the model's labels per grid system, then for each sheet its GRID-layer labels, which of them
the model names, and which it does not. Built 2026-09-11 when 31168's 2026-09-10 reissue placed 1 of
3 parkade sheets where the 08-25 issue placed 3 of 3, and the report said only "their axes name nothing
the model names" — true, and not a reason.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from model_to_page import dxf_grid_labels  # noqa: E402


def e2k_grids(path):
    systems = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s*GRID\s+"([^"]*)"\s+LABEL\s+"([^"]+)"\s+DIR\s+"([XY])"', line)
        if m:
            systems.setdefault(m.group(1), []).append((m.group(2), m.group(3)))
    return systems


def main():
    model, sheets = sys.argv[1], sys.argv[2:]
    systems = e2k_grids(model)
    named = set()
    for system, axes in systems.items():
        xs = [l for l, d in axes if d == "X"]
        ys = [l for l, d in axes if d == "Y"]
        print(f"model grid system {system!r}: X {' '.join(xs)} | Y {' '.join(ys)}")
        named.update(l for l, _ in axes)
    for sheet in sheets:
        labels, lines, _ = dxf_grid_labels(sheet)
        have = sorted(labels, key=lambda s: (len(s), s))
        yes = [l for l in have if l in named]
        no = [l for l in have if l not in named]
        print(f"\n{os.path.basename(sheet)}: {len(have)} labels, {len(lines)} grid lines")
        print(f"   named by the model ({len(yes)}): {' '.join(yes)}")
        print(f"   not named        ({len(no)}): {' '.join(no)}")


if __name__ == "__main__":
    main()
