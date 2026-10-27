r"""frame_rotation_check.py [work-dir] [yardsticks-dir] : the sets whose engineer's model is a rigid ROTATION of ours - the
class the yardstick's translation-only registration cannot find (2026-10-26: 30986-02 at 270 deg, 31098-01 at 270, 31183-01
at 355, all left FRAME by the gate though our read is right).

For every <job>\out.e2k under the work dir (default: the corpus gate's, %TEMP%\kor-drawings\corpus) with the engineer's model
in the yardsticks dir: ONE rigid fit for the whole set, pooled over every storey both models name - rotation 0-359.5 deg in
0.5 deg steps, then the translation the column pairs vote for - against the translation-only fit (rotation 0). A set is
ROTATED when the rotated fit places at least twice as many of our columns within 100 mm as translation alone does, and at
least a third of our columns on those storeys. Exit 1 when any set is ROTATED, so it fails until the judge finds the turn
itself; the yardstick.txt beside the build then says "rotated" and its support, and this reads it and passes the set.

COVERS: column points, storeys both models name (judge_drift_moves.members). The registration the yardstick PRINTED is read
from yardstick.txt when it names a rotation ("rotated N deg"); without one it is taken as translation only.
DOES NOT: walls or plates; a model mirrored rather than rotated; a set whose storeys are named differently in the two models
(nothing pooled); a set with one storey (judged, but a single storey's fit is weak evidence).
SAME-CLASS FAULT IT MISSES: a model rotated AND scaled (feet read as inches) - only scale 1 is tried; and a set two
buildings at two angles - the pooled fit finds the larger one and passes.
Exit 0 none rotated or all found by the judge; 1 a rotated set the judge missed; 2 bad arguments.
"""
import math, os, re, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import members, YARD, MIRROR   # noqa: E402

work = sys.argv[1] if len(sys.argv) > 1 else os.path.join(MIRROR, "corpus")
yard = sys.argv[2] if len(sys.argv) > 2 else YARD
if not os.path.isdir(work) or not os.path.isdir(yard):
    print(__doc__); sys.exit(2)


def pooled(pairs, deg):
    """columns of ours within 100 mm of hers under one rotation + the translation the pairs vote for, pooled over storeys"""
    t = math.radians(deg); c, n = math.cos(t), math.sin(t)
    rot = [(np.stack([a[:, 0] * c - a[:, 1] * n, a[:, 0] * n + a[:, 1] * c], axis=1), b) for a, b in pairs]
    d = np.concatenate([(b[None, :, :] - r[:, None, :]).reshape(-1, 2) for r, b in rot])
    keys, counts = np.unique(np.round(d / 200).astype(np.int64), axis=0, return_counts=True)
    best = (0, (0.0, 0.0))
    for k in keys[np.argsort(-counts)[:3]]:
        o = k * 200.0
        for _ in range(2):   # refine to the median of the pairs within 300 mm
            res = []
            for r, b in rot:
                near = b[np.argmin(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)]
                res.append(near - (r + o))
            res = np.concatenate(res); m = np.hypot(res[:, 0], res[:, 1]) <= 300
            if m.sum(): o = o + np.median(res[m], axis=0)
        hit = sum(int((np.min(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1) <= 100).sum()) for r, b in rot)
        if hit > best[0]: best = (hit, (float(o[0]), float(o[1])))
    return best


rotated = missed = judged = 0
for job in sorted(os.listdir(work)):
    ours_p, hers_p = os.path.join(work, job, "out.e2k"), os.path.join(yard, job + ".e2k")
    if not (os.path.exists(ours_p) and os.path.exists(hers_p)): continue
    ours, hers = members(ours_p), members(hers_p)
    shared = sorted(st for st in ours if st in hers and ours[st]["column"] and hers[st]["column"] and " " not in st)
    if not shared: continue
    judged += 1
    pairs = [(np.array(ours[st]["column"]), np.array(hers[st]["column"])) for st in shared]
    total = sum(len(a) for a, _ in pairs)
    plain = pooled(pairs, 0.0)[0]
    best_deg, best = 0.0, (plain, None)
    for deg in (x * 0.5 for x in range(1, 720)):
        f = pooled(pairs, deg)
        if f[0] > best[0]: best_deg, best = deg, f
    if not (best[0] >= 2 * max(plain, 1) and best[0] * 3 >= total): continue
    rotated += 1
    ys = os.path.join(work, job, "yardstick.txt")
    said = re.search(r"rotated (-?[\d.]+) deg", open(ys, encoding="utf-8", errors="replace").read()) if os.path.exists(ys) else None
    found = said is not None and abs(((float(said.group(1)) - best_deg) + 180) % 360 - 180) <= 1.0
    if not found: missed += 1
    print(f"{job}: ROTATED {best_deg:g} deg - {best[0]} of {total} of our columns within 100 mm (translation only: {plain}), "
          f"{len(shared)} storeys; the judge {'found it (' + said.group(0) + ')' if found else 'did NOT find it'}")
print(f"\n{rotated} of {judged} sets with columns in both models are a rigid rotation of ours; {missed} not found by the judge")
sys.exit(1 if missed else 0)
