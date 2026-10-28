import csv, re, sys, collections
src, issue = sys.argv[1], sys.argv[2]
kinds = {'floors': 'Slab', 'walls': 'Wall', 'columns': 'Column', 'foundations': 'Foundation'}
t = collections.defaultdict(float)
for k, el in kinds.items():
    lines = list(csv.reader(open(f'{src}/31065-{k}-{issue}.csv', encoding='utf-8-sig')))
    hdr = [h.strip().lower() for h in lines[1]]
    vi = hdr.index('volume'); li = next(i for i, h in enumerate(hdr) if 'level' in h or 'constraint' in h)
    for r in lines[2:]:
        if len(r) <= max(vi, li) or r[0].lower().startswith('grand total'): continue
        m = re.match(r'\s*([\d.,]+)\s*m', r[vi])
        if m: t[(r[li].strip() or '(none)', el)] += float(m.group(1).replace(',', ''))
levels = sorted({l for l, _ in t})
print('%-28s %8s %8s %8s %8s' % ('level', 'slab', 'wall', 'column', 'fdn'))
for l in levels:
    print('%-28s %8.0f %8.0f %8.0f %8.0f' % (l, t[(l, 'Slab')], t[(l, 'Wall')], t[(l, 'Column')], t[(l, 'Foundation')]))
