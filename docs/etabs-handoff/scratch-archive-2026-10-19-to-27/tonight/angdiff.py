import sys, re
# angdiff.py <before.e2k> <after.e2k> [--rebank <bank.e2k> <out.e2k>]
# The column-ANG fix may change ONE thing: a column's LINEASSIGN ANG, to (before + 90) mod 360. Every other line identical,
# in the same order. COVERS: the whole e2k text, line by line. DOES NOT: anything not in the e2k (DXFs, reports, workbooks).
# A fault it would NOT catch: one present in both builds (a differential cannot see it).
# --rebank: write <out.e2k> = <bank.e2k> with exactly that change applied to its column LINEASSIGNs, nothing else.
ANG = re.compile(r'(\bANG\s+)(-?[0-9.]+)')


def columns(lines):
    return {m.group(1) for l in lines for m in [re.match(r'\s*LINE\s+"([^"]+)"\s+COLUMN\b', l)] if m}


def turned(line, cols):
    m = re.match(r'\s*LINEASSIGN\s+"([^"]+)"', line)
    if not m or m.group(1) not in cols or not ANG.search(line): return None
    def bump(a):
        v = (float(a.group(2)) + 90.0) % 360.0
        return a.group(1) + (str(int(v)) if v == int(v) else repr(round(v, 6)).rstrip('0').rstrip('.'))
    return ANG.sub(bump, line, count=1)


a = open(sys.argv[1], encoding='utf-8', errors='replace').read().splitlines()
b = open(sys.argv[2], encoding='utf-8', errors='replace').read().splitlines()
cols = columns(a)
if len(a) != len(b):
    sys.exit(f"FAIL: {len(a)} lines before, {len(b)} after")
changed = bad = 0
for i, (x, y) in enumerate(zip(a, b)):
    if x == y: continue
    t = turned(x, cols)
    if t is not None and abs(float(ANG.search(t).group(2)) - float(ANG.search(y).group(2))) < 1e-6 and ANG.sub('', t) == ANG.sub('', y):
        changed += 1
    else:
        bad += 1
        if bad <= 5: print(f"  line {i + 1}:\n    - {x}\n    + {y}")
print(f"{'OK' if bad == 0 else 'FAIL'}: {len(a)} lines, {changed} column ANG turned by 90, {bad} other change(s); {len(cols)} column objects")
if bad: sys.exit(1)
if len(sys.argv) > 3 and sys.argv[3] == '--rebank':
    bank = open(sys.argv[4], encoding='utf-8', errors='replace', newline='').read().splitlines(keepends=True)
    bcols = columns(bank); out = []; n = 0
    for l in bank:
        t = turned(l.rstrip('\r\n'), bcols)
        if t is None: out.append(l)
        else: out.append(t + l[len(l.rstrip('\r\n')):]); n += 1
    open(sys.argv[5], 'w', encoding='utf-8', newline='').write(''.join(out))
    print(f"rebank: {n} column ANG turned in {sys.argv[4]} -> {sys.argv[5]}")
