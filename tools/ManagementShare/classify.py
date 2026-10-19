"""Classify every file in the inventory into the map's four categories by its top-two folder (plus junk extensions).
a = knowledge to ingest, b = structured data / records for the platform, c = private/HR/legal (list only), d = junk/support."""
import csv, os, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
rows = [r for r in csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')) if r['Dir'] != 'True']
JUNK = {'.db', '.lnk', '.tmp', '.pat', '.shx', '.ttf', '.pfb', '.pfm', '.fon', '.dmp', '.ini'}
A = {('Correspondence', '01 Fee Proposals'), ('Correspondence', 'Wordings'), ('Correspondence', '07 Notices'), ('Correspondence', 'EOR Letters'),
     ('Administration', 'Reference'), ('Administration', 'Notices'), ('Administration', '1. Docs to be Rebranded'), ('Administration', '(files)'),
     ('Meeting Minutes', '*'), ('Field Review Reports', '*'), ('Business Development', '*'), ('Project Management', 'Project Specifications')}
C = {('Recruitment', '*'), ('Administration', 'Timesheet Archive'), ('Legal Files', '*'), ('LK', '*'), ('Contact Information', '*')}
D = {('Administration', 'Manuals'), ('Administration', 'Purchasing'), ('Administration', 'Bluebeam Stamps'), ('Monthly Billings', 'TEMP Folders for Monthly Billings - Copy')}
def cat(p, ext, name):
    if ext in JUNK or name.startswith('~$'): return 'd'
    k1 = p[0] if len(p) > 1 else '(root)'; k2 = p[1] if len(p) > 2 else '(files)'
    for s, c in ((D, 'd'), (C, 'c'), (A, 'a')):
        if (k1, k2) in s or (k1, '*') in s: return c
    return 'b'
agg = collections.defaultdict(lambda: [0, 0]); area = collections.defaultdict(lambda: collections.defaultdict(lambda: [0, 0]))
yrs = collections.Counter()
for r in rows:
    p = r['Path'].strip('\\').split('\\'); n = int(r['Length'] or 0)
    c = cat(p, r['Extension'].lower(), p[-1])
    agg[c][0] += 1; agg[c][1] += n
    key = p[0] + ' \\ ' + (p[1] if len(p) > 2 else '(files)')
    area[c][key][0] += 1; area[c][key][1] += n
    y = int(r['Modified'][:4]); yrs['<=2009' if y <= 2009 else '2010-2014' if y <= 2014 else '2015-2019' if y <= 2019 else str(y)] += 1
tot = len(rows)
for c in 'abcd':
    print(f'{c}: {agg[c][0]} of {tot} files, {agg[c][1] / 1e9:.2f} GB')
    for k, (f, b) in sorted(area[c].items(), key=lambda kv: -kv[1][0])[:40]: print(f'     {f:6d} {b / 1e9:6.2f} GB  {k}')
print('years:', dict(sorted(yrs.items())))
