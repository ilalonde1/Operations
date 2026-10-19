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

    # 8. A BUILD OR TEST RUN PIPED WITHOUT pipefail. The pipe's exit code replaces the compiler's.
    #    On 2026-10-14 `dotnet build ... 2>&1 | tail -30` exited 0 over four CS0104 errors; "build
    #    green" was reported, the filtered tests and the render harness then ran on the previous
    #    binaries, and five stale PNGs were read as evidence of the new code. CLAUDE.md rule 5.
    (re.compile(r"(?is)^(?!.*pipefail).*?\bdotnet\s+(?:build|test|publish)\b[^|;\n]*\|(?!\|)"),
     "A dotnet build/test/publish piped without pipefail: the pipe's exit code replaces the build's "
     "(2026-10-14: four CS0104 errors reported as a green build; tests and PNGs then ran on stale "
     "binaries). Prefix the command with `set -o pipefail &&`, or do not pipe it."),

    # 9. THE SAME FAULT, THROUGH THE WRAPPER SCRIPT. Rule 8 matches the literal `dotnet` command, and
    #    nothing in this shop runs the compiler directly: every deploy is a .ps1 that wraps it
    #    (publish.ps1, publish-networkops.ps1, publish-opportunities.ps1, deploy-*.ps1). So the rule
    #    read as covered while the thing actually typed went straight through.
    #    On 2026-10-19 `.\build\publish.ps1 2>&1 | Select-String ...` showed eight green builds and
    #    was reported as published. It had not published: the share still held the previous build,
    #    six days old, and the filter had eaten the failure. Found only because the next command
    #    happened to read version.txt off the share. Ian: "Why not read the rules FIRST?" - the rule
    #    had been read ten minutes earlier, which is the whole point of putting it in a gate.
    #    PowerShell has no pipefail, so the check is different: capture the output and read
    #    $LASTEXITCODE, which is the only thing that carries the script's real outcome.
    (re.compile(r"(?is)^(?!.*(?:pipefail|LASTEXITCODE)).*?"
                r"\b(?:publish|deploy)[A-Za-z0-9._-]*\.ps1\b[^|;\n]*\|(?!\|)"),
     "A publish/deploy script piped without reading its exit code: the pipe's result replaces the "
     "script's, so a failed deploy reads as a successful one (2026-10-19: eight green build lines "
     "from publish.ps1 that never reached the share, reported as published). Assign it instead - "
     "`$out = .\\build\\publish.ps1 2>&1` - then check $LASTEXITCODE and print what you need from "
     "$out. Or run it unpiped."),
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

# 6. A SQL CALL THAT CAN HANG. On 2026-10-13 three sqlcmd calls over the VPN hung for minutes.
#    sqlcmd's query timeout defaults to 0, which waits forever. A raw sqlcmd must carry -t <seconds>,
#    so a dropped VPN packet becomes an error in seconds instead of a stall.
#    REPLACED 2026-10-15 (Ian: "It causes more harm than good"): this rule used to cap calls at three
#    per ten minutes. The count was never the harm; the hang was. On 2026-10-15 the cap blocked a
#    correct apply-and-verify three times and prevented no hang.
#    CHECKS: sqlcmd in command position (start of a command, after ; & | ( or a newline, or run by
#    `python [-flags]`) without -t. ALLOWS: oq.py / oqwide.py, which pass -l 15 -t 120 themselves.
#    DOES NOT CATCH: a copy of oq.py edited to drop its timeouts; a SqlClient tool with
#    CommandTimeout = 0; a query that is merely slow, not hung, inside its 120 seconds.
RAW_SQLCMD_CALL = re.compile(r'(?im)(?:^|[;&|(])\s*(?:&\s*)?(?:"[^"\n]*[/\\]|[^\s"]*[/\\])?sqlcmd(?:\.exe)?"?(?=\s|$)')
SQLCMD_QUERY_TIMEOUT = re.compile(r'(?<!\S)-t\s*\d+')

# 7. GOING SILENT. "Say the duration before any step over a minute" was promised on
#    2026-10-13 and broken the same afternoon: a 3.5-minute suite ran with no word, then a
#    40-minute stretch. The one line Ian always sees is the command's description. A command
#    that asks for more than two minutes must open that line with its expected duration
#    ("~3 min: ..."), or it does not run.
LONG_TIMEOUT_MS = 120_000
DURATION_FIRST = re.compile(r"(?i)^\s*~?\s*\d+\s*(?:s|sec|secs|seconds|m|min|mins|minutes|h|hr|hours)\b")

# 8. A SLOW RUN WHOSE INPUTS ARE NOT THERE. On 2026-10-19 three runs were wasted in one afternoon on inputs a
#    second's check would have shown missing: the five stick-file PDFs gone from the local mirror (empty since
#    10-07), then a sixth source moved on the share - the six-set gate ran 7 m 22 s and refused. Ian: "STOP DOING
#    ANYTHING WITHOUT FUCKING CHECKING. THAT'S A CARDINAL FUCKING RULE."
#    CHECKS: a `dotnet test` of the Core test project that is not filtered to the fast set (Speed!=Slow) is denied
#    unless every \\Kor-fs01 source SixSetsBuildAsBankedTests names exists and the stick-file mirror holds its five
#    PDFs. The unfiltered App EngineeringTools.Tests suite is denied outright: it hangs headless (297 s, 10-19).
#    DOES NOT CATCH: a source that exists but is a different file (the gate's own hash check does that); inputs of
#    other slow tests (LiveProjects share paths); a run started from inside the test project's folder with no
#    project path in the command.
CORE_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*EngineeringTools\.Core\.Tests")
FAST_ONLY = re.compile(r"Speed!=Slow")
ENGINEERING_APP_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*(?:EngineeringTools\.Tests\.csproj|App[\\/]EngineeringTools\.Tests)")
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIX_SETS = os.path.join(REPO, "Kor.Operations.EngineeringTools.Core.Tests", "Intake", "SixSetsBuildAsBankedTests.cs")
STICK_JOBS = ("31130-01", "31138-01", "31168-01", "31065-01", "31202-01")


def missing_slow_inputs():
    """Every input the slow gates read that is not where they will look - empty when all are there."""
    missing = []
    try:
        text = open(SIX_SETS, encoding="utf-8").read()
        for path in re.findall(r'@"(\\\\Kor-fs01[^"]+\.pdf)"', text):
            if not os.path.exists(path):
                missing.append(path)
    except OSError:
        missing.append(SIX_SETS)
    mirror = os.environ.get("KOR_GUARD_STICKFILES") or os.path.join(
        os.environ.get("LOCALAPPDATA", ""), "Temp", "kor-drawings", "stickfiles")
    for job in STICK_JOBS:
        if not os.path.exists(os.path.join(mirror, job + ".pdf")):
            missing.append(os.path.join(mirror, job + ".pdf"))
    return missing



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

    if ENGINEERING_APP_TESTS.search(cmd) and not FILTERED.search(cmd):
        deny("The unfiltered App EngineeringTools.Tests suite hangs headless (297 s on 2026-10-19, testhost alive, no "
             "progress). Run it filtered: --filter \"FullyQualifiedName~PdfToSafe\" --blame-hang-timeout 120s.")

    if CORE_TESTS.search(cmd) and not FAST_ONLY.search(cmd):
        missing = missing_slow_inputs()
        if missing:
            deny("A slow Core test run whose inputs are not there - it would run for minutes and then refuse. "
                 "Missing:\n  " + "\n  ".join(missing) + "\nFind where each went, mend the path in "
                 "SixSetsBuildAsBankedTests.cs or restore the mirror, then run. (Rule 8, 2026-10-19.)")

    if RAW_SQLCMD_CALL.search(cmd) and not SQLCMD_QUERY_TIMEOUT.search(cmd):
        deny("sqlcmd without a query timeout: its default is 0, which waits forever, and three calls hung "
             "for minutes over the VPN on 2026-10-13. Add -t <seconds> (and -l 15 for the login), or use "
             "oq.py, which sets both.")


if __name__ == "__main__":
    main()
