r"""further_bins.py <gate_pair_moves output with MOVED lines> : every FURTHER re-placed member binned by kind and by how much
further from hers (after - before), per set with that set's worst (storey, from -> to). Counts the whole file."""
import collections, re, sys
job = st = None
rows = []
for line in open(sys.argv[1], encoding="utf-8"):
    if m := re.match(r"^(\S+) (.+?) \(hers ", line): job, st = m.group(1), m.group(2); continue
    if m := re.match(r"^\s+MOVED\s+(\w+)\s+\(([-\d,]+), ([-\d,]+)\) -> \(([-\d,]+), ([-\d,]+)\) ([\d,]+) mm; hers ([\d,]+) -> ([\d,]+) mm\s+FURTHER", line):
        f = lambda s: float(s.replace(",", ""))
        rows.append((job, st, m.group(1), f(m.group(4)), f(m.group(5)), f(m.group(6)), f(m.group(7)), f(m.group(8))))
print(f"FURTHER: {len(rows)}")
b = collections.Counter()
for r in rows:
    w = r[7] - r[6]
    b[(r[2], "<=100" if w <= 100 else "<=300" if w <= 300 else "<=1000" if w <= 1000 else ">1000")] += 1
for k, n in sorted(b.items()): print(f"  {n:4d}  {k[0]:<6} worse by {k[1]} mm")
per = collections.defaultdict(list)
for r in rows: per[r[0]].append(r)
print("per set (count, worst):")
for j, v in sorted(per.items(), key=lambda kv: -max(r[7] - r[6] for r in kv[1])):
    w = max(v, key=lambda r: r[7] - r[6])
    print(f"  {j}  {len(v):3d}  worst {w[2]} {w[1]} at ({w[3]:.0f}, {w[4]:.0f}): hers {w[6]:.0f} -> {w[7]:.0f} mm (moved {w[5]:.0f})")
