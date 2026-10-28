import sys, re, math, collections
# colang.py <ours.e2k> <theirs.e2k> <dx_mm> <dy_mm> : for every oblong column (long/short >= 1.3) of ours within 100 mm of an
# oblong column of theirs on the same storey (ours shifted by dx, dy - model-yardstick's registration), the bearing of the
# LONG face in each model read the same way (D along local 2, local 2 along global X at ANG 0), and the difference, mod 180.
# COVERS: rectangular columns both models have at one place and storey. DOES NOT: round or square columns; which reading is
# CSI's (a constant 90 in every pair says the two models disagree, not which one is right).
TO_MM = {'MM': 1.0, 'M': 1000.0, 'IN': 25.4, 'INCH': 25.4, 'FT': 304.8, 'CM': 10.0}


def read(path):
    u = 1.0; pts = {}; secs = {}; cols = {}; out = []
    for line in open(path, encoding='utf-8', errors='replace'):
        s = line.strip()
        if s.startswith('UNITS'):
            q = re.findall(r'"([^"]+)"', s); u = TO_MM.get(q[1].upper(), 1.0)
        elif s.startswith('POINT '):
            m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
            if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u)
        elif s.startswith('FRAMESECTION') and 'Rectangular' in s:
            m = re.match(r'FRAMESECTION\s+"([^"]+)"', s); d = re.search(r'\bD\s+(\S+)', s); b = re.search(r'\bB\s+(\S+)', s)
            if m and d and b: secs[m.group(1)] = (float(d.group(1)), float(b.group(1)))
        elif s.startswith('LINE ') and ' COLUMN ' in s:
            m = re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s)
            if m: cols[m.group(1)] = m.group(2)
        elif s.startswith('LINEASSIGN'):
            m = re.match(r'LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s); a = re.search(r'\bANG\s+(\S+)', s)
            if m and m.group(1) in cols and m.group(3) in secs and cols[m.group(1)] in pts:
                d, b = secs[m.group(3)]
                if max(d, b) < 1.3 * min(d, b): continue
                ang = float(a.group(1)) if a else 0.0
                out.append((m.group(2), pts[cols[m.group(1)]], (ang if d >= b else ang + 90) % 180, m.group(3), ang))
    return out


ours, theirs = read(sys.argv[1]), read(sys.argv[2])
dx, dy = float(sys.argv[3]), float(sys.argv[4])
by = collections.defaultdict(list)
for t in theirs: by[t[0]].append(t)
diffs = collections.Counter(); examples = {}
for st, (x, y), bear, sec, ang in ours:
    best = min(by.get(st, []), key=lambda t: math.hypot(t[1][0] - x - dx, t[1][1] - y - dy), default=None)
    if best is None or math.hypot(best[1][0] - x - dx, best[1][1] - y - dy) > 100: continue
    dd = round(((bear - best[2]) % 180) / 15) * 15 % 180
    diffs[dd] += 1
    if dd != 0 and __import__('os').environ.get('COLANG_LIST'):
        print(f"    {st} ours {sec} ANG {ang} at ({x:.0f},{y:.0f}); hers {best[3]} ANG {best[4]}")
    examples.setdefault(dd, (st, sec, ang, best[3], best[4]))
print(f"oblong columns: ours {len(ours)}, theirs {len(theirs)}; matched pairs {sum(diffs.values())}")
for dd, n in sorted(diffs.items()):
    print(f"  long faces differ by {dd:3d} deg: {n}   e.g. {examples[dd]}")
