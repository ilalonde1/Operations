r"""ownvsextended.py : step 184 off (arm-off) vs on (arm-on2), every set, every storey: the wall/column objects assigned at the
storey split into OWN (not also assigned at the storey directly above) and EXTENDED (also assigned there - a stack reaching
down, which the carry-down, E2kDocument "A MEMBER DOES NOT END IN MID-AIR", adds by plate). The claim under test: step 184
changes only EXTENDED members, never OWN ones. COVERS: AREAASSIGN/LINEASSIGN object names per storey, storey order from the
STORY lines. DOES NOT: positions or sections (an own member re-made under another name would read as one lost and one
gained - reported, not hidden); the top storey has no storey above, so all of its members count as own."""
import collections, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
A = re.compile(r'\s*(AREAASSIGN|LINEASSIGN)\s+"([^"]+)"\s+"([^"]+)"')
S = re.compile(r'\s*STORY\s+"([^"]+)"')


KIND = re.compile(r'\s*AREA\s+"([^"]+)"\s+(\w+)')


def read(path):
    """members only: an AREA is a wall when it is a PANEL (a FLOOR is a plate or an opening, step 184's own subject)"""
    order, by, panel = [], collections.defaultdict(set), set()
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for line in lines:
        if m := KIND.match(line):
            if m.group(2).upper() == "PANEL": panel.add(m.group(1))
    for line in lines:
        if m := S.match(line): order.append(m.group(1).upper())
        elif m := A.match(line):
            if m.group(1) == "AREAASSIGN" and m.group(2) not in panel: continue
            by[m.group(3).upper()].add((m.group(1), m.group(2)))
    return order, by


own_changed = []; ext = collections.Counter(); sets = 0
for job in sorted(os.listdir(os.path.join(HERE, "arm-on2"))):
    a, b = (os.path.join(HERE, d, job, "out.e2k") for d in ("arm-off", "arm-on2"))
    if not (os.path.exists(a) and os.path.exists(b)): continue
    sets += 1
    (oa, ba), (ob, bb) = read(a), read(b)
    for i, st in enumerate(oa):
        above = oa[i - 1] if i > 0 else None             # STORY lines run top first
        def split(by):
            here = by.get(st, set()); up = by.get(above, set()) if above else set()
            return here - up, here & up
        own_off, ext_off = split(ba); own_on, ext_on = split(bb)
        if own_off != own_on:
            own_changed.append(f"{job} {st}: own {len(own_off)} -> {len(own_on)} (lost {len(own_off - own_on)}, gained {len(own_on - own_off)})")
        if ext_off != ext_on:
            ext["storeys"] += 1; ext["lost"] += len(ext_off - ext_on); ext["gained"] += len(ext_on - ext_off)
print(f"{sets} sets compared")
print(f"EXTENDED members changed on {ext['storeys']} storey(s): {ext['lost']} lost, {ext['gained']} gained")
print(f"OWN members changed on {len(own_changed)} storey(s):")
for r in own_changed: print("   " + r)
