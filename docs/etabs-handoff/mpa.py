"""Every text entity mentioning MPa or concrete strength, with its layer and position."""
import re
import sys

raw = [l.rstrip() for l in open(sys.argv[1], encoding="utf-8", errors="replace")]

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
        if re.search(r"MPa|STRENGTH|f'c|CONCRETE", txt, re.I):
            try:
                x, y = float(d.get("10", "0")), float(d.get("20", "0"))
            except ValueError:
                x = y = 0.0
            found.append((re.sub(r"\s+", " ", txt)[:120], d.get("8", ""), x, y))
    i = j

for txt, layer, x, y in found:
    print(f"  {txt:<80} [{layer}] ({x:.0f},{y:.0f})")
print(f"{len(found)} entity(ies)")
