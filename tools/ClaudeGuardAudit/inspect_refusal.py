r"""Print the guard's refusal for one record of a real transcript, plus the instrument's runs, so the
refusal can be judged true or false rather than counted. Args: <transcript> <record index>."""
import importlib.util
import json
import sys

# -I implies -E, so PYTHONIOENCODING is ignored; the transcript carries characters cp1252 cannot encode.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

GUARD = r"C:\VIsual Studio Projects\Operations\tools\claude_instrument_guard.py"
spec = importlib.util.spec_from_file_location("guard", GUARD)
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)

path, idx = sys.argv[1], int(sys.argv[2])
recs = []
with open(path, encoding="utf-8", errors="replace") as fh:
    for line in fh:
        try:
            recs.append(json.loads(line))
        except Exception:
            continue

text = g.text_of(recs[idx])
print("=== THE REFUSAL ===")
print(g.verdict(recs[:idx + 1], text))
print()
print("=== THE CLAIM (first 700 chars) ===")
print(" ".join(text.split())[:700])
print()
print("=== scripts edited before this point, and their runs ===")
outputs = g.results_by_id(recs[:idx + 1])
edits, runs = {}, []
for i, rec in enumerate(recs[:idx + 1]):
    for b in g.blocks(rec):
        if not isinstance(b, dict) or b.get("type") != "tool_use":
            continue
        name, inp = b.get("name"), (b.get("input") or {})
        if name in g.WRITE_TOOLS:
            p = inp.get("file_path") or ""
            if g.SCRIPT.search(p):
                edits[__import__("os").path.basename(p)] = i
        elif name in g.RUN_TOOLS:
            cmd = inp.get("command") or ""
            for base in edits:
                if base in cmd:
                    runs.append((i, base, outputs.get(b.get("id"), "")))
for base, i in sorted(edits.items(), key=lambda kv: kv[1]):
    print("  edited @%-6d %s" % (i, base))
print()
for i, base, out in runs:
    if i > edits[base]:
        flat = " ".join((out or "").split())
        print("  run @%-6d %-34s nil=%-5s  %s" % (
            i, base, bool(g.OUTPUT_NIL.search(out or "")), flat[:110]))
