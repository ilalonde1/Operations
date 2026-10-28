r"""still_lost.py : for each REAL lost member member_verdicts.py found on the engineer's model, is one of that kind back at that
spot in today's build (walk-86a2e160), same storey, within 300 mm (column) / 500 mm (wall)? And for each real gained member
she lacks, is it still in today's build? Live faults only."""
import os, re
src = open("member_verdicts.py", encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])
for line in open("member-verdicts.out", encoding="utf-8"):
    if m := re.match(r"^(\S+) (\S+) @ (\S+):", line):
        job, st, c = m.groups()
        fin = members(os.path.join(SIX, job, "walk-86a2e160", "out.e2k")).get(norm(st), {"column": [], "wall": []})
        print(line.split(" = ")[0])
    elif m := re.match(r"^\s+(LOST|GAINED)\s+(column|wall)\s+at \(([-\d,]+), ([-\d,]+)\) ([\d,]+) long  hers: (YES|no)", line):
        k, kind, x, y, ln, h = m.groups()
        if (k == "LOST") != (h == "YES"): continue          # only lost-she-has and gained-she-lacks can be faults
        p = (float(x.replace(",", "")), float(y.replace(",", "")))
        there = near(p, fin[kind], 300 if kind == "column" else 500)
        print(f"     {k:<6} {kind:<6} at ({x}, {y}) {ln} long - today: {'present' if there else 'ABSENT'}"
              f"  -> {'LIVE FAULT' if (k == 'LOST') != there else 'not live'}")
