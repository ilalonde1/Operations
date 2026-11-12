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
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time

class HeredocEscape:
    """Rule 3b's matcher (narrowed 12 Nov, see the rule): .search(cmd) is truthy when a heredoc body holds an escape that
    Python or bash will change."""
    HEAD = re.compile(r"<<-?[ \t]*(['\"]?)([A-Za-z_][\w-]*)\1[^\n]*\n")
    RAW = re.compile(r"(?<![\w])(?:[rR][bB]?|[bB][rR])(?:'''[\s\S]*?'''|\"\"\"[\s\S]*?\"\"\"|'[^'\n]*'|\"[^\"\n]*\")")
    BAD = re.compile(r"\\[^nrt'\"$`\\\s]")

    def search(self, cmd):
        for m in self.HEAD.finditer(cmd):
            quoted, delim = bool(m.group(1)), m.group(2)
            end = re.search(rf"(?m)^[ \t]*{re.escape(delim)}[ \t]*$", cmd[m.end():])
            body = cmd[m.end():m.end() + end.start()] if end else cmd[m.end():]
            if not quoted and "\\\\" in body:
                return True
            text = self.RAW.sub("''", body).replace("\\\\", "")
            if self.BAD.search(text):
                return True
        return None


class QuotesBlanked:
    """Rule 8's matcher (narrowed 12 Nov): the pattern runs on the command with every quoted string emptied, so a '|' inside
    --filter "A|B", or 'dotnet build' inside an echo or a JSON string, is not a pipe or a build."""
    QUOTED = re.compile(r"\"(?:[^\"\\\n]|\\.)*\"|'[^'\n]*'")

    def __init__(self, pattern):
        self.pattern = pattern

    def search(self, cmd):
        return self.pattern.search(self.QUOTED.sub('""', cmd))


def blank_quotes(cmd, keep=None):
    """cmd with the inside of every quoted string replaced by spaces - same length, so a position in the result is a
    position in cmd. A quoted string whose inside matches keep (e.g. a quoted path to an executable) is left as it is."""
    def sub(m):
        inner = m.group(0)[1:-1]
        return m.group(0) if keep and keep.match(inner) else m.group(0)[0] + " " * len(inner) + m.group(0)[-1]
    return QuotesBlanked.QUOTED.sub(sub, cmd)


class UnsavedCheckout:
    """Rule 2's matcher for `git checkout/restore -- <paths>` (narrowed 12 Nov, see the rule): .search(cmd) is truthy
    unless the same command, BEFORE the discard, saved the work (git stash, a cp / Copy-Item, or a git diff/show
    redirected to a file) and names every path it discards."""
    CALL = re.compile(r"git\s+(?:checkout|restore)\s+--(?=\s|$)([^;&|\n)]*)")
    SAVE = re.compile(r"git\s+stash\b"
                      r"|(?:^|[;&|(\n]\s*|\bdo\s+|\bthen\s+)(?:cp|Copy-Item)\b"
                      r"|git\s+(?:diff|show)\b(?![^;&|\n]*--(?:stat|shortstat|numstat|name-only|name-status)\b)"
                      r"[^;&|\n]*?(?<![\d&>])>{1,2}(?!&)")

    def search(self, cmd):
        blank = blank_quotes(cmd)
        for m in self.CALL.finditer(blank):
            args = [a.strip("\"'") for a in cmd[m.start(1):m.end(1)].split()]
            before = cmd[:m.start()]
            if args and self.SAVE.search(blank[:m.start()]) and all(a in before for a in args):
                continue
            return m
        return None


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

    # 1b. EVERY OTHER FORM OF THE SAME WALK (Mistake 120, 2 Nov: a Python os.walk counted 50,352 files of the fs01 IT
    #     archive file by file from KOR-1001 over the VPN, ~10 min, while the rule above caught only the PowerShell
    #     form with -Recurse BEFORE the path). The class: a command that enumerates a share's tree from this PC.
    #     A UNC path here = // or \\ at the START of a path (the command's start, or after whitespace, a quote, ( or =),
    #     then a host name (beginning with a letter, or a dotted IP address), then / or \. So https://, C:\\x (doubled
    #     backslashes after a drive) and integer division ` //2` are not UNC paths.
    #     CHECKS, each with a UNC path in the same pipeline segment: Get-ChildItem / gci / ls / dir with -r or -Recurse*
    #     in any position; dir /s; tree; robocopy /L with /S or /E (a listing); du; python / py in command position
    #     whose text that follows - across lines, so a heredoc body counts - holds os.walk, rglob or recursive=True AND
    #     a UNC path, before any && or ||.
    #     ALLOWS: one folder's listing (no recurse flag), git ls-remote / push / fetch to an fs01 remote, Test-Path,
    #     a robocopy that copies (no /L), any walk of a local path - including one run ON the target via
    #     Invoke-CimMethod with the target's local path (E:\...).
    #     DOES NOT CATCH: a walk inside a script FILE (`python walk.py //Kor-fs01/IT` - Mistake 120's own form if its
    #     walk was in a file; the share path is only an argument), a UNC held in a variable or a mapped drive letter
    #     (Z:\ is local-looking), a .NET Directory.EnumerateFiles in a PowerShell one-liner.
    #     KNOWN FALSE REFUSALS: bash `ls -r //host/x` (-r = reverse order there, one folder) is read as a recurse; a
    #     python one-liner that walks a local path and merely names a share in the same segment (`os.walk('.')` and a
    #     print of '//Kor-fs01/IT/') is refused; a python walk joined to a UNC listing by a single `;` or `|` is refused.
    (re.compile(r"(?i)(?:^|[;&|(\n{]\s*)(?:"
                r"(?:Get-ChildItem|gci|ls|dir)\b(?=[^|;\n]*\s-r(?:ecurse\w*)?\b)"
                r"|(?:cmd(?:\.exe)?\s+/c\s+)?dir\b(?=[^|;\n]*\s/s\b)"
                r"|tree(?:\.com)?\b"
                r"|robocopy(?:\.exe)?\b(?=[^|;\n]*\s/L\b)(?=[^|;\n]*\s/(?:S|E)\b)"
                r"|du\b"
                r")[^|;\n]*?(?:^|(?<=[\s'\"(=]))(?://|\\\\)(?:[A-Za-z][\w.$-]*|\d+\.[\d.]+)[/\\]"
                r"|(?:^|[;&|(\n]\s*)(?:\S*[/\\])?(?:python[\w.]*|py)\b"
                r"(?=(?:(?!&&|\|\|)(?s:.))*?(?:os\.walk|rglob|recursive\s*=\s*True))"
                r"(?=(?:(?!&&|\|\|)(?s:.))*?(?<=[\s'\"(=])(?://|\\\\)(?:[A-Za-z][\w.$-]*|\d+\.[\d.]+)[/\\])"),
     "A recursive walk of a network share from this PC (Mistake 120, 2 Nov: 50,352 files of the fs01 IT archive walked "
     "file by file over the VPN). Run it ON the target - Invoke-CimMethod -ComputerName <host> -ClassName "
     "Win32_Process -MethodName Create -Arguments @{CommandLine='<the walk, on its local path, writing one result "
     "file>'} - and read that one result back (memory feedback_run_on_the_target_never_chatty_over_vpn). One folder's "
     "listing without a recurse flag is allowed."),

    # 2. DESTROYING UNCOMMITTED WORK. On 2026-08-25 a `git checkout --` wiped a parallel
    #    session's uncommitted module. There is no undo; it survived only because a
    #    transcript still held the edit. `git stash push` keeps it.
    #    NARROWED 12 Nov (FW-RESET-2 M8, RESET-PLAN item 3): judged on transcripts 1 of 5 right (6790: only a
    #    `git diff --stat` before it, in a shared checkout) and 4 of 5 wrong - 4822, 5092 saved the paths with
    #    `git diff -- <p> > file`, 6472 and 5618 cp'd them aside first, in the same command.
    #    CHECKS (UnsavedCheckout): every `git checkout|restore -- <paths>` outside a quoted string.
    #    ALLOWS: one whose command, BEFORE it, has a save step - git stash, cp / Copy-Item in command position, or a
    #      git diff/show (not --stat/--name-only) redirected to a file - and names every path it discards.
    #    DOES NOT CATCH: a save step that writes nothing (cp to a folder that does not exist, an empty diff of an
    #      untracked file), or a save of different paths that merely mention the discarded ones; a discard through a
    #      variable that the save step never expanded the same way.
    (UnsavedCheckout(),
     "git checkout/restore -- discards uncommitted work with no undo, and another session "
     "may own it. Use: git stash push -- <paths>, or save each path first in the same command "
     "(git diff -- <paths> > file, or cp)."),

    (re.compile(r"git\s+reset\s+--hard|git\s+clean\s+-\w*[dfx]"),
     "git reset --hard / git clean destroys uncommitted work with no undo. "
     "Use: git stash push"),

    # 3. A WINDOWS PATH OR REGEX THROUGH SHELL QUOTING. Repo rule 7. Three attempts on
    #    2026-08-25 produced "Invalid escape \P", a UNC collapsed to a single backslash,
    #    and \03 read as a control character -- each time silently.
    (re.compile(r"python3?\s+-c\b[^\n]*\\\\"),
     "Backslashes inside python -c. Repo rule 7: bash eats them and the path or regex "
     "silently changes. Write the script to a file with the Write tool and run that."),

    #    NARROWED 12 Nov (FW-RESET M8, RESET-PLAN item 3): `<<'EOF' ... \\[A-Za-z]` matched ANY backslash-letter after a
    #    heredoc began. 587 refusals in the week to 12 Nov; judged on transcripts, 1 of 8 right (4970: a C# regex through a
    #    non-raw Python string) and 7 of 8 wrong - '\r\n' and '\n' in Python strings, regexes already in r'' literals, all
    #    in quoted heredocs where bash changes nothing (5120: nl='\r\n' if '\r\n' in t else '\n').
    #    CHECKS (HeredocEscape): an UNQUOTED delimiter whose body holds '\\' (bash collapses it: the lost UNC prefix);
    #      in any heredoc body, a backslash followed by anything but n r t, a quote, $, ` or another backslash, outside
    #      r''/r"" literals (\b becomes a backspace, \0 octal, \U a SyntaxError, \P/\d kept only by luck).
    #    ALLOWS: '\n', '\r\n', '\t', '\\' pairs, and anything inside a raw literal.
    #    DOES NOT CATCH: a '\n' that was meant for C# and lands as a real newline; a non-Python body (PowerShell, SQL)
    #      whose backslash is harmless is still refused; a path in a raw literal that ends in a backslash.
    (HeredocEscape(), "A backslash escape inside a heredoc that Python (or bash, for an unquoted delimiter) will change. Repo rule 7: "
     "this is how \\P, \\03, a \\b backspace in a C# regex and a lost UNC prefix happened. Use a raw literal (r'...'), or "
     "the Write or Edit tool for a Windows path or regex."),

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
    #    NARROWED 12 Nov (FW-RESET M8): 62 refusals in the week, judged 1 of 8 right (4753: dotnet test | grep). 5 of 8
    #    wrong were the '|' inside --filter "A|B" with the output sent to a file and the exit code echoed (6351); 2 of 8
    #    were 'dotnet build' inside a JSON or echo string (3565, 5361). So quoted strings are emptied first and dotnet
    #    must stand in command position. DOES NOT CATCH: a build run through a variable ($D build | tail).
    #    12 Nov (review row 1988 minor): a wrapper in front of dotnet - timeout N, time, env, nice, command, VAR=x - is
    #    still command position (`timeout 600 dotnet test X | tail` refused again). DOES NOT CATCH either: a build
    #    inside a quoted string run by bash -c / pwsh -Command (the quotes are emptied first).
    (QuotesBlanked(re.compile(r"(?is)^(?!.*pipefail).*?(?:^|[;&|(\n])\s*"
                              r"(?:(?:timeout(?:\s+-\S+)*\s+\S+|time|env|nice|command|[A-Za-z_]\w*=\S*)\s+)*"
                              r"(?:\S*[/\\])?dotnet(?:\.exe)?\s+(?:build|test|publish)\b[^|;\n]*\|(?!\|)")),
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
    #    TIGHTENED 2026-10-25: the script name had to appear only SOMEWHERE before a pipe, so naming
    #    one as an ARGUMENT tripped it. `Select-String -Path '...\build\publish.ps1' | ForEach-Object`
    #    was denied for reading the file, which teaches the habit of routing around the gate - worse
    #    than the fault it guards. So the name must now stand where a command stands, the same idiom
    #    rules 6 and 7 already use.
    #    CHECKS: publish*/deploy*.ps1 in command position (the start, or after ; & | ( { or a newline,
    #      optionally via &), or passed to -File/-FilePath/-Command, and then piped.
    #    ALLOWS: the same name as an argument to a reader (-Path/-LiteralPath/-Include/-Pattern,
    #      Get-Content, cat, Test-Path, Get-AuthenticodeSignature), and any command that sets
    #      pipefail or reads $LASTEXITCODE.
    #    DOES NOT CATCH: a run through a variable or Invoke-Expression (`$s = '.\publish.ps1'; & $s |
    #      tail`), where the name is nowhere near the pipe; a wrapper not named publish*/deploy*; or a
    #      script that swallows its own failure and exits 0, which no exit-code check can see.
    (re.compile(r"(?is)^(?!.*(?:pipefail|LASTEXITCODE)).*?"
                r"(?:(?:^|[;&|({\n])\s*(?:&\s*)?|-(?:File|FilePath|Command)\s+)"
                r"['\"]?(?:[A-Za-z]:)?[^\s'\";|&]*"
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
#    NARROWED 12 Nov (FW-RESET-2 M8): judged 2 of 5 right (6292, 6648: a real -Q query with no -t) and 3 of 5 wrong -
#    4941 and 6288 were the WORD sqlcmd inside a grep pattern after '\|' (read as a pipe), 6288/6380 a `sqlcmd -?`
#    (help, no query). So the call is looked for with quoted strings emptied (a quoted string that is itself a path
#    to sqlcmd is kept: "C:/Program Files/sqlcmd" is still a call), and a call whose only argument is -? or /? passes.
#    DOES NOT CATCH, added: sqlcmd run as a quoted argument of another shell (pwsh -Command "sqlcmd ...").
#    KNOWN FALSE REFUSAL (seen 12 Nov on this commit's own message): a heredoc body - a `git commit -F -` message -
#    with sqlcmd at a line start or after '(' is read as a call. Write the message to a file and use -F <file>.
RAW_SQLCMD_CALL = re.compile(r'(?im)(?:^|[;&|(])\s*(?:&\s*)?(?:"[^"\n]*[/\\]|[^\s"]*[/\\])?sqlcmd(?:\.exe)?"?(?=\s|$)')
SQLCMD_QUERY_TIMEOUT = re.compile(r'(?<!\S)-t\s*\d+')
SQLCMD_PATH = re.compile(r'(?i)^[^|"\n]*?(?:^|[/\\])sqlcmd(?:\.exe)?$')
SQLCMD_HELP = re.compile(r'\s+[-/]\?(?=\s|$|[;&|)])')


def sqlcmd_without_timeout(cmd):
    """Rule 6: a sqlcmd call (not a mention inside a quoted string, not `sqlcmd -?`) in a command with no -t."""
    if SQLCMD_QUERY_TIMEOUT.search(cmd):
        return False
    text = blank_quotes(cmd, keep=SQLCMD_PATH)
    return any(not SQLCMD_HELP.match(text, m.end()) for m in RAW_SQLCMD_CALL.finditer(text))

# 7. GOING SILENT. "Say the duration before any step over a minute" was promised on
#    2026-10-13 and broken the same afternoon: a 3.5-minute suite ran with no word, then a
#    40-minute stretch. The one line Ian always sees is the command's description. A command
#    that asks for more than two minutes must open that line with its expected duration
#    ("~3 min: ..."), or it does not run.
LONG_TIMEOUT_MS = 120_000
# 12 Nov (FW-RESET M8): a range ("~1-2 min:", "~5-10 min") is a duration too - 6696 was refused for it.
DURATION_FIRST = re.compile(r"(?i)^\s*~?\s*\d+(?:\s*(?:-|–|to)\s*\d+)?\s*(?:s|sec|secs|seconds|m|min|mins|minutes|h|hr|hours)\b")

# 8. A SLOW RUN WHOSE INPUTS ARE NOT THERE. On 2026-10-19 three runs were wasted in one afternoon on inputs a
#    second's check would have shown missing: the five stick-file PDFs gone from the local mirror (empty since
#    10-07), then a sixth source moved on the share - the six-set gate ran 7 m 22 s and refused. Ian: "STOP DOING
#    ANYTHING WITHOUT FUCKING CHECKING. THAT'S A CARDINAL FUCKING RULE."
#    CHECKS: a `dotnet test` of the Core test project that is not filtered to the fast set (Speed!=Slow) is denied
#    unless every \\Kor-fs01 source SixSetsBuildAsBankedTests names exists and the stick-file mirror holds its five
#    PDFs. The unfiltered App EngineeringTools.Tests suite is denied outright: it hangs headless (297 s, 10-19).
#    Since 9 Nov (Operations 5da3715c, board row 1985) the Core tests live in KOR.Drafter, so the source list is
#    read from KOR.Drafter's SixSetsBuildAsBankedTests.cs; if that file cannot be read the run is denied (fail closed).
#    DOES NOT CATCH: a source that exists but is a different file (the gate's own hash check does that); inputs of
#    other slow tests (LiveProjects share paths); a run started from inside the test project's folder with no
#    project path in the command. It ASSUMES KOR.Drafter is a sibling folder of this Operations checkout: a
#    KOR.Drafter cloned anywhere else reads as a missing file and every unfiltered Core run is denied.
CORE_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*EngineeringTools\.Core\.Tests")
FAST_ONLY = re.compile(r"Speed!=Slow")
ENGINEERING_APP_TESTS = re.compile(r"dotnet\s+test\b[^\n|;&]*(?:EngineeringTools\.Tests\.csproj|App[\\/]EngineeringTools\.Tests)")
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIX_SETS = os.path.join(os.path.dirname(REPO), "KOR.Drafter", "Kor.Operations.EngineeringTools.Core.Tests", "Intake",
                        "SixSetsBuildAsBankedTests.cs")
STICK_JOBS = ("31130-01", "31138-01", "31168-01", "31065-01", "31202-01")


def mirror_copy_of(remote):
    """DrawingMirror.LocalCopyOf: %TEMP%\\kor-drawings\\<SHA1(lower(dir))[:16] hex>\\<file name>. No share access."""
    directory, name = remote.rsplit("\\", 1)
    key = hashlib.sha1(directory.lower().encode("utf-8")).hexdigest().upper()[:16]
    root = os.environ.get("KOR_GUARD_MIRROR_ROOT") or os.path.join(tempfile.gettempdir(), "kor-drawings")
    return os.path.join(root, key, name)


def missing_slow_inputs():
    """Every input the slow gates read that is not where they will look - empty when all are there."""
    missing = []
    # the gate's LOCAL mirror copies (DrawingMirror.LocalCopyOf), never the share: 2026-10-19 this check itself
    # statted six files across the VPN before every run
    try:
        text = open(SIX_SETS, encoding="utf-8").read()
        for path in re.findall(r'@"(\\\\Kor-fs01[^"]+\.pdf)"', text):
            local = mirror_copy_of(path)
            if not os.path.exists(local):
                missing.append(local)
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
    # Only a host running from THIS checkout holds its lock. 10 Nov 13:24: a KOR.Drafter suite copy
    # (KOR.Drafter\work\_suite\<sha>\Kor.Operations.EngineeringTools.Core.Tests) held no Operations output and still
    # refused every Operations build for over an hour.
    return any("\\visual studio projects\\operations\\" in line.lower().replace("/", "\\")
               for line in out.splitlines() if "kor.operations" in line.lower())


PAYLOAD = {}


def deny(reason):
    try:   # F11 (Framework row 1159): every refusal is a row; logging can never change what this guard does
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        guard_log.record("bash", reason.split(". ")[0][:200], (PAYLOAD.get("tool_input") or {}).get("command"), None, payload=PAYLOAD)
    except Exception:
        pass
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
    PAYLOAD.update(payload if isinstance(payload, dict) else {})
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

    if sqlcmd_without_timeout(cmd):
        deny("sqlcmd without a query timeout: its default is 0, which waits forever, and three calls hung "
             "for minutes over the VPN on 2026-10-13. Add -t <seconds> (and -l 15 for the login), or use "
             "oq.py, which sets both.")


if __name__ == "__main__":
    main()
