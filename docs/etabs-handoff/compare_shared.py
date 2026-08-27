"""Where two models of one building disagree about the SAME storey.

Both published models carry the parkade, the ground floor, the mezzanine and LEVEL 2. An
engineer opening both must not find two answers for one slab, and nothing in either file says
they differ -- both report zero open questions.
"""
import io, re, sys, collections

def load(path):
    pts, props, plates = {}, {}, collections.defaultdict(list)
    prop_of = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)', line)
        if m:
            pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
            continue
        m = re.match(r'\s*SHELLPROP\s+"([^"]+)".*?SLABTHICKNESS\s+([\d.]+)', line)
        if m:
            props[m.group(1)] = float(m.group(2))
            continue
        m = re.match(r'\s*AREA\s+"(KF\d+)"\s+FLOOR\s+(\d+)\s+(.*)$', line)
        if m:
            names = re.findall(r'"([^"]+)"', m.group(3))[: int(m.group(2))]
            ring = [pts[n] for n in names if n in pts]
            if len(ring) >= 3:
                s = 0.0
                for i in range(len(ring)):
                    x1, y1 = ring[i]
                    x2, y2 = ring[(i + 1) % len(ring)]
                    s += x1 * y2 - x2 * y1
                prop_of[m.group(1)] = abs(s) / 2.0 / 144.0
            continue
        m = re.match(r'\s*AREAASSIGN\s+"(KF\d+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', line)
        if m and m.group(1) in prop_of:
            plates[m.group(2)].append((prop_of[m.group(1)], m.group(3)))
    return plates, props

a_path, b_path = sys.argv[1], sys.argv[2]
a, aprops = load(a_path)
b, bprops = load(b_path)

shared = sorted(set(a) & set(b))
print(f'{len(a)} storey(s) with plates in A, {len(b)} in B, {len(shared)} shared\n')
print(f'{"storey":<16}{"A: area / thickness":<34}{"B: area / thickness":<34}verdict')

for st in shared:
    aa = sum(x[0] for x in a[st]); ba = sum(x[0] for x in b[st])
    at = sorted({aprops.get(p, 0) for _, p in a[st]})
    bt = sorted({bprops.get(p, 0) for _, p in b[st]})
    da = abs(aa - ba) / max(aa, ba, 1)
    same_t = at == bt
    verdict = 'ok' if da < 0.02 and same_t else 'DISAGREE'
    astr = f'{aa:,.0f} sf  {"/".join(f"{t:g}" for t in at)}"'
    bstr = f'{ba:,.0f} sf  {"/".join(f"{t:g}" for t in bt)}"'
    print(f'{st:<16}{astr:<34}{bstr:<34}{verdict}')

only_a = sorted(set(a) - set(b))
only_b = sorted(set(b) - set(a))
if only_a: print(f'\nplated only in A: {", ".join(only_a)}')
if only_b: print(f'plated only in B: {", ".join(only_b)}')
