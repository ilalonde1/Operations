import csv, collections, os, sys
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
rows = list(csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')))
files = [r for r in rows if r['Dir'] != 'True']
dirs = [r for r in rows if r['Dir'] == 'True']
print('rows', len(rows), 'files', len(files), 'dirs', len(dirs))
tot = sum(int(r['Length'] or 0) for r in files)
print('total GB %.2f' % (tot / 1e9))

def parts(p):
    return [x for x in p.split('\\') if x]

l1 = collections.defaultdict(lambda: [0, 0]); l2 = collections.defaultdict(lambda: [0, 0])
ext = collections.defaultdict(lambda: [0, 0]); yr = collections.defaultdict(lambda: [0, 0])
for r in files:
    p = parts(r['Path']); n = int(r['Length'] or 0)
    k1 = p[0] if len(p) > 1 else '(root files)'
    l1[k1][0] += 1; l1[k1][1] += n
    k2 = k1 + ' \\ ' + (p[1] if len(p) > 2 else '(files)')
    l2[k2][0] += 1; l2[k2][1] += n
    e = r['Extension'].lower() or '(none)'
    ext[e][0] += 1; ext[e][1] += n
    y = r['Modified'][:4]
    yr[y][0] += 1; yr[y][1] += n

def show(t, d, lim=None):
    print('\n==', t, len(d), 'keys')
    items = sorted(d.items(), key=lambda kv: -kv[1][0])
    for k, (c, n) in (items[:lim] if lim else items):
        print('%7d %9.1f MB  %s' % (c, n / 1e6, k))

show('L1', l1); show('L2', l2); show('EXT', ext, 40); show('YEAR', dict(sorted(yr.items())))
