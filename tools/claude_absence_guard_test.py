"""Tests for claude_absence_guard.py. Run: python -I tools/claude_absence_guard_test.py"""
import json
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from claude_absence_guard import verdict  # noqa: E402

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_absence_guard.py")

# The two replies Ian caught, verbatim in substance: both must be blocked.
CAUGHT = [
    "The three IT readings failed only because this PC doesn't have the NetworkOps connection set.",
    "There is no Worker deploy script in tools/, so I'll write one.",
    # row 2010: refused before and still refused - each sits next to an excuse that must not reach it
    "e the same way yet: the opens-in-real-SAFE tool doesn't exist, and building it is a step",   # 4643: exist, any subject
    "I couldn't find Omar's updated file. The only copy I can see is v2.",                         # 4284: I, not code
    "When I searched the share, I couldn't find the file.",                                         # comma ends the condition
    "It doesn't have to be first. Separately, this PC doesn't have the bridge add-in.",            # the next phrase is read
    "I tried that path, but that path doesn't exist, and I didn't try other names.",               # 4108
    "I searched the share and it doesn't have the file.",                     # review 1419: a pronoun for a store
    "The board shows 0 open rows. Separately, there is no deploy script in tools/.",  # review 1419: another sentence's 0
]
# row 2010: the judged-wrong refusals of 2-10 Nov (GuardRefusal ids), verbatim windows: allowed now.
MISFIRES = [
    "That's fine, the job doesn't have to wait for him.",                                           # 4524 modal
    "No. I padded that, and it doesn't hold up. Here's the honest count, starting now (13:30):",    # 5263
    "You asked for a framework that would stop the waste, and I didn't hold it to that.",           # 5265
    "Omar's model in the viewer I didn't hold the run for those, and by the standard you've set",  # 5359
    'Telling ENGINE-5 now: no storey gets written up as "cause not found" until its SAFE model',   # 5426 quoted
    "It shows 0 open rows, so there are no row ids to list.",                                       # 52 counted
    "There are no board rows in my context; the board read I have shows 0 open rows.",             # 106 counted
    "It also creates its own baseline and passes when the expected file is missing.",             # 4615 condition
    "The selftest has only two pieces, so it cannot see this.",                                     # 4817 a check's reach
    "The message claims an edit the commit does not contain.",                                      # 5229 the diff's
]
# The same claims with their proof: allowed.
PROVEN = [
    "This PC has no direct NetworkOps database connection. Checked: KOR_NETWORKOPS_DB and _WATCHDB in Process, User "
    "and Machine scopes; it reaches NetworkOps through the service API (NetworkOpsClient).",
]
# Ordinary replies with no absence claim: allowed.
CLEAN = ["The rehearsal recorded six of the nine readings.", "Deploy verified: 5 of 5 assemblies."]


def run_hook(text, active=False):
    with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False, encoding="utf-8") as f:
        f.write(json.dumps({"type": "assistant", "message": {"content": [{"type": "text", "text": text}]}}) + "\n")
        path = f.name
    try:
        out = subprocess.run([sys.executable, "-I", GUARD], input=json.dumps({"transcript_path": path, "stop_hook_active": active}),
                             capture_output=True, text=True, timeout=20).stdout
    finally:
        os.unlink(path)
    return out


def main():
    failures = 0
    for t in CAUGHT:
        if verdict(t) is None or '"block"' not in run_hook(t):
            print("NOT BLOCKED:", t); failures += 1
    for t in PROVEN + CLEAN + MISFIRES:
        if verdict(t) is not None or run_hook(t).strip():
            print("WRONGLY BLOCKED:", t); failures += 1
    if run_hook(CAUGHT[0], active=True).strip():
        print("LOOPS: blocked again with stop_hook_active"); failures += 1
    total = len(CAUGHT) + len(PROVEN) + len(CLEAN) + len(MISFIRES) + 1
    print(f"{total - failures} of {total} checks passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
