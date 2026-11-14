# JM 01819 irregularity screen (row 1982, JM-LAT-PREP 14 Nov 2026; scratch irr_type1.py brought into the repo). Reads WHM's drawn walls only
# (model-audit/drawn-walls.csv), never a model. Output to stdout; independent/irregularity/type1.txt is its committed output. See IRREGULARITY.md.
# Type 1 SCREEN (proxy, not stiffness): k ~ sum(t L^3)/h^3 per storey, building, direction, from irr/csw.txt (CSW only,
# clipped) and irr/storeys.txt (other walls: WHM CW#/unmarked, not SFRS per S111). Storey heights from A3.01 (answer-key.csv).
# Tests (Table 4.1.8.6 Type 1): k_i < 0.70 k_adjacent, or k_i < 0.80 avg(k of the 3 storeys above) or (3 below).
import re, sys
irr = sys.argv[1]
h = {'B': {'L1':3.252,'L2':3.505,'L3':2.896}, 'A': {'L1':3.252,'L2':3.706,'L3':2.694,'L9':3.301,'L10':3.048,'L11':2.338}}
for i in range(4, 32): h['B'][f'L{i}'] = 2.896
for i in range(4, 9): h['A'][f'L{i}'] = 2.896
h['B']['ROOF AMENITY'] = 3.301
stB = ['L1','L2'] + [f'L{i}' for i in range(3,32)] + ['ROOF AMENITY']
stA = ['L1','L2'] + [f'L{i}' for i in range(3,12)]
csw, oth = {}, {}
for line in open(irr + '/csw.txt'):
    p = line.split(' | ')
    if len(p) > 4 and not line.startswith('storey'):
        v = [x.split() for x in p[1:5]]; csw[p[0]] = {k: float(a[1]) for k, a in zip(('AH','AV','BH','BV'), v)}
for line in open(irr + '/storeys.txt'):
    if ' || ' in line:
        st = line.split(' | ')[0]
        for m in re.finditer(r'([AB][HV]): ([\d.]+) ([\d.]+) \([^)]*\) \|\| ([\d.]+) ([\d.]+)', line):
            oth.setdefault(st, {})[m.group(1)] = float(m.group(5))
def run(b, sts, d, with_other):
    k = [(csw[s][b+d] + (oth[s][b+d] if with_other else 0)) / h[b][s]**3 for s in sts]
    flags = []
    for i, s in enumerate(sts):
        f = []
        for j in (i-1, i+1):
            if 0 <= j < len(sts) and k[i] < 0.70 * k[j]: f.append(f'{k[i]/k[j]:.2f} of {sts[j]}')
        for rng, nm in ((sts[i+1:i+4], 'avg 3 above'), (sts[max(0,i-3):i], 'avg 3 below')):
            if len(rng) == 3:
                a = sum(k[sts.index(x)] for x in rng) / 3
                if k[i] < 0.80 * a: f.append(f'{k[i]/a:.2f} of {nm}')
        if f: flags.append(f'{s} (k {k[i]:.1f}): ' + ', '.join(f))
    return k, flags
for b, sts in (('B', stB), ('A', stA)):
    for d in 'HV':
        for wo in (False, True):
            k, fl = run(b, sts, d, wo)
            print(f"{b} {d} {'CSW + other walls' if wo else 'CSW only'}: k L1..L5 = {', '.join(f'{x:.1f}' for x in k[:5])}; flagged {len(fl)} of {len(sts)}: " + ('; '.join(fl) or 'none'))
