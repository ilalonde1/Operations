"""Self-test for claude_instrument_guard.py. Known-positives AND known-negatives: a guard about
testing instruments that was never tested on a case it must refuse, and one it must allow, would
be its own first instance.

Cases 9-11 came from Virtual Drafter's dry run over a 73,337-record transcript, which found the
first version allowing a nil because the words "self-test" appeared in unrelated output after a
script edit. Evidence now counts only in the OUTPUT of a run of the same script.
"""
import json
import os
import subprocess
import sys
import tempfile

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_instrument_guard.py")


def rec_write(path):
    return {"message": {"role": "assistant", "content": [
        {"type": "tool_use", "name": "Write", "input": {"file_path": path}}]}}


def rec_run(cmd, use_id):
    return {"message": {"role": "assistant", "content": [
        {"type": "tool_use", "id": use_id, "name": "Bash", "input": {"command": cmd}}]}}


def rec_out(use_id, text):
    return {"message": {"role": "user", "content": [
        {"type": "tool_result", "tool_use_id": use_id, "content": text}]}}


def rec_text(role, text):
    return {"message": {"role": role, "content": [{"type": "text", "text": text}]}}


def run(records, payload_extra):
    fd, tp = tempfile.mkstemp(suffix=".jsonl")
    with os.fdopen(fd, "w", encoding="utf-8") as fh:
        for r in records:
            fh.write(json.dumps(r) + "\n")
    payload = {"transcript_path": tp}
    payload.update(payload_extra)
    p = subprocess.run([sys.executable, "-I", GUARD], input=json.dumps(payload),
                       capture_output=True, text=True)
    os.unlink(tp)
    return p.stdout.strip()


NIL = "The scan came back clean: 0 of 62 transcripts deleted it, so nothing was deleted."
OK_CLAIM = "The scan found 3 of 62 transcripts with a deletion."
SCAN = r"C:\x\scan.py"
EVIDENCE = "self-test passed: 2 of 2 known-positive samples detected\n\nresult: 0 found"
NO_EVIDENCE = "scanned 62 files\nresult: 0 found"

CASES = [
    ("POSITIVE: nil claim, script edited and run, output carries no known answer",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: the same script's own run output carries the known answer",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", EVIDENCE),
      rec_text("assistant", NIL)], {}, False),

    ("NEGATIVE: nil claim, but no script written this session",
     [rec_text("assistant", NIL)], {}, False),

    ("NEGATIVE: script edited and run, but the claim is not nil",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_text("assistant", OK_CLAIM)], {}, False),

    ("POSITIVE: the self-test ran BEFORE the last edit, so it does not count",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", EVIDENCE),
      rec_write(SCAN), rec_run("python scan.py", "u2"), rec_out("u2", NO_EVIDENCE),
      rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: stop_hook_active must never loop",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_text("assistant", NIL)], {"stop_hook_active": True}, False),

    ("POSITIVE: SendMessage carrying an untested nil is denied",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE)],
     {"tool_name": "SendMessage", "tool_input": {"message": NIL}}, True),

    ("NEGATIVE: a Write that is not a script is not an instrument",
     [rec_write(r"C:\x\notes.md"), rec_text("assistant", NIL)], {}, False),

    # --- Virtual Drafter's dry-run findings ---
    ("POSITIVE: VD - evidence words in SOMEONE ELSE'S output must not count",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_run("python other_tool.py", "u2"), rec_out("u2", "self-test: 8 of 8"),
      rec_text("assistant", NIL)], {}, True),

    ("POSITIVE: VD - the claim text saying 'self-test' is no longer an escape",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_text("assistant", NIL + " (self-test)")], {}, True),

    ("NEGATIVE: a script written but NEVER run cannot have produced the nil",
     [rec_write(SCAN), rec_text("assistant", NIL)], {}, False),
]

passed = 0
for name, records, extra, must_block in CASES:
    out = run(records, extra)
    blocked = bool(out) and ("block" in out or "deny" in out)
    ok = blocked == must_block
    passed += ok
    print(("  OK   " if ok else "  FAIL ") + name + ("   -> blocked" if blocked else "   -> allowed"))

print()
print("self-test: {0} of {1}".format(passed, len(CASES)))
sys.exit(0 if passed == len(CASES) else 1)
