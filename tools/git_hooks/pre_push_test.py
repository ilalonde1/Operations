r"""Tests for pre_push.py against REAL history of this clone (no fixtures written by the hand that wrote the hook):
the 22:1x five-commit stack b94b8d33..8f58e04c from three sessions. Run: python -I tools/git_hooks/pre_push_test.py
Known-bad first: the stack pushed by any one session must be refused.
Gap 1 phase 2 (18 Nov): exceptions come only from the exceptions file (a temp file here, never the real
C:\KOR-Operations one) - the listed stack passes, 4 of 5 listed is refused, KOR_PUSH_CARRY_OK in the environment carries
nothing, a missing file means none, UTF-16 and junk lines are handled.
Does NOT cover: the file's ACL (Ian's lock is read back by hand), a real `git push` through the shim.
"""
import io, os, subprocess, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pre_push as h

# Every case reads this path unless it names a file: it does not exist, so the real exceptions file never leaks in.
NOFILE = os.path.join(tempfile.mkdtemp(prefix="pp-exc-"), "push-exceptions.txt")
assert not os.path.exists(NOFILE)
def exc_file(text, encoding="utf-8"):
    path = os.path.join(tempfile.mkdtemp(prefix="pp-exc-"), "push-exceptions.txt")
    with open(path, "w", encoding=encoding) as f: f.write(text)
    return path
def run(stdin, env, exceptions_file=NOFILE):
    return h.main(stdin, env, exceptions_file=exceptions_file)

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
check("refused: the lead pushing the 5-commit stack it owns none of", run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}), 1)
check("refused: the PDF Ingestor pushing the whole stack (3 sessions)", run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": PDF}), 1)
check("allowed: the PDF Ingestor pushing only its own b94b8d33", run(line(B94, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": PDF}), 0)
shas = h.git("rev-list", f"{BASE}..{TOP}").split()
check("allowed: the lead with every owner's OK listed in the exceptions file (comments, short and full shas)",
      run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD},
          exc_file("# Ian 18 Nov: the 22:1x stack\n" + "\n".join(s[:8] if i % 2 else s.upper() + "  # ok" for i, s in enumerate(shas)) + "\n")), 0)
check("allowed: the same list written as UTF-16 (Windows PowerShell's >)",
      run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}, exc_file("\n".join(shas) + "\n", encoding="utf-16")), 0)
check("refused: the exceptions file listing 4 of the 5", run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}, exc_file("\n".join(shas[1:]) + "\n")), 1)
check("refused: KOR_PUSH_CARRY_OK in the environment naming all 5 carries nothing",
      run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD, "KOR_PUSH_CARRY_OK": ",".join(shas)}), 1)
check("refused: a missing exceptions file means no exceptions", run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}, NOFILE), 1)
check("refused: short, commented-out and non-hex lines carry nothing",
      run(line(TOP, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD},
          exc_file("".join(f"{s[:6]}\n# {s}\n{s[:10]}x\n" for s in shas))), 1)
check("allowed: a push from outside a Claude session (Ian)", run(line(TOP, BASE), {}), 0)
check("allowed: a branch delete", run(line("0" * 40, BASE), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD}), 0)

# 28 Oct 14:1x: the remote moved after the last fetch, so its tip was not in this clone and rev-list exited 128 -- the
# hook threw a traceback. It must refuse cleanly and say "fetch". (A sha no clone can hold: all f's.)
missing = "f" * 40
assert subprocess.run(["git", "cat-file", "-e", f"{missing}^{{commit}}"], capture_output=True).returncode != 0, "the 'missing' sha exists?"
err = io.StringIO(); real, sys.stderr = sys.stderr, err
try:
    try: got = run(line(TOP, missing), {"CLAUDE_CODE_BRIDGE_SESSION_ID": LEAD})
    except Exception as e: got = f"raised {type(e).__name__}"
finally: sys.stderr = real
check("refused, no traceback: the remote's tip is not in this clone", got, 1)
check("  and the message says to fetch", "git fetch" in err.getvalue() and missing[:8] in err.getvalue(), True)
total = 13
print(f"{total - fails} of {total} passed")
sys.exit(1 if fails else 0)
