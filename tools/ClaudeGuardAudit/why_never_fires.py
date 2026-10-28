r"""0 of 369 refusals is the reading a broken guard and an accurate one both produce. This asks WHY each nil
claim was allowed: never armed at all (no attributable run of an edited script), or armed and then cleared by
one of the four exits. Args: <transcript dir> [max files].

Replicates verdict()'s arming logic rather than calling it, because verdict() only returns the decision, not
the reason. Any drift between the two would show as a disagreement in the final line, which is checked.
"""
import importlib.util
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

GUARD = r"C:\VIsual Studio Projects\Operations\tools\claude_instrument_guard.py"
spec = importlib.util.spec_from_file_location("guard", GUARD)
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)

root = sys.argv[1]
cap = int(sys.argv[2]) if len(sys.argv) > 2 else 10 ** 6
files = sorted(f for f in os.listdir(root) if f.endswith(".jsonl"))[:cap]

reasons = {"never armed - no edited script was run": 0,
           "armed, cleared by BEHAVIOUR (a run found something)": 0,
           "armed, cleared by WORDS (self-test vocabulary)": 0,
           "armed, cleared by UNREADABLE output (no nil marker either)": 0,
           "armed, cleared as INDIRECT (unattributable run)": 0,
           "armed, cleared as FOREIGN nils (another tool's numbers)": 0,
           "REFUSED": 0}
disagreements = 0
scripts_seen = {}

for fn in files:
    recs = []
    with open(os.path.join(root, fn), encoding="utf-8", errors="replace") as fh:
        for line in fh:
            try:
                recs.append(json.loads(line))
            except Exception:
                continue
    for i, rec in enumerate(recs):
        if (rec.get("message") or {}).get("role") != "assistant":
            continue
        text = g.text_of(rec)
        if not text.strip():
            continue
        nils = [m.group(0) for m in g.NIL_CLAIM.finditer(text)]
        if not nils:
            continue
        sub = recs[:i + 1]
        outputs = g.results_by_id(sub)
        edits, runs, others, indirect = {}, [], [], []
        for j, r2 in enumerate(sub):
            for b in g.blocks(r2):
                if not isinstance(b, dict) or b.get("type") != "tool_use":
                    continue
                name, inp = b.get("name"), (b.get("input") or {})
                if name in g.WRITE_TOOLS:
                    p = inp.get("file_path") or ""
                    if g.SCRIPT.search(p):
                        edits[os.path.basename(p)] = j
                elif name in g.RUN_TOOLS:
                    cmd = inp.get("command") or ""
                    out = outputs.get(b.get("id"), "")
                    hit = [base for base in edits if base in cmd]
                    for base in hit:
                        runs.append((j, base, out))
                    if not hit:
                        others.append((j, out))
                        if g.INDIRECT_RUN.search(cmd):
                            indirect.append(j)
        armed = [r for r in runs if r[0] > edits[r[1]]]
        decided = g.verdict(sub, text)
        if not armed:
            reasons["never armed - no edited script was run"] += 1
            if decided:
                disagreements += 1
            continue
        last = armed[-1][1]
        scripts_seen[last] = scripts_seen.get(last, 0) + 1
        mine = [(j, out) for j, base, out in armed if base == last]
        if any(g.found_something(out) for _, out in mine):
            key = "armed, cleared by BEHAVIOUR (a run found something)"
        elif any(g.SELF_TEST.search(out or "") for _, out in mine):
            key = "armed, cleared by WORDS (self-test vocabulary)"
        elif not any(not (out or "").strip() or g.OUTPUT_NIL.search(out or "") for _, out in mine):
            key = "armed, cleared by UNREADABLE output (no nil marker either)"
        elif any(j > edits[last] for j in indirect):
            key = "armed, cleared as INDIRECT (unattributable run)"
        else:
            foreign = "\n".join(out or "" for _, out in others).lower()
            if foreign and all(n.lower() in foreign for n in nils):
                key = "armed, cleared as FOREIGN nils (another tool's numbers)"
            else:
                key = "REFUSED"
        reasons[key] += 1
        if (key == "REFUSED") != bool(decided):
            disagreements += 1

total = sum(reasons.values())
print("nil claims examined: %d over %d of %d transcripts" % (
    total, len(files), len([f for f in os.listdir(root) if f.endswith(".jsonl")])))
print()
for k, v in sorted(reasons.items(), key=lambda kv: -kv[1]):
    print("  %4d of %d  %s" % (v, total, k))
print()
print("disagreements with verdict(): %d of %d  (must be 0, or this breakdown is not trustworthy)"
      % (disagreements, total))
print()
print("instruments the guard armed on, most frequent first:")
for s, n in sorted(scripts_seen.items(), key=lambda kv: -kv[1])[:12]:
    print("  %4d  %s" % (n, s))
