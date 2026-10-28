"""Aggregate a census bundle: shape markups by fill colour - how many, in how many files, which subjects, which words,
and how thin (band-like) they are. usage: colours.py <bundle.jsonl>"""
import json, sys, collections
def name(c):
    if not c: return "none"
    r, g, b = (round(v, 2) for v in c[:3])
    known = {(0.5, 0, 0): "burgundy", (1, 1, 0): "yellow", (0, 0.25, 0): "dark green", (1, 0, 0): "red", (0, 0, 1): "blue",
             (0, 0.5, 0.75): "teal", (0, 1, 0): "green", (1, 0.5, 0): "orange", (1, 0, 1): "magenta", (0, 1, 1): "cyan"}
    return known.get((r, g, b), f"rgb({r},{g},{b})")
files = 0
by = collections.defaultdict(lambda: {"n": 0, "files": set(), "subjects": collections.Counter(), "words": collections.Counter(), "thin": 0})
for line in open(sys.argv[1], encoding="utf-8"):
    rec = json.loads(line); files += 1
    for a in rec.get("annots", []):
        if a["type"] not in ("Polygon", "Square", "Circle", "PolyLine"): continue
        k = name(a.get("fill"))
        d = by[k]; d["n"] += 1; d["files"].add(rec["path"]); d["subjects"][a.get("subject") or "-"] += 1
        if a.get("text"): d["words"][a["text"][:40]] += 1
        w, h = a.get("w") or 0, a.get("h") or 0
        if min(w, h) > 0 and min(w, h) <= 25 and max(w, h) >= 4 * min(w, h): d["thin"] += 1
print(files, "files read")
for k, d in sorted(by.items(), key=lambda kv: -kv[1]["n"]):
    print(f"{k:22} {d['n']:6} marks in {len(d['files']):4} files  band-shaped {d['thin']:5}  subjects {d['subjects'].most_common(3)}")
    if d["words"]: print(f"{'':22} words: {d['words'].most_common(6)}")
