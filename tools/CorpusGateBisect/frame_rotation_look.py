r"""frame_rotation_look.py <ours.e2k> <hers.e2k> <out.png> [scale ...] : why a set's frame does not register against the
engineer's model. For every storey both models name (with columns in both), the rigid fit - rotation 0-359.5 deg in 0.5 deg
steps, each scale given (default 1), then the translation most column pairs vote for (200 mm bins), refined to the median of
the pairs within 300 mm - that puts the most of OUR columns within 100 mm of HERS. Printed per storey beside the
translation-only fit (rotation 0, scale 1: what ModelYardstick's column registration does), and plotted: ours (red) moved by
the best fit over hers (grey). FRAME_STOREYS=L4,L10 limits the storeys.

Written 2026-10-26 for the seven sets the corpus gate leaves FRAME (unjudged by position): 30986-02, 31098 and 31183 fit at a
rigid rotation (270, 270, 355 deg) that translation alone cannot find; see docs/pdf-intake/frames-unregistered-2026-10-26.md.

COVERS: column points only (LINE ... COLUMN; in a model under the yardstick folder our own K-named objects are dropped);
storeys matched by judge_drift_moves.members' names.
DOES NOT: walls, plates or grids; mirroring (a model drawn mirrored fits nothing here); a storey named differently in the two
models; a storey with columns in only one of them.
SAME-CLASS FAULT IT MISSES: a regular grid of columns fits at several rotations and offsets (a square bay fits at 0/90/180/270)
- the best count wins, and a near tie is not flagged. Read the per-storey rows: a rotation that holds on every storey at one
offset is the frame; one storey's best fit alone is not.
Exit 0; 2 on bad arguments.
"""
import math, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import members   # noqa: E402

if len(sys.argv) < 4:
    print(__doc__); sys.exit(2)
ours_p, hers_p, out = sys.argv[1:4]
scales = [float(s) for s in sys.argv[4:]] or [1.0]
ours, hers = members(ours_p), members(hers_p)
shared = sorted(st for st in ours if st in hers and ours[st]["column"] and hers[st]["column"] and " " not in st)
if os.environ.get("FRAME_STOREYS"): shared = [st for st in shared if st in os.environ["FRAME_STOREYS"].split(",")]


def nearest(r, b):
    return np.min(np.hypot(*((r[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)


def fit(a, b, angles, scales):
    """(columns of ours within 100 mm, degrees, scale, offset) of the best rigid fit of a onto b"""
    best = (0, 0.0, 1.0, (0.0, 0.0))
    for s in scales:
        for deg in angles:
            t = math.radians(deg); c, n = math.cos(t), math.sin(t)
            r = np.stack([s * (a[:, 0] * c - a[:, 1] * n), s * (a[:, 0] * n + a[:, 1] * c)], axis=1)
            d = (b[None, :, :] - r[:, None, :]).reshape(-1, 2)
            keys, counts = np.unique(np.round(d / 200).astype(np.int64), axis=0, return_counts=True)
            for k in keys[np.argsort(-counts)[:3]]:
                o = k * 200.0
                dist = nearest(r + o, b)
                m = dist <= 300
                if m.sum():
                    pair = b[np.argmin(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)]
                    o = o + np.median((pair - (r + o))[m], axis=0)
                    dist = nearest(r + o, b)
                hit = int((dist <= 100).sum())
                if hit > best[0]: best = (hit, deg, s, (float(o[0]), float(o[1])))
    return best


rows = []
fig, axes = plt.subplots(1, max(1, len(shared)), figsize=(6 * max(1, len(shared)), 6), squeeze=False)
for ax, st in zip(axes[0], shared):
    a, b = np.array(ours[st]["column"]), np.array(hers[st]["column"])
    plain = fit(a, b, [0.0], [1.0])
    best = fit(a, b, [x * 0.5 for x in range(720)], scales)
    rows.append(f"  {st:<8} ours {len(a):4} hers {len(b):4} | translation only: {plain[0]:4} within 100 mm | best: {best[0]:4} "
                f"at {best[1]:5.1f} deg, scale {best[2]:g}, offset ({best[3][0]:,.0f}, {best[3][1]:,.0f})")
    t = math.radians(best[1]); c, n = math.cos(t), math.sin(t); s = best[2]
    r = np.stack([s * (a[:, 0] * c - a[:, 1] * n), s * (a[:, 0] * n + a[:, 1] * c)], axis=1) + np.array(best[3])
    ax.scatter(b[:, 0], b[:, 1], s=30, c="0.6", label=f"hers {len(b)}")
    ax.scatter(r[:, 0], r[:, 1], s=10, c="red", label=f"ours {len(a)} moved")
    ax.set_title(f"{st}: {best[0]}/{len(a)} at {best[1]:.1f} deg x{s:g}"); ax.set_aspect("equal"); ax.legend(fontsize=7)
fig.savefig(out, dpi=60, bbox_inches="tight")
print(os.path.basename(ours_p), "->", os.path.basename(hers_p), f"{len(shared)} shared storey(s) with columns in both")
print("\n".join(rows))
print(out)
