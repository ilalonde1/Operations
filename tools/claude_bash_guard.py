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
import re
import subprocess
import sys

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
    cmd = (payload.get("tool_input") or {}).get("command") or ""
    if not cmd:
        return

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


if __name__ == "__main__":
    main()
