r"""tilt_pair.py <before-work> <after-work> : per set, wall panel assigns more than 0.5 deg off every axis the set's own walls mostly
follow (the dominant bearings, mod 90, taken from the BEFORE model's longest walls), before vs after. Counts every set.
COVERS: 4-point PANEL plan bearing. DOES NOT: tell a drawn skew wall from a tilted one - only the change counts; a set whose new
off-axis walls are drawn skew would read as tilted here, so the sets it names are rendered before any verdict."""
import collections, math, os, re, sys
def walls(p):
    t = open(p, encoding="utf-8", errors="replace").read()
    P = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    A = {}
    for m in re.finditer(r'^\s+AREA\s+"(\S+)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        xy = sorted({P[q] for q in re.findall(r'"(\S+)"', m.group(2)) if q in P})
        if len(xy) >= 2: A[m.group(1)] = max(((a, b) for a in xy for b in xy), key=lambda ab: math.dist(*ab))
    out = []
    for k in re.findall(r'^\s+AREAASSIGN\s+"(\S+)"', t, re.M):
        if k in A:
            (x1, y1), (x2, y2) = A[k]
            out.append((math.degrees(math.atan2(y2 - y1, x2 - x1)) % 90, math.dist((x1, y1), (x2, y2)), k[:2]))
    return out
def off(ws, axes):
    return sum(1 for a, _, n in ws if n == KIND and min(min(abs(a - g), 90 - abs(a - g)) for g in axes) > 0.5)
b, a, KIND = sys.argv[1:4]
tb = ta = 0; rows = []
for j in sorted(os.listdir(a)):
    pb, pa = os.path.join(b, j, "out.e2k"), os.path.join(a, j, "out.e2k")
    if not (os.path.isfile(pb) and os.path.isfile(pa)): continue
    wb, wa = walls(pb), walls(pa)
    votes = collections.Counter()
    for ang, ln, _ in wb: votes[round(ang * 2) / 2 % 90] += ln
    axes = [g for g, _ in votes.most_common(4)] or [0.0]
    ob, oa = off(wb, axes), off(wa, axes); tb += ob; ta += oa
    rows.append((j, ob, oa, len(wb), len(wa)))
for j, ob, oa, nb, na in rows:
    print(f"{j:10s} off-axis {ob:4d} of {nb:5d} -> {oa:4d} of {na:5d}{'   <-- MORE' if oa > ob else '   fewer' if oa < ob else ''}")
print(f"TOTAL {len(rows)} sets: off-axis {tb} -> {ta}; sets with MORE: {sum(1 for r in rows if r[2] > r[1])}, fewer: {sum(1 for r in rows if r[2] < r[1])}")
