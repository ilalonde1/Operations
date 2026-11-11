"""Tests for claude_bash_sample_guard.py (row 2010). Run: python -I tools/claude_bash_sample_guard_test.py

Each case is a PostToolUse payload (command + the stdout it printed) run through the hook as Claude Code runs it.
NOTICE: the cut may have dropped lines, so the sample notice must be printed. SILENT: it must not be."""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_sample_guard.py")


def lines(n):
    return "\n".join(f"line {i}" for i in range(1, n + 1))


NOTICE = [
    # the paid-for case (2026-09-01): five lines shown, the population was 37
    ('grep -oE "implied thickness [0-9.]+ in" report.txt | head -5', lines(5)),
    ("git log --oneline | head", lines(10)),                    # bare head keeps 10 and printed 10: maybe cut
    ("dotnet test x 2>&1 | tail -n 3", lines(3)),
    ("grep -n x a.py | head -20 | cut -c1-100", lines(4)),      # a later stage could filter: not provable
    ("grep -n x a.py | head -3 > top.txt; echo ok", "ok"),      # the cut's lines went to a file
    ("sed -n 1,80p tools/guard_log.py", lines(80)),
    ("sed -n 40,80p tools/guard_log.py", lines(10)),            # a window not from line 1 dropped lines 1-39
    ("Get-ChildItem | Select-Object -First 5", lines(5)),
    ("grep -m 3 x a.txt", lines(3)),
    ("tail -n +2 x.csv", lines(2)),                             # the limit cannot be read: not provable
    # review row 1419: the cut's lines are not what got printed
    ("for f in $(ls | head -5); do grep -n x $f; done", lines(2)),
    ("x=$(git log --oneline | head -3); echo $x", lines(1)),
    ("n=`ls | head -5`; echo $n", lines(1)),
    ("git log --oneline | head -5 >&2", lines(0)),
    ("git log --oneline | head -5 &> f.txt", lines(0)),
]
SILENT = [
    # row 2010: the cut kept more lines than were printed, so it dropped nothing
    ("git log -1 --format=%H | head -1", lines(0)),
    ('python -I tools/board_render.py --text 2>&1 | head -5', lines(3)),
    ("grep -n x e2k_columns.py | head -20", lines(10)),
    ("grep -n a f.py | head; grep -n b g.py", lines(5)),
    ("sed -n 1,80p short.py", lines(40)),
    ("Get-ChildItem | Select-Object -First 5", lines(2)),
    ("dotnet test x 2>&1 | head -50", lines(12)),                 # 2>&1 before the cut is not a redirect after it
    # unchanged: a command that counts its population
    ("grep -c x f | head -5", lines(5)),
    ("ls | wc -l", lines(1)),
    ("echo hi", lines(1)),
]


def run(cmd, out):
    payload = {"hook_event_name": "PostToolUse", "tool_name": "Bash", "tool_input": {"command": cmd},
               "tool_response": {"stdout": out, "stderr": "", "interrupted": False}}
    return subprocess.run([sys.executable, "-I", GUARD], input=json.dumps(payload), capture_output=True, text=True,
                          timeout=20).stdout


def main():
    failures = 0
    for cmd, out in NOTICE:
        if "TRUNCATED OUTPUT" not in run(cmd, out):
            print("NO NOTICE:", cmd); failures += 1
    for cmd, out in SILENT:
        if run(cmd, out).strip():
            print("WRONG NOTICE:", cmd); failures += 1
    # no tool_response (an older payload): every cut is a notice, as before row 2010
    if "TRUNCATED OUTPUT" not in subprocess.run([sys.executable, "-I", GUARD], input=json.dumps(
            {"tool_input": {"command": "git log | head -1"}}), capture_output=True, text=True, timeout=20).stdout:
        print("NO NOTICE without tool_response"); failures += 1
    total = len(NOTICE) + len(SILENT) + 1
    print(f"{total - failures} of {total} checks passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
