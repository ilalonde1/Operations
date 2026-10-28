r"""offplate.py : across ALL the gate's sets, the build report's per-storey "X of Y wall(s) and A of B column(s) stand beyond every
plate read for the storey" lines - a storey whose plate is not under its own walls (a legend box taken for a floor, a floor read
at the wrong place). COVERS: the report text as written by the build; a storey absent from those lines had its members on a
plate or no plate at all. DOES NOT: say which ring the plate was; storeys with no plate."""
import collections, csv, os, re

ROOT = os.path.join(os.environ["TEMP"], "kor-drawings", "corpus")
LINE = re.compile(r"-\s+(\S+): (\d+) of (\d+) wall\(s\) and (\d+) of (\d+) column\(s\) stand beyond every plate")
jobs = sorted({r["job"] for r in csv.DictReader(open(os.path.join(ROOT, "gate-sets.csv"), encoding="utf-8-sig"))})
hits = collections.Counter(); sets = 0; reports = 0
for job in jobs:
    p = os.path.join(ROOT, job, "report.txt")
    if not os.path.exists(p): continue
    reports += 1
    rows = []
    for line in open(p, encoding="utf-8", errors="replace"):
        m = LINE.search(line)
        if not m: continue
        st, w, W, c, C = m.group(1), *map(int, m.groups()[1:])
        if W + C == 0: continue
        share = (w + c) / (W + C)
        if share >= 0.8: rows.append(f"{st} {w}/{W} walls {c}/{C} cols")
    if rows:
        sets += 1; hits["storeys"] += len(rows)
        print(f"{job}: {len(rows)} storey(s) with 80%+ of members off every plate: " + "; ".join(rows))
print(f"\n{sets} of {reports} sets with a report have a storey whose plate is not under 80%+ of its members; {hits['storeys']} storeys")
