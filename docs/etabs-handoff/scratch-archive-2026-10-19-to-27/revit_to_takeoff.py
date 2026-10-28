import csv, re, sys
src, out_dir = sys.argv[1], sys.argv[2]
kinds = {'floors': 'Slab', 'walls': 'Wall', 'columns': 'Column', 'foundations': 'Foundation'}
for issue in ('before', 'after'):
    rows, tot = [], {}
    for k, el in kinds.items():
        lines = list(csv.reader(open(f'{src}/31065-{k}-{issue}.csv', encoding='utf-8-sig')))
        hdr = [h.strip().lower() for h in lines[1]]
        vi = hdr.index('volume'); li = next(i for i, h in enumerate(hdr) if 'level' in h or 'constraint' in h)
        for r in lines[2:]:
            if len(r) <= max(vi, li) or r[0].lower().startswith('grand total'): continue
            m = re.match(r'\s*([\d.,]+)\s*m', r[vi])
            if not m: continue
            v = float(m.group(1).replace(',', ''))
            rows.append((r[li].strip() or '(none)', el, v)); tot[el] = tot.get(el, 0) + v
    with open(f'{out_dir}/31065-{issue}.csv', 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f); w.writerow(['Level', 'Element', 'ConcreteVolume']); w.writerows(rows)
    print(issue, len(rows), {k: round(v, 1) for k, v in tot.items()})
