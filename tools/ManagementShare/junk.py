import csv, os, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
rows = [r for r in csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')) if r['Dir'] != 'True']
n = lambda r: int(r['Length'] or 0)
JUNK_EXT = {'.db': 'Thumbs.db', '.lnk': 'shortcut', '.tmp': 'temp', '.pat': 'CAD hatch', '.shx': 'CAD font', '.ttf': 'font', '.pfb': 'font', '.pfm': 'font', '.fon': 'font', '.dmp': 'dump', '.ini': 'ini'}
j = collections.Counter(); js = collections.Counter()
for r in rows:
    nm = r['Path'].split('\\')[-1]
    k = 'Office lock file (~$)' if nm.startswith('~$') else JUNK_EXT.get(r['Extension'].lower())
    if k: j[k] += 1; js[k] += n(r)
print('junk/support files:', sum(j.values()), 'of', len(rows), '; %.1f MB' % (sum(js.values()) / 1e6), dict(j))
grp = collections.defaultdict(list)
for r in rows:
    if n(r) > 0: grp[(r['Path'].split('\\')[-1].lower(), n(r))].append(r)
dups = {k: v for k, v in grp.items() if len(v) > 1}
extra = sum(len(v) - 1 for v in dups.values()); extra_b = sum((len(v) - 1) * k[1] for k, v in dups.items())
print('same name + same size in more than one place: %d groups, %d redundant copies, %.2f GB' % (len(dups), extra, extra_b / 1e9))
top = collections.Counter()
for k, v in dups.items():
    for r in v[1:]: top[r['Path'].strip('\\').split('\\')[0]] += k[1]
print('redundant bytes by top folder (GB):', {a: round(b / 1e9, 2) for a, b in top.most_common(8)})
zips = [r for r in rows if r['Extension'].lower() == '.zip']
print('zip files', len(zips), '%.2f GB' % (sum(n(r) for r in zips) / 1e9), 'largest:', [(r['Path'][-80:], round(n(r) / 1e9, 2)) for r in sorted(zips, key=n, reverse=True)[:3]])
mb = collections.Counter(r['Path'].strip('\\').split('\\')[1] if r['Path'].count('\\') > 2 else '(files)' for r in rows if r['Path'].strip('\\').startswith('Monthly Billings'))
print('Monthly Billings L2:', dict(mb.most_common(5)))
