r"""new_headers.py <before.e2k> <after.e2k> : header panels (KS*) only in one model or the other, with plan ends and bearing."""
import math, re, sys


def ks(p):
    t = open(p, encoding="utf-8", errors="replace").read()
    P = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    out = set()
    for m in re.finditer(r'^\s+AREA\s+"(KS\d+)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        xy = sorted({P[q] for q in re.findall(r'"(\S+)"', m.group(2)) if q in P})
        a, b = max(((p, q) for p in xy for q in xy), key=lambda pq: math.dist(*pq))
        out.add((round(a[0]), round(a[1]), round(b[0]), round(b[1]), round(math.degrees(math.atan2(b[1] - a[1], b[0] - a[0])) % 180, 2)))
    return out


b, f = ks(sys.argv[1]), ks(sys.argv[2])
print(f"headers before {len(b)}, after {len(f)}; only after {len(f - b)}, only before {len(b - f)}")
for h in sorted(f - b): print("  new ", h)
for h in sorted(b - f): print("  gone", h)
