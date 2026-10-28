r"""wall_length_pair.py <before-work> <after-work> : per set, wall panels (count, total plan length m) and columns, before vs after,
summed over every storey assign. COVERS: 4-point PANEL plan length (the two distinct plan ends), COLUMN lines. DOES NOT: where."""
import math, os, re, sys
def measure(p):
    t = open(p, encoding="utf-8", errors="replace").read()
    unit = 304.8 if re.search(r'UNITS\s+"?(KIP|LB)\S*"?\s+"?FT', t) else 25.4 if re.search(r'UNITS\s+"?\S+"?\s+"?IN', t) else 1.0
    P = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    A = {}
    for m in re.finditer(r'^\s+AREA\s+"(\S+)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        ps = re.findall(r'"(\S+)"', m.group(2)); xy = sorted({P[q] for q in ps if q in P})
        A[m.group(1)] = max((math.dist(a, b) for a in xy for b in xy), default=0.0)
    C = set(re.findall(r'^\s+LINE\s+"(\S+)"\s+COLUMN', t, re.M))
    n = L = 0
    for k in re.findall(r'^\s+AREAASSIGN\s+"(\S+)"', t, re.M):
        if k in A: n += 1; L += A[k]
    c = sum(1 for k in re.findall(r'^\s+LINEASSIGN\s+"(\S+)"', t, re.M) if k in C)
    return n, L * unit / 1000.0, c
b, a = sys.argv[1:3]
rows = []
for j in sorted(os.listdir(a)):
    pb, pa = os.path.join(b, j, "out.e2k"), os.path.join(a, j, "out.e2k")
    if os.path.isfile(pb) and os.path.isfile(pa): rows.append((j, measure(pb), measure(pa)))
tb = [0, 0.0, 0]; ta = [0, 0.0, 0]
print(f"{'set':10s} {'walls':>13s} {'length m':>19s} {'columns':>11s}")
for j, x, y in rows:
    for k in range(3): tb[k] += x[k]; ta[k] += y[k]
    flag = "  <-- length -1%+" if y[1] < x[1] * 0.99 else "  <-- length +1%+" if y[1] > x[1] * 1.01 else ""
    print(f"{j:10s} {x[0]:6d}->{y[0]:<6d} {x[1]:9.1f}->{y[1]:<9.1f} {x[2]:5d}->{y[2]:<5d}{flag}")
print(f"TOTAL {len(rows)} sets: walls {tb[0]}->{ta[0]}, length {tb[1]:.0f}->{ta[1]:.0f} m ({(ta[1]/tb[1]-1)*100:+.2f}%), columns {tb[2]}->{ta[2]}")
