r"""nearmiss_vs_hers.py <outdir> <job> <tag>: each near-miss column (nearmiss.py's rule) with the distance to the engineer's
nearest column on that storey (her frame registered as judge_drift_moves does) - on hers within 150 mm means the stack is
what moved, not this column."""
import math, os, re, subprocess, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import members, offset, her_storey, near, YARD, e2k_in   # noqa: E402
out, job, tag = sys.argv[1:4]
e2k = e2k_in(os.path.join(out, job, tag))
r = subprocess.run([sys.executable, "-I", os.path.join(os.path.dirname(os.path.abspath(__file__)), "nearmiss.py"), e2k], capture_output=True, text=True).stdout
hm = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k")); ours = members(e2k)
o = offset(out, job, tag, ours, hm)
on = off = 0
for line in r.splitlines():
    if m := re.match(r"\s+(\S+): \d+\s+(.*)", line):
        st = m.group(1); hs = her_storey(hm, st)
        for x, y, d in re.findall(r"\((-?\d+) (-?\d+)\)~(\d+)", m.group(2)):
            p = (float(x), float(y))
            dh = near((p[0] + o[0], p[1] + o[1]), hm[hs]["column"]) if hs else float("inf")
            on += dh <= 150; off += 150 < dh < 1e9
            print(f"{st} ({x},{y}) stack misses by {d} mm; hers {dh:,.0f} mm")
print(f"on hers (<=150 mm): {on}; off hers: {off}")
