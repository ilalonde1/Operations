"""Floor plates per storey in two e2k files side by side, in square feet.

usage: plate_diff.py <a.e2k> <b.e2k> [unit]
  unit: "mm" (default) or "in" — the length unit the models are written in.

Written for the differential of intake step 23: the same drawings built by two binaries, so a
change that is supposed to alter one property can be seen not to alter the rest.
"""
import re
import sys

PT = re.compile(r'^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)')
AREA = re.compile(r'^\s*AREA\s+"(KF\d+)"\s+FLOOR\s+(\d+)\s+(.*)$')
ASSIGN = re.compile(r'^\s*AREAASSIGN\s+"(KF\d+)"\s+"([^"]+)"')
NAMES = re.compile(r'"([^"]+)"')

PER_SQ_FT = {"mm": 92903.04, "in": 144.0}


def plates(path, per):
    pts, ring, assign = {}, {}, {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = PT.match(line)
        if m:
            pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
            continue
        m = AREA.match(line)
        if m:
            ring[m.group(1)] = NAMES.findall(m.group(3))
            continue
        m = ASSIGN.match(line)
        if m:
            assign[m.group(1)] = m.group(2)
    out = {}
    for k, names in ring.items():
        p = [pts[n] for n in names if n in pts]
        if len(p) < 3:
            continue
        a = abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1]
                    for i in range(len(p)))) / 2
        out.setdefault(assign.get(k, "?"), []).append(a / per)
    return out


def main():
    per = PER_SQ_FT[sys.argv[3] if len(sys.argv) > 3 else "mm"]
    a, b = plates(sys.argv[1], per), plates(sys.argv[2], per)
    for s in sorted(set(a) | set(b)):
        x = sorted(a.get(s, []), reverse=True)
        y = sorted(b.get(s, []), reverse=True)
        left = " ".join(f"{v:,.0f}" for v in x) or "-"
        right = " ".join(f"{v:,.0f}" for v in y) or "-"
        same = "" if left == right else "   <-- moved"
        print(f"  {s:10s} {left:>30s}  |  {right:>30s}{same}")


main()
