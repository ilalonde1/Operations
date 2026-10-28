# How much of the shorter wall each overlapping pair shares, from the provenance probe's pairs.tsv and its rebuilt e2k files.
import collections, csv, glob, math, os, re, sys

R = sys.argv[1]
rows = list(csv.DictReader(open(os.path.join(R, "pairs.tsv"), encoding="utf-8"), delimiter="\t"))
length = {}
for f in glob.glob(os.path.join(R, "*.e2k")):
    job = os.path.splitext(os.path.basename(f))[0]
    t = open(f, encoding="utf-8", errors="replace").read()
    P = {a: (float(b), float(c)) for a, b, c in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    for m in re.finditer(r'^\s+AREA\s+"(KW\S*)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        xy = sorted({P[q] for q in re.findall(r'"(\S+)"', m.group(2)) if q in P})
        if len(xy) == 2:
            length[(job, m.group(1))] = math.dist(*xy)
bins = collections.Counter()
drift = collections.Counter()
for r in rows:
    la, lb = length[(r["job"], r["wallA"])], length[(r["job"], r["wallB"])]
    f = float(r["shared_mm"]) / min(la, lb)
    bins[">=0.99" if f >= 0.99 else "0.9-0.99" if f >= 0.9 else "0.5-0.9" if f >= 0.5 else "<0.5"] += 1
    d = abs(la - lb)
    drift["<=5 mm" if d <= 5 else "5-50 mm" if d <= 50 else "50-500 mm" if d <= 500 else ">500 mm"] += 1
for k in [">=0.99", "0.9-0.99", "0.5-0.9", "<0.5"]:
    print(f"{bins[k]} of {len(rows)} pairs share {k} of the shorter wall")
for k in ["<=5 mm", "5-50 mm", "50-500 mm", ">500 mm"]:
    print(f"{drift[k]} of {len(rows)} pairs differ in length by {k}")
