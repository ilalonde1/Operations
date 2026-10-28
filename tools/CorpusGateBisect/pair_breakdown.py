r"""pair_breakdown.py <gate_pair_moves output> : the unpaired LOST / GAINED lines counted by kind, by the distance to the nearest
member of the kind that stands after (lost only), and by distance to hers; plus the re-placed scores by set. Counts the whole file."""
import collections, re, sys
t = open(sys.argv[1], encoding="utf-8").read().splitlines()
job = None
lost = collections.Counter(); gained = collections.Counter(); per = collections.defaultdict(lambda: [0, 0, 0, 0, 0])
def band(d):
    if d == "inf": return "no hers"
    d = float(d.replace(",", ""))
    return "<=100" if d <= 100 else "<=300" if d <= 300 else "<=1000" if d <= 1000 else ">1000"
for line in t:
    if m := re.match(r"^== (\S+):", line): job = m.group(1); continue
    if m := re.match(r"^\s+LOST\s+(\w+)\s+\(.*?\)\s+[\d,]+\s+(\S+) mm from hers; (?:nearest after ([\d,]+) mm away|none)", line):
        near = m.group(3); nb = "none after" if near is None else ("after<=60" if float(near.replace(",", "")) <= 60 else "after<=300" if float(near.replace(",", "")) <= 300 else "after>300")
        lost[(m.group(1), nb, band(m.group(2)))] += 1; per[job][3] += 1
    elif m := re.match(r"^\s+GAINED\s+(\w+)\s+\(.*?\)\s+[\d,]+\s+(\S+) mm from hers", line):
        gained[(m.group(1), band(m.group(2)))] += 1; per[job][4] += 1
    elif m := re.search(r"\((\d+) closer to hers, (\d+) further, (\d+) same", line):
        for k in range(3): per[job][k] += int(m.group(k + 1))
print("LOST unpaired, by kind / nearest same-kind member after / distance to hers:")
for k, n in sorted(lost.items(), key=lambda kv: -kv[1]): print(f"  {n:5d}  {k}")
print(f"  total {sum(lost.values())}")
print("GAINED unpaired, by kind / distance to hers:")
for k, n in sorted(gained.items(), key=lambda kv: -kv[1]): print(f"  {n:5d}  {k}")
print(f"  total {sum(gained.values())}")
print("per set: closer further same lost gained (top 15 by lost+gained):")
for j, v in sorted(per.items(), key=lambda kv: -(kv[1][3] + kv[1][4]))[:15]: print(f"  {j}  {v}")
print(f"sets with any 'further': {sum(1 for v in per.values() if v[1])} of {len(per)}")
