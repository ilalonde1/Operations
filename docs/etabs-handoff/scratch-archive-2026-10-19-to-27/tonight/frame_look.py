r"""frame_look.py <ours.e2k> <hers.e2k> <out.png> [scale ...] : why a set's frame does not register. For every storey both
models name, the rigid fit (rotation 0-359.5 deg in 0.5 steps, plus scale candidates, then the translation most column pairs
vote for) that puts the most of OUR columns within 100 mm of HERS; printed per storey beside the translation-only fit
(rotation 0, scale 1 - what model-yardstick does). A plot per storey: ours (red) moved by the best fit over hers (grey).
COVERS: column points only (LINE ... COLUMN, our K-objects dropped from her model). DOES NOT: walls, plates, mirroring
(a set drawn mirrored will fit nothing here), a storey named differently in the two models."""
import math, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import members   # noqa: E402

ours_p, hers_p, out = sys.argv[1:4]
scales = [float(s) for s in sys.argv[4:]] or [1.0]
ours, hers = members(ours_p), members(hers_p)
shared = sorted(st for st in ours if st in hers and ours[st]["column"] and hers[st]["column"] and " " not in st)
if os.environ.get("FRAME_STOREYS"): shared = [st for st in shared if st in os.environ["FRAME_STOREYS"].split(",")]


def fit(a, b, angles, scales):
    best = (0, 0.0, 1.0, (0.0, 0.0))
    for s in scales:
        for deg in angles:
            t = math.radians(deg); c, n = math.cos(t), math.sin(t)
            r = np.stack([s * (a[:, 0] * c - a[:, 1] * n), s * (a[:, 0] * n + a[:, 1] * c)], axis=1)
            d = (b[None, :, :] - r[:, None, :]).reshape(-1, 2)
            keys, counts = np.unique(np.round(d / 200).astype(np.int64), axis=0, return_counts=True)
            for k in keys[np.argsort(-counts)[:3]]:
                o = k * 200.0
                dist = np.min(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)
                # refine: median residual of the matched pairs
                m = dist <= 300
                if m.sum():
                    near = b[np.argmin(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)]
                    o = o + np.median((near - (r + o))[m], axis=0)
                    dist = np.min(np.hypot(*(((r + o)[:, None, :] - b[None, :, :]).transpose(2, 0, 1))), axis=1)
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
