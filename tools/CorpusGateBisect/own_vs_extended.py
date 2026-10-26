r"""own_vs_extended.py <workA> <workB> : two builds of the same sets (two corpus-gate --work dirs, each <job>\out.e2k), compared
member by member on every storey, split into what each storey DRAWS and what it only CARRIES from the storey above.

WHY (2026-10-26, step 184). Step 184 was written to take concrete plates off wood floors and leave walls and columns alone. The
plates came out right, 69 of 69 storeys - and model-diff showed walls and columns moving on 13 of 21 sets, some by 50. Split
this way the answer was immediate: OWN members changed on 0 storeys of 24 sets; every change was a stack EXTENDED down from
the storey above, 137 lost and 171 gained on 18 storeys - E2kDocument's carry-down ("A MEMBER DOES NOT END IN MID-AIR") reads
plates to decide what is hanging. Any change to plates, storeys or the composer can move members that way; this says whether
it did, and whether it touched a drawn member at all.

HOW. Members only: an AREA that is a PANEL (a wall) and every LINE object (columns, beams); FLOOR areas are plates and openings,
the composer's other output, and are left to model-diff. An object assigned at a storey is OWN there when it is not also assigned
at the storey directly above (STORY lines run top first), EXTENDED when it is - a stack reaching down, as the stack merge and the
carry-down write them. Exit 0 when no OWN member changed, 1 when one did, 2 on bad arguments.

COVERS: object names per storey, both kinds, every storey of every set present in BOTH dirs (a set in one only is named).
DOES NOT: positions, lengths or sections - a member moved but keeping its name and storeys reads as unchanged (model-diff sees
that); a set whose two builds name their objects differently (renumbered) reads as everything changed; the top storey has no
storey above, so all its members count as own; one change can show twice - a drawn member taken off a storey whose stack
also reached the storey below turns that lower assign from extended to own (proved red 2026-10-26 by deleting KW55 at
31158 L6 from a copy: exit 1, "L6 own 91 -> 90" and "L5 own 0 -> 1"). A same-class fault it would NOT catch: a drawn member re-made under a new name
in the same place reads as one lost and one gained, which is reported as an OWN change though nothing structural moved.
"""
import collections, os, re, sys

ASSIGN = re.compile(r'\s*(AREAASSIGN|LINEASSIGN)\s+"([^"]+)"\s+"([^"]+)"')
STORY = re.compile(r'\s*STORY\s+"([^"]+)"')
AREA = re.compile(r'\s*AREA\s+"([^"]+)"\s+(\w+)')


def read(path):
    """(storey names top first, storey -> member objects assigned there)"""
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    panels = {m.group(1) for l in lines if (m := AREA.match(l)) and m.group(2).upper() == "PANEL"}
    order, by = [], collections.defaultdict(set)
    for l in lines:
        if m := STORY.match(l): order.append(m.group(1).upper())
        elif m := ASSIGN.match(l):
            if m.group(1) == "AREAASSIGN" and m.group(2) not in panels: continue
            by[m.group(3).upper()].add((m.group(1), m.group(2)))
    return order, by


def split(order, by, i):
    here = by.get(order[i], set())
    above = by.get(order[i - 1], set()) if i > 0 else set()
    return here - above, here & above


def main(a_dir, b_dir):
    jobs_a = {d for d in os.listdir(a_dir) if os.path.exists(os.path.join(a_dir, d, "out.e2k"))}
    jobs_b = {d for d in os.listdir(b_dir) if os.path.exists(os.path.join(b_dir, d, "out.e2k"))}
    for d in sorted(jobs_a ^ jobs_b): print(f"{d}: built in only one of the two dirs - not compared")
    own_changed, ext = [], collections.Counter()
    for job in sorted(jobs_a & jobs_b):
        (oa, ba), (ob, bb) = read(os.path.join(a_dir, job, "out.e2k")), read(os.path.join(b_dir, job, "out.e2k"))
        if oa != ob:
            print(f"{job}: the two builds have different storeys ({len(oa)} / {len(ob)}) - compared by name where both have one")
        for i, st in enumerate(oa):
            if st not in ob: continue
            own_a, ext_a = split(oa, ba, i)
            own_b, ext_b = split(ob, bb, ob.index(st))
            if own_a != own_b:
                own_changed.append(f"{job} {st}: own {len(own_a)} -> {len(own_b)} (lost {len(own_a - own_b)}, gained {len(own_b - own_a)})")
            if ext_a != ext_b:
                ext["storeys"] += 1; ext["lost"] += len(ext_a - ext_b); ext["gained"] += len(ext_b - ext_a)
                ext[job] += 1
    n = len(jobs_a & jobs_b)
    print(f"{n} set(s) compared")
    print(f"EXTENDED members (stacks reaching down from the storey above) changed on {ext['storeys']} storey(s) in "
          f"{sum(1 for k in ext if k not in ('storeys', 'lost', 'gained'))} set(s): {ext['lost']} lost, {ext['gained']} gained")
    print(f"OWN members (drawn on the storey) changed on {len(own_changed)} storey(s):")
    for r in own_changed: print("   " + r)
    return 1 if own_changed else 0


if __name__ == "__main__":
    if len(sys.argv) != 3 or not all(os.path.isdir(p) for p in sys.argv[1:]):
        print(__doc__.split("\n\n")[0])
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
