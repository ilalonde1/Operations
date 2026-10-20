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
    for t in PROVEN + CLEAN:
        if verdict(t) is not None or run_hook(t).strip():
            print("WRONGLY BLOCKED:", t); failures += 1
    if run_hook(CAUGHT[0], active=True).strip():
        print("LOOPS: blocked again with stop_hook_active"); failures += 1
    total = len(CAUGHT) + len(PROVEN) + len(CLEAN) + 1
    print(f"{total - failures} of {total} checks passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
