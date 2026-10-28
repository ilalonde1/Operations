r"""l14_where.py : 31130's 10-26 OFF HER STACK column at L14 (114533, 47205), sought in today's gate model (run d808d99c) and
the 10-26 six-set build (sixknob fixed), both registered at (253,708-9, 316,577) to the engineer's model - so one frame.
Prints, on L13 / L14 / L15 of each of the three models, every column within 1,000 mm of the point (ours in our frame, hers
moved into ours by subtracting the registration)."""
import math, os, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import members, norm   # noqa: E402
S = os.path.dirname(os.path.abspath(__file__))
X, Y = 114533, 47205
models = {
    "today (2026-09-18 PDF)": (os.path.join(S, "..", "gate-twin3", "31130-01", "out.e2k"), (0, 0)),
    "10-26 six-set (2026-05-20 PDF)": (os.path.join(S, "sixknob", "31130-01", "fixed", "out.e2k"), (0, 0)),
    "engineer's": (os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks", "31130-01.e2k"), (253709, 316577)),
}
for name, (path, (dx, dy)) in models.items():
    if not os.path.isfile(path): print(f"{name}: no file {path}"); continue
    cols = members(path)
    print(f"== {name}  ({sum(len(v['column']) for v in cols.values())} columns)")
    for st in ("L15", "L14", "L13"):
        near = sorted((math.hypot(a - dx - X, b - dy - Y), a - dx, b - dy) for a, b in cols.get(norm(st), {"column": []})["column"])
        near = [n for n in near if n[0] <= 1000]
        print(f"  {st}: " + ("; ".join(f"({a:.0f} {b:.0f}) {d:.0f} mm" for d, a, b in near) if near else f"none within 1,000 mm ({len(cols.get(norm(st), {'column': []})['column'])} on the storey)"))
