"""Columns and wall panels per storey in two e2k files, aligned by level name.
usage: storey_counts.py <a.e2k> <b.e2k>"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from e2k_read import read


def key(name):
    n = name.upper().strip()
    n = re.sub(r"^(?:([A-Z])-)?LEVEL\s+", lambda m: (m.group(1) + "-" if m.group(1) else "") + "L", n)
    n = re.sub(r"^LP", "P", n)
    return n


def counts(path):
    order, area, line, kind, on = read(path)
    out = {}
    for s in order:
        objs = on.get(s, [])
        cols = sum(1 for o in objs if kind.get(o) == "COLUMN")
        walls = sum(1 for o in objs if kind.get(o) == "PANEL")
        out[key(s)] = (s, cols, walls)
    return out


a, b = counts(sys.argv[1]), counts(sys.argv[2])
names = sorted(set(a) | set(b), key=lambda k: (a.get(k, b.get(k))[0]))
print(f"{'storey':12s} {'cols A':>7s} {'cols B':>7s} {'walls A':>8s} {'walls B':>8s}")
ta = tb = wa = wb = 0
for k in names:
    ca, wa_ = a.get(k, ("", 0, 0))[1:]
    cb, wb_ = b.get(k, ("", 0, 0))[1:]
    ta += ca; tb += cb; wa += wa_; wb += wb_
    flag = "" if abs(ca - cb) <= 2 and abs(wa_ - wb_) <= 2 else "  <--"
    print(f"{k:12s} {ca:7d} {cb:7d} {wa_:8d} {wb_:8d}{flag}")
print(f"{'total':12s} {ta:7d} {tb:7d} {wa:8d} {wb:8d}")
