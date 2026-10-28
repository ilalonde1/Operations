r"""knobdiff.py <job> <dirA> <dirB> : plate area per storey of groupbisect\<job>\<dirA>\<job>\out.e2k against <dirB>'s, storeys
that differ by more than 1 sq ft. COVERS: AREA ... FLOOR plates (c183cmp.plates). DOES NOT: walls, columns, openings."""
import os, sys
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "c183cmp.py"), encoding="utf-8").read().split("\nfor job in")[0]
ns = {"__file__": os.path.join(os.path.dirname(os.path.abspath(__file__)), "c183cmp.py")}
exec(src, ns)
job, a, b = sys.argv[1:4]
base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "groupbisect", job)
pa, pb = (ns["plates"](os.path.join(base, d, job, "out.e2k")) for d in (a, b))
for st in sorted(set(pa) | set(pb)):
    x = sum(pa.get(st, {}).values()) * 10.7639
    y = sum(pb.get(st, {}).values()) * 10.7639
    if abs(x - y) > 1:
        print(f"  {st:12} {a} {x:9,.0f}  {b} {y:9,.0f}  diff {y - x:+,.0f} sq ft")
