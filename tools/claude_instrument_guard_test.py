"""Self-test for claude_instrument_guard.py. Known-positives AND known-negatives: a guard about
testing instruments that was never tested on a case it must refuse, and one it must allow, would
be its own first instance."""
import json
import os
import subprocess
import sys
import tempfile

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_instrument_guard.py")


def rec_write(path):
    return {"message": {"role": "assistant", "content": [
        {"type": "tool_use", "name": "Write", "input": {"file_path": path}}]}}


def rec_text(role, text):
    return {"message": {"role": role, "content": [{"type": "text", "text": text}]}}


def rec_result(text):
    return {"message": {"role": "user", "content": [{"type": "tool_result", "content": text}]}}


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

CASES = [
    # (name, records, payload, must_block)
    ("POSITIVE: nil claim, script written, no self-test",
     [rec_write(r"C:\x\scan.py"), rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: nil claim, script written, self-test ran after the edit",
     [rec_write(r"C:\x\scan.py"),
      rec_result("self-test passed: 2 of 2 known-positive samples detected"),
      rec_text("assistant", NIL)], {}, False),

    ("NEGATIVE: nil claim, but no script written this session",
     [rec_text("assistant", NIL)], {}, False),

    ("NEGATIVE: script written, but the claim is not nil",
     [rec_write(r"C:\x\scan.py"), rec_text("assistant", OK_CLAIM)], {}, False),

    ("POSITIVE: a self-test BEFORE the edit does not count",
     [rec_result("self-test passed"), rec_write(r"C:\x\scan.py"),
      rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: stop_hook_active must never loop",
     [rec_write(r"C:\x\scan.py"), rec_text("assistant", NIL)],
     {"stop_hook_active": True}, False),

    ("POSITIVE: SendMessage carrying a nil claim is denied",
     [rec_write(r"C:\x\scan.py")],
     {"tool_name": "SendMessage", "tool_input": {"message": NIL}}, True),

    ("NEGATIVE: a Write that is not a script is not an instrument",
     [rec_write(r"C:\x\notes.md"), rec_text("assistant", NIL)], {}, False),
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
