r"""arms.py : step 184 on vs off, from scratchpad\wood\arm-on and arm-off (corpus-gate --work dirs). For every set: out.e2k
identical or not; for every storey whose plate area moved, off -> on beside the engineer's plate there; a tally of what the
drop did against her model. COVERS: AREA ... FLOOR plates by storey (names normalised as framingover.py), the e2k bytes.
DOES NOT: walls, columns or openings (only the plates' storeys are compared; the hash says whether anything else moved)."""
import collections, contextlib, hashlib, io, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
with contextlib.redirect_stdout(io.StringIO()):   # its module body prints its own table over the gate's ledger
    from framingover import plates, normed   # noqa: E402

ON, OFF = os.path.join(HERE, os.environ.get("ARM_ON", "arm-on")), os.path.join(HERE, "arm-off")
YARD = os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks")
tally = collections.Counter(); same = []; moved_sets = []
for job in sorted(d for d in os.listdir(ON) if os.path.isdir(os.path.join(ON, d))):
    a, b = os.path.join(OFF, job, "out.e2k"), os.path.join(ON, job, "out.e2k")
    if not (os.path.exists(a) and os.path.exists(b)):
        print(f"{job}: missing out.e2k (off {os.path.exists(a)}, on {os.path.exists(b)})"); tally["missing"] += 1; continue
    ha, hb = (hashlib.sha256(open(p, "rb").read()).hexdigest()[:16] for p in (a, b))
    if ha == hb: same.append(job); continue
    off, on = normed(plates(a)) or {}, normed(plates(b)) or {}
    y = os.path.join(YARD, job + ".e2k")
    hers = normed(plates(y)) if os.path.exists(y) else None
    rows = []
    for st in sorted(set(off) | set(on)):
        o, n = sum(off.get(st, {}).values()), sum(on.get(st, {}).values())
        if abs(o - n) < 1: continue
        h = sum((hers or {}).get(st, {}).values()) if hers is not None else None
        if n < o and not h: k = "RIGHT: plate removed where she plates nothing"
        elif n < o and h and n >= 0.5 * h: k = "removed some, she plates it, we still carry half or more"
        elif n < o and h: k = "LOSS: removed where she plates it, under half hers left"
        else: k = "GREW (step 184 only removes - look)"
        tally[k] += 1
        rows.append(f"   {st:<6} off {o:9,.0f} -> on {n:9,.0f}   hers {('-' if h is None else f'{h:,.0f}'):>9}   {k}")
    moved_sets.append(job)
    print(f"{job}: e2k differs; {len(rows)} storey(s) with plate moved")
    for r in rows: print(r)
print(f"\nidentical e2k: {len(same)} of {len(same) + len(moved_sets)} sets: {', '.join(same)}")
print("storeys moved, by verdict:")
for k, n in sorted(tally.items(), key=lambda kv: -kv[1]): print(f"   {n:4}  {k}")
