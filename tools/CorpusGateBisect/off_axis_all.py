r"""off_axis_all.py <e2k> : per storey, wall PANEL assigns and how many lie more than 0.5 deg off 0/90/180/270, plus column count.
COVERS: 4-point PANEL areas via AREAASSIGN; COLUMN lines via LINEASSIGN. DOES NOT: judge whether an off-axis wall is drawn that way
(a drawn skew wall counts the same as a tilted corner)."""
import math, re, sys
t = open(sys.argv[1], encoding="utf-8").read()
P = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'^  POINT "(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
A = {m[0]: (m[1], m[2]) for m in re.findall(r'^  AREA "(\S+)"\s+PANEL\s+4\s+"(\S+)"\s+"(\S+)"', t, re.M)}
C = {m for m in re.findall(r'^\s+LINE\s+"(\S+)"\s+COLUMN', t, re.M)}
stories = [" ".join(s.split()) for s in re.findall(r'^  STORY "([^"]+)"', t, re.M)]
walls, off, cols = {}, {}, {}
for k, st in re.findall(r'^  AREAASSIGN\s+"(\S+)"\s+"([^"]+)"', t, re.M):
    if k not in A: continue
    st = " ".join(st.split()); (x1, y1), (x2, y2) = P[A[k][0]], P[A[k][1]]
    ang = math.degrees(math.atan2(y2 - y1, x2 - x1)) % 90
    walls[st] = walls.get(st, 0) + 1
    if min(ang, 90 - ang) > 0.5: off[st] = off.get(st, 0) + 1
for k, st in re.findall(r'^  LINEASSIGN\s+"(\S+)"\s+"([^"]+)"', t, re.M):
    if k in C: st = " ".join(st.split()); cols[st] = cols.get(st, 0) + 1
for st in stories:
    print(f"{st:20s} walls {walls.get(st, 0):4d}  off-axis {off.get(st, 0):3d}  columns {cols.get(st, 0):4d}")
print(f"TOTAL {len(stories)} storeys: walls {sum(walls.values())}  off-axis {sum(off.values())}  columns {sum(cols.values())}")
