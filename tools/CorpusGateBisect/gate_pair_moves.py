r"""gate_pair_moves.py <before-work> <after-work> <out-dir> [job,job] : two corpus-gate --work dirs built from the SAME views by two
bins (corpus-gate --recompose on copies of one run), set by set: byte-identical or not, and for each set that is not, every
storey's moves judged against the engineer's model by judge_drift_moves.py's members judge.

WHY (2026-10-27). The WallNetwork unit fix (Operations 0f3e4ee4) moves every millimetre model's snap from 2 mm to 50.8 mm, and
ee's rule is that a moved wall or column is neither a fix nor a fault until it is judged against her model and looked at. A gate
ledger judges plate area, thickness and openings - it cannot see a wall that moved. Two gates on the same views differ ONLY by
the code between the two bins, so every difference here is that code's.

Writes <out-dir>\<job>\before\out.e2k, <out-dir>\<job>\after\out.e2k (+ after's yardstick.txt, for the registration) - the
layout judge_drift_moves.py reads - and prints per set: SAME, or each storey's lost / gained / re-placed (closer to hers,
further, same within 50 mm) and every unpaired member with its distance to hers. Totals last, as X of Y sets.

COVERS: what judge_drift_moves members covers - column points and wall midpoints by storey, paired within 1,000 mm.
DOES NOT: plates, openings, sections, thickness (the gate ledger beside each work dir judges those); wall ENDS - a wall whose
midpoint stays while an end moves 50 mm reads as unmoved here, and that is the commonest move a snap change makes.
SAME-CLASS FAULT IT MISSES: a corner pulled 50 mm off both of hers with the wall's midpoint unchanged. So the verdict is the
render, not this table: takeoff model-render --storey on before and after for every set it lists.
Exit 0 always (a report); 2 on bad arguments.
"""
import contextlib, filecmp, io, os, re, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import diff, judge_members   # noqa: E402

if len(sys.argv) < 4:
    print(__doc__); sys.exit(2)
before_w, after_w, out = sys.argv[1:4]
only = set(sys.argv[4].split(",")) if len(sys.argv) > 4 else None
jobs = sorted(j for j in os.listdir(after_w) if os.path.isdir(os.path.join(after_w, j)) and (only is None or j in only))
same, changed, missing = [], [], []
tot = [0, 0, 0, 0, 0]   # closer, further, same, lost unpaired, gained unpaired
for job in jobs:
    a, b = os.path.join(before_w, job, "out.e2k"), os.path.join(after_w, job, "out.e2k")
    if not (os.path.isfile(a) and os.path.isfile(b)):
        missing.append(f"{job} (before {'has' if os.path.isfile(a) else 'NO'} model, after {'has' if os.path.isfile(b) else 'NO'})"); continue
    if filecmp.cmp(a, b, shallow=False):
        same.append(job); continue
    for side, src in (("before", a), ("after", b)):
        os.makedirs(os.path.join(out, job, side), exist_ok=True)
        shutil.copyfile(src, os.path.join(out, job, side, "out.e2k"))
    ys = os.path.join(after_w, job, "yardstick.txt")
    if os.path.isfile(ys): shutil.copyfile(ys, os.path.join(out, job, "after", "yardstick.txt"))
    storeys = list(diff(os.path.join(out, job, "before", "out.e2k"), os.path.join(out, job, "after", "out.e2k")).keys())
    if not storeys:
        changed.append((job, [])); print(f"== {job}: bytes differ, no member moved (names / order / plates only)"); continue
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        judge_members(out, job, "after", "before", storeys, show_pairs=True)   # MOVED lines: every re-placed member closer/further
    text = buf.getvalue()
    for m in re.finditer(r"\((\d+) closer to hers, (\d+) further, (\d+) same within 50 mm\)", text):
        for k in range(3): tot[k] += int(m.group(k + 1))
    tot[3] += len(re.findall(r"^\s+LOST ", text, re.M)); tot[4] += len(re.findall(r"^\s+GAINED ", text, re.M))
    changed.append((job, storeys))
    print(f"== {job}: {len(storeys)} storey(s) moved")
    print(text.rstrip())
print()
print(f"{len(same)} of {len(jobs)} set(s) byte-identical; {len(changed)} differ; {len(missing)} without a model on a side")
for m in missing: print(f"  no model: {m}")
print(f"re-placed members: {tot[0]} closer to hers, {tot[1]} further, {tot[2]} same within 50 mm; unpaired: {tot[3]} lost, {tot[4]} gained")
print("changed: " + ", ".join(f"{j} ({len(s)})" for j, s in changed))
