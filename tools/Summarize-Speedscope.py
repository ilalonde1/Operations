"""Summarise a dotnet-trace speedscope profile: inclusive and self time per frame, heaviest first.

    dotnet-trace collect --format speedscope -o out.nettrace -- takeoff.exe corpus-analyze --jobs 31101-01 --force --parallel 1
    python tools/Summarize-Speedscope.py out.speedscope.json [--top 40] [--match Kor.Operations]

The sampled-thread-time profile gives one event stream per thread with open/close frame events at
microsecond timestamps; inclusive time is the span a frame was open, self time is that span minus its
children's. Frames are named as the runtime names them ("Kor.Operations.EngineeringTools.PdfToSafe!
GeometryFilterService.SlabEdgesFromLoops(...)"). Used 2026-09-14 (BridgeChains, PdfIntake §76) and
2026-09-15 (the compose); kept here so the next profile is a two-line job, not a rediscovery.
"""
import json, sys
from collections import defaultdict

path = sys.argv[1]
top = 40
match = None
args = sys.argv[2:]
for i, a in enumerate(args):
    if a == "--top": top = int(args[i + 1])
    if a == "--match": match = args[i + 1]

doc = json.load(open(path, encoding="utf-8"))
frames = doc["shared"]["frames"]
inclusive = defaultdict(float)
self_time = defaultdict(float)
total = 0.0
for prof in doc["profiles"]:
    if prof.get("type") != "evented":
        continue
    stack = []           # (frame, openedAt, childTime)
    for ev in prof["events"]:
        t = ev["at"]
        if ev["type"] == "O":
            stack.append([ev["frame"], t, 0.0])
        else:
            f, opened, child = stack.pop()
            span = t - opened
            inclusive[f] += span
            self_time[f] += span - child
            if stack:
                stack[-1][2] += span
            else:
                total += span
unit = doc["profiles"][0].get("unit", "microseconds")
scale = {"microseconds": 1e-6, "milliseconds": 1e-3, "seconds": 1.0, "nanoseconds": 1e-9}.get(unit, 1e-6)
def name(f):
    return frames[f]["name"]
rows = [(name(f), inclusive[f] * scale, self_time[f] * scale) for f in inclusive]
if match:
    rows = [r for r in rows if match in r[0]]
print(f"total sampled thread time {total * scale:,.1f} s across {len(doc['profiles'])} threads; unit {unit}")
print(f"{'inclusive s':>12} {'self s':>9}  frame")
for n, inc, slf in sorted(rows, key=lambda r: -r[1])[:top]:
    print(f"{inc:12,.1f} {slf:9,.1f}  {n[:150]}")
print()
print("by SELF time:")
for n, inc, slf in sorted(rows, key=lambda r: -r[2])[:top // 2]:
    print(f"{inc:12,.1f} {slf:9,.1f}  {n[:150]}")
