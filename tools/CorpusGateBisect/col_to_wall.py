r"""col_to_wall.py <gate_pair_moves output> : every unpaired LOST column, and whether a GAINED wall on the same set+storey has its
midpoint within 150 mm (the column became a wall in place). Counts the whole file, by distance-to-hers band."""
import collections, math, re, sys
blocks = collections.defaultdict(lambda: {"col": [], "wall": []})
job = st = None
for line in open(sys.argv[1], encoding="utf-8"):
    if m := re.match(r"^== (\S+):", line): job = m.group(1); continue
    if m := re.match(r"^(\S+) (.+?) \(hers ", line): st = (m.group(1), m.group(2)); continue
    if m := re.match(r"^\s+LOST\s+column\s+\(([-\d,]+), ([-\d,]+)\)\s+[\d,]+\s+(\S+) mm from hers", line):
        blocks[st]["col"].append((float(m.group(1).replace(",", "")), float(m.group(2).replace(",", "")), m.group(3)))
    elif m := re.match(r"^\s+GAINED\s+wall\s+\(([-\d,]+), ([-\d,]+)\)\s+([\d,]+)", line):
        blocks[st]["wall"].append((float(m.group(1).replace(",", "")), float(m.group(2).replace(",", "")), float(m.group(3).replace(",", ""))))
c = collections.Counter(); ex = collections.defaultdict(list)
for k, v in blocks.items():
    for x, y, dh in v["col"]:
        inplace = any(math.hypot(x - a, y - b) <= 150 for a, b, _ in v["wall"])
        band = "no hers" if dh == "inf" else "<=100" if float(dh.replace(",", "")) <= 100 else "<=1000" if float(dh.replace(",", "")) <= 1000 else ">1000"
        c[(inplace, band)] += 1
        if len(ex[(inplace, band)]) < 4: ex[(inplace, band)].append(f"{k[0]} {k[1]} ({x:,.0f}, {y:,.0f})")
print(f"lost columns: {sum(c.values())}")
for k, n in sorted(c.items(), key=lambda kv: -kv[1]): print(f"  {n:4d}  became a wall in place={k[0]}  hers {k[1]}   e.g. {'; '.join(ex[k])}")
