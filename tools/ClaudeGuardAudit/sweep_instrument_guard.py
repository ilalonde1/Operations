r"""Dry-run claude_instrument_guard.py over EVERY transcript in a project folder, to measure the false-refusal
rate on a real population rather than on fixtures. Args: <transcript dir> [max files].

Prints its own known answers first - one case that MUST be refused, one that MUST be allowed - so the totals
below can be read as a measurement. Reports X of Y, never a bare count.
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
BLIND = [rec_write(r"C:\x\scan.py"), rec_run("python scan.py", "u1"),
         rec_out("u1", "scanned 62 files\nresult: 0 found")]
TESTED = [rec_write(r"C:\scratch\net_probe.ps1"),
          rec_run("pwsh -File 'C:/scratch/net_probe.ps1' -Plan 31087", "u1"),
          rec_out("u1", "31087 axes in plan: 80,103 and 80,124 listed"),
          rec_run("pwsh -File 'C:/scratch/net_probe.ps1' -Outside", "u2"),
          rec_out("u2", "nothing listed outside the plan")]

ok_refuse = bool(g.verdict(BLIND, NIL))
ok_allow = not g.verdict(TESTED, NIL)
print("known answer, must REFUSE :", "REFUSED" if ok_refuse else "ALLOWED  <-- WRONG")
print("known answer, must ALLOW  :", "ALLOWED" if ok_allow else "REFUSED  <-- WRONG")
if not (ok_refuse and ok_allow):
    print("\nself-test FAILED - totals below are not reportable")
    sys.exit(1)
print()

root = sys.argv[1]
cap = int(sys.argv[2]) if len(sys.argv) > 2 else 10 ** 6
files = sorted(f for f in os.listdir(root) if f.endswith(".jsonl"))[:cap]

tot_recs = tot_texts = tot_nils = tot_refused = 0
rows = []
for fn in files:
    recs = []
    with open(os.path.join(root, fn), encoding="utf-8", errors="replace") as fh:
        for line in fh:
            try:
                recs.append(json.loads(line))
            except Exception:
                continue
    texts = nils = refused = 0
    examples = []
    for i, rec in enumerate(recs):
        if (rec.get("message") or {}).get("role") != "assistant":
            continue
        text = g.text_of(rec)
        if not text.strip():
            continue
        texts += 1
        if not g.NIL_CLAIM.search(text):
            continue
        nils += 1
        why = g.verdict(recs[:i + 1], text)
        if why:
            refused += 1
            if len(examples) < 3:
                examples.append((i, " ".join(text.split())[:120]))
    tot_recs += len(recs)
    tot_texts += texts
    tot_nils += nils
    tot_refused += refused
    rows.append((fn, len(recs), texts, nils, refused, examples))
    print("  %-40s recs %-7d texts %-5d nils %-4d refused %d" % (fn[:40], len(recs), texts, nils, refused))

print()
print("TOTALS over %d of %d transcripts in the folder" % (len(files), len(
    [f for f in os.listdir(root) if f.endswith(".jsonl")])))
print("  records                 :", tot_recs)
print("  assistant texts         :", tot_texts)
print("  carrying a nil          :", tot_nils)
print("  REFUSED                 : %d of %d nil claims" % (tot_refused, tot_nils))
print()
for fn, _, _, _, refused, examples in rows:
    for i, snippet in examples:
        print("  %s @%d  %s" % (fn[:28], i, snippet))
