"""How many of a file's POINT definitions are actually referenced by its geometry."""
import re
import sys

POINT = re.compile(r'^  POINT\s+"([^"]+)"', re.M)
GEOM = re.compile(r'^\s*(?:LINE|AREA)\s+"K')
QUOTED = re.compile(r'"([^"]+)"')

for path in sys.argv[1:]:
    txt = open(path, encoding='utf-8', errors='replace').read()
    defined = set(POINT.findall(txt))
    used = set()
    for line in txt.splitlines():
        if GEOM.match(line):
            # first quoted token is the object's own name; the rest are joints
            used.update(QUOTED.findall(line)[1:])
    used &= defined
    name = path.replace('\\', '/').split('/')[-1]
    print(f"{name:34s} POINT defs {len(defined):5d}   referenced {len(used):5d}   orphan {len(defined) - len(used):5d}")
