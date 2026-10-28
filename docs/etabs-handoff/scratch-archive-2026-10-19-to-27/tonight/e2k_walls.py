r"""e2k_walls.py <e2k> [storey] : every wall PANEL on a storey, as its two plan ends in mm (file units FT), length and angle.
COVERS: 4-point PANEL areas and their AREAASSIGN storey/section. DOES NOT: openings, frames (headers), other storeys' panels."""
import math, re, sys
t = open(sys.argv[1], encoding="utf-8").read()
want = sys.argv[2] if len(sys.argv) > 2 else "LEVEL 1"
P = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'^  POINT "(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
A = {m[0]: (m[1], m[2]) for m in re.findall(r'^  AREA "(\S+)"\s+PANEL\s+4\s+"(\S+)"\s+"(\S+)"', t, re.M)}
# an AREA is assigned once per storey it stands on (the same name repeated), so walk the assigns, not the areas
S = re.findall(r'^  AREAASSIGN\s+"(\S+)"\s+"([^"]+)"\s+SECTION "([^"]+)"', t, re.M)
n = 0
for k, st, sec in S:
    if k not in A or " ".join(st.split()) != want:
        continue
    a, b = A[k]
    (x1, y1), (x2, y2) = P[a], P[b]
    n += 1
    mm = lambda v: v * 304.8
    ang = math.degrees(math.atan2(y2 - y1, x2 - x1)) % 180
    print(f"{k:6} {sec:16} ({mm(x1):6.0f},{mm(y1):6.0f})-({mm(x2):6.0f},{mm(y2):6.0f})  len {mm(math.hypot(x2-x1, y2-y1)):5.0f}  ang {ang:6.2f}")
print(n, "panels on", want, "of", len(A), "in the file")
