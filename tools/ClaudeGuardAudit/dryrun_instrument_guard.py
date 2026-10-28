r"""Dry-run claude_instrument_guard.py over a REAL transcript, not fixtures I wrote to suit my own regex.

Virtual Drafter did this to the first version and found two defects my 8-of-8 suite passed over. The fixtures
test the rules I thought of; a real transcript tests the rules I did not. Takes the transcript path as an
argument so no path is baked in.

Prints its own known answers first - one case that MUST be refused and one that MUST be allowed - so the count
below it is readable as a measurement rather than an assertion.
"""
import importlib.util
import json
import sys

GUARD = r"C:\VIsual Studio Projects\Operations\tools\claude_instrument_guard.py"
spec = importlib.util.spec_from_file_location("guard", GUARD)
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)


def rec_write(path):
    return {"message": {"role": "assistant", "content": [
        {"type": "tool_use", "name": "Write", "input": {"file_path": path}}]}}


def rec_run(cmd, use_id):
    return {"message": {"role": "assistant", "content": [
        {"type": "tool_use", "id": use_id, "name": "Bash", "input": {"command": cmd}}]}}


def rec_out(use_id, text):
    return {"message": {"role": "user", "content": [
        {"type": "tool_result", "tool_use_id": use_id, "content": text}]}}


NIL = "The scan came back clean: 0 of 62 transcripts deleted it."

# MUST refuse: an instrument whose every run returned nothing.
BLIND = [rec_write(r"C:\x\scan.py"), rec_run("python scan.py", "u1"),
         rec_out("u1", "scanned 62 files\nresult: 0 found")]
# MUST allow: the PDF Ingestor's real case - a must-match and a must-not, in its own words.
TESTED = [rec_write(r"C:\scratch\net_probe.ps1"),
          rec_run("pwsh -File 'C:/scratch/net_probe.ps1' -Plan 31087", "u1"),
          rec_out("u1", "31087 axes in plan: 80,103 and 80,124 listed"),
          rec_run("pwsh -File 'C:/scratch/net_probe.ps1' -Outside", "u2"),
          rec_out("u2", "nothing listed outside the plan")]

print("known answer, must REFUSE :", "REFUSED" if g.verdict(BLIND, NIL) else "ALLOWED  <-- WRONG")
print("known answer, must ALLOW  :", "ALLOWED" if not g.verdict(TESTED, NIL) else "REFUSED  <-- WRONG")
print()

path = sys.argv[1]
recs = []
with open(path, encoding="utf-8", errors="replace") as fh:
    for line in fh:
        try:
            recs.append(json.loads(line))
        except Exception:
            continue

claims = 0
nil_claims = 0
refused = []
for i, rec in enumerate(recs):
    if (rec.get("message") or {}).get("role") != "assistant":
        continue
    text = g.text_of(rec)
    if not text.strip():
        continue
    claims += 1
    if not g.NIL_CLAIM.search(text):
        continue
    nil_claims += 1
    why = g.verdict(recs[:i + 1], text)
    if why:
        refused.append((i, text))

print("records in transcript      :", len(recs))
print("assistant texts examined   :", claims)
print("of those, carrying a nil   :", nil_claims)
print("of those nils, REFUSED     :", len(refused))
print()
for i, text in refused:
    flat = " ".join(text.split())
    hits = [m.group(0) for m in g.NIL_CLAIM.finditer(text)]
    print("  record %-6d nils %-40s %s" % (i, str(hits)[:40], flat[:150]))
