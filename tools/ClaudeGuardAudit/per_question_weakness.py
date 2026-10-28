r"""Two questions the aggregate breakdown cannot answer.

1. Is the BEHAVIOUR exit per-SCRIPT rather than per-QUESTION? For a general-purpose runner like oq.py, one
   successful query would clear every later nil from it forever. Measures: among armed claims, how often the
   run that found something asked a DIFFERENT question from the run that returned the nil.
2. Did the guard's own founding instance - the cadtest scan that returned a clean 0 - fall in the never-armed
   bucket? It was a heredoc into python, so it should. Prints the never-armed claims to see.

Args: <transcript dir>
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
files = sorted(f for f in os.listdir(root) if f.endswith(".jsonl"))

armed_total = 0
cleared_by_other_question = 0
same_question = 0
never_armed = []
multi_use = {}

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
        if not g.NIL_CLAIM.search(text):
            continue
        sub = recs[:i + 1]
        outputs = g.results_by_id(sub)
        edits, runs = {}, []
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
                    for base in edits:
                        if base in cmd:
                            runs.append((j, base, out, cmd))
        armed = [r for r in runs if r[0] > edits[r[1]]]
        if not armed:
            never_armed.append((fn[:12], i, " ".join(text.split())[:110]))
            continue
        armed_total += 1
        last = armed[-1][1]
        mine = [(j, out, cmd) for j, base, out, cmd in armed if base == last]
        multi_use[last] = max(multi_use.get(last, 0), len(mine))
        # The run that cleared it, and the run that most plausibly produced the nil (the latest one).
        finders = [(j, cmd) for j, out, cmd in mine if g.found_something(out)]
        if not finders:
            continue
        latest_cmd = mine[-1][2]
        if any(cmd != latest_cmd for _, cmd in finders) and finders[-1][0] != mine[-1][0]:
            cleared_by_other_question += 1
        else:
            same_question += 1

print("armed nil claims                      :", armed_total)
print("  cleared by a run asking a DIFFERENT question : %d of %d" % (cleared_by_other_question, armed_total))
print("  cleared by the SAME run/command              : %d of %d" % (same_question, armed_total))
print()
print("most runs of one instrument behind a single claim (a general-purpose runner shows high):")
for s, n in sorted(multi_use.items(), key=lambda kv: -kv[1])[:8]:
    print("  %4d runs  %s" % (n, s))
print()
print("never armed: %d - the inline/heredoc gap. Every one of them:" % len(never_armed))
for fn, i, snippet in never_armed:
    print("  %s @%-6d %s" % (fn, i, snippet))
