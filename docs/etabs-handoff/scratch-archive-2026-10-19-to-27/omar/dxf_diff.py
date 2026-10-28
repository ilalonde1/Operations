"""Compare two R12 DXFs entity by entity: every POLYLINE (layer + vertices rounded to 0.1 mm) and every
TEXT (layer, string, position, height). usage: dxf_diff.py <a.dxf> <b.dxf>"""
import sys, collections

def read(path):
    L = [l.strip() for l in open(path, encoding='cp1252').read().splitlines()]
    pairs = list(zip(L[0::2], L[1::2]))
    polys, texts, header = [], [], {}
    i = 0
    while i < len(pairs):
        code, val = pairs[i]
        if code == '9':
            header[val] = pairs[i + 1][1] if i + 1 < len(pairs) else None
        if code == '0' and val == 'POLYLINE':
            layer, verts, closed = '', [], 0
            i += 1
            while pairs[i][0] != '0':
                if pairs[i][0] == '8': layer = pairs[i][1]
                if pairs[i][0] == '70': closed = int(pairs[i][1]) & 1
                i += 1
            while pairs[i][1] == 'VERTEX':
                x = y = None; i += 1
                while pairs[i][0] != '0':
                    if pairs[i][0] == '10': x = round(float(pairs[i][1]), 1)
                    if pairs[i][0] == '20': y = round(float(pairs[i][1]), 1)
                    i += 1
                verts.append((x, y))
            polys.append((layer, closed, tuple(verts)))
            continue
        if code == '0' and val == 'TEXT':
            d = {}; i += 1
            while pairs[i][0] != '0':
                d[pairs[i][0]] = pairs[i][1]; i += 1
            texts.append((d.get('8'), d.get('1'), round(float(d.get('10', 0)), 1), round(float(d.get('20', 0)), 1), d.get('40'), d.get('50')))
            continue
        i += 1
    return header, polys, texts

ha, pa, ta = read(sys.argv[1]); hb, pb, tb = read(sys.argv[2])
print('INSUNITS', ha.get('$INSUNITS'), hb.get('$INSUNITS'))
print('polylines', len(pa), len(pb), ' texts', len(ta), len(tb))
print('layers A', collections.Counter(p[0] for p in pa) + collections.Counter(t[0] for t in ta))
print('layers B', collections.Counter(p[0] for p in pb) + collections.Counter(t[0] for t in tb))
ga = collections.Counter((p[1], p[2]) for p in pa); gb = collections.Counter((p[1], p[2]) for p in pb)
print('polyline geometry identical (ignoring layer):', ga == gb, ' only in A:', sum((ga - gb).values()), ' only in B:', sum((gb - ga).values()))
la = collections.Counter(pa); lb = collections.Counter(pb)
print('polylines identical incl. layer:', la == lb)
for t in tb[:25]: print('  B text', t)
for t in ta[:25]: print('  A text', t)
