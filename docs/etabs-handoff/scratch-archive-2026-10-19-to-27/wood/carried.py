r"""carried.py <off.e2k> <on.e2k> <storey> <above> : the wall/column objects assigned at <storey>, off and on, split by whether
the same object is also assigned at <above> (a member carried down from the storey above - E2kDocument "A MEMBER DOES NOT END
IN MID-AIR" - adds exactly such an assign). COVERS: AREAASSIGN and LINEASSIGN lines by object name and storey. DOES NOT: say
why a carry happened."""
import collections, re, sys

A = re.compile(r'\s*(AREAASSIGN|LINEASSIGN)\s+"([^"]+)"\s+"([^"]+)"')


def assigns(path):
    by = collections.defaultdict(set)
    for line in open(path, encoding="utf-8", errors="replace"):
        if m := A.match(line): by[(m.group(1), m.group(2))].add(m.group(3).upper())
    return by


storey, above = sys.argv[3].upper(), sys.argv[4].upper()
for label, path in (("off", sys.argv[1]), ("on", sys.argv[2])):
    by = assigns(path)
    here = [k for k, s in by.items() if storey in s]
    also = [k for k in here if above in by[k]]
    kinds = collections.Counter(k[0] for k in here)
    print(f"{label}: {len(here)} objects assigned at {storey} ({dict(kinds)}); of them also assigned at {above}: {len(also)}")
