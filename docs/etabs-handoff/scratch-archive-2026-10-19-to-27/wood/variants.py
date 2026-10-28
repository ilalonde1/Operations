r"""variants.py : two narrowings of step 184, scored on the arms without rebuilding, per (set, storey) whose plate step 184
moved (arms.out), against the engineer's model.
  A - only a clause that says FRAMING OVER frames ("SHOWING ROOF OVER" does not).
  B - A, and a storey framed by a sheet whose OWN storey nothing frames (the bottom of a chain) keeps its plate.
A storey the variant would no longer drop goes back to its OFF plate. COVERS: the storeys step 184 moved, from its own report
lines. DOES NOT: storeys a variant would drop that step 184 did not (none: both only narrow it)."""
import collections, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
ON = os.path.join(HERE, "arm-on")
LINE = re.compile(r"(S[\w.]+?_\d+_[^;(]+?) \((\S+?): (\d+) outline\(s\), (\d+) opening\(s\); framed by ([^)]+)\)")

verdict = {}; job = None
for line in open(os.path.join(HERE, "arms.out"), encoding="utf-8"):
    if m := re.match(r"^(\d{5}-\d\d):", line): job = m.group(1); continue
    if m := re.match(r"\s+(\S+)\s+off\s+([\d,]+) -> on\s+([\d,]+)\s+hers\s+(\S+)\s+(.*)", line):
        off, on = (float(m.group(i).replace(",", "")) for i in (2, 3))
        hers = None if m.group(4) == "-" else float(m.group(4).replace(",", ""))
        verdict[(job, m.group(1))] = (off, on, hers)


def judge(plate, hers):
    if not hers: return "she plates nothing" + (": WE STILL PLATE" if plate > 0 else ": none - right")
    return "she plates it: " + ("half or more of hers - kept" if plate >= 0.5 * hers else "under half of hers - LOSS")


for variant in ("as built", "A", "B", "B'"):
    tally = collections.Counter(); lost = []
    for job in sorted(os.listdir(ON)):
        rp = os.path.join(ON, job, "report.txt")
        if not os.path.exists(rp): continue
        text = next((l for l in open(rp, encoding="utf-8", errors="replace") if "step 184" in l), "")
        rows = LINE.findall(text)
        dropped = {n.strip() for n, *_ in rows}
        keep_storeys = collections.defaultdict(lambda: True)          # storey -> every dropped sheet of it is kept by the variant
        any_rows = set()
        for name, storey, _, _, framer in rows:
            st = re.sub(r"^L0*", "L", storey); any_rows.add(st)
            kept = False
            if variant in ("A", "B") and "FRAMING OVER" not in framer.upper(): kept = True
            if variant in ("B", "B'") and framer.strip() not in dropped: kept = True     # the framer's own storey is framed by nothing
            keep_storeys[st] = keep_storeys[st] and kept
        for st in any_rows:
            if (job, st) not in verdict: continue
            off, on, hers = verdict[(job, st)]
            plate = off if keep_storeys[st] else on
            v = judge(plate, hers); tally[v] += 1
            if "LOSS" in v or "STILL" in v: lost.append(f"{job} {st} ({plate:,.0f} vs hers {hers or 0:,.0f})")
    print(f"{variant}: " + "; ".join(f"{n} {k}" for k, n in sorted(tally.items(), key=lambda kv: kv[0])))
    for l in lost: print("      " + l)
