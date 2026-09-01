"""Slab thickness / step call-outs printed on a sheet, with where they sit."""
import re
import sys

raw = [l.rstrip() for l in open(sys.argv[1], encoding="utf-8", errors="replace")]

THICK = re.compile(
    r'(\d+(?:\s*\d*/\d+)?)\s*"?\s*(DP|DEEP|DROP)?\s*SLAB|SLAB\s*(\d+)\s*"|(\d+)\s*"\s*DP\b',
    re.I)

i = 0
found = []
while i < len(raw) - 1:
    if raw[i].strip() != "0":
        i += 1
        continue
    kind = raw[i + 1].strip()
    j = i + 2
    d = {}
    while j < len(raw) - 1 and raw[j].strip() != "0":
        d.setdefault(raw[j].strip(), raw[j + 1].strip())
        j += 2
    if kind in ("TEXT", "MTEXT"):
        txt = (d.get("1") or "").strip()
        if THICK.search(txt) or re.search(r"THICKEN|STEP|DROP|DEPRESS|RECESS", txt, re.I):
            try:
                x, y = float(d.get("10", "0")), float(d.get("20", "0"))
            except ValueError:
                x = y = 0.0
            found.append((txt, d.get("8", ""), x, y))
    i = j

print(f"{len(found)} thickness / step call-out(s)")
seen = {}
for txt, layer, x, y in found:
    key = re.sub(r"\s+", " ", txt)[:60]
    seen.setdefault(key, []).append((x, y, layer))

for key, places in sorted(seen.items(), key=lambda kv: -len(kv[1])):
    spots = " ".join(f"({x:.0f},{y:.0f})" for x, y, _ in places[:6])
    print(f"  {len(places):3d}x  {key:<46} {spots}")
