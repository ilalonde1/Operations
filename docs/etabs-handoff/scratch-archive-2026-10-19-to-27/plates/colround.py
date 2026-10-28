import sys, re, math, os, glob, subprocess, collections
# colround.py <takeoff.exe> <corpus work dir> <yardsticks dir> : for every set with her model, our columns matched to hers on
# the same storey within 100 mm (model-yardstick's registration), counted by shape: round in both (right), ours round and hers
# not (a false round), hers round and ours not (missed). COVERS: columns at a shared place and storey. DOES NOT: columns
# either model has alone; sizes. A fault it would NOT catch: a round column we place more than 100 mm off hers.
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
        elif s.startswith('FRAMESECTION'):
            m = re.match(r'FRAMESECTION\s+"([^"]+)"', s); sh = re.search(r'SHAPE\s+"([^"]+)"', s)
            if m and sh: secs[m.group(1)] = 'round' if 'Circle' in sh.group(1) or 'Circular' in sh.group(1) else 'other'
        elif s.startswith('LINE ') and ' COLUMN ' in s:
            m = re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s)
            if m: cols[m.group(1)] = m.group(2)
        elif s.startswith('LINEASSIGN'):
            m = re.match(r'LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
            if m and m.group(1) in cols and cols[m.group(1)] in pts:
                out.append((m.group(2), pts[cols[m.group(1)]], secs.get(m.group(3), 'other'), m.group(3)))
    return out


exe, work, yard = sys.argv[1:4]
tot = collections.Counter()
for ours_path in sorted(glob.glob(os.path.join(work, '*', 'out.e2k'))):
    job = os.path.basename(os.path.dirname(ours_path))
    hers_path = os.path.join(yard, job + '.e2k')
    if not os.path.exists(hers_path): continue
    r = subprocess.run([exe, 'model-yardstick', ours_path, hers_path], capture_output=True, text=True, encoding='utf-8', errors='replace')
    m = re.search(r'registration \(([-\d,]+), ([-\d,]+)\)', r.stdout)
    if not m: continue
    dx, dy = float(m.group(1).replace(',', '')), float(m.group(2).replace(',', ''))
    ours, hers = read(ours_path), read(hers_path)
    by = collections.defaultdict(list)
    for h in hers: by[h[0]].append(h)
    c = collections.Counter(ours_round=sum(1 for o in ours if o[2] == 'round'), hers_round=sum(1 for h in hers if h[2] == 'round'))
    for st, (x, y), shape, _ in ours:
        best = min(by.get(st, []), key=lambda h: math.hypot(h[1][0] - x - dx, h[1][1] - y - dy), default=None)
        if best is None or math.hypot(best[1][0] - x - dx, best[1][1] - y - dy) > 100: continue
        c['matched'] += 1
        kind = ('right' if shape == 'round' else 'missed') if best[2] == 'round' else ('false_round' if shape == 'round' else 'both_square')
        c[kind] += 1
        if kind in ('false_round', 'missed') and os.environ.get('COLROUND_LIST'):
            print(f"    {job} {kind} {st} ours ({x:.0f}, {y:.0f}) mm; hers {best[3]} at ({best[1][0]:.0f}, {best[1][1]:.0f})")
    tot.update(c)
    if c['ours_round'] or c['hers_round']:
        print(f"{job:14} ours round {c['ours_round']:4}  hers round {c['hers_round']:4}  matched {c['matched']:5}: right {c['right']:4}  false round {c['false_round']:4}  missed {c['missed']:4}")
print(f"TOTAL ours round {tot['ours_round']}, hers round {tot['hers_round']}; matched {tot['matched']}: right {tot['right']}, false round {tot['false_round']}, missed {tot['missed']}, both square {tot['both_square']}")
