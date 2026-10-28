r"""net_probe_look.py <net_probe output.txt> <out.png> [title] : draws what net_probe.ps1 printed for one sheet - walls before the
network (grey, thick), walls after it (red), openings / headers (blue) - so a joint or header is LOOKED at, not read off numbers.
WHY (2026-10-27): 30990 S1.21 (a design load plan, not placed in the model) is where the network check's HEADER rule was
loosened to the jamb's axis; it has no engineer's model to render against, so its own before / after is the picture.
COVERS: the lines net_probe printed (its window and radius). DOES NOT: the DXF's linework itself (dxf_window.py), other storeys."""
import re, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
src, out = sys.argv[1], sys.argv[2]
title = sys.argv[3] if len(sys.argv) > 3 else src
pat = re.compile(r"^(before|after|OPENING)\s+\(\s*(-?[\d.]+),\s*(-?[\d.]+)\)-\(\s*(-?[\d.]+),\s*(-?[\d.]+)\)\s+t\s*(\d+)\s+bearing\s+([\d.]+)")
style = {"before": dict(color="0.75", lw=7, zorder=1), "after": dict(color="#b22222", lw=2, zorder=2), "OPENING": dict(color="#1f5fbf", lw=2, ls="--", zorder=3)}
fig, ax = plt.subplots(figsize=(8, 8)); n = 0
for line in open(src, encoding="utf-8-sig", errors="replace"):
    m = pat.match(line.strip())
    if not m: continue
    k, x1, y1, x2, y2, t, b = m.group(1), *map(float, m.groups()[1:])
    ax.plot([x1, x2], [y1, y2], **style[k]); n += 1
    ax.annotate(f"{b:.2f}", ((x1 + x2) / 2, (y1 + y2) / 2), fontsize=7, color=style[k]["color"])
ax.set_aspect("equal"); ax.set_title(f"{title}\ngrey = before the network, red = after, blue dashed = header; labels = bearing deg", fontsize=9)
fig.tight_layout(); fig.savefig(out, dpi=90); print(out, f"{n} lines drawn")
