r"""leftasread.py : on the final step 184 (arm-on2), every storey a framing-over sheet gives where we STILL write plate and the
engineer's model plates nothing (the "left as read" list), and every storey whose plate step 184 removed (arm-off vs arm-on2)
that the first-version simulation did not (variants.py B' kept it). COVERS: the 21 framing-over sets' storeys given by a
framing-over sheet, plate area by storey (names normalised). DOES NOT: storeys given only by other sheets."""
import collections, contextlib, csv, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
with contextlib.redirect_stdout(io.StringIO()):
    from framingover import plates, normed, norm, FRAMING   # noqa: E402

ON2, OFF = os.path.join(HERE, "arm-on2"), os.path.join(HERE, "arm-off")
YARD = os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks")
left = []; removed = collections.defaultdict(list)
for job in sorted(os.listdir(ON2)):
    sp = os.path.join(ON2, job, "sheets.csv")
    if not os.path.exists(sp): continue
    fo = {norm(r["storeys"] or "") for r in csv.DictReader(open(sp, encoding="utf-8-sig"))
          if r["sheet_type"] == "plan" and FRAMING.search(r["dxf_files"] or "") and "," not in (r["storeys"] or "")}
    fo.discard("")
    if not fo: continue
    on, off = normed(plates(os.path.join(ON2, job, "out.e2k"))) or {}, normed(plates(os.path.join(OFF, job, "out.e2k"))) or {}
    y = os.path.join(YARD, job + ".e2k"); hers = normed(plates(y)) if os.path.exists(y) else {}
    for st in sorted(fo):
        n, o, h = sum(on.get(st, {}).values()), sum(off.get(st, {}).values()), sum((hers or {}).get(st, {}).values())
        if n > 0 and h == 0: left.append(f"{job} {st} {n:,.0f} sq ft")
        if o - n >= 1: removed[job].append(st)
print(f"LEFT AS READ: {len(left)} storey(s) a framing-over sheet gives where we still plate and the engineer plates nothing:")
for l in left: print("   " + l)
print(f"\nREMOVED by the final rule: {sum(len(v) for v in removed.values())} storey(s) in {len(removed)} set(s):")
for j, v in removed.items(): print(f"   {j}: {' '.join(v)}")
