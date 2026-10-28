r"""offplate_root.py <root> [share] : offplate.py over any root of <job>\report.txt (a corpus-gate --work dir): storeys where
share (default 0.8) or more of the members stand beyond every plate read for the storey. COVERS: the report's own lines.
DOES NOT: storeys with no plate at all (the build writes no such line), or why the plate is elsewhere."""
import os, re, sys

root = sys.argv[1]; share = float(sys.argv[2]) if len(sys.argv) > 2 else 0.8
LINE = re.compile(r"-\s+(\S+): (\d+) of (\d+) wall\(s\) and (\d+) of (\d+) column\(s\) stand beyond every plate")
sets = reports = storeys = 0
for job in sorted(os.listdir(root)):
    p = os.path.join(root, job, "report.txt")
    if not os.path.exists(p): continue
    reports += 1; rows = []
    for line in open(p, encoding="utf-8", errors="replace"):
        m = LINE.search(line)
        if not m: continue
        st = m.group(1); w, W, c, C = map(int, m.groups()[1:])
        if W + C and (w + c) / (W + C) >= share: rows.append(f"{st} {w}/{W} walls {c}/{C} cols")
    if rows:
        sets += 1; storeys += len(rows)
        print(f"{job}: " + "; ".join(rows))
print(f"\n{sets} of {reports} sets have a storey with {share:.0%}+ of members off every plate; {storeys} storeys")
