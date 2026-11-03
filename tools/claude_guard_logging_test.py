r"""F11 (Framework row 1159): every guard behaves EXACTLY as before when its refusal log fails.
Run: python -I tools/claude_guard_logging_test.py

Each of the seven Operations guards wired to Framework tools/guard_log.py (bash, api, prior_art, sql_schema, look,
instrument, absence) is run as a real subprocess on a payload it refuses, three ways:
  (a) fixture  - no hook_event_name, so guard_log writes nothing (the guards' own suites run this way);
  (b) live, bad DB - hook_event_name set, KOR_OPPORTUNITIES_OPPORTUNITIESDB pointing at a host that does not exist;
  (c) live, raising - hook_event_name set, a fake guard_log first on PYTHONPATH whose record() raises.
stdout and exit code must be byte-identical across (a), (b) and (c), and (a) must be a refusal. A guard whose logging
call were not behind its try/except fails (c). Added after review row 58: no guard's own suite sends hook_event_name,
so the logging path ran in none of them.
Does NOT cover: the two Framework guards (claude_board_guard, pre_push_review - Framework tools/guard_log_test.py and
pre_push_review_test.py); a live run that SUCCEEDS writing (guard_log_test.py does the round trip); a DB that hangs
past 3 s (guard_log kills sqlcmd at 3 s; (b) times out at the login instead).
"""
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
RUN = os.urandom(4).hex()
FAILS, RAN = [], []
BAD = {"KOR_OPPORTUNITIES_OPPORTUNITIESDB": "Server=zz-no-such-host;Database=x;User ID=u;Password=p"}


def check(name, ok, detail=""):
    RAN.append(name)
    print(("PASS " if ok else "FAIL ") + name + (f" - {detail}" if detail and not ok else ""))
    if not ok:
        FAILS.append(name)


tmp = tempfile.mkdtemp(prefix="zz-guardlogging-")
with open(os.path.join(tmp, "guard_log.py"), "w", encoding="utf-8") as f:
    f.write("def record(*a, **k):\n    raise RuntimeError('a guard_log that raises')\n")


def transcript(name, records):
    path = os.path.join(tmp, name + ".jsonl")
    with open(path, "w", encoding="utf-8") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")
    return path


def say(text):
    return {"type": "assistant", "message": {"role": "assistant", "content": [{"type": "text", "text": text}]}}


def call(name, inp, use_id):
    return {"type": "assistant", "message": {"role": "assistant", "content": [
        {"type": "tool_use", "id": use_id, "name": name, "input": inp}]}}


def result(use_id, text):
    return {"type": "user", "message": {"role": "user", "content": [
        {"type": "tool_result", "tool_use_id": use_id, "content": text}]}}


SCAN = os.path.join(tmp, "scan.py")
CASES = [
    ("bash", "PreToolUse", {"tool_name": "Bash", "tool_input": {"command": "git reset --hard"}}),
    ("api", "PreToolUse", {"tool_name": "Bash", "tool_input": {"command": "curl https://api." + "anthropic.com/v1/x"}}),
    ("prior_art", "PreToolUse", {"tool_name": "Write", "tool_input": {
        "file_path": "C:/VIsual Studio Projects/Operations/docs/zz-" + RUN + "-quixotic-zebra.md", "content": "x"}}),
    ("sql_schema", "PreToolUse", {"tool_name": "Bash", "tool_input": {"command": "INSERT INTO dbo.zzNeverInspected" + RUN + " (a) VALUES (1)"}}),
    ("absence", "Stop", {"transcript_path": transcript("absence", [say("There is no such table.")])}),
    ("look", "Stop", {"transcript_path": transcript("look", [
        call("Bash", {"command": "git push fs01 main"}, "u1"), result("u1", "ok"), say("Pushed - done.")])}),
    ("instrument", "Stop", {"transcript_path": transcript("instrument", [
        call("Write", {"file_path": SCAN}, "w1"), call("Bash", {"command": "python scan.py"}, "u1"),
        result("u1", "scanned 62 files\nresult: 0 found"),
        say("The scan came back clean: 0 of 62 transcripts deleted it, so nothing was deleted.")])}),
]


def run_guard(name, payload, env):
    r = subprocess.run([sys.executable, os.path.join(HERE, f"claude_{name}_guard.py")], input=json.dumps(payload),
                       capture_output=True, text=True, timeout=60, env=env)
    return r.returncode, r.stdout


for name, event, base in CASES:
    base = dict(base, session_id=f"zz-guardlogging-{RUN}-{name}")
    fixture = run_guard(name, base, dict(os.environ))
    live_bad = run_guard(name, dict(base, hook_event_name=event), dict(os.environ, **BAD))
    live_raise = run_guard(name, dict(base, hook_event_name=event), dict(os.environ, PYTHONPATH=tmp))
    try:
        out = json.loads(fixture[1])
        decision = (out.get("hookSpecificOutput") or {}).get("permissionDecision") or out.get("decision")
    except ValueError:
        decision = None
    check(f"{name}: refuses the fixture (exit 0, a deny/block decision)",
          fixture[0] == 0 and decision == ("deny" if event == "PreToolUse" else "block"), repr(fixture))
    check(f"{name}: bad DB -> same exit and stdout", live_bad == fixture, f"{live_bad!r} vs {fixture!r}")
    check(f"{name}: raising guard_log -> same exit and stdout", live_raise == fixture, f"{live_raise!r} vs {fixture!r}")

print(f"{len(RAN) - len(FAILS)} of {len(RAN)} passed")
sys.exit(1 if FAILS else 0)
