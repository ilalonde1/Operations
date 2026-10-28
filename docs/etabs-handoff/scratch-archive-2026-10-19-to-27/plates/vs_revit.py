import sys, re, collections, openpyxl
# vs_revit.py <revit_per_level.txt> <ours.xlsx>... : slab by our storey against Revit's slab by level (both towers summed),
# and the whole-building element totals. Revit levels are named "LEVEL 7 - TN"; ours "L7".
rev = collections.defaultdict(float); rtot = collections.Counter()
for line in open(sys.argv[1]):
    p = line.split()
    if len(p) < 5 or p[0] == 'level': continue
    name = ' '.join(p[:-4]); slab, wall, col, fdn = map(float, p[-4:])
    m = re.match(r'LEVEL (P?\d+)', name)
    key = (('P' + m.group(1)[1:]) if m.group(1).startswith('P') else 'L' + m.group(1)) if m else ('ROOF' if name.startswith('ROOF') else name)
    rev[key] += slab; rtot['slab'] += slab; rtot['wall'] += wall; rtot['column'] += col
def ours(path):
    wb = openpyxl.load_workbook(path, data_only=True)
    t = collections.defaultdict(float); tot = collections.Counter()
    for ws in wb.worksheets:
        hdr = None
        for row in ws.iter_rows(values_only=True):
            cells = [str(c).strip().lower() if c is not None else '' for c in row]
            if hdr is None and 'level' in cells and 'element' in cells: hdr = cells; continue
            if hdr and row[hdr.index('level')] and row[hdr.index('element')]:
                vi = next(i for i, h in enumerate(hdr) if 'concrete' in h or 'volume' in h)
                try: v = float(row[vi])
                except (TypeError, ValueError): continue
                el = str(row[hdr.index('element')]).lower(); lv = str(row[hdr.index('level')])
                if 'slab' in el: t[lv] += v
                tot[el] += v
        if hdr: break
    return t, tot
runs = [ours(p) for p in sys.argv[2:]]
levels = sorted(set(rev) | set().union(*[set(r[0]) for r in runs]), key=lambda s: (s[0] != 'P', -int(s[1:]) if s[0] == 'P' and s[1:].isdigit() else (int(s[1:]) if s[1:].isdigit() else 999)))
print(f"{'storey':8} {'Revit':>7} " + ' '.join(f"{'ours' + str(i + 1):>7} {'%':>5}" for i in range(len(runs))))
for lv in levels:
    r = rev.get(lv, 0)
    print(f"{lv:8} {r:7.0f} " + ' '.join(f"{run[0].get(lv, 0):7.0f} {(100 * run[0].get(lv, 0) / r if r else 0):5.0f}" for run in runs))
print('totals  Revit ' + ' '.join(f"{k} {v:,.0f}" for k, v in rtot.items()))
for i, run in enumerate(runs):
    print(f"ours{i + 1}  " + ' '.join(f"{k} {v:,.0f}" for k, v in sorted(run[1].items())))
