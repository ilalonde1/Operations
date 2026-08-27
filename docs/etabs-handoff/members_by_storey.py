import io, re, sys, collections

def load(path):
    kind, on = {}, collections.defaultdict(lambda: collections.Counter())
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*AREA\s+"(K\w+)"\s+(PANEL|FLOOR|AREA)\b', line)
        if m:
            kind[m.group(1)] = m.group(2)
            continue
        m = re.match(r'\s*LINE\s+"(K\w+)"\s+(COLUMN|BEAM|BRACE)\b', line)
        if m:
            kind[m.group(1)] = m.group(2)
            continue
        m = re.match(r'\s*(?:AREA|LINE)ASSIGN\s+"(K\w+)"\s+"([^"]+)"', line)
        if m:
            k = kind.get(m.group(1))
            if not k:
                continue
            name = m.group(1)
            role = ('wall' if name.startswith('KW') else
                    'header' if name.startswith('KS') else
                    'column' if name.startswith('KC') else
                    'plate' if name.startswith('KF') else
                    'opening' if name.startswith('KO') else 'other')
            on[m.group(2)][role] += 1
    return on

a, b = load(sys.argv[1]), load(sys.argv[2])
shared = sorted(set(a) & set(b))
print(f'{"storey":<16}{"A walls/cols/plates":<26}{"B walls/cols/plates":<26}verdict')
for st in shared:
    x, y = a[st], b[st]
    ax = f"{x['wall']}/{x['column']}/{x['plate']}"
    bx = f"{y['wall']}/{y['column']}/{y['plate']}"
    v = 'ok' if ax == bx else 'DIFFER'
    print(f'{st:<16}{ax:<26}{bx:<26}{v}')
