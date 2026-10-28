r"""topframes.py <speedscope.json> : per thread profile, the Kor.* frames on its stack and how long, deepest Kor frame first."""
import json, sys, collections
d = json.load(open(sys.argv[1]))
fr = [f["name"] for f in d["shared"]["frames"]]
for p in d["profiles"]:
    weight = collections.Counter()
    if p["type"] == "evented":
        stack, last = [], None
        for e in p["events"]:
            if last is not None and stack:
                for i in set(stack): weight[i] += e["at"] - last
            last = e["at"]
            if e["type"] == "O": stack.append(e["frame"])
            elif stack: stack.pop()
    else:
        for s, w in zip(p.get("samples", []), p.get("weights", [])):
            for i in set(s): weight[i] += w
    kor = [(w, fr[i]) for i, w in weight.items() if "Kor." in fr[i]]
    if not kor: continue
    print(f"== {p['name']}  (end {p.get('endValue')})")
    for w, n in sorted(kor, reverse=True)[:14]: print(f"   {w:10.1f}  {n[:170]}")
