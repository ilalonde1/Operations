# Cross-check: Deltek promo projects (10xxx, ChargeType P) link via SiblingWBS1 to a real job (won) or a 20xxx pursuit record
# (Stage LOST / DNP / InPursuit). M: folder names often carry the 10xxx number. Compare the two outcome sources.
import json, csv, os, re, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
pr = {r['WBS1']: r for r in json.load(open(os.path.join(base, 'dk_pr.json'), encoding='utf-8'))}
sib = {r['WBS1']: r for r in json.load(open(os.path.join(base, 'dk_sib.json'), encoding='utf-8'))}
promo = {}
for w, r in sib.items():
    if not w.startswith('10'): continue
    s = pr.get(r['Sib']) or {}
    st = s.get('Stage') or ''
    out = 'Won' if s.get('ChargeType') == 'R' and st not in ('InPursuit', 'LOST', 'DNP') else {'LOST': 'Lost', 'DNP': 'Did not pursue', 'InPursuit': 'In pursuit'}.get(st, '?')
    promo[w[:5]] = (out, r['Created'], r['Sib'])
print('promo records with sibling:', len(promo), collections.Counter(v[0] for v in promo.values()))
print('promo created range:', min(v[1] for v in promo.values()), '..', max(v[1] for v in promo.values()))
print('by year:', sorted(collections.Counter((v[1][:4], v[0]) for v in promo.values()).items()))
P = list(csv.DictReader(open(os.path.join(base, 'winloss-records.csv'), encoding='utf-8-sig')))
inv = [r for r in csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig'))]
cmp = collections.Counter(); rows = []
for p in P:
    m = re.search(r'(?<!\d)(10\d{3})-\d\d', p['project'] + ' ' + p['sample_path'])
    if not m or m.group(1) not in promo: continue
    d = promo[m.group(1)][0]
    mo = 'Won' if p['outcome'].startswith('Won') else 'Lost' if p['outcome'] == 'Lost' else 'Not won' if p['outcome'].startswith('Not won') else p['outcome']
    cmp[(mo, d)] += 1; rows.append((p['pursuit_id'], p['project'][:70], p['outcome'], d))
print('\nM: outcome vs Deltek promo-sibling outcome (pursuits carrying a 10xxx number):')
for k, v in sorted(cmp.items(), key=lambda kv: -kv[1]): print(f'  {v:4d}  M:{k[0]:<10} Deltek:{k[1]}')
json.dump({k: list(v) for k, v in promo.items()}, open(os.path.join(base, 'dk_promo_outcomes.json'), 'w'))
