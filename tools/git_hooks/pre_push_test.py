"""Tests for pre_push.py against REAL history of this clone (no fixtures written by the hand that wrote the hook):
the 22:1x five-commit stack b94b8d33..8f58e04c from three sessions. Run: python -I tools/git_hooks/pre_push_test.py
Known-bad first: the stack pushed by any one session must be refused.
"""
import io, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pre_push as h

BASE = h.git("rev-parse", "b94b8d33^").strip()       # origin/develop before the stack
TOP = h.git("rev-parse", "8f58e04c").strip()
B94 = h.git("rev-parse", "b94b8d33").strip()
LEAD = "session_01GN818JKgmCvR6gvS4n3qrc"             # this session (Virtual Drafter)
PDF = h.owner(B94)                                    # the PDF Ingestor, read from its own trailer
line = lambda local, remote: io.StringIO(f"refs/heads/develop {local} refs/heads/develop {remote}\n")
fails = 0
def check(name, got, want):
    global fails
    ok = got == want; fails += not ok
    print(("PASS" if ok else "FAIL") + f"  {name} (exit {got}, want {want})")

assert PDF and PDF.startswith("session_") and PDF != LEAD, f"b94b8d33's trailer did not read: {PDF}"
check("refused: the lead pushing the 5-commit stack it owns none of", h.main(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}), 1)
check("refused: the PDF Ingestor pushing the whole stack (3 sessions)", h.main(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": PDF}), 1)
check("allowed: the PDF Ingestor pushing only its own b94b8d33", h.main(line(B94, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": PDF}), 0)
stack = ",".join(h.git("rev-list", f"{BASE}..{TOP}").split())
check("allowed: the lead with every owner's OK named in KOR_PUSH_CARRY_OK", h.main(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD, "KOR_PUSH_CARRY_OK": stack}), 0)
one_short = ",".join(h.git("rev-list", f"{BASE}..{TOP}").split()[1:])
check("refused: the override naming 4 of the 5", h.main(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD, "KOR_PUSH_CARRY_OK": one_short}), 1)
check("allowed: a push from outside a Claude session (Ian)", h.main(line(TOP, BASE), {}), 0)
check("allowed: a branch delete", h.main(line("0" * 40, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}), 0)

# 28 Oct 14:1x: the remote moved after the last fetch, so its tip was not in this clone and rev-list exited 128 -- the
# hook threw a traceback. It must refuse cleanly and say "fetch". (A sha no clone can hold: all f's.)
missing = "f" * 40
assert subprocess.run(["git", "cat-file", "-e", f"{missing}^{{commit}}"], capture_output=True).returncode != 0, "the 'missing' sha exists?"
err = io.StringIO(); real, sys.stderr = sys.stderr, err
try:
    try: got = h.main(line(TOP, missing), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD})
    except Exception as e: got = f"raised {type(e).__name__}"
finally: sys.stderr = real
check("refused, no traceback: the remote's tip is not in this clone", got, 1)
check("  and the message says to fetch", "git fetch" in err.getvalue() and missing[:8] in err.getvalue(), True)
total = 9
print(f"{total - fails} of {total} passed")
sys.exit(1 if fails else 0)
