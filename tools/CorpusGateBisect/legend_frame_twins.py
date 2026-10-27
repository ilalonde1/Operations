r"""legend_frame_twins.py [work-dir] : plates a step-185 build still WRITES at the very rectangle of a legend frame it took on the
same storey - the frame drawn again on another sheet of that storey without its key stroke (31066-01: building 2's S2.15/S2.20
draw building 1's frame; it was deduplicated against building 1's copy and, with that taken, is written).

For each set in the work dir: the takes from report.txt ("rectangle on ... at (x, y)" - the lower-left corner, model units, and
the area), and the written FLOOR plates of out.e2k; a twin is a written plate on that storey whose lower-left corner is within
2 units of a take's and whose area is within 1% of it. Exit 1 when any twin is written.

COVERS: the step's takes as its report lists them; plates by storey, corner and area. DOES NOT: a twin drawn at another place
on its sheet (only the exact rectangle counts); a frame the step did not take on any sheet of the storey.
SAME-CLASS FAULT IT MISSES: a frame drawn without its stroke on a storey where no sheet's copy of it was taken.
"""
import glob, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import model   # noqa: E402

work = sys.argv[1] if len(sys.argv) > 1 else "."
TAKE = re.compile(r"^\s*-\s+(\S+): a ([\d,]+) sq ft rectangle on (.+?)\.dxf at \(([-\d,]+), ([-\d,]+)\)")
num = lambda s: float(s.replace(",", ""))
total = sets = 0
for rep in sorted(glob.glob(os.path.join(work, "*", "report.txt"))):
    job = os.path.basename(os.path.dirname(rep))
    takes = [(m.group(1), num(m.group(2)), m.group(3), num(m.group(4)), num(m.group(5)))
             for line in open(rep, encoding="utf-8", errors="replace") if (m := TAKE.match(line))]
    e2k = os.path.join(work, job, "out.e2k")
    if not takes or not os.path.isfile(e2k): continue
    u, pts, _, _, floors, _, lines = model(e2k)
    written = []
    for s in lines:
        if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION', s)) and m.group(1) in floors:
            p = [pts[q] for q in floors[m.group(1)] if q in pts]
            if len(p) < 3: continue
            a = abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 92903.04
            written.append((m.group(2), min(x for x, _ in p), min(y for _, y in p), a))
    twins = [(st, sheet, area) for st, area, sheet, x, y in takes
             for wst, wx, wy, wa in written
             if wst == st and abs(wx - x * u) <= 2 * u and abs(wy - y * u) <= 2 * u and abs(wa - area) <= 0.01 * area]
    if twins:
        sets += 1; total += len(twins)
        print(f"{job}: " + "; ".join(f"{st} {area:,.0f} sq ft (taken from {sheet[:34]})" for st, sheet, area in twins))
print(f"\n{total} legend frame twin(s) still written, on {sets} set(s)")
sys.exit(1 if total else 0)
