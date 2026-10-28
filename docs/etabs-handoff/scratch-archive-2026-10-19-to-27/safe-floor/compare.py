# Each storey's SAFE counts (safe-floor all) against the model picture's own label (model-render), storey by storey.
import re, sys

o = sys.argv[1]
svg = open(o + '/kc-review/all-storeys.svg', encoding='utf-8').read()
pic = {}
for m in re.finditer(r'>([A-Z0-9 ]+?)  (\d+)f (\d+)w (\d+)c(?: (\d+)o)?<', svg):
    pic[m.group(1).strip()] = (int(m.group(2)), int(m.group(5) or 0), int(m.group(3)), int(m.group(4)))
safe = {}
for line in open(o + '/safe-floor/all.txt', encoding='utf-8'):
    m = re.match(r'(.+?): .*slabs (\d+)  openings (\d+)  walls (\d+)  columns (\d+)', line)
    if m:
        safe[m.group(1)] = tuple(int(m.group(i)) for i in range(2, 6))
same = [s for s in safe if pic.get(s) == safe[s]]
print(f"storeys in SAFE run {len(safe)}, in picture {len(pic)}, identical (floors, openings, walls, columns) {len(same)}")
for s in safe:
    if pic.get(s) != safe[s]:
        print("DIFFER", s, "picture", pic.get(s), "safe", safe[s])
