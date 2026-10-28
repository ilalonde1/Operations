r"""wall_overlap_dangle.py <gate-work-dir> [<gate-work-dir> ...] [--list N] : two wall-network faults counted in every composed
model (out.e2k) of a corpus-gate --work dir, per set and in total.
  OVERLAP: two wall panels on one storey that lie on the same line (parallel within 0.5 deg, centrelines within 25 mm) and
           share more than 100 mm of their length - one wall modelled twice.
  DANGLE:  a wall end that stops short of another wall on its storey: the end lies within REACH of the other wall's centreline
           (its perpendicular foot inside that wall, clear of its ends by 25 mm), more than 25 mm off it, and no other wall end
           sits at the same point - a T or corner joint the network did not make. REACH = 0.75 x the two thicknesses' sum.

WHY (2026-10-28, board 52): JoinThroughColumns' first gate run joined 31053 L2's north wall through its columns and left it
modelled twice over 20 m (KW109 18,687-42,701 and KW87 22,730-45,245 at y 72,896), and the cross wall KW86 stopping 280 mm
short of it - neither is seen by network-check (TILT / HEADER / SELF: a short wall joined only to a copy of itself).

COVERS: PANEL walls in the e2k (two plan points), thickness from the section name's KOR-W<mm> when present (else 200 mm), each
storey alone. DOES NOT: columns, openings, slabs; an overlap of walls 25+ mm apart (two faces of one wall read as two walls);
a dangle further than REACH (a wall that genuinely stops short). SAME-CLASS FAULT IT MISSES: a duplicate that is offset 30 mm
from its twin, and a stem whose end was carried to the wrong wall (it is joined, so it does not dangle).
Exit 0 always (a report)."""
import collections, glob, math, os, re, sys

PARALLEL, ON_LINE, SHARED, CLEAR, MATCH = 0.5, 25.0, 100.0, 25.0, 1.0


def walls_of(path):
    t = open(path, encoding="utf-8", errors="replace").read()
    m = re.search(r'^\s*UNITS\s+"[^"]*"\s+"([^"]+)"', t, re.M)
    u = {"MM": 1.0, "M": 1000.0, "CM": 10.0, "IN": 25.4, "FT": 304.8}.get(m.group(1).upper(), 1.0) if m else 1.0
    P = {a: (float(b) * u, float(c) * u) for a, b, c in re.findall(r'^\s+POINT\s+"(\S+)"\s+(\S+)\s+(\S+)', t, re.M)}
    seg = {}
    for mm in re.finditer(r'^\s+AREA\s+"(KW\S*)"\s+PANEL\s+4\s+((?:"\S+"\s+){4})', t, re.M):
        xy = sorted({P[q] for q in re.findall(r'"(\S+)"', mm.group(2)) if q in P})
        if len(xy) == 2:
            seg[mm.group(1)] = xy
    by = collections.defaultdict(list)
    for k, st, sec in re.findall(r'^\s+AREAASSIGN\s+"(\S+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', t, re.M):
        if k in seg:
            th = re.search(r'KOR-W([\d.]+)', sec)
            by[" ".join(st.split())].append((k, seg[k][0], seg[k][1], float(th.group(1)) if th else 200.0))
    return by


def unit(a, b):
    L = math.dist(a, b)
    return ((b[0] - a[0]) / L, (b[1] - a[1]) / L, L) if L > 1e-9 else (0.0, 0.0, 0.0)


def faults(walls):
    over, dangle = [], []
    for i, (k1, a1, b1, t1) in enumerate(walls):
        ux, uy, L1 = unit(a1, b1)
        if L1 == 0:
            continue
        for k2, a2, b2, t2 in walls[i + 1:]:
            vx, vy, L2 = unit(a2, b2)
            if L2 == 0 or math.degrees(math.asin(min(1.0, abs(ux * vy - uy * vx)))) > PARALLEL:
                continue
            off = lambda p: abs((p[0] - a1[0]) * -uy + (p[1] - a1[1]) * ux)
            if off(a2) > ON_LINE or off(b2) > ON_LINE:
                continue
            s = sorted(((a2[0] - a1[0]) * ux + (a2[1] - a1[1]) * uy, (b2[0] - a1[0]) * ux + (b2[1] - a1[1]) * uy))
            shared = min(L1, s[1]) - max(0.0, s[0])
            if shared > SHARED:
                over.append((k1, k2, shared))
    ends = [p for _, a, b, _ in walls for p in (a, b)]
    for k1, a1, b1, t1 in walls:
        for p in (a1, b1):
            if sum(1 for q in ends if math.dist(p, q) < MATCH) > 1:
                continue                                   # a joint already
            for k2, a2, b2, t2 in walls:
                if k2 == k1:
                    continue
                ux, uy, L2 = unit(a2, b2)
                if L2 == 0:
                    continue
                s = (p[0] - a2[0]) * ux + (p[1] - a2[1]) * uy
                d = abs((p[0] - a2[0]) * -uy + (p[1] - a2[1]) * ux)
                if CLEAR < s < L2 - CLEAR and CLEAR < d <= 0.75 * (t1 + t2):
                    dangle.append((k1, k2, d, p))
                    break
    return over, dangle


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    show = int(sys.argv[sys.argv.index("--list") + 1]) if "--list" in sys.argv else 3
    args = [a for a in args if not a.isdigit()]
    for work in args:
        tot_o = tot_d = tot_w = 0
        rows = []
        for path in sorted(glob.glob(os.path.join(work, "*", "out.e2k"))):
            job = os.path.basename(os.path.dirname(path))
            o_n = d_n = 0; ex = []
            for st, walls in walls_of(path).items():
                tot_w += len(walls)
                o, d = faults(walls)
                o_n += len(o); d_n += len(d)
                ex += [f"      {st:8} OVERLAP {a} x {b} share {s:.0f} mm" for a, b, s in o]
                ex += [f"      {st:8} DANGLE  {a} ends {g:.0f} mm off {b} at ({p[0]:.0f},{p[1]:.0f})" for a, b, g, p in d]
            tot_o += o_n; tot_d += d_n
            if o_n or d_n:
                rows.append((job, o_n, d_n, ex))
        print(f"== {work}\nwalls {tot_w}; overlapping pairs {tot_o}; dangling ends {tot_d}; sets with either {len(rows)}")
        for job, o_n, d_n, ex in sorted(rows, key=lambda r: -(r[1] + r[2])):
            print(f"  {job}: overlaps {o_n}, dangles {d_n}")
            for line in ex[:show]:
                print(line)


if __name__ == "__main__":
    main()
