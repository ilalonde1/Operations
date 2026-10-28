"""Tests for claude_grep_sample_guard.py. Run: python -I tools/claude_grep_sample_guard_test.py

Known-bad first: the real 2026-10-27 result that hid NetworkOpsStore.Prompts.cs (oversight.Mistake 76) must be
STAMPED, with its own numbers; then results that were not cut must stay SILENT; then the hook's output shape.
"""
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from claude_grep_sample_guard import stamp  # noqa: E402

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_grep_sample_guard.py")


def grep(response, **inp):
    return {"tool_name": "Grep", "tool_input": inp, "tool_response": response}


# The real tool_response of the miss, from transcript 32f94af3 (content abbreviated; the counts are what it said).
MISS = grep({"mode": "content", "numFiles": 0, "filenames": [], "numLines": 15, "totalLines": 65, "appliedLimit": 15,
             "content": "Kor.Operations.NetworkOps.Service\\Store\\NetworkOpsStore.cs:59: ... retire ..."},
            pattern="(?i)amend|retire|supersed", path=r"C:\x\Kor.Operations.NetworkOps.Service\Store",
            output_mode="content", head_limit=15)

failures = 0


def check(name, ok):
    global failures
    print(("PASS " if ok else "FAIL ") + name)
    failures += 0 if ok else 1


# --- STAMPED
note = stamp(MISS)
check("the 10-27 miss is stamped", note is not None)
check("the stamp gives shown and whole: 15 of 65 lines", note is not None and "showing 15 lines of 65 lines" in note)
check("the stamp names the absence words", note is not None and "there is no" in note)
check("the default limit cuts too (250 of 392, no head_limit given)",
      stamp(grep({"mode": "content", "numLines": 250, "totalLines": 392, "appliedLimit": 250, "content": "x"},
                 pattern="try")) is not None)
check("the same miss as the text the model saw (pagination line) is stamped",
      stamp({"tool_name": "Grep", "tool_input": MISS["tool_input"],
             "tool_response": "...RetiredUtc IS NULL;\");\n\n[Showing results with pagination = limit: 15]"}) is not None)
check("text with no pagination line is silent",
      stamp({"tool_name": "Grep", "tool_input": {}, "tool_response": "a.cs:1: x\nb.cs:2: y"}) is None)
check("a files cut the model was NOT told about (40 of 41, no pagination line; dry run 10-27) names 40 of 41",
      "showing 40 files of 41 files" in (stamp(grep({"mode": "files_with_matches", "numFiles": 40, "totalFiles": 41,
                                                     "appliedLimit": 40, "filenames": ["a"] * 40},
                                                    pattern="Mesh", head_limit=40)) or ""))
check("a files_with_matches result cut by the limit is stamped",
      stamp(grep({"mode": "files_with_matches", "numFiles": 20, "filenames": ["a"] * 20, "appliedLimit": 20},
                 pattern="x", head_limit=20)) is not None)

# --- SILENT
check("a complete content result (no appliedLimit) is silent",
      stamp(grep({"mode": "content", "numLines": 40, "totalLines": 40, "content": "x"}, pattern="x")) is None)
check("a complete files_with_matches result is silent",
      stamp(grep({"mode": "files_with_matches", "numFiles": 2, "filenames": ["a", "b"]}, pattern="x")) is None)
check("limit reached exactly (shown == total) is silent",
      stamp(grep({"mode": "content", "numLines": 15, "totalLines": 15, "appliedLimit": 15, "content": "x"},
                 pattern="x", head_limit=15)) is None)
check("a Bash payload is not this guard's business",
      stamp({"tool_name": "Bash", "tool_input": {"command": "grep x | head -5"}, "tool_response": {}}) is None)
check("a missing tool_response is silent", stamp({"tool_name": "Grep", "tool_input": {}}) is None)

# --- the hook end to end: PostToolUse additionalContext on stdout, nothing when silent
p = subprocess.run([sys.executable, "-I", GUARD], input=json.dumps(MISS), capture_output=True, text=True)
out = json.loads(p.stdout) if p.stdout.strip() else {}
check("hook prints PostToolUse additionalContext for the miss",
      out.get("hookSpecificOutput", {}).get("hookEventName") == "PostToolUse"
      and "15 lines of 65 lines" in out["hookSpecificOutput"].get("additionalContext", ""))
p = subprocess.run([sys.executable, "-I", GUARD], input=json.dumps(grep({"mode": "content", "numLines": 3, "content": "x"})),
                   capture_output=True, text=True)
check("hook prints nothing for a complete result", p.stdout.strip() == "" and p.returncode == 0)
p = subprocess.run([sys.executable, "-I", GUARD], input="not json", capture_output=True, text=True)
check("hook survives a bad payload", p.stdout.strip() == "" and p.returncode == 0)

TOTAL = 16
print(f"\n{TOTAL - failures} of {TOTAL} passed")
sys.exit(1 if failures else 0)
