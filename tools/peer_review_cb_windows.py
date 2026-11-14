# JM 01819 lateral prep (row 1982, JM-LAT-PREP 14 Nov 2026; scratch cb_windows.py brought into the repo). See
# KOR.Drafter jobs/01819-01-renfrew/review/lateral/independent/LATERAL-PREP-r32.md for what it backs and its limits.
r"""check.txt spandrel windows vs WHM's S802 doors (drawn-headers.csv) on a lateral e2k. Scratch (JM-LAT-PREP)."""
import re, sys, csv, collections
e2k, chk, hdr = sys.argv[1:4]
pts, areas, stories = {}, {}, []
span = []
for line in open(e2k, encoding="utf-8", errors="replace"):
    m = re.match(r'\s+STORY "([^"]+)"\s+HEIGHT', line)
    if m: stories.append(m[1]); continue
    m = re.match(r'\s+POINT "([^"]+)"\s+([-\d.E+]+)\s+([-\d.E+]+)', line)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'\s+AREA "([^"]+)"\s+PANEL\s+\d+\s+(.*)', line)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'\s+AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION "([^"]+)".*SPANDREL\s+"([^"]+)"', line)
    if m: span.append((m[1], m[2], m[3]))
win = {}
for ln in open(chk, encoding="utf-8"):
    m = re.match(r'spandrel:(\S+)\s*=\s*([\d.]+)\.\.([\d.]+),\s*([\d.]+)\.\.([\d.]+)', ln)
    if m: win[m[1]] = tuple(float(m[i]) for i in range(2, 6))
doors = collections.defaultdict(list); hs = collections.defaultdict(set)
order = list(reversed(stories))
for r in csv.DictReader(l for l in open(hdr, encoding="utf-8") if not l.startswith("#")):
    m = re.search(r'door plan ([XY]) ([\d.]+)-([\d.]+)', r["how"])
    k = f"{r['wall']}/{r['header']}"
    doors[k].append((m[1], float(m[2]), float(m[3])))
    i0, i1 = order.index(r["storey_from"]), order.index(r["storey_to"])
    for s in order[i0:i1 + 1]: hs[k].add(s)
caught = collections.defaultdict(list); multi = []
for name, st, sec in span:
    xs = [pts[p][0] for p in areas[name]]; ys = [pts[p][1] for p in areas[name]]
    cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
    hit = [k for k, (x0, x1, y0, y1) in win.items() if x0 <= cx <= x1 and y0 <= cy <= y1]
    if len(hit) > 1: multi.append((name, st, hit))
    for k in hit:
        ax, lo, hi = doors[k][0]
        c = cx if ax == "X" else cy
        inside = min(lo for _, lo, _ in doors[k]) - 150 <= c <= max(hi for _, _, hi in doors[k]) + 150
        caught[k].append((st, name, round(cx), round(cy), sec, inside))
print("windows:", win); print("spandrels in e2k:", len(span), "caught by 2+ windows:", multi)
for k in sorted(win):
    rows = caught[k]; per = collections.Counter(s for s, *_ in rows)
    out = [r for r in rows if not r[5]]
    drawn = hs[k]; miss_drawn = sorted(drawn - set(per), key=order.index)
    print(f"\n{k}: WHM door {sorted(set(doors[k]))}; {len(rows)} spandrel(s) caught on {len(per)} storeys; "
          f"outside WHM's door +-150 mm: {len(out)} {out[:6]}; storeys with 2+: {[s for s, n in per.items() if n > 1]}")
    print(f"  S802-drawn storeys with no caught spandrel: {miss_drawn}")
    print(f"  caught storeys S802 does not draw (breaks L11-L15, L24-L29 expected): {sorted(set(per) - drawn, key=order.index)}")
