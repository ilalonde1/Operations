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

# The PDF Ingestor's actual instrument, commands and output on 2026-10-27, as Virtual Drafter
# reported them. Nothing here is phrased to suit the guard's vocabulary; that is the point.
PROBE = r"C:\scratch\net_probe.ps1"
PROBE_RUN = "pwsh -File 'C:/scratch/net_probe.ps1' -Plan 31087"
PROBE_MATCH = "31087 axes in plan: 80,103 and 80,124 listed"
PROBE_NOMATCH = "nothing listed outside the plan"
PROBE_CLAIM = ("Probe result: no axes were found outside the plan; tasklist shows 0 of 6 PIDs "
               "left and the network-check returned none found.")

# Verbatim from this session's transcript, record 2636 - the icon comparison sheet.
FINAL = r"C:\scratch\final.py"
FINAL_OUT = "145 buttons: certain 50 | likely 27 | no match found 68\nwrote icons-vs-legacy.png"

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

    # --- The PDF Ingestor's three wrong refusals, 2026-10-27 18:20-18:29, reported by Virtual
    # Drafter. Its net_probe.ps1 WAS tested on a must-match and a must-not; the guard refused
    # anyway. These are its real commands and its real output, not phrasing written to suit the
    # guard's own regex - which is how the first version came to pass 11 of 11 while being wrong.
    ("NEGATIVE: VD/Ingestor - a must-match and a must-not pair IS a known-answer test, in the "
     "instrument's own words rather than the guard's",
     [rec_write(PROBE),
      rec_run(PROBE_RUN, "u1"), rec_out("u1", PROBE_MATCH),
      rec_run(PROBE_RUN + " -Outside", "u2"), rec_out("u2", PROBE_NOMATCH),
      rec_text("assistant", PROBE_CLAIM)], {}, False),

    ("NEGATIVE: VD/Ingestor - an indirect invocation (& $p) cannot be attributed, so it must not "
     "block: the guard cannot tell whether the instrument ran",
     [rec_write(PROBE), rec_run("& $p -Plan 31087", "u1"), rec_out("u1", PROBE_MATCH),
      rec_text("assistant", PROBE_CLAIM)], {}, False),

    ("NEGATIVE: VD/Ingestor - nils produced by OTHER tools are not the instrument's nils",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_run("tasklist", "u2"), rec_out("u2", "0 of 6 PIDs left"),
      rec_text("assistant", "tasklist shows 0 of 6 PIDs left.")], {}, False),

    ("POSITIVE: two runs after the last edit, BOTH returning nothing, is still an untested "
     "instrument - the unblocking must not be a switch-off",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", NO_EVIDENCE),
      rec_run("python scan.py --all", "u2"), rec_out("u2", "scanned 14 files\nresult: 0 found"),
      rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: an instrument that has returned a real finding has been shown to work, whatever "
     "words its output uses",
     [rec_write(SCAN), rec_run("python scan.py", "u1"),
      rec_out("u1", "3 deletions at lines 41, 88, 203"),
      rec_text("assistant", NIL)], {}, False),

    # From the dry run over this session's own 15,558-record transcript - the only message the fixed
    # guard refused, and it was a FALSE refusal. final.py's output reports 77 matches; the words "no
    # match found" sit in the same line as the count of the rest. A nil marker in PART of an output
    # does not make the output nil.
    ("NEGATIVE: real transcript - an output that reports findings AND a nil for the remainder has "
     "demonstrated the instrument",
     [rec_write(FINAL), rec_run("python final.py", "u1"), rec_out("u1", FINAL_OUT),
      rec_text("assistant", "no equivalent found in his 312-file library for 68 of the 145")],
     {}, False),

    ("POSITIVE: and the counter-case it must still catch - the only nonzero number sizes the input, "
     "every result in it is zero",
     [rec_write(SCAN), rec_run("python scan.py", "u1"),
      rec_out("u1", "scanned 62 files\nresult: 0 found"),
      rec_text("assistant", NIL)], {}, True),

    ("NEGATIVE: an output this guard cannot read is not an output that found nothing",
     [rec_write(SCAN), rec_run("python scan.py", "u1"), rec_out("u1", "done; see the log"),
      rec_text("assistant", NIL)], {}, False),
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
