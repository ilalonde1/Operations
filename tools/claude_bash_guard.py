"""PreToolUse guard for Bash: block the mistakes this repo has actually paid for.

CLAUDE.md states every one of these in prose. Prose is read once at the start of a session
and then loses to momentum -- on 2026-08-25 all four were broken, each within minutes of the
rule being quoted out loud. A hook fires at the moment of the action, which is the only
moment that matters.

No jq: it is not installed on this machine, and a jq-based hook exits 0 silently, which
reads as "allowed" and protects nothing.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time

# Repo rule 7 in force here too: every pattern is a raw string.
RULES = [
    # 1. A RECURSIVE WALK OVER SMB. db/tools/verify_e2k_claims.ps1 says it in its own
    #    header: "Get-ChildItem -Recurse over SMB is unusably slow." Two 120s timeouts
    #    came from exactly this, against \\Kor-fs01 and \\KOR-302N, both returning nothing.
    (re.compile(r"(?:^|[;&|(]\s*)(?:find|ls\s+-\w*R)\s+['\"]?(?://|\\\\)"),
     "Recursive walk over SMB. CLAUDE.md rule 1: unusably slow -- it will time out and "
     "return nothing. List one directory at a time, or push the filter to the filesystem."),

    (re.compile(r"Get-ChildItem[^|;]*-Recurse[^|;]*(?://|\\\\)\w"),
     "Get-ChildItem -Recurse over a UNC path. CLAUDE.md rule 1: unusably slow. "
     "Target one directory, or filter at the filesystem."),

    # 2. DESTROYING UNCOMMITTED WORK. On 2026-08-25 a `git checkout --` wiped a parallel
    #    session's uncommitted module. There is no undo; it survived only because a
    #    transcript still held the edit. `git stash push` keeps it.
    (re.compile(r"git\s+(?:checkout|restore)\s+--(?:\s|$)"),
     "git checkout/restore -- discards uncommitted work with no undo, and another session "
     "may own it. Use: git stash push -- <paths>"),

    (re.compile(r"git\s+reset\s+--hard|git\s+clean\s+-\w*[dfx]"),
     "git reset --hard / git clean destroys uncommitted work with no undo. "
     "Use: git stash push"),

    # 3. A WINDOWS PATH OR REGEX THROUGH SHELL QUOTING. Repo rule 7. Three attempts on
    #    2026-08-25 produced "Invalid escape \P", a UNC collapsed to a single backslash,
    #    and \03 read as a control character -- each time silently.
    (re.compile(r"python3?\s+-c\b[^\n]*\\\\"),
     "Backslashes inside python -c. Repo rule 7: bash eats them and the path or regex "
     "silently changes. Write the script to a file with the Write tool and run that."),

    (re.compile(r"<<\s*['\"]?(?:EOF|PY|SH)\b[\s\S]*\\[A-Za-z]"),
     "A backslash-letter sequence inside a heredoc. Repo rule 7: this is how \\P, \\03 and "
     "a lost UNC prefix happened. Use the Write or Edit tool for a Windows path or regex."),

    # 4. KILLING A PROCESS BY NAME. Several Claude sessions run test suites on this box at once.
    #    On 2026-09-18 a session's pre-build `Get-Process testhost* | Stop-Process -Force` and
    #    `taskkill /F /IM testhost.exe` ended the PDF-intake suite six times (exit code -1, no
    #    dump, "Test host process crashed"); two hours went to crash hypotheses before the
    #    neighbours' transcripts named it. Kill only your own, by its command line.
    #    The verb must stand where a command stands (the start, or after ; & | ( or a newline), so a
    #    commit message or a comment that NAMES the mistake is not the mistake.
    (re.compile(r"(?i)(?:^|[;&|(\n]\s*)(?:Stop-Process|taskkill|pkill|killall)[^|;\n]*\b(?:testhost|dotnet|takeoff|vstest)\b"
                r"|(?:^|[;&|(\n]\s*)Get-Process\s+[^|;\n]*\b(?:testhost|dotnet|takeoff|vstest)[^|;\n]*\|\s*Stop-Process"),
     "A kill by process NAME (testhost/dotnet/takeoff/vstest). Other Claude sessions run suites on "
     "this machine; a name-wide kill ends theirs too (six runs lost on 2026-09-18). Kill only your own, "
     "by command line: Get-CimInstance Win32_Process -Filter \"Name='testhost.exe'\" | "
     "Where-Object { $_.CommandLine -like '*<your test project>*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"),
]

BUILD = re.compile(r"dotnet\s+(?:test|build)")
# a command whose path names a checkout other than Operations under "VIsual Studio Projects" (JoeBrain, KOR.Drafter, ...)
OTHER_REPO = re.compile(r"(?i)VIsual Studio Projects[\\/](?!Operations(?:[\\/\"' ]|$))[A-Za-z0-9_.-]+")

# 5. THE FULL APP SUITE FOR A ONE-AREA CHANGE. 3.5 minutes and the build lock per run. On
#    2026-10-13 it ran twice, 7 minutes, for a change confined to BdReports whose own classes
#    run in 30 s -- with feedback_run_the_targeted_tests_not_the_suite sitting in memory.
#    A `dotnet test` that names the App test project must carry --filter, unless the run is
#    declared deliberate with FULL_SUITE=1 (the pre-publish run).
APP_SUITE = re.compile(r"dotnet\s+test\b[^\n|;&]*(?:Kor\.Operations\.App\.Tests|Kor\.Transmittals\.App\.Tests)")
FILTERED = re.compile(r"--filter\b")
DELIBERATE_FULL = re.compile(r"\bFULL_SUITE=1\b")

# 6. CHATTY SQL OVER THE VPN. Every sqlcmd is a VPN round trip to KOR-APP01; on 2026-10-13
#    there were about fifteen, one at a time, and three hung for minutes -- with
#    feedback_run_on_the_target_never_chatty_over_vpn in memory. Three calls in ten minutes is
#    a dry-run, an apply and a check. The fourth is a batch that was not written.
#    A call is the tool in COMMAND position: the start of a command, after ; & | ( or a newline,
#    or as the script `python [-flags]` runs. On 2026-10-14 `cp oq.py oqwide.py ...` and
#    `cat oq.py` were counted as three calls and the session's first real query was denied as
#    the fourth -- the prior-art guard's own documented miss (a name mentioned in any command
#    satisfies the check), in this file.
SQL_CALL = re.compile(r'(?im)(?:^|[;&|(]|(?<![\w.])(?:python3?|py)(?:\.exe)?"?(?:\s+-\S+)*)\s*"?(?:[^\s"]*[/\\])?(?:sqlcmd|oq\.py|oqwide\.py)\b')
SQL_WINDOW_SECONDS = 600
SQL_MAX_CALLS = 3

# 7. GOING SILENT. "Say the duration before any step over a minute" was promised on
#    2026-10-13 and broken the same afternoon: a 3.5-minute suite ran with no word, then a
#    40-minute stretch. The one line Ian always sees is the command's description. A command
#    that asks for more than two minutes must open that line with its expected duration
#    ("~3 min: ..."), or it does not run.
LONG_TIMEOUT_MS = 120_000
DURATION_FIRST = re.compile(r"(?i)^\s*~?\s*\d+\s*(?:s|sec|secs|seconds|m|min|mins|minutes|h|hr|hours)\b")


def sql_calls_allowed(session_id, n_new, now=None):
    """Sliding-window count of SQL calls for this session, kept in a temp file. Returns
    (calls already in the window, allowed). A denied call is not recorded."""
    now = now if now is not None else time.time()
    path = os.path.join(tempfile.gettempdir(), f"claude_bash_guard_sql_{session_id or 'default'}.json")
    try:
        with open(path, encoding="utf-8") as f:
            stamps = [t for t in json.load(f) if now - t < SQL_WINDOW_SECONDS]
    except Exception:
        stamps = []
    if len(stamps) + n_new > SQL_MAX_CALLS:
        return len(stamps), False
    stamps.extend([now] * n_new)
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(stamps, f)
    except Exception:
        pass
    return len(stamps), True


def testhost_running():
    """A test run OF THIS REPO holds the build output lock; a second one fails with MSB3027.

    By command line, not by name: other Claude sessions run other repos' suites on this box
    (JoeBrain's, FileSync's), and their hosts hold none of our locks (2026-09-19)."""
    try:
        out = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command",
             "Get-CimInstance Win32_Process -Filter \"Name='testhost.exe'\" | Select-Object -ExpandProperty CommandLine"],
            capture_output=True, text=True, timeout=15).stdout
    except Exception:
        return False
    return "kor.operations" in out.lower()


def deny(reason):
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, sys.stdout)
    sys.exit(0)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    tool_input = payload.get("tool_input") or {}
    cmd = tool_input.get("command") or ""
    if not cmd:
        return

    timeout = tool_input.get("timeout") or 0
    if isinstance(timeout, (int, float)) and timeout > LONG_TIMEOUT_MS \
            and not DURATION_FIRST.search(tool_input.get("description") or ""):
        deny(f"A command with a {int(timeout) // 60000}-minute timeout and no duration in its description. "
             "The description is the one line Ian sees; it must open with the expected duration, "
             "e.g. \"~3 min: run the App suite\". Promised 2026-10-13 and broken the same day, so it is a hook.")

    for pattern, reason in RULES:
        if pattern.search(cmd):
            deny(reason)

    # CLAUDE.md: "Do not start one and then keep editing" -- it holds the build output
    # lock. This fired for real on 2026-08-25 and cost a ten-minute wait.
    # Only for a build OF THIS REPO: a command that names another repo's checkout ("cd .../JoeBrain && dotnet build")
    # touches none of our output, and on 2026-09-19 this held the JoeBrain session for twenty minutes while the
    # drawing pipeline's suite ran - the lock it feared was not the one the command would take.
    if BUILD.search(cmd) and not OTHER_REPO.search(cmd) and testhost_running():
        deny("A testhost is already running and holds the build output lock -- this will "
             "fail with MSB3027. Wait for it to finish first.")

    if APP_SUITE.search(cmd) and not FILTERED.search(cmd) and not DELIBERATE_FULL.search(cmd):
        deny("The full App suite without --filter: 3.5 minutes and the build lock, for a change that "
             "touches one area (it ran twice on 2026-10-13, 7 minutes, for BdReports alone). Run the "
             "classes you changed: --filter \"FullyQualifiedName~<Namespace or class>\". The one "
             "deliberate full run, before a publish, is prefixed FULL_SUITE=1.")

    n_sql = len(SQL_CALL.findall(cmd))
    if n_sql:
        seen, ok = sql_calls_allowed(payload.get("session_id"), n_sql)
        if not ok:
            deny(f"SQL call {seen + n_sql} in ten minutes (limit {SQL_MAX_CALLS}). Every sqlcmd is a VPN "
                 "round trip to KOR-APP01 and three hung on 2026-10-13. Write the remaining queries into "
                 "ONE .sql file and run it once, or run the script on the server (Invoke-CimMethod). "
                 "A dry-run, an apply and a check fit the limit; a fourth call is a batch not written.")


if __name__ == "__main__":
    main()
