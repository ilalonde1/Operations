import sys


def ents(path):
    lines = [l.rstrip('\r\n') for l in open(path, encoding='utf-8', errors='replace')]
    out, cur = [], None
    for c, v in zip(lines[0::2], lines[1::2]):
        c = c.strip()
        if c == '0':
            if cur:
                out.append(cur)
            cur = {'type': v.strip(), 'pts': [], 'layer': ''}
        elif cur is not None:
            if c == '8':
                cur['layer'] = v.strip()
            elif c == '10':
                cur['pts'].append([float(v), None])
            elif c == '20' and cur['pts'] and cur['pts'][-1][1] is None:
                cur['pts'][-1][1] = float(v)
    if cur:
        out.append(cur)
    merged = []
    for x in out:
        if x['type'] == 'VERTEX' and merged and merged[-1]['type'] == 'POLYLINE':
            merged[-1]['pts'].extend(x['pts'])
        elif x['type'] != 'SEQEND':
            merged.append(x)
    return merged


def area(p):
    n = len(p)
    return abs(sum(p[i][0] * p[(i + 1) % n][1] - p[(i + 1) % n][0] * p[i][1] for i in range(n))) / 2


for path in sys.argv[1:]:
    e = [x for x in ents(path) if x['type'] in ('LWPOLYLINE', 'POLYLINE') and 'SLAB' in x['layer'].upper()]
    print(path.replace('\\', '/').split('/')[-4], len(e), 'slab polylines; sq ft:',
          sorted([round(area(x['pts']) / 92903) for x in e if len(x['pts']) > 2], reverse=True)[:8],
          'verts:', sorted([len(x['pts']) for x in e], reverse=True)[:5], sorted(set(x['layer'] for x in e)))
