r"""retrim.py <job> <storey> <commit> : pair each member the commit lost with the nearest one of that kind it gained (within
1,000 mm - the same member re-placed or re-trimmed) and score the pair against the engineer: closer to hers by > 50 mm, further
by > 50 mm, or the same. Unpaired lost/gained are listed with their distance to hers. Distances are midpoint to midpoint."""
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "member_verdicts.py"), encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])
job, st, c = sys.argv[1:4]
ox, oy = offset(job)
hm = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k"))
h = hm.get(her_storey(hm, st), {"column": [], "wall": []})
i = COMMITS.index(c); prev = f"walk-{COMMITS[i - 1]}" if i else "rb-7345"
mv = diff(os.path.join(SIX, job, prev, "out.e2k"), os.path.join(SIX, job, f"walk-{c}", "out.e2k")).get(st, [])
dh = lambda m: min((math.hypot(m[2] + ox - q[0], m[3] + oy - q[1]) for q in h[m[1]]), default=float("inf"))
lost = [m for m in mv if m[0] == "LOST"]; gained = [m for m in mv if m[0] == "GAINED"]
pairs = sorted(((math.hypot(l[2] - g[2], l[3] - g[3]), a, b) for a, l in enumerate(lost) for b, g in enumerate(gained) if l[1] == g[1]))
ul, ug, closer, further, same = set(range(len(lost))), set(range(len(gained))), 0, 0, 0
for d, a, b in pairs:
    if d > 1000 or a not in ul or b not in ug: continue
    ul.discard(a); ug.discard(b)
    x, y = dh(lost[a]), dh(gained[b])
    if y < x - 50: closer += 1
    elif y > x + 50: further += 1
    else: same += 1
print(f"{job} {st} @ {c}: {len(lost)} lost, {len(gained)} gained -> {len(lost) - len(ul)} re-placed"
      f" ({closer} closer to hers, {further} further, {same} same); unpaired {len(ul)} lost, {len(ug)} gained")
for a in sorted(ul): print(f"    lost   {lost[a][1]} ({lost[a][2]:,.0f}, {lost[a][3]:,.0f}) {lost[a][4]:,.0f}  hers {dh(lost[a]):,.0f} mm")
for b in sorted(ug): print(f"    gained {gained[b][1]} ({gained[b][2]:,.0f}, {gained[b][3]:,.0f}) {gained[b][4]:,.0f}  hers {dh(gained[b]):,.0f} mm")
