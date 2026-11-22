"""The guard's own tests: python tools/claude_bash_guard_test.py

Each rule is proved by a command it denies and a neighbour it allows. Rule 4 (a kill by process name)
was added 2026-09-19 after six test runs were ended by another session's name-wide kills.
"""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_guard.py")
NO_PROCS = os.path.join(__import__("tempfile").mkdtemp(prefix="guard-noprocs-"), "none.json")
with open(NO_PROCS, "w", encoding="utf-8") as _f:
    _f.write("[]")


def verdict(command, session_id=None, timeout=None, description=None, env=None, cwd=None):
    tool_input = {"command": command}
    if timeout is not None:
        tool_input["timeout"] = timeout
    if description is not None:
        tool_input["description"] = description
    payload = json.dumps({"tool_name": "Bash", "session_id": session_id or f"guard-test-{os.getpid()}",
                          "tool_input": tool_input, **({"cwd": cwd} if cwd else {})})
    # hermetic (19 Nov): no live process list and no slot file unless a case gives one - a real test run in this tree
    # made 20 unrelated cases deny while the W8 rule was being proved
    out = subprocess.run([sys.executable, GUARD], input=payload, capture_output=True, text=True, timeout=30,
                         env={**os.environ, "KOR_GUARD_PROCS": NO_PROCS, "KOR_GUARD_SLOT": NO_PROCS + ".absent",
                              **(env or {})}).stdout
    if not out.strip():
        return "allow"
    h = json.loads(out)["hookSpecificOutput"]
    if "updatedInput" in h and "permissionDecision" not in h:   # rule 7 fills the description (19 Nov)
        u = h["updatedInput"]
        return "fill" if {k: v for k, v in u.items() if k != "description"} == {k: v for k, v in tool_input.items() if k != "description"} \
            and u["description"].startswith("up to ") else f"BAD FILL {u!r}"
    return h["permissionDecision"]


def sql_rate_cases():
    """Rule 6 (replaced 2026-10-15): a raw sqlcmd without -t is denied; with -t it runs; the helpers
    run any number of times, since they carry their own timeouts. There is no call count any more."""
    cases = [
        ("sqlcmd -S KOR-APP01 -i x.sql", "deny"),                          # no query timeout
        ("sqlcmd -S KOR-APP01 -l 15 -i x.sql", "deny"),                    # login timeout alone is not enough
        ("sqlcmd -S KOR-APP01 -t 120 -i x.sql", "allow"),
        ("sqlcmd.exe -S KOR-APP01 -l 15 -t 60 -Q \"SELECT 1\"", "allow"),
        ('cd "C:/x" && sqlcmd -S KOR-APP01 -i x.sql', "deny"),             # in command position after &&
        ('"C:/Program Files/sqlcmd" -S h -i x.sql', "deny"),               # a quoted path is still a call
        ("cat notes-about-sqlcmd.txt", "allow"),                           # a mention is not a call
    ]
    got = [verdict(c) for c, _ in cases]
    got += [verdict('python -I oq.py -q "SELECT 1"') for _ in range(5)]   # the helpers: never counted
    expected = [e for _, e in cases] + ["allow"] * 5
    return [(f"sql timeout {i + 1}", e, g) for i, (e, g) in enumerate(zip(expected, got))]


def duration_cases():
    """Rule 7: a long timeout without a duration-first description gets "up to N min: " written in (19 Nov: filled, not
    refused); every other field is carried; short commands are untouched; another rule's deny still wins."""
    probes = [
        ("long, no description",         600000, None,                              "fill"),
        ("long, duration not first",     600000, "Run the App suite (~3 min)",     "fill"),
        ("long, duration first",         600000, "~3 min: run the App suite",      "allow"),
        ("long, duration range first (6696, 12 Nov)", 600000, "~1-2 min: run the viewer check", "allow"),
        ("long, range with no unit",     600000, "~1-2: run the viewer check",     "fill"),
        ("long, seconds first",          180000, "90 s: build and test",           "allow"),
        ("short, no description",        60000,  None,                              "allow"),
        ("default timeout",              None,   "Run the Data suite",             "allow"),
    ]
    got = [(label, e, verdict("dotnet build -c Release", timeout=t, description=d)) for label, t, d, e in probes]
    got.append(("long, no duration, AND a denied command: the deny wins", "deny",
                verdict("sqlcmd -S KOR-APP01 -i x.sql", timeout=600000, description="query")))
    return got


CASES = [
    # rule 5: the full App suite without a filter
    ("dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-build -nologo", "deny"),
    ("dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-build --filter \"FullyQualifiedName~BdReports\"", "allow"),
    ("FULL_SUITE=1 dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-build", "allow"),
    ("dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj -c Debug --no-build", "allow"),
    ("dotnet build Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug", "allow"),
    # rule 4: a kill by process name
    ("Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force", "deny"),
    ("Get-Process JoeBrain.Web, testhost* -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet build", "deny"),
    ("cd /c/x && (taskkill //F //IM testhost.exe 2>/dev/null; true) && dotnet build", "deny"),
    ("Stop-Process -Name testhost -Force", "deny"),
    ("pkill -f dotnet", "deny"),
    ("Get-CimInstance Win32_Process -Filter \"Name='testhost.exe'\" | Where-Object { $_.CommandLine -like '*JoeBrain*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }", "allow"),
    ("Stop-Process -Id 12345 -Force", "allow"),
    ("tasklist | grep -ic testhost", "allow"),
    ("git commit -q -m 'the guard denies Stop-Process by the name testhost'", "allow"),
    # rule 8: a build or test run piped without pipefail (2026-10-14: four errors read as green)
    ("dotnet build Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug -nologo -v q 2>&1 | tail -30", "deny"),
    ("dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj --filter \"FullyQualifiedName~X\" 2>&1 | grep Passed", "deny"),
    ("set -o pipefail && dotnet build Kor.Opportunities.Data/Kor.Opportunities.Data.csproj -c Debug 2>&1 | tail -30", "allow"),
    ("dotnet build Kor.Opportunities.Data/Kor.Opportunities.Data.csproj -c Debug -nologo", "allow"),
    ("dotnet build Kor.Opportunities.Data/Kor.Opportunities.Data.csproj || echo build failed", "allow"),
    # rule 8 narrowed 12 Nov (FW-RESET M8): judged wrong 6351 (a '|' inside --filter "A|B", output to a file), 3565/5361
    # (dotnet build inside a quoted string); judged right 4753 (dotnet test | grep) must still be denied
    ('dotnet test X.Tests --filter "FullyQualifiedName~A|FullyQualifiedName~B" > /tmp/t1.log 2>&1; echo "rc=$?"', "allow"),
    ("python -I -c \"import json; print(json.dumps({'cmd': 'dotnet build X.csproj'}))\" | head -3", "allow"),
    ('echo "run dotnet publish next" | tee note.txt', "allow"),
    ('cd "/c/x" && dotnet test X.Tests --list-tests 2>&1 | grep -c Test', "deny"),
    ('dotnet test X.Tests --filter "A|B" 2>&1 | tail -5', "deny"),
    # rule 8 narrowed 21 Nov (plan step 3, Ian "pipefail myself"): judged wrong 8594/8597/8805/8842/8894/8904/8905/8912/
    # 8931 (the exit code read with ${PIPESTATUS[0]} in the very next command, which office rule 13 names as a fix) and
    # 8275 (a pipe in a later command, after &&, matched as the build's); judged right 8592/8603/8691/... (a build piped
    # with no exit capture) must still be denied, and so must a PIPESTATUS read after another command, or before the build
    ('cd x && dotnet build X.csproj 2>&1 | grep -E "error|Warn" | head -20; echo exit ${PIPESTATUS[0]}', "allow"),
    ('dotnet build X 2>&1 | tail -5; echo EXIT ${PIPESTATUS[0]}', "allow"),
    ('dotnet test X | grep -c Passed; rc=${PIPESTATUS[0]}; echo $rc', "allow"),
    ('dotnet build X | tail -3 && echo "rc=${PIPESTATUS[0]}"', "allow"),
    ('dotnet build X > "$L" 2>&1 && P=$(ls bin/*.dll | head -1); echo "$P"', "allow"),
    ('dotnet build X 2>&1 | grep error; ls; echo ${PIPESTATUS[0]}', "deny"),
    ('echo ${PIPESTATUS[0]}; dotnet build X | tail -3', "deny"),
    ('dotnet build X > a.log 2>&1 && dotnet test Y | tail -3', "deny"),
    ('dotnet build X 2>&1 | grep -E "error" | sort | head', "deny"),
    ('dotnet test X |& tail -3', "deny"),
    # review 2636: the word is not a read, and inside a group the next PIPESTATUS is the group's (the tail's)
    ('dotnet build X 2>&1 | tail -3; echo "PIPESTATUS skipped"', "deny"),
    ('dotnet build X 2>&1 | tail -3; echo $PIPESTATUS_X', "deny"),
    ('( dotnet build X 2>&1 | tail -3 ); echo ${PIPESTATUS[0]}', "deny"),
    ('{ dotnet build X 2>&1 | tail -3; }; echo ${PIPESTATUS[0]}', "deny"),
    ('dotnet build X 2>&1 | tail -3; echo $PIPESTATUS', "allow"),
    # rule 3b narrowed 12 Nov: judged wrong 5120-like ('\r\n' and '\n' in a quoted heredoc), right 4970-like (a C# \b
    # through a non-raw Python string), and the UNC collapse an unquoted delimiter does
    ("python - <<'EOF'\nnl = '\\r\\n' if '\\r\\n' in t else '\\n'\nprint('a\\tb')\nEOF", "allow"),
    ("python - <<'EOF'\nimport re\nR = re.compile(r'\\bLEVEL\\s+\\d+')\nP = r\"C:\\Users\\x\"\nEOF", "allow"),
    ("python - <<'EOF'\ns = s.replace('x', '@\"\\bLEVEL\\b\"')\nEOF", "deny"),
    ("python - <<'EOF'\np = 'C:\\Users\\ilalonde\\x.txt'\nEOF", "deny"),
    ("python - <<'EOF'\nc = '\\03'\nEOF", "deny"),
    ("cat > x.ps1 <<EOF\n\\\\Kor-fs01\\Projects\nEOF", "deny"),
    ("python - <<'EOF'\np = 'a\\\\b'\nEOF", "allow"),
    ("python - <<PY\nprint('\\P')\nPY", "deny"),
    # rule 2: destroying uncommitted work
    ("git checkout -- src/File.cs", "deny"),
    ("git stash push -- src/File.cs", "allow"),
    # rule 2 narrowed 12 Nov (FW-RESET-2 M8): judged wrong 4822/5092 (git diff -- <p> > file first), 6472/5618 (cp aside
    # first); judged right 6790 (only --stat before it) must still be denied, and so must a save that comes AFTER, a save
    # that misses a path, and a diff whose only redirect is 2>&1
    ('cd "/c/x" && git diff -- tools/s.py > work/s8/attempt.patch && sha256sum work/s8/attempt.patch && git checkout -- tools/s.py', "allow"),
    ('cd "/c/x" && cp coord/sweep.csv "$TEMP/sweep-new.csv" && git checkout -- coord/sweep.csv && python -I x.py', "allow"),
    ('S="a.cs b.cs"; mkdir -p ../_jfl; for f in $S; do cp $f ../_jfl/; done; git checkout -- $S', "allow"),
    ('cd "C:/x" && git diff --stat -- Tests/X.cs && git checkout -- Tests/X.cs', "deny"),
    ("git diff -- a.py 2>&1 && git checkout -- a.py", "deny"),
    ("cp a.py /tmp/ && git checkout -- a.py b.py", "deny"),
    ("git checkout -- a.py && cp a.py /tmp/", "deny"),
    ("git restore -- a.py", "deny"),
    # review row 2025 minors: a stash that saves nothing, and a longer path that merely contains the discarded one
    ("git stash list; echo a.py; git checkout -- a.py", "deny"),
    ("cp tools/xa.py /tmp/ && git checkout -- a.py", "deny"),
    ("git stash && git checkout -- a.py", "allow"),
    ("git stash push -- a.py && git checkout -- a.py", "allow"),
    ('git commit -q -m "never git checkout -- x without a save"', "allow"),
    # rule 6 narrowed 12 Nov: judged wrong 4941/6288 (the word in a grep pattern after \|), 6380 (sqlcmd -?); judged right
    # 6292/6648 (a real -Q query with no -t) must still be denied
    (r'cd "/c/x" && python -I oqwide.py -i r.sql -d KorStandards 2>&1; echo "exit $?"; grep -n "argv\|-d\b\|sqlcmd" oqwide.py | head -20', "allow"),
    (r'grep -n "sqlcmd\|STANDARDSDB\|KORBRAIN_SQL" tools/standards_db.py | head -20', "allow"),
    ("sqlcmd -? >/dev/null 2>&1 && echo sqlcmd ok", "allow"),
    (r'cd /tmp; sqlcmd -S "KOR-APP01\SQLEXPRESS" -d KorStandards -E -W -s"|" -Q "SET NOCOUNT ON; SELECT 1" 2>&1 | head -40', "deny"),
    (r'sqlcmd -S "KOR-APP01\SQLEXPRESS" -d KorStandards -E -Q "SELECT TOP 0 * FROM brain.Memory" 2>&1 | head -3', "deny"),
    ('sqlcmd -? ; sqlcmd -S h -Q "SELECT 1"', "deny"),
    # rule 8, review row 1988 minor: a wrapper in front of dotnet is still command position
    ("timeout 600 dotnet test X.Tests 2>&1 | tail -5", "deny"),
    ("time dotnet build X.csproj | tail", "deny"),
    ("env A=1 dotnet build X.csproj | tail", "deny"),
    ("timeout 600 dotnet test X.Tests > t.log 2>&1", "allow"),
    # rule 1: a recursive walk over SMB
    ("find //Kor-fs01/Projects -name '*.e2k'", "deny"),
    ("ls //Kor-fs01/Projects/Projects", "allow"),
    # rule 1 widened 2026-11-02 (Mistake 120: 50,352 files of the fs01 IT archive walked file by file over the VPN
    # with a Python os.walk). Every recursive-walk form on a UNC path, in either slash style, is denied...
    (r"Get-ChildItem \\Kor-fs01\E$\IT\Archive -Recurse -File | Measure-Object", "deny"),
    (r"Get-ChildItem -LiteralPath '\\Kor-fs01\E$\IT' -Recurse -Filter *.pdf", "deny"),
    (r"gci -r //Kor-fs01/IT", "deny"),
    (r"ls -Recurse \\Kor-fs01\IT", "deny"),
    (r"dir /s \\Kor-fs01\IT", "deny"),
    (r"cmd /c dir /s /b \\Kor-fs01\IT", "deny"),
    (r"tree \\Kor-fs01\IT /f", "deny"),
    (r"robocopy \\Kor-fs01\E$\IT C:\x /L /S /NJH", "deny"),
    (r'find "\\Kor-fs01\IT" -type f', "deny"),
    (r"ls -lR //Kor-fs01/IT", "deny"),
    (r"du -sh //Kor-fs01/IT", "deny"),
    ("python -c \"import os; print(sum(len(f) for _, _, f in os.walk('//Kor-fs01/IT')))\"", "deny"),
    ("python -I -c \"from pathlib import Path; print(sum(1 for _ in Path('//Kor-fs01/IT').rglob('*')))\"", "deny"),
    # ...while one folder's listing, git to an fs01 remote, Test-Path, a local walk and a walk run ON the target pass
    (r"Get-ChildItem \\Kor-fs01\E$\IT", "allow"),
    (r"ls //Kor-fs01/IT/Archive", "allow"),
    (r"git ls-remote //KOR-FS01/E$/IT/git/KOR.Drafter.git", "allow"),
    (r"git push fs01 main && git ls-remote fs01 main", "allow"),
    (r"git fetch //KOR-FS01/E$/IT/git/Framework.git main", "allow"),
    (r"Test-Path \\Kor-fs01\E$\IT\robocopy.log", "allow"),
    (r"Get-ChildItem C:\x -Recurse -File", "allow"),
    (r"gci -r C:/x", "allow"),
    (r"find . -name '*.py'", "allow"),
    (r"robocopy \\Kor-fs01\E$\IT C:\x robocopy.log", "allow"),
    ("python -c \"import os; print(os.listdir('//Kor-fs01/IT'))\"", "allow"),
    (r"Invoke-CimMethod -ComputerName Kor-fs01 -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine='powershell -c Get-ChildItem E:\IT -Recurse | Measure-Object > E:\IT\count.txt'}", "allow"),
    ("curl -s https://example.com/a | head", "allow"),
    # review row 58: doubled backslashes after a drive are a LOCAL path; a python walk is judged on its own line and
    # segment, not on a share named after && or ||; ` //2` is integer division; the Windows `py` launcher is python
    (r'Get-ChildItem "C:\\x" -Recurse', "allow"),
    (r"dir /s C:\\x", "allow"),
    ("python -c \"import os; print(len(list(os.walk('.'))))\" && ls //Kor-fs01/IT", "allow"),
    ("python -c \"import os; print(sum(len(f) for _, _, f in os.walk('.')) //2)\"", "allow"),
    ("py -c \"import os; print(sum(len(f) for _, _, f in os.walk('//Kor-fs01/IT')))\"", "deny"),
    (r"Get-ChildItem -Path=\\Kor-fs01\IT -Recurse", "deny"),
    # review row 62: what 1b must STILL catch after that narrowing - a walk in a heredoc body, an IP-addressed share
    ("python - <<'EOF'\nimport os\nn = sum(len(f) for _, _, f in os.walk('//Kor-fs01/IT'))\nprint(n)\nEOF", "deny"),
    (r"Get-ChildItem \\10.0.0.5\IT -Recurse", "deny"),
    ("python -c \"import os; print(len(list(os.walk('//192.168.1.10/IT'))))\"", "deny"),
    # rule 9: the publish/deploy WRAPPER piped. Rule 8 matches a literal `dotnet` command and nothing
    # here runs the compiler directly, so on 2026-10-19 `publish.ps1 | Select-String` showed eight
    # green build lines, published nothing, and was reported as shipped. The share still held a build
    # six days old. PowerShell has no pipefail: the exit code has to be read.
    # The script name is split so this file does not trip the guard it is testing.
    (r".\build\publi" + r"sh.ps1 2>&1 | Select-String -Pattern 'Signed'", "deny"),
    (r".\publish-networkop" + r"s.ps1 2>&1 | Select-Object -Last 25", "deny"),
    (r"$out = .\build\publi" + r"sh.ps1 2>&1; $c = $LASTEXITCODE; $out | Select-Object -Last 5", "allow"),
    (r".\build\publi" + r"sh.ps1", "allow"),
    ("Get-ChildItem . | Select-Object -First 3", "allow"),
    # rule 9, tightened 2026-10-25: the name must stand where a COMMAND stands. It used to be enough
    # for the name to appear anywhere before a pipe, so READING the script was denied - which teaches
    # routing around the gate rather than reading the exit code.
    (r"Select-String -Path 'C:\x\build\publi" + r"sh.ps1' -Pattern 'Kor-fs01' | ForEach-Object { $_ }", "allow"),
    (r"Get-Content .\build\publi" + r"sh.ps1 | Select-Object -First 5", "allow"),
    (r"Get-AuthenticodeSignature .\deplo" + r"y-bridge.ps1 | Format-List", "allow"),
    # ...while every form that actually RUNS it is still denied, including the two the old pattern
    # caught only by accident of the name being adjacent to the pipe.
    (r"& 'C:\x\build\publi" + r"sh.ps1' | Out-String", "deny"),
    (r"powershell -File build\publi" + r"sh.ps1 | Select-Object -Last 5", "deny"),
    (r"cd C:\x; .\deplo" + r"y-opportunities.ps1 | Select-Object -Last 5", "deny"),
]


# Real GuardRefusal commands, recovered in full; no cuts or substitutions.
# Source: local .claude/projects transcripts, assistant Bash tool_use input.command.
HEREDOC_REAL = [
    # 4970: 1119f13b-af83-4549-8a67-24e4289569d5, line 93, 2026-11-10T07:17:13.844Z
    # SHA-256 (UTF-8 command): fbe6e9431da0e5032da0ff796e21cba811ac5b2864a41094ae97dfa8d28769fb
    ('GuardRefusal 4970',
     'cd "C:/VIsual Studio Projects/KOR.Drafter" && python - <<\'EOF\'\n'
     'import io\n'
     "p='Kor.Operations.EngineeringTools.Core/Intake/Schedules/StatedLadder.cs'\n"
     "s=io.open(p,encoding='utf-8',newline='').read()\n"
     "nl = '\\r\\n' if '\\r\\n' in s else '\\n'\n"
     "s=s.replace('\\r\\n','\\n')\n"
     'def rep(a,b):\n'
     '    global s\n'
     '    assert s.count(a)==1, (s.count(a), a[:60])\n'
     '    s=s.replace(a,b)\n'
     '\n'
     "rep('''// are listed, never chained (a chained one becomes an empty storey that cuts every column passing it). A floor two\n"
     '// sheets state differently is refused and listed. Where the OTHER building states the same floor at another elevation\n'
     "''','''// are listed, never chained (a chained one becomes an empty storey that cuts every column passing it). A floor is ONE\n"
     "// LEVEL TOKEN: a building suffix is taken off the name first ('L2- BLDG A' is building A's L2), and 'P1/GF' and 'P1'\n"
     '// are one floor under two spellings. A floor stated at two elevations - under one spelling or two - is refused and\n'
     '// listed, and so are two chained names that share a token. Where the OTHER building states the same floor at another elevation\n'
     "''')\n"
     "rep('''// Check(): the ladder a model was built on against that chain, and each plan view against the storeys its title names.\n"
     '// COVERS: every chained floor present under any spelling, their order, each height between consecutive stated floors,\n'
     '// each view on exactly the storeys expected of it (the expectation is GIVEN - read off the titles by a person).\n'
     '// DOES NOT COVER: the roof storeys (their stated names are not plan names); whether a member sits on the right storey\n'
     '// within a sheet; a floor the architect does not state. SAME-CLASS MISS: a floor stated wrong on every sheet alike.\n'
     "''','''// Check(): the ladder a model was built on against that chain, and each plan view against the storeys its title names.\n"
     '// A chain with no floor in it does NOT pass: there is nothing to check a model against (a set whose levels are all\n'
     '// named another way, or a building the rows do not hold).\n'
     '// COVERS: every chained floor present under any spelling, their order, each height between consecutive stated floors,\n'
     '// each view on exactly the storeys expected of it (the expectation is GIVEN - read off the titles by a person).\n'
     '// DOES NOT COVER: the roof storeys (their stated names are not plan names); whether a member sits on the right storey\n'
     '// within a sheet; a floor the architect does not state; WHERE A RUN PLACED A VIEW - the view check places each view\n'
     '// again here (PlanSheetNaming.MatchStories with the static vocabulary and only the judged views as the set, where the\n'
     '// composer ranks the floor words and passes every sheet), it does not read what was built; drift over the height -\n'
     '// each storey may be 2 mm off and the top is not checked against the datum.\n'
     '// SAME-CLASS MISS: a floor stated wrong on every sheet alike.\n'
     "''')\n"
     "rep('''    private const double AgreeMm = 2.0; ''','''    // the building a level is named for, after the floor: 01819's A3.01 writes '- BLDG A', '- BLD A', '-BUILD B', '- BLD B'\n"
     '    private static readonly Regex BuildingSuffix = new(@"\\\\s*-\\\\s*(?:BUILDING|BUILD|BLDG|BLD)\\\\.?\\\\s*[A-Z0-9]{1,2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);\n'
     "    private const double AgreeMm = 2.0; ''')\n"
     'rep(\'\'\'        foreach (var g in ours.GroupBy(e => Regex.Replace(e.Row.LevelFrom.Trim(), @"\\\\s+", " ").ToUpperInvariant()))\n'
     '        {\n'
     '            var sheets = g.Select(e => e.Row.Sheet).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();\n'
     '            if (!FloorName.IsMatch(g.Key))\n'
     '            {\n'
     '                notFloors.Add($"{g.Key} = {g.First().Row.Value} {g.First().Row.Unit} ({string.Join(", ", sheets)})");\n'
     '                continue;\n'
     '            }\n'
     '            double lo = g.Min(e => e.Mm!.Value), hi = g.Max(e => e.Mm!.Value);\n'
     '            if (hi - lo > AgreeMm)\n'
     '            {\n'
     '                problems.Add($"{g.Key} is stated differently: " + string.Join("; ", g.Select(e => $"{e.Row.Value} {e.Row.Unit} on {e.Row.Sheet}").Distinct()) + " - not chained");\n'
     '                continue;\n'
     '            }\n'
     '            var first = g.First().Row;\n'
     '            levels.Add((g.Key, g.First().Mm!.Value, $"stated: {first.Value} {first.Unit} on {string.Join(", ", sheets)}"));\n'
     '        }\n'
     "''','''        // the name as printed (spaces closed up) and, where it is a floor's, the floor name with the building suffix off\n"
     '        var named = ours.Select(e =>\n'
     '        {\n'
     '            string printed = Regex.Replace(e.Row.LevelFrom.Trim(), @"\\\\s+", " ").ToUpperInvariant();\n'
     '            string floor = BuildingSuffix.Replace(printed, "").Trim();\n'
     '            return (e.Row, Mm: e.Mm!.Value, Printed: printed, Floor: FloorName.IsMatch(floor) ? Regex.Replace(floor, @"\\\\s*/\\\\s*", "/") : null);\n'
     '        }).ToList();\n'
     '        foreach (var g in named.Where(n => n.Floor is null).GroupBy(n => n.Printed))\n'
     '            notFloors.Add($"{g.Key} = {g.First().Row.Value} {g.First().Row.Unit} ({string.Join(", ", Sheets(g.Select(n => n.Row)))})");\n'
     "        // one floor is one level token, whatever else its name carries: 'P1/GF' and 'P1' are grouped, and judged, together\n"
     '        foreach (var g in named.Where(n => n.Floor is not null).GroupBy(n => LevelToken.Match(n.Floor!).Value))\n'
     '        {\n'
     '            string name = g.Select(n => n.Floor!).Distinct().OrderByDescending(f => f.Length).ThenBy(f => f, StringComparer.Ordinal).First();\n'
     '            if (g.Max(n => n.Mm) - g.Min(n => n.Mm) > AgreeMm)\n'
     '            {\n'
     '                problems.Add($"{g.Key} is stated differently: " + string.Join("; ", g.Select(n => $"{n.Row.Value} {n.Row.Unit} on {n.Row.Sheet}{(n.Printed == g.Key ? "" : $" as \'{n.Printed}\'")}").Distinct()) + " - not chained");\n'
     '                continue;\n'
     '            }\n'
     '            var first = g.First().Row;\n'
     '            levels.Add((name, g.First().Mm, $"stated: {first.Value} {first.Unit} on {string.Join(", ", Sheets(g.Select(n => n.Row)))}"));\n'
     '        }\n'
     "        // a name holding a second floor's token ('P1/L1' beside 'L1') would chain that floor twice: neither is chained\n"
     '        foreach (var shared in levels.SelectMany(l => LevelToken.Matches(l.Name).Select(t => (Token: t.Value, l.Name))).GroupBy(x => x.Token).Where(g => g.Count() > 1).ToList())\n'
     '        {\n'
     '            var names = shared.Select(x => x.Name).ToList();\n'
     '            if (levels.RemoveAll(l => names.Contains(l.Name)) > 0)\n'
     '                problems.Add($"{shared.Key} is named by two stated floors ({string.Join(", ", names)}) - neither chained");\n'
     '        }\n'
     "''')\n"
     "rep('''    private static double? Mm(ProvenanceRow r)''','''    private static List<string> Sheets(IEnumerable<ProvenanceRow> rows) => rows.Select(r => r.Sheet).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();\n"
     '\n'
     "    private static double? Mm(ProvenanceRow r)''')\n"
     "rep('''        public bool Passes => MissingFloors.Count == 0 &&''','''        /// <summary>A chain with no floor is no pass: nothing was checked.</summary>\n"
     "        public bool Passes => Floors > 0 && MissingFloors.Count == 0 &&''')\n"
     "rep('''        public string Summary =>\n"
     '            $"stated floors\'\'\',\'\'\'        public string Summary =>\n'
     '            (Floors == 0 ? "NO STATED FLOOR to check against - not a pass; " : "") +\n'
     '            $"stated floors\'\'\')\n'
     'rep(\'\'\'Find(w)?.Name ?? $"(no storey {w})").OrderBy(\'\'\',\'\'\'Find(w)?.Name ?? $"(no storey {w})").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(\'\'\')\n'
     "io.open(p,'w',encoding='utf-8',newline='').write(s.replace('\\n',nl))\n"
     'EOF\n'
     'git diff --stat -- Kor.Operations.EngineeringTools.Core/Intake/Schedules/StatedLadder.cs; grep -n "BuildingSuffix = \\|s\\*/" Kor.Operations.EngineeringTools.Core/Intake/Schedules/StatedLadder.cs',
     'deny'),
    # 8264: a0ed29c9-a893-4264-b14d-14cca0d980b5, line 2374, 2026-11-19T21:30:11.863Z
    # SHA-256 (UTF-8 command): 8189f25853c8ff2f50e9da93a25ffd211eca5ae0b7d08bd70c25eb6085899fb8
    ('GuardRefusal 8264',
     'cd "/c/VIsual Studio Projects/Framework/tools" && python -I - <<\'EOF\'\n'
     'import subprocess, re, sys\n'
     "sys.path.insert(0, '.')\n"
     'import kor_review as kr\n'
     'OLD = re.compile(r"\\bred\\b", re.IGNORECASE)\n'
     'for repo in ["C:/VIsual Studio Projects/KOR.Drafter", "C:/VIsual Studio Projects/Operations", "C:/VIsual Studio Projects/Framework"]:\n'
     '    out = subprocess.run(["git", "-C", repo, "log", "--all", "--no-merges", "--format=%H%x00%B%x01"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout\n'
     '    commits = [c.split("\\x00", 1) for c in out.split("\\x01") if "\\x00" in c]\n'
     '    new_m = [(h.strip(), b) for h, b in commits if kr.RED_CLAIM.search(b)]\n'
     '    old_m = [h for h, b in commits if OLD.search(b)]\n'
     '    # of the new matches, which change a .cs file containing [Fact]/[Theory]  (the only ones W4 can refuse)\n'
     '    exposed = []\n'
     '    for h, b in new_m:\n'
     '        files = subprocess.run(["git", "-C", repo, "diff-tree", "--no-commit-id", "-r", "--name-only", "--diff-filter=AMR", h], capture_output=True, text=True).stdout.split()\n'
     '        if any(f.endswith(".cs") and ("Test" in f) for f in files):\n'
     '            exposed.append((h, kr.RED_CLAIM.search(b).group(0)))\n'
     '    print(f"{repo.split(\'/\')[-1]}: {len(commits)} commits; old word-match {len(old_m)}; claim-match {len(new_m)}; claim-match touching C# test files {len(exposed)}")\n'
     '    phrases = {}\n'
     '    for h, b in new_m:\n'
     '        p = kr.RED_CLAIM.search(b).group(0).lower(); phrases[p] = phrases.get(p, 0) + 1\n'
     '    print("   matched phrases:", sorted(phrases.items(), key=lambda x: -x[1])[:12])\n'
     'EOF',
     'allow'),
    # 8591: acc84549-5143-46b1-ad9c-630e3f716c8d, line 426, 2026-11-20T04:06:51.921Z
    # SHA-256 (UTF-8 command): 878b96b32992251e687f2e247b3673af4d42656f3072023439cf679581c8cc3f
    ('GuardRefusal 8591',
     "mkdir -p /c/Users/kor-claude/scratch-g5b/dump && cd /c/Users/kor-claude/scratch-g5b/dump && cat > rawdump.py <<'EOF'\n"
     'import sys, fitz\n'
     '# one line per char/span/line, Python repr of each float: the C# dumper prints the same\n'
     'def r(v): return repr(float(v))\n'
     'out = open(sys.argv[2], "w", encoding="utf-8", newline="\\n")\n'
     'for p in fitz.open(sys.argv[1]):\n'
     '    out.write(f"P {p.number}\\n")\n'
     '    for b in p.get_text("rawdict")["blocks"]:\n'
     '        for l in b.get("lines", []):\n'
     '            out.write("L %d %s %s %s %d\\n" % (b["number"], " ".join(map(r, l["bbox"])), r(l["dir"][0]), r(l["dir"][1]), l["wmode"]))\n'
     '            for s in l["spans"]:\n'
     '                out.write("S %s %s %s %d %s %d %d %s %s\\n" % (" ".join(map(r, s["bbox"])), r(s["origin"][0]), r(s["origin"][1]), r(s["size"]), s["flags"], s["font"], s["color"], s["alpha"], r(s["ascender"]), r(s["descender"])))\n'
     '                for ch in s["chars"]:\n'
     '                    out.write("C %d %s %s %s\\n" % (ord(ch["c"]), r(ch["origin"][0]), r(ch["origin"][1]), " ".join(map(r, ch["bbox"]))))\n'
     'EOF\n'
     "cat > dump.csproj <<'EOF'\n"
     '<Project Sdk="Microsoft.NET.Sdk">\n'
     '  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><PlatformTarget>x64</PlatformTarget><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>\n'
     '  <ItemGroup>\n'
     '    <ProjectReference Include="C:\\VIsual Studio Projects\\KOR.Drafter-geometry-gates\\Kor.Operations.EngineeringTools.Core\\Kor.Operations.EngineeringTools.Core.csproj" />\n'
     '    <ProjectReference Include="C:\\VIsual Studio Projects\\KOR.Drafter-geometry-gates\\Kor.Operations.EngineeringTools.Pdf.MuPdf\\Kor.Operations.EngineeringTools.Pdf.MuPdf.csproj" />\n'
     '  </ItemGroup>\n'
     '</Project>\n'
     'EOF\n'
     'echo ok',
     'allow'),
    # 8595: cd2499ed-046a-4e0b-9392-a3e8c95f0137, line 459, 2026-11-20T04:13:06.672Z
    # SHA-256 (UTF-8 command): 8fe6f0b8a4a91ed9468bd1b07d3d41623bb64455954f6721b2d35c38d6a26082
    ('GuardRefusal 8595',
     'cd "C:/VIsual Studio Projects/KOR.Drafter-comparison" && python -X utf8 - <<\'EOF\'\n'
     'import json,glob\n'
     'n=ok=0\n'
     'for p in sorted(glob.glob("jobs/*/job.json")):\n'
     '    raw=open(p,encoding="utf-8",newline="").read()\n'
     '    d=json.loads(raw); n+=1\n'
     '    out=json.dumps(d,indent=1,ensure_ascii=False)\n'
     '    same = out==raw or out+"\\n"==raw\n'
     '    ok+=same\n'
     '    print("SAME" if same else "DIFF", p, "trailingNL" if raw.endswith("\\n") else "")\n'
     'print(ok,"of",n)\n'
     's={"a":1.0,"b":1e-06,"c":1e16,"c2":1e15,"d":[],"e":"é\\n\\"\\\\\\t\\x01\\u2028","f":0.0001,"g":123456789012345678,"h":{},"i":-0.0,"j":0.1+0.2,"k":1.5e-10,"l":[True,False,None],"m":-12.5,"n":100.0,"o":2.5e+20}\n'
     'print(repr(json.dumps(s,indent=1,ensure_ascii=False)))\n'
     'EOF',
     'allow'),
    # 8609: c2ad5988-6082-4890-b0bb-2a69792f929d, line 1410, 2026-11-20T04:57:36.366Z
    # SHA-256 (UTF-8 command): 5fd1ee7a2b9fceeb54dea1a65455caaeacd331a4f88955b2cabe5583a07df008
    ('GuardRefusal 8609',
     "mkdir -p /c/kor-suite/g4-scratch/census-planted && cd /c/kor-suite/g4-scratch && python -X utf8 - <<'EOF'\n"
     'import os\n'
     'def e2k(unit, sections, beams, loose=()):\n'
     '    t = [f\'$ CONTROLS\', f\'  UNITS  "LB"  "{unit}"  "F"  \', \'$ FRAME SECTIONS\']\n'
     '    for name, d, b in sections:\n'
     '        q = name.replace(\'"\', \'""\')\n'
     '        t.append(f\'  FRAMESECTION  "{q}"  MATERIAL "4000Psi"  SHAPE "Concrete Rectangular"  D {d} B {b} \')\n'
     '        t.append(f\'  FRAMESECTION  "{q}"  JMOD 0.01 I2MOD 0.35 I3MOD 0.35 \')\n'
     "    t.append('$ LINE CONNECTIVITIES')\n"
     '    for i, (line, _, _) in enumerate(beams): t.append(f\'  LINE  "{line}"  BEAM  "{i}"  "{i + 100}"  0\')\n'
     '    t.append(\'  LINE  "C1"  COLUMN  "1"  "1"  1\')\n'
     "    t.append('$ LINE ASSIGNS')\n"
     '    for line, section, n in beams:\n'
     '        for s in range(n):\n'
     '            q = section.replace(\'"\', \'""\')\n'
     '            t.append(f\'  LINEASSIGN  "{line}"  "L{s + 1}"  SECTION "{q}"  MINNUMSTA 3 \')\n'
     '    for line in loose: t.append(f\'  LINEASSIGN  "{line}"  "L1"  RELEASE "M3I M3J"  \')\n'
     '    t.append(\'  LINEASSIGN  "C1"  "L1"  SECTION "Loading"  \')\n'
     '    return "\\n".join(t) + "\\n"\n'
     'd = "census-planted"\n'
     'open(os.path.join(d, "a.e2k"), "w").write(e2k("IN", [("Loading", 1, 1), ("Line Load", 0, 0), ("Glazing", 4, 4), ("thin", 24, 5.5), ("6x24", 24, 6), (\'16" x 24"\', 24, 16)],\n'
     '    [("B1", "Loading", 3), ("B2", "Line Load", 1), ("B3", "Glazing", 2), ("B4", "thin", 1), ("B5", "6x24", 1), ("B6", \'16" x 24"\', 5)], loose=("B6",)))\n'
     'open(os.path.join(d, "b.e2k"), "w").write(e2k("IN", [("Loading", 24, 16)], [("B1", "Loading", 2)]))\n'
     'open(os.path.join(d, "m.e2k"), "w").write(e2k("MM", [("null", 25.4, 25.4), ("B400x600", 600, 400)], [("B1", "null", 4), ("B2", "B400x600", 1)]))\n'
     'os.makedirs(os.path.join(d, "sub", ".hidden"), exist_ok=True)\n'
     'open(os.path.join(d, "sub", "c.e2k"), "w").write(e2k("FT", [("1x2", 2, 1), ("bad", 1, 1)], [("B1", "1x2", 3), ("B2", "bad", 2)]))\n'
     'open(os.path.join(d, "sub", ".hidden", "h.e2k"), "w").write(e2k("IN", [("x", 24, 16)], [("B1", "x", 9)]))\n'
     'open(os.path.join(d, "sub", ".dot.e2k"), "w").write(e2k("IN", [("x", 24, 16)], [("B1", "x", 9)]))\n'
     'open(os.path.join(d, "u.e2k"), "w").write(\'$ CONTROLS\\n  UNITS  "LB"  "YD"  "F"\\n\')\n'
     'print("planted", sorted(os.listdir(d)))\n'
     'EOF\n'
     "cat > census-sets.txt <<'EOF'\n"
     'planted=C:\\kor-suite\\g4-scratch\\census-planted\n'
     'pdftosafe-fixtures=C:\\kor-suite\\harness-src-450cb229\\Kor.Operations.EngineeringTools.PdfToSafe.Tests\n'
     'core-tests=C:\\kor-suite\\harness-src-450cb229\\Kor.Operations.EngineeringTools.Core.Tests\n'
     'fixtures=C:\\kor-suite\\harness-fixtures\n'
     'renfrew-replay=C:\\kor-suite\\g4-base-450cb229\\01819-01-renfrew\n'
     'one-file=C:\\kor-suite\\harness-src-450cb229\\Kor.Operations.EngineeringTools.PdfToSafe.Tests\\PdfToSafe\\Fixtures\\regent_typ_floor.etabs-app.e2k\n'
     'EOF\n'
     'echo ok',
     'allow'),
    # 8615: 3920cc62-71f9-45e4-9c3a-70b447e9aada, line 1682, 2026-11-20T05:09:39.281Z
    # SHA-256 (UTF-8 command): e2ec759a7d635ef91a9d7cb843c44311876fffeeec4e8e68c8439ab8b7024b31
    ('GuardRefusal 8615',
     'cat > "$TEMP/g1/lesson.md" <<\'EOF\'\n'
     'port-g1, 19 Nov 2026 21:33-22:06: re-pointing a replay harness at a frozen copy (KOR_PARITY_MAIN) changed what "a main-checkout path" meant, and the run records\' ABSOLUTE paths (runs after 28 Oct name C:\\VIsual Studio Projects\\KOR.Drafter\\work\\<job>\\1-read) no longer matched it, so Remap returned them unchanged: the oracle Python and the C# port both WROTE into the live work\\31218-01-137th-street\\1-read (17 files) and work\\31180-01-halifax-v6\\1-read (13). Found by diffing live against the snapshot taken before the run; restored 30 of 30 from it (0 differences left); mtimes of those files are now 22:07.\n'
     'Rule: a change to WHERE a harness reads from is a change to where it WRITES; a replay must refuse any argument under a live work\\ folder, not trust a mapping.\n'
     'Check that ships with it: Pdf.Tests ReadChain.NotLive (every chain argument, both sides) + FromRecorded; ReplayNeverTouchesLiveWorkTests (8 cases; red when FromRecorded is made identity).\n'
     'EOF\n'
     'python -I "C:\\VIsual Studio Projects\\Framework\\tools\\kor_brain.py" note --lane "KOR.Drafter" --name replay-frozen-root-wrote-live-work-19-nov --kind feedback --description "A replay re-pointed at a frozen copy passed recorded absolute paths through and wrote 30 files into live work/ (restored); ReadChain.NotLive now refuses any arg under live work/" --body-file "$(cygpath -w "$TEMP/g1/lesson.md")" 2>&1 | tail -2',
     'allow'),
    # 8686: ac80da06-010b-48fe-b321-e576eb44a116, line 2504, 2026-11-20T05:42:07.277Z
    # SHA-256 (UTF-8 command): 9878ec248c31a8ed3e3be43c52c37168b7b4ada58223314ef6c201c4653addc0
    ('GuardRefusal 8686',
     'S="C:/Users/kor-claude/AppData/Local/Temp/claude/f4247977c5009216/ac80da06-010b-48fe-b321-e576eb44a116/scratchpad"; cat > "$S/state2.md" <<\'EOF\'\n'
     '# C# PORT LEAD (row 1392) - 19 Nov 22:41 (date), session ac80da06, kor-claude window. Ian in bed; his words tonight: "2 - parallel", "yes" (one live read-only play), "go ahead with wave 2".\n'
     'RUNNING (launcher lanes/2026-11-19/launch.sh, preflight-gated; exits land in lanes/2026-11-19/started.txt):\n'
     '  harness KOR.Drafter-comparison 2055 | g1 KOR.Drafter-markup-intake 2197 | g4 KOR.Drafter-etabs 2087 | g5b KOR.Drafter-geometry-gates 2201\n'
     '  g0a-2 KOR.Drafter-revit 2084 (hang fix row 2208, then ONE read-only play on KOR-302N; blocked if 302N silent - no retries, no Revit start)\n'
     '  g5a KOR.Drafter-plan 2200 (wave 2, from b720fcbd)\n'
     'FINISHED, NOT YET ACCEPTED: g11 (2205, exit 21:51; 79/82 dead deleted, rows 2206/2207 handed) ; g0a (offline part, exit 22:07).\n'
     'OVERNIGHT PLAN (told to Ian): on each exit check result vs brief DoneWhen, review status, accept or send back on the row; merge accepted branches into port-merge-19nov (kor_review, push fs01), remove that worktree.\n'
     '  g4 NOT accepted until 31218/Halifax/hotel proofs rerun against Python (it used the fixture chain while python lacked fitz).\n'
     '  Wave 2 next as slots free (cap ~6 running): g3 -> KOR.Drafter-catalogue (row 2199), g6-8 -> KOR.Drafter-quantities (2202); g7 -> KOR.Drafter-viewer (2203) after g0a-2; g2 -> KOR.Drafter-drafted-set (2198) after g5b glyph model; g9 -> KOR.Drafter-safe (2204) after g4. g0b last, not tonight.\n'
     '  Launch recipe: git worktree add -b port-<tag> "C:/VIsual Studio Projects/KOR.Drafter-<part>" fs01/port-merge-19nov (paths LITERAL - check #3982 refuses $vars); header + brief -> lane_row.py wrap --row N; launch-detached.ps1 -Name -Brief -Cwd -Row.\n'
     '  STOP: no new launch past 80% weekly (52% at 22:31); two regressions one cause -> that lane stops; handover before ~600k context.\n'
     'MORNING FOR IAN: accepted/merged, running, blocked (likely 302N play).\n'
     'Shared Python (C:\\Program Files\\Python312) has all 9 of 9 pins under python -I. Messages to headless lanes are HELD - board rows only.\n'
     'EOF\n'
     '"/c/KOR-Operations/kor/kor.exe" state --module "KOR.Drafter" --file "$S/state2.md" 2>&1 | tail -1',
     'allow'),
    # 8687: 3920cc62-71f9-45e4-9c3a-70b447e9aada, line 1990, 2026-11-20T05:49:00.851Z
    # SHA-256 (UTF-8 command): 1581ecc157e6a1a1b5e64d3ebee998569512c1712983c4804fa2dd711e53cb19
    ('GuardRefusal 8687',
     'cd "C:/VIsual Studio Projects/KOR.Drafter-markup-intake" && cat > "$TEMP/g1/msg1.txt" <<\'EOF\'\n'
     'port-g1 step 2: ONE C# entry for the group-1 readers - Plan.Read.ReadTools + `takeoff read <tool> args`\n'
     '\n'
     "Each reader by the name virtual_drafter.py's read stage starts it (dump_annots, sketch_markup_to_plan read|cover|grids,\n"
     'markup_to_plan, plan_snap_columns, plan_written_dims, arch_to_plan/scheme/doors/openings, manifest_add_wall_openings,\n'
     'manifest_add_openings, plan_balconies_from_arch, plan_outline_from_columns/issued_markup, plan_vehicle_entrance (job via\n'
     'C# JobDecisions.LoadJob), plan_to_revit_sheets grids, slab_step_from_dashed) with the SAME arguments, in process; refusals are\n'
     'exit 1 + text as sys.exit. look refused (no run step), selftest -> xUnit.\n'
     'Searched first: TakeoffVerbs.cs (99 verbs: no read/dispatch verb; markup-list, port-proof, pdf-readable do other jobs);\n'
     "src/KOR.Drafter.Pipeline RunCommand.cs only starts virtual_drafter.py and does not reference Core; the readers' entries\n"
     "were reachable only from Pdf.Tests' private table (ReadChainParityTests.Ports).\n"
     "takeoff now references Pdf.MuPdf (#133775). MuPDF.NET's clean-up deleted runtimes\\win in every project that gets it (Core.Tests\n"
     'via takeoff: 6 SqlClient PlatformNotSupported reds); the KorRestoreWinRuntimes target moves from Pdf.Tests.csproj to one\n'
     'Directory.Build.targets. Fast suites vs 450cb229 by name: Core 46 = 46 (2775 run), PdfToSafe 7 = 7, Pdf 0.\n'
     '\n'
     'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n'
     'EOF\n'
     'git add Kor.Operations.EngineeringTools.Core/Plan/Read/ReadTools.cs Kor.Operations.EngineeringTools.TakeoffCli/Verbs/ReadVerb.cs Directory.Build.targets && git commit -q -F "$TEMP/g1/msg1.txt" -- Kor.Operations.EngineeringTools.Core/Plan/Read/ReadTools.cs Kor.Operations.EngineeringTools.TakeoffCli/Verbs/ReadVerb.cs Kor.Operations.EngineeringTools.TakeoffCli/TakeoffVerbs.cs Kor.Operations.EngineeringTools.TakeoffCli/Kor.Operations.EngineeringTools.TakeoffCli.csproj Directory.Build.targets Kor.Operations.EngineeringTools.Pdf.Tests/Kor.Operations.EngineeringTools.Pdf.Tests.csproj && git show --name-only --oneline HEAD',
     'allow'),
    # 8688: 51d2fd14-eabc-404f-a29f-f18a4e6f4969, line 333, 2026-11-20T05:49:14.817Z
    # SHA-256 (UTF-8 command): fe570a6f6ca2c131842ba4b0006a734a045abffd17912e9c56148a08351461a0
    ('GuardRefusal 8688',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-plan" && mkdir -p Kor.Operations.EngineeringTools.Core/Gates && F=Kor.Operations.EngineeringTools.Core/Gates/GateEarliest.cs && cat > $F <<\'EOF\'\n'
     '// EVERY GATE NAMES THE EARLIEST STAGE IT CAN RUN, AND A GATE THAT FAILS AFTER REVIT IS ASKED WHETHER IT COULD HAVE FAILED SOONER\n'
     '// (Ian 4 Nov, board row 1648). The C# port of tools/gate_earliest.py (board row 1392, port-g5a): the same table, the same source\n'
     '// scan, the same sooner-owed.jsonl rows, the same printed lines. `takeoff gate earliest check|owed <job.json>|answer ...`.\n'
     '//\n'
     '// COVERS: every run.gate, gate_or_na, na_or and "gate:" step in virtual_drafter.py and stage_viewer.py by its name in the source (a\n'
     '//     name carried in a variable named `name` too), and every failure a run records from the revit stage on.\n'
     '// DOES NOT: judge an answer ("no" with a thin reason passes). Gates inside child tools reach the run as a failed step. The promote,\n'
     "//     revise and place verbs' gates are theirs, not a run's. While the run is Python, the scan reads the Python source.\n"
     '// SAME-CLASS FAULT IT WOULD MISS: a fault no gate fails on, found by someone looking: nothing here asks about it.\n'
     'using System.Text;\n'
     'using System.Text.RegularExpressions;\n'
     'using Kor.Operations.EngineeringTools.Drafting;\n'
     '\n'
     'namespace Kor.Operations.EngineeringTools.Gates;\n'
     '\n'
     'public static class GateEarliest\n'
     '{\n'
     '    public static readonly string[] Sources = ["virtual_drafter.py", "stage_viewer.py"];\n'
     '    public static readonly string[] NotARun = ["promote", "revise", "place", "check_plot", "main", "run_stages"];\n'
     '    public static readonly string[] Stages = ["read-etabs", "stage2", "host", "replay", "revit"];\n'
     '\n'
     "    /// <summary>(regex on the gate's name, the earliest stage, why) - gate_earliest.EARLIEST, row for row (the proof compares the two tables).</summary>\n"
     '    public static readonly (string Pattern, string Stage, string Why)[] Earliest =\n'
     '    [\n'
     'EOF\n'
     "cat /c/Users/kor-claude/g5a/gen/earliest.txt >> $F && cat >> $F <<'EOF'\n"
     '    ];\n'
     '\n'
     '    private static readonly Regex Sample = new(@" \\(sample\\)$");\n'
     '    private static readonly Regex[] Compiled = Earliest.Select(e => new Regex(e.Pattern)).ToArray();\n'
     '\n'
     '    /// <summary>answer(gate): the first row whose pattern is found in the name, a sample run\'s " (sample)" taken off; null = no row.</summary>\n'
     '    public static (string Stage, string Why)? Answer(string gate)\n'
     '    {\n'
     '        gate = Sample.Replace(gate, "", 1);\n'
     '        for (int i = 0; i < Earliest.Length; i++)\n'
     '            if (Compiled[i].IsMatch(gate)) return (Earliest[i].Stage, Earliest[i].Why);\n'
     '        return null;\n'
     '    }\n'
     '\n'
     '    private static readonly Regex DefSplit = new(@"(?m)^(?=def )");\n'
     '    private static readonly Regex DefName = new(@"^def (\\w+)");\n'
     '    private static readonly Regex GateCall = new(@"(?:run\\.gate|gate_or_na|na_or)\\((?:run, j, )?f?""([^""]+)""");\n'
     '    private static readonly Regex NameAssign = new(@"\\bname = f?""([^""]+)""");\n'
     '    private static readonly Regex GateStep = new(@"run\\.step\\(f?""(gate: [^""]+)""");\n'
     '\n'
     "    /// <summary>source_gates: every gate a run can name, from the source (an f-string's braces left in), sorted by code point.</summary>\n"
     '    public static List<string> SourceGates(string toolsDir, IEnumerable<string>? sources = null)\n'
     '    {\n'
     '        string text = string.Concat((sources ?? Sources).Select(s => File.ReadAllText(Path.Combine(toolsDir, s), new UTF8Encoding(false))));\n'
     '        var found = new HashSet<string>(StringComparer.Ordinal);\n'
     '        foreach (string chunk in DefSplit.Split(text))\n'
     '        {\n'
     '            var m = DefName.Match(chunk);\n'
     '            string fn = m.Success ? m.Groups[1].Value : "-";\n'
     '            if (NotARun.Contains(fn)) continue;\n'
     '            foreach (Match g in GateCall.Matches(chunk)) found.Add(g.Groups[1].Value);\n'
     '            foreach (Match g in NameAssign.Matches(chunk)) found.Add(g.Groups[1].Value);\n'
     '            foreach (Match g in GateStep.Matches(chunk)) found.Add(g.Groups[1].Value);\n'
     '        }\n'
     '        var list = found.ToList();\n'
     '        list.Sort(StringComparer.Ordinal);\n'
     '        return list;\n'
     '    }\n'
     '\n'
     '    public static List<string> Unregistered(IEnumerable<string> gates) => gates.Where(g => Answer(g) is null).ToList();\n'
     '\n'
     '    public static string OwedPath(string work) => Path.Combine(work, "sooner-owed.jsonl");\n'
     '\n'
     '    /// <summary>rows(work): each non-blank line of sooner-owed.jsonl, as json.loads reads it.</summary>\n'
     '    public static List<PyDict> Rows(string work)\n'
     '    {\n'
     '        string p = OwedPath(work);\n'
     '        if (!File.Exists(p)) return [];\n'
     '        return ReadLinesPy(p).Where(l => l.Trim().Length > 0).Select(l => Py.Dict(PyJson.Parse(l))).ToList();\n'
     '    }\n'
     '\n'
     "    /// <summary>Lines as Python's text mode splits them (universal newlines): \\r\\n, \\r and \\n.</summary>\n"
     '    private static IEnumerable<string> ReadLinesPy(string path)\n'
     '    {\n'
     '        string t = File.ReadAllText(path, new UTF8Encoding(false));\n'
     '        return t.Replace("\\r\\n", "\\n").Replace(\'\\r\', \'\\n\').Split(\'\\n\');\n'
     '    }\n'
     '\n'
     '    /// <summary>owed(work): (gate, run) failed after Revit and not answered since, sorted by gate.</summary>\n'
     '    public static List<(string Gate, string Run)> Owed(string work)\n'
     '    {\n'
     '        var order = new List<string>();\n'
     '        var map = new Dictionary<string, object?>(StringComparer.Ordinal);\n'
     '        foreach (var r in Rows(work))\n'
     '        {\n'
     '            string kind = Py.Text(r["kind"]); string gate = Py.Text(r["gate"]);\n'
     '            if (kind == "failed") { if (!map.ContainsKey(gate)) order.Add(gate); map[gate] = r["run"]; }\n'
     '            else if (kind == "answered") { if (map.Remove(gate)) order.Remove(gate); }\n'
     '        }\n'
     '        var list = map.Select(kv => (kv.Key, Py.Str(kv.Value))).ToList();\n'
     '        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));\n'
     '        return list;\n'
     '    }\n'
     '\n'
     "    /// <summary>record_failures: a run's post-Revit failures appended with the table's answer; the lines to print.</summary>\n"
     '    public static List<string> RecordFailures(string work, string runStarted, IEnumerable<string> gates)\n'
     '    {\n'
     '        var lines = new List<string>();\n'
     '        var sb = new StringBuilder();\n'
     '        foreach (string g in gates)\n'
     '        {\n'
     '            var a = Answer(g);\n'
     '            var row = new PyDict().Set("kind", "failed").Set("gate", g).Set("run", runStarted).Set("earliest", a?.Stage).Set("why", a?.Why);\n'
     "            sb.Append(PyJson.Dump(row)).Append('\\n');\n"
     '            if (a is null) lines.Add($"  COULD IT FAIL SOONER? {g}: NO ROW in gate_earliest.py: add it, with the earliest stage it can run");\n'
     '            else if (a.Value.Stage == "revit") lines.Add($"  COULD IT FAIL SOONER? {g}: the table says no ({a.Value.Why}): answer it, or move the check");\n'
     '            else lines.Add($"  COULD IT FAIL SOONER? {g}: YES, at {a.Value.Stage} ({a.Value.Why}): why did that stage not stop it? Fix that in this fix");\n'
     '        }\n'
     '        AppendPy(OwedPath(work), sb.ToString());\n'
     '        return lines;\n'
     '    }\n'
     '\n'
     '    /// <summary>Python text-mode append on Windows: "\\n" is written as "\\r\\n".</summary>\n'
     '    private static void AppendPy(string path, string text) =>\n'
     '        File.AppendAllText(path, OperatingSystem.IsWindows() ? text.Replace("\\n", "\\r\\n") : text, new UTF8Encoding(false));\n'
     '\n'
     '    /// <summary>refusal(work): why the stages from revit on may not start, or null.</summary>\n'
     '    public static string? Refusal(string work)\n'
     '    {\n'
     '        var rows = Owed(work);\n'
     '        if (rows.Count == 0) return null;\n'
     '        return $"REFUSED: {rows.Count} gate(s) failed after Revit and have not been asked whether they could have failed sooner: " + string.Join("; ", rows.Select(r => r.Gate))\n'
     '            + ". Answer each: tools/gate_earliest.py answer <job.json> \\"<gate>\\" moved|no --because \\"...\\"";\n'
     '    }\n'
     '\n'
     '    /// <summary>give_answer: an answer row, or the refusal text Python raises SystemExit with.</summary>\n'
     '    public static string? GiveAnswer(string work, string gate, string what, string because, DateTime? now = null)\n'
     '    {\n'
     '        if (what is not ("moved" or "no")) return "answer: moved or no";\n'
     '        if (string.IsNullOrEmpty(because) || because.Length < 10) return "answer: --because says why, in words";\n'
     '        var owed = Owed(work);\n'
     '        if (!owed.Any(o => o.Gate == gate)) return $"answer: {Py.Repr(gate)} is not owed: [{string.Join(", ", owed.Select(o => Py.Repr(o.Gate)))}]";\n'
     '        var row = new PyDict().Set("kind", "answered").Set("gate", gate).Set("answer", what).Set("because", because)\n'
     '            .Set("at", (now ?? DateTime.Now).ToString("yyyy-MM-dd\'T\'HH:mm:ss"));\n'
     '        AppendPy(OwedPath(work), PyJson.Dump(row) + "\\n");\n'
     '        return null;\n'
     '    }\n'
     '}\n'
     'EOF\n'
     "grep -c '^        (@' $F",
     'allow'),
    # 8689: 24484c67-7a4f-42ec-a905-a48d47e32dbe, line 329, 2026-11-20T05:52:05.305Z
    # SHA-256 (UTF-8 command): f116ec0ddb3db7c112e854557e1863aeb759874e17a8566c6452324d80ac5eee
    ('GuardRefusal 8689',
     'cd "C:/VIsual Studio Projects/KOR.Drafter-revit" && git status --short && cat > "$TEMP/port-g0a-2/msg1.txt" <<\'EOF\'\n'
     'port-g0a-2: the hang fix - a batch NO REPLY keeps what the bridge ran (row 2084 item 1; row 2208; finding #134928)\n'
     '\n'
     "On a batch the bridge stops answering, the client now reads the batch's progress file (bridge\\batch\\<id>.jsonl, one\n"
     '{"name","reply"} line per request the bridge ran) and records those replies beside the requests as if the batch had\n'
     'answered; only the hung request and the rest of its batch are NO REPLY. A re-play then skips what Revit already ran.\n'
     '- IBridgeTransport.LastProgress (default empty); NetworkTransport fills it on a timeout, reading the file sharing write\n'
     "  (the bridge's StreamWriter holds it FileShare.Read). BridgePlay.Flush records in order up to the first line that is\n"
     '  not whole JSON or names another request.\n'
     '- Test HungBatchTests (fast; frozen fixture Halifax v6 2-model, RecordedBridge hanging at 117 of 232, local folder):\n'
     "  red at 7f979786 (recorded 0 of 116), green here: 116 of 116 recorded, equal by value to the bridge's reply; a re-play\n"
     '  sends 116 (the hung one on), re-sent 0 of 116.\n'
     '- ParityWithBridgePlayTests.AHangStops... updated: client ok = Python ok (116), the same reply files by value; the\n'
     '  difference left is named: the 115 after the hung one are NO REPLY lines on the client, never sent by Python.\n'
     'Searched first: BridgeBatch.Run / BridgeExec.Batch.cs (progress line format and writer share mode), the RecordedBridge\n'
     '(already writes the progress file), Transports.cs (the one place the progress path is known).\n'
     'Gates by name vs 450cb229 base trx: Core fast 46 of 2714 failing, all at base; PdfToSafe fast 7 of 612, all at base;\n'
     'Pdf 1 of 90, at base; Pipeline.Tests 69 of 69 (13,419 of 13,419 parity, queue 345 of 345).\n'
     '\n'
     'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n'
     'EOF\n'
     'git commit -q -F "$TEMP/port-g0a-2/msg1.txt" -- src/KOR.Drafter.BridgeClient/BridgePlay.cs src/KOR.Drafter.BridgeClient/Transports.cs src/KOR.Drafter.Pipeline.Tests/HungBatchTests.cs src/KOR.Drafter.Pipeline.Tests/ParityWithBridgePlayTests.cs && git show --name-only --oneline HEAD | head',
     'allow'),
    # 8708: 51d2fd14-eabc-404f-a29f-f18a4e6f4969, line 848, 2026-11-20T06:11:19.830Z
    # SHA-256 (UTF-8 command): 4d26af9f92127eef79d14a8fbe193014b7eb1798c31a653c8d1ee7f887524f4b
    ('GuardRefusal 8708',
     'mkdir -p "/c/VIsual Studio Projects/KOR.Drafter-plan/docs/port-need-evidence/port-g5a" && cat > /c/Users/kor-claude/g5a/state.md <<\'EOF\'\n'
     '# port-g5a state (19 Nov 23:1x)\n'
     'Branch port-g5a in C:\\VIsual Studio Projects\\KOR.Drafter-plan. Board row 2200.\n'
     'DONE (uncommitted, proven by `takeoff port-proof gates`): gate_earliest (18 cases + planted), e2k_audit_vs_plan, e2k_vertical, revit_audit_vs_plan (3 jobs each byte-identical + planted red).\n'
     'Code: Core/Gates/{GateEarliest,GatePy,E2kAuditVsPlan,E2kBook,E2kVertical,RevitAuditVsPlan}.cs; TakeoffCli/Verbs/{GateVerb,GateEarliestMain,PortProofGatesVerb}.cs; PortProofVerb + TakeoffVerbs registration.\n'
     'NEXT: revit_vs_etabs (+ --pieces) with revit_headers_vs_etabs merged; then schedule_vs_model, column_stack/bearing/clash/section; commit after fast suites (base fails Core 46, PdfToSafe 7, Pdf 2 in C:/Users/kor-claude/g5a/base/*.trx).\n'
     'EOF\n'
     'python -I "C:/VIsual Studio Projects/Framework/tools/kor_brain.py" state --lane "KOR.Drafter" --file /c/Users/kor-claude/g5a/state.md 2>&1 | tail -1',
     'allow'),
    # 8801: 5c5a10b5-ce8a-4fb3-9cbc-8938fb0393c3, line 181, 2026-11-20T06:43:21.764Z
    # SHA-256 (UTF-8 command): 4fe4621c01e16153632a8756d4887446c77385399f1364defd66752b46bd7686
    ('GuardRefusal 8801',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-revit" && date +"%d %b %H:%M %Z" && cat >> docs/port-need-evidence/port-g0a/LIVE-PLAY.txt <<\'EOF\'\n'
     '\n'
     '==========================================================================================================================\n'
     'PORT-G0A-3, 19 Nov 23:3x-23:4x BCDT (`date`), KOR-1001 as kor-claude, branch port-g0a at 512a54fb.\n'
     'The share Ian made: \\\\KOR-302N\\KorBridge = C:\\KOR.Drafter\\bridge on 302N, Modify for kor-claude. C$ was not tried.\n'
     '\n'
     'STATE: BLOCKED - played ONCE, NOT ANSWERED. The bridge on KOR-302N is not consuming its queue (Revit closed or the bridge not\n'
     'loaded). No retry, Revit not started. Bridge version: NOT READ (no ping reply).\n'
     '\n'
     'WHAT WAS SEEN BEFORE SENDING (read-only, 23:35-23:37):\n'
     '  inbox:  0 *.json waiting; 29 stale c2r-084029123-NN-query.tmp files from 28 Oct 10:11 (never renamed to .json, so the bridge\n'
     '          never sees them; not touched) and the folder done\\.\n'
     '  outbox: 187,554 *.json; the newest is batch-000-ping-326167.json, 16 Nov 19:57:46 (EnumerateFiles over the share, 1.2 s).\n'
     '          The last consumed request (inbox\\done) is the same ping, 16 Nov 19:57:45. Nothing answered on this queue since.\n'
     '  batch:  3,508 files.\n'
     '  The outbox is never pruned (board row 67).\n'
     '\n'
     'BUILD: dotnet build src/KOR.Drafter.Pipeline -c Release at 512a54fb, 0 errors (exit read through pipefail). kor-drafter.exe and\n'
     '  KOR.Drafter.BridgeClient.dll stamped 19 Nov 23:38.\n'
     '\n'
     "ATTEMPT 0, 23:38:25 - NOTHING SENT. In Git Bash the argument '\\\\KOR-302N\\KorBridge' reached the exe as '\\KOR-302N\\KorBridge'\n"
     "  (MSYS argument conversion strips a leading backslash), and NetworkTransport's constructor threw ArgumentException from\n"
     '  FileSystemWatcher ("directory \'\\KOR-302N\\KorBridge\\outbox\' does not exist") before any request was written (Transports.cs:56\n'
     '  runs before Exchange). Lesson: play a UNC --bridge-root from PowerShell or cmd, never Git Bash.\n'
     '\n'
     'THE PLAY, 23:38:42 - 23:40:44, from PowerShell:\n'
     '  kor-drafter.exe play %TEMP%\\lp-cs-233842 --computer KOR-302N --bridge-root \\\\KOR-302N\\KorBridge --timeout 120\n'
     '  (--bridge-root is checked before --computer in PlayCommand.cs, so the queue was the share; --computer was not used.)\n'
     '  Output, verbatim:\n'
     '    000-ping.json                                NO REPLY in 120 s of no progress\n'
     '    001-query.json                               NO REPLY in 120 s of no progress\n'
     '    2 file(s): 0 already done, 2 sent, 2 failed, 121 s\n'
     '  exit 1. No out-*.json written to the folder.\n'
     '  The C# client sent ONE batch file, inbox\\batch-000-ping-482555.json (170 bytes, written 23:38:42):\n'
     '    {"verb":"batch","continue":false,"group":true,"made":{},"requests":[{"name":"000-ping","cmd":{"verb":"ping"}},\n'
     '     {"name":"001-query","cmd":{"verb":"query","kind":"views"}}]}\n'
     '  At 23:41:03 it was still in inbox (not moved to done\\), and no batch\\*.jsonl progress file newer than 10 min existed: the bridge\n'
     '  never picked it up. It was not a slow reply: no progress at all.\n'
     '  CLEANUP: at 23:42:46 that ONE file was removed by its exact name (sha256 B11A38B2...2646F18), so it cannot run later as a second,\n'
     '  unwatched play when Revit next opens. inbox *.json afterwards: 0. Nothing else on the share was written or removed.\n'
     '\n'
     'WHAT THIS PROVES (and what it does not):\n'
     '  PROVES: the C# client works against the new share as a transport: it wrote its request atomically (.tmp then rename) into\n'
     '  \\\\KOR-302N\\KorBridge\\inbox as kor-claude, watched the outbox, and reported NO REPLY for both requests after 120 s with no\n'
     '  progress, without hanging, and exited 1.\n'
     "  DOES NOT PROVE: anything about the bridge's replies. No live reply was received, so the offline comparison through bridge_play.py's\n"
     '  reply reading (the ParityWithBridgePlayTests pattern) was NOT RUN: it has nothing to compare. The bridge version on 302N was\n'
     '  not read.\n'
     '\n'
     "WHAT UNBLOCKS IT (Ian's): open Revit on KOR-302N with the Drafter Bridge loaded and a document active (any read-only one), then the\n"
     '  lane re-runs exactly THE PLAY above, once, from PowerShell. Who closed Revit or when: NOT CHECKED (the share shows the last\n'
     '  answered request at 16 Nov 19:57; a remote process look goes through NetworkOps, whose sign-in is missing on this PC).\n'
     '\n'
     'C# REPLIES: none (sent, not answered).      PYTHON COMPARISON: not run (no replies).\n'
     'EOF\n'
     'git add docs/port-need-evidence/port-g0a/LIVE-PLAY.txt && git commit -q -m "port-g0a-3: LIVE-PLAY.txt - the one read-only play on KOR-302N via \\\\\\\\KOR-302N\\\\KorBridge SENT and NOT ANSWERED (bridge not consuming since 16 Nov 19:57); unconsumed request removed; no comparison possible (row 2084)\n'
     '\n'
     'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -- docs/port-need-evidence/port-g0a/LIVE-PLAY.txt && git show --name-only --format="%h %s" HEAD',
     'allow'),
    # 8891: 5750502b-789a-4668-9619-607de3348260, line 533, 2026-11-20T07:38:02.732Z
    # SHA-256 (UTF-8 command): 671cef0e5a48a272fec1861389746743dbc75247b327e34f208880039b7f34b6
    ('GuardRefusal 8891',
     "mkdir -p /c/Users/kor-claude/scratch-g5b2/pdcrun && cd /c/Users/kor-claude/scratch-g5b2/pdcrun && cat > pdcrun.csproj <<'EOF'\n"
     '<Project Sdk="Microsoft.NET.Sdk">\n'
     '  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>\n'
     '  <ItemGroup>\n'
     '    <ProjectReference Include="C:\\VIsual Studio Projects\\KOR.Drafter-geometry-gates\\Kor.Operations.EngineeringTools.Core\\Kor.Operations.EngineeringTools.Core.csproj" />\n'
     '    <ProjectReference Include="C:\\VIsual Studio Projects\\KOR.Drafter-geometry-gates\\Kor.Operations.EngineeringTools.Pdf.MuPdf\\Kor.Operations.EngineeringTools.Pdf.MuPdf.csproj" />\n'
     '  </ItemGroup>\n'
     '</Project>\n'
     'EOF\n'
     "cat > Program.cs <<'EOF'\n"
     'using Kor.Operations.EngineeringTools.Pdf;\n'
     'using Kor.Operations.EngineeringTools.Pdf.MuPdf;\n'
     'var sw = System.Diagnostics.Stopwatch.StartNew();\n'
     'var (lines, err, code) = PlotDraftingRun.Run(new MuPdfEngine(), args);\n'
     'Console.OutputEncoding = new System.Text.UTF8Encoding(false);\n'
     'foreach (var l in lines) Console.WriteLine(l);\n'
     'if (err.Length > 0) Console.Error.WriteLine(err);\n'
     'Console.Error.WriteLine($"[cs {sw.Elapsed.TotalSeconds:F1}s exit {code}]");\n'
     'return code;\n'
     'EOF\n'
     'grep -n "TargetFramework" "/c/VIsual Studio Projects/KOR.Drafter-geometry-gates/Kor.Operations.EngineeringTools.Pdf.MuPdf/Kor.Operations.EngineeringTools.Pdf.MuPdf.csproj"; dotnet build -v q -nologo -o out > build.txt 2>&1; echo exit $?; grep -E "error" build.txt | head -5',
     'allow'),
    # 8909: d0258715-45e4-41d3-9008-2ba89942b35f, line 1001, 2026-11-20T08:55:17.975Z
    # SHA-256 (UTF-8 command): abda27076d92618b33f46c826d93de4fa4d653647316683b384115ce3e793f8e
    ('GuardRefusal 8909',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-plan"; python -X utf8 -I - <<\'EOF\'\n'
     'import csv,subprocess,os\n'
     'rows=[r for r in csv.DictReader(open(r"C:\\VIsual Studio Projects\\Framework\\lanes\\2026-11-19\\port-groups-450cb229.csv",encoding="utf-8")) if r["brief"]=="g5a" and r["verdict"]=="DELETE-DEAD"]\n'
     'for root in [r"C:\\VIsual Studio Projects\\KOR.Drafter-plan", r"C:\\VIsual Studio Projects\\Operations", r"C:\\VIsual Studio Projects\\Framework"]:\n'
     '    for r in rows:\n'
     '        st=os.path.splitext(os.path.basename(r["file"]))[0]\n'
     '        pat=rf"(import|from)\\s+{st}\\b|{st}\\.py[\\"\']|\\"{st}\\.py|\'{st}\\.py|run\\.py\\(\\s*[\\"\']{st}"\n'
     '        p=subprocess.run(["git","-C",root,"grep","-n","-E",pat,"--","*.py","*.cs","*.ps1","*.sh","*.json"],capture_output=True,text=True,encoding="utf-8",errors="replace")\n'
     '        for l in p.stdout.splitlines():\n'
     '            if not l.startswith(r["file"]+":"): print(os.path.basename(root), l[:180])\n'
     'print("done")\n'
     'EOF',
     'allow'),
    # 8911: d0258715-45e4-41d3-9008-2ba89942b35f, line 1370, 2026-11-20T09:21:31.016Z
    # SHA-256 (UTF-8 command): 0136fd1af80974d6156e2ad6195aa1506c74cd80d1cfe057372e5938b404e0ae
    ('GuardRefusal 8911',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-plan" && cp C:/Users/kor-claude/g5a2/port-proof-gates-3.txt docs/port-need-evidence/port-g5a/port-proof-gates-3.txt && cat >> docs/port-need-evidence/port-g5a/PROOF-PORT-G5A.txt <<\'EOF\'\n'
     '\n'
     '======================================================================================================================================\n'
     "SESSION 2 (port-g5a-2, 20 Nov 01:14-), HANDOVER items 2-5. Oracle: tools/<gate>.py at this branch's tip (c94262f4 + this session's\n"
     'commits - no tools/<gate>.py of a g5a gate changed between); the Python is run by the proof itself (python -X utf8 tools/<gate>.py).\n'
     '\n'
     "SEARCHED FIRST (rule 1): `git grep` over *.cs for each gate's job before porting. markup_to_plan.inches = Plan/Read M2P.Inches (PORT-2),\n"
     'expand_sheet = Drafting/RevitRequests.ExpandSheet, pad_axes = RevitRequests.PadAxes (PORT-3), plan_balconies_from_arch.asked =\n'
     'Plan/Read/PlanBalconies.Asked (PORT-2d; made internal, not changed), shapely = Plan/Read/Geos GeosOps + Arch/GeosRect (GEOS 3.14.1),\n'
     'the PDF = Core/Pdf IPdfEngine (xref_object / xref_get_key / annots as PyMuPDF prints them). model-audit / precheck / Core/Review:\n'
     "none reads the engineer's written core dimensions, his drawn pad rectangles, the manifest's slab rings against the balcony ask, the\n"
     "fit's anchor, the bridge log or Revit's view collector replies - PORTED, not merged. takeoff's MuPDF reference: port-g1's exact\n"
     'TakeoffCli.csproj + Directory.Build.targets (byte-identical files, so the two branches merge without conflict).\n'
     '\n'
     'PROVEN (takeoff port-proof gates --sheet-audit-run <copy>, port-proof-gates-3.txt): 155 of 155 cases byte-identical stdout + exit\n'
     "code (and every file a case writes), 63 of 63 planted faults red on both sides; session 1's 101 cases and 42 planted are in it, re-run.\n"
     "  plan_core_dims_check     Halifax v6 + 31218 (virtual_drafter's 3 args), and each with --report (the report FILE byte-identical:\n"
     '                           schema 1 on Halifax, schema 2 on 31218 which carries core_dims_stated - STATED walked 3x, UNRESOLVED, EMPTY,\n'
     '                           PASS) + --require-pages <first page>; planted: every dimension Line\'s words 9\'-9".\n'
     '  plan_slab_edge_check     Halifax v6 (balconies, expect-storeys/groups, reviewed: the asked-against branch, 3 groups, the reviewed\n'
     "                           line), 31218, 31218 with its job's slab_steps_drawn (the stale-statement branch: 2 FAIL), hotel (composed);\n"
     '                           planted: a 1 m wide 200 mm notch in the first long slab edge.\n'
     '  THE ONE FOOTING GATE     Gates/FootingGates.cs: one frame (sheet by --sheet, mm_per_pt, offset, the fit, DrawnRects off the PDF) and\n'
     '                           three rules; EACH SOURCE TOOL keeps its own entry, lines and exit (review note), each proven on its own:\n'
     '    plan_footing_angles_check   Halifax (recorded + request mode), 31218 (P6, recorded); planted: every scheduled pad turned 37 deg.\n'
     '    plan_pad_footprint_check    Halifax recorded (--readback + --type-replies: the Revit and size rows, 19 of 19) + request mode;\n'
     '                                planted: the first newfooting request asked twice.\n'
     "    plan_strip_footings_check   Halifax recorded (--replies: Revit's areas and boxes) + request mode, 31218 request mode; planted:\n"
     '                                the first strip request 300 mm off.\n'
     '  fit_anchor_check         Halifax; planted: fit.arch_core_centre 500 mm east.\n'
     '  bridge_provenance        its recorded runs read \\\\<computer>\\C$ (an SMB share - not read here): each recorded window (3 jobs) is\n'
     '                           run on a folder of day logs WRITTEN for it (an older version 2 days before, the expected version the\n'
     '                           evening before - the read-back stops at the first day holding a start - and a same-version restart a\n'
     '                           minute into the run), --out compared as a file; plus --expect wrong, --days 0 (no start found) and a\n'
     '                           reversed window; planted: a different version coming up 5 minutes into the run.\n'
     "  sheet_audit              NOT on 31218's stale drafting fixture (#133763) and no other fixture holds a sheet-audit folder: a frozen\n"
     '                           COPY of the live 31218 run of 16 Nov 17:22 (work/31218-01-137th-street: 3-revit/{levels,build,fix},\n'
     "                           4-drafting/{requests,sheet-audit}, 4-drafting-sample/{requests,sheet-audit}; copied read-only to the lane's\n"
     '                           scratch, 171 MB) - `report` as sheet_audit_gate calls it (both folders) and `write` on the same catalog\n'
     '                           (every query file compared); planted: the first plan\'s view reply "ok": false (NO VIEW REPLY).\n'
     "  NOT COUNTED (named): the hotel's recorded plan_footing_angles_check and fit_anchor_check runs crash in the Python itself\n"
     '                           (StopIteration: the hotel has no "LEVEL  P1" sheet / no fit sheet - it names those gates not applicable,\n'
     '                           #124848); the C# crashes with the same error after the same bytes. The proof names a matched crash\n'
     "                           CRASH-BOTH and never counts it a case (row 2243's class: a shared crash is not a match).\n"
     '\n'
     "COMPARES: stdout bytes and exit code, and every file a case writes (--report, --out, write's folder: per-side path masked).\n"
     'DOES NOT COMPARE: stderr text; options no recorded or composed run passes (--min-iou / --slop-mm / --size-mm / --tol-mm overrides,\n'
     '  --before-plan / --before-requests of the angles gate, --preexisting of sheet_audit, plan_slab_edge_check --n); 01819 (no run.json).\n'
     "SAME-CLASS FAULT IT WOULD MISS: a branch no fixture walks - the STATED-found branch of --drawn-steps (31218's fixture manifest predates\n"
     '  the roof steps: only the stale branch is walked), the angles gate\'s before-* checks, sheet_audit\'s "above the view range" branch if\n'
     "  the copy has no wall above its plan's top (not checked), a polygon intersection a strip cut returns (raised on both, not met).\n"
     'DEFECTS TRAVEL (brief-port-common item 6): ported as the Python does them, none fixed.\n'
     '\n'
     'DELETE-DEAD: 24 of 24 re-proved and deleted (017100cd, 0858b4bd, 654b9df3; dead-24-referrers.tsv).\n'
     'NOT PORTED: delegated_check_execution.py (MERGE row, 425 lines) - no stage, no run.json and no tool runs it: its only caller is its own\n'
     '  test. Its job is to snapshot the PYTHON checker sources (plan_core_dims_check.py, plot_vs_markup.py, ...) and run them in child\n'
     "  processes as evidence; a C# merge needs plot_vs_markup in C# first (g5b's group). Listed in callers-to-switch.txt; DECISION for the\n"
     '  lead on row 2200: delete it as dead with its test, or merge after g5b.\n'
     'EOF\n'
     'echo ok',
     'allow'),
    # 8920: 780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7, line 1359, 2026-11-20T10:45:03.086Z
    # SHA-256 (UTF-8 command): 1c251a1ce631445cf61b25f83b2f550e5aba47a7def60fd78fd82808788a9595
    ('GuardRefusal 8920',
     "mkdir -p /c/Users/kor-claude/g68-2 && cat > /c/Users/kor-claude/g68-2/state.txt <<'EOF'\n"
     'port-g6-8-2 (row 2202, lane port-g6-8-2) state 20 Nov ~03:5x, folder C:\\VIsual Studio Projects\\KOR.Drafter-quantities, branch port-g6-8.\n'
     'DONE: item 0 kor_review 5 of 5 approve, pushed fs01 port-g6-8 = 21f1c8ea. item (a) merge_pdfs in C# via whole-page copy\n'
     '(Pdf.MuPdf/PageGraft.cs, seam InsertPdfWhole): 923/923 Halifax annots, 2053/2053 markups, proof 1281/1281, seed 1278/1281,\n'
     'commit e5095016 (NOT pushed, not reviewed yet).\n'
     'HALF-DONE (uncommitted): revit_model_pdf port = Core/Package/RevitModelPdf.cs + seam TextBlockRects/ShowPdfPage + PackageVerb\n'
     'model-pdf (kor-drafter play as bridge client) + PortProofPackagePdfsVerb ModelPdfCase (frozen model-pdf folders of 3 jobs,\n'
     'stub play; --only model-pdf). First proof run in flight to C:/Users/kor-claude/g68-2/mp1.txt.\n'
     'NEXT: get model-pdf proof equal (or named diffs) + seed + look; xUnit for VD-8/4A-16 (C# side) and action-items scale checks;\n'
     "gates; commit; (b) e2k_book proposal on row; kor_review all unpushed; push fs01; handover; row 'doing'.\n"
     'EOF\n'
     'python -I "C:/VIsual Studio Projects/Framework/tools/kor_brain.py" state --lane "KOR.Drafter" --file /c/Users/kor-claude/g68-2/state.txt 2>&1 | tail -2',
     'allow'),
    # 8921: 2d0ec830-6eee-449a-8361-af26bbffa277, line 1148, 2026-11-20T10:47:36.101Z
    # SHA-256 (UTF-8 command): 8aae549738fda0850e4f5e79c52ac5532516ffe96f47fc0a6340345be68186e8
    ('GuardRefusal 8921',
     'cd "C:/VIsual Studio Projects/KOR.Drafter-geometry-gates/Kor.Operations.EngineeringTools.Core" && python -I - <<\'EOF\'\n'
     "p='Pdf/PlotChecks.cs'\n"
     "s=open(p,encoding='utf-8').read()\n"
     'import re\n'
     's=re.sub(r"\\(\\\\x0b, \\\\x1c, .{1,3} in a tool\'s output\\)", "(a vertical tab, a file separator or a Unicode line separator in a tool\'s output)", s)\n'
     "assert '\\u2028' not in s\n"
     'reps=[\n'
     "('    public sealed record Result(List<PlotGate> Gates, List<string> NotPorted);',\n"
     " '''    /// <summary>Gates: what was computed, in order. NotPorted: (gate name as the Python records it, why) for each gate the Python\n"
     '    /// would record here and this set does not compute. Notes: a deviation in a computed gate, stated.</summary>\n'
     '    public sealed record Result(List<PlotGate> Gates, List<(string Gate, string Why)> NotPorted, List<string> Notes);\n'
     '\n'
     '    public const string SheetAuditGate = "every element we built shows in its placed plan view, every tag asked for is there, every plan\'s crop on (sheet_audit)";\'\'\'),\n'
     "('var gates = new List<PlotGate>(); var notPorted = new List<string>();',\n"
     " 'var gates = new List<PlotGate>(); var notPorted = new List<(string, string)>(); var notes = new List<string>();'),\n"
     '(\'\'\'                notPorted.Add($"{name}: v5_acceptance_policy.delegation_deferral was not asked (the Halifax v5 policy is not ported); computed as if it answered None");\'\'\',\n'
     ' \'\'\'                notes.Add($"{G(name)}: v5_acceptance_policy.delegation_deferral was not asked (the Halifax v5 policy is not ported); computed as if it answered None");\'\'\'),\n'
     '(\'\'\'        if (exists) notPorted.Add("no assumption cloud takes in a column or footing that is not its own: plot_cloud_marks.py is not C# (it needs g2\'s settle port: settle_dims.plot_map, settle_text.items_for)");\'\'\',\n'
     ' \'\'\'        if (exists) notPorted.Add((G("no assumption cloud takes in a column or footing that is not its own"), "plot_cloud_marks.py is not C# (it needs g2\'s settle port: settle_dims.plot_map, settle_text.items_for)"));\'\'\'),\n'
     '(\'\'\'            notPorted.Add("the job\'s own values printed in the cover notes, the template\'s gone: cover_sheets.cover_text_check is not C#");\'\'\',\n'
     ' \'\'\'            notPorted.Add((G("the job\'s own values printed in the cover notes, the template\'s gone"), "cover_sheets.cover_text_check is not C#"));\'\'\'),\n'
     '(\'\'\'                notPorted.Add($"{name}: the run has drafting requests, so the Python adds --requests (H9\'s own lines): not C# until g2\'s settle port - the gate is not computed");\'\'\',\n'
     ' \'\'\'                notPorted.Add((G(name), "the run has drafting requests, so the Python adds --requests (H9\'s own lines): not C# until g2\'s settle port"));\'\'\'),\n'
     '(\'\'\'        notPorted.Add("every element we built shows in its placed plan view, every tag asked for is there, every plan\'s crop on (sheet_audit): sheet_audit.py is not C# on this branch (g5a)");\n'
     "        return new Result(gates, notPorted);''',\n"
     " '''        // NOT ASKED needs no reply reader; a catalog's replies are read by sheet_audit.py, not C# on this branch (g5a)\n"
     '        if (!File.Exists(Path.Combine(run.DraftingDir, "sheet-audit", "catalog.json")))\n'
     '            Gate(SheetAuditGate, false, "NOT ASKED: no sheet-audit/catalog.json - Revit was never asked what each placed plan view shows");\n'
     '        else notPorted.Add((G(SheetAuditGate), "sheet_audit.py report is not C# on this branch (g5a ports it)"));\n'
     "        return new Result(gates, notPorted, notes);'''),\n"
     "('''/// NOT PORTED, and so named in Result.NotPorted (never a silent skip, never a pass): plot_cloud_marks (g2's settle port),\n"
     "/// cover_sheets.cover_text_check, H9's --requests (g2), sheet_audit (g5a's sheet_audit.py), and v5_acceptance_policy's\n"
     '/// delegation_deferral (the Halifax v5 policy is not ported - brief g5b): where the Python would ask it, NotPorted says so\n'
     "/// and the gate is computed as if it answered None.''',\n"
     "'''/// NOT PORTED, and so named in Result.NotPorted with the gate's own name (never a silent skip, never a pass): plot_cloud_marks\n"
     "/// (g2's settle port), cover_sheets.cover_text_check, the red-pen gate when the run has requests (H9's --requests, g2), and\n"
     "/// sheet_audit when a catalog exists (g5a's sheet_audit.py; NOT ASKED is computed). Stated in Result.Notes: v5_acceptance_policy's\n"
     '/// delegation_deferral (the Halifax v5 policy is not ported - brief g5b) - where the Python would ask it, the gate is computed\n'
     "/// as if it answered None.'''),\n"
     ']\n'
     'for a,b in reps:\n'
     '    assert a in s,a[:60]; s=s.replace(a,b)\n'
     "open(p,'w',encoding='utf-8',newline='').write(s)\n"
     'EOF\n'
     'grep -rlP "\\x{2028}" --include=*.cs . ../Kor.Operations.EngineeringTools.Pdf.Tests ../src 2>/dev/null; cd .. && set -o pipefail && dotnet build KOR.Drafter.sln 2>&1 | grep -E " error |Build succeeded|Time Elapsed" | sort -u | head -20',
     'allow'),
    # 8922: 780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7, line 1877, 2026-11-20T11:06:06.944Z
    # SHA-256 (UTF-8 command): 30a68b2cf98847c4a9d5e91445b3d607b41645ebbb77d91012710e29c7b7f536
    ('GuardRefusal 8922',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-quantities" && cat >> docs/port-need-evidence/port-g6-8/PROOF-PORT-G6-8.md <<\'EOF\'\n'
     '\n'
     '## 4. revit_model_pdf.py PORTED (group 8; it follows (a): the merge passed)\n'
     '\n'
     "Core/Package/RevitModelPdf.cs, `takeoff package model-pdf` (the Python's arguments; --bridge-client <kor-drafter.exe>, by\n"
     "default the checkout's src/KOR.Drafter.Pipeline/bin/Release/net8.0/kor-drafter.exe, virtual_drafter's KOR_DRAFTER). The same\n"
     'request folders (open, views, export) and JSON, the same reply checks (VD-8 a failed or unanswered setscale fails the step;\n'
     "4A-16 a failed step leaves no model PDF at the success path, a prior run's included), the same compose (each view's ink -\n"
     'paths and text blocks, a white background fill over half the page left out, padded 2% - on its own 11x17 sheet under a title\n'
     'band). Seam: page.get_text("blocks") rects and show_pdf_page. Searched first: no C# composes Revit view PDFs; the bridge\n'
     "client is `kor-drafter play` (bridge_play.py's contract, board row 2084 retires bridge_play.py) - the Python ran\n"
     'bridge_play.py with --continue --timeout 900 --filter ^\\d{3}-, the C# runs kor-drafter play with the same.\n'
     'The Python never ran merge_pdfs (its docstring says so; the code composes with show_pdf_page) - callers-to-switch corrected.\n'
     '\n'
     "PROOF (`takeoff port-proof package-pdfs`, case ModelPdfCase; `--only model-pdf` runs it alone): the three jobs' FROZEN\n"
     "model-pdf folders (C:/kor-suite/harness-fixtures/<job>/4-drafting/model-pdf: the requests, the bridge's replies and the\n"
     "exported view PDFs of a real run; read only), Python vs C#, the same arguments (virtual_drafter's: --open-detached and\n"
     "--focus from the frozen open requests, --box-xy from the frozen view request, --levels the job's tracked levels.csv,\n"
     '--job-title from job.json), a stub play on each side that hands back the frozen replies (and for the export the frozen view\n'
     "PDFs). Compares: printed lines + exit; every request file both sides wrote, byte for byte, AND against the frozen run's own\n"
     "request files (open, views); the composed PDF's page count, outline, page text, boxes and annotation lists; every page\n"
     'rendered at 72 dpi, tolerance stated: at most 0.1% of pixels with a channel off by more than 2.\n'
     '    Halifax v6 27 pages, 31218 36, hotel 10: printed, requests (both sides and vs frozen), text, boxes equal; renders within\n'
     "    tolerance on 68 of 73 pages; 5 pages NAMED, each checked by the proof: the page's content is equal but for how a real\n"
     '    under 1 is written (PyMuPDF ".75 w", MuPDF.NET "0.75 w") and its show_pdf_page forms are equal but for /Matrix - PyMuPDF\n'
     "    writes the view's scale to 5 decimals (.51168, 1.06791), MuPDF.NET to 7 digits (.5116787 = 624 / 1219.51529, the exact\n"
     "    fit). Scratch check (not shipped): copying the Python's /Matrix into the C# file's forms gives 0 pixels off on 73 of 73\n"
     '    pages - the matrix is the whole difference. The C# saves deflated: MuPDF.NET copies each view stream decompressed\n'
     '    (6.2 MB against 2.2 MB on 31218 before; 2.18 / 4.25 / 1.12 MB against 2.17 / 4.22 / 1.11 MB after).\n'
     '    LOOKED AT (helper, rule 9): page 5 of each job, both sides - the same title, subtitle and storey view, filling the area,\n'
     "    nothing cut off or garbled. It also saw three faults the Python has too, which travel per the port rules: 31218's subtitle\n"
     '    prints "&amp;" (the job\'s cover title as stored), "LEVEL  P1" keeps the levels.csv double space, and the hotel\'s subtitle\n'
     '    is the folder name "6th-g-hotel " (no job number or cover title in its job.json).\n'
     "PLANTED: --seed-fault withholds Halifax's out-003-setscale.json from the C# stub: the C# prints FAILED and exits 1 where the\n"
     'Python composes - the run exits 1. In the code: ignoring the setscale replies turned 3 of 5 RevitModelPdfTests red - reverted.\n'
     'NOT PROVEN OFFLINE: the bridge call itself (kor-drafter play against Revit) and the admin-share fetch-back - they need Revit;\n'
     'the first live model-pdf run proves them (the requests it sends are proven equal above).\n'
     'SAME-CLASS MISS: a view whose ink box differs only by a stroke the 72-dpi render cannot show.\n'
     'xUnit (Pdf.Tests): RevitModelPdfTests (5; VD-8 and 4A-16 of model_pdf_gate_red_test.py on the C#, stand-in play, real view\n'
     'PDFs) and ActionItemsScaleTests (7; the tool cases of action_items_on_sheets_test.py on the C# ActionItemsOnSheets). Both\n'
     "Python tests stay: their other subjects (virtual_drafter's circles_spec, model_pdf_gate, tag_census; drafted_set) are live\n"
     'Python, and their tool cases still cover the live .py files.\n'
     'EOF\n'
     "python -I - <<'EOF'\n"
     'p = r"docs/port-need-evidence/port-g6-8/callers-to-switch.txt"\n'
     's = open(p, encoding="utf-8", newline="").read()\n'
     's += """revit_model_pdf.py        -> Core/Package/RevitModelPdf.cs (takeoff package model-pdf; PROOF section 4)\n'
     '    tools/virtual_drafter.py            g0b   stage_drafting: run.py("revit_model_pdf.py", ...model-pdf, 5-revit-model.pdf, --open-detached ...)\n'
     '    tools/model_pdf_gate_red_test.py    g6-8  VD-8 / 4A-16 moved to Pdf.Tests RevitModelPdfTests; kept (its other subjects are live Python)\n'
     '    tools/bridge_call_machines.py       ?     names it in TOOLS (a check of stage literals) - its own row decides\n'
     '    tools/post_revit_replay.py          g5a   REVIT_TOOLS list (a replay filter)\n'
     "action_items_on_sheets_test.py      tool cases moved to Pdf.Tests ActionItemsScaleTests; kept (circles_spec is virtual_drafter's)\n"
     "e2k_book.py               STAYS PYTHON (lead decision (b), 20 Nov): a C# re-plot changes how a deliverable looks - Ian's call;\n"
     '                                     proposal on row 2202. e2k_book.read is also imported by e2k_headers, e2k_selfweight,\n'
     '                                     e2k_to_glb, e2k_vertical.\n'
     '"""\n'
     'open(p, "w", encoding="utf-8", newline="").write(s)\n'
     'EOF\n'
     'tail -3 docs/port-need-evidence/port-g6-8/callers-to-switch.txt',
     'allow'),
    # 8924: c779e271-25a7-4297-807b-4090b8483dc3, line 339, 2026-11-20T11:40:27.566Z
    # SHA-256 (UTF-8 command): 0288ce56d1e144686b4226c5b595680c6800bf2419135aea5948ed29352bceb3
    ('GuardRefusal 8924',
     'cd "C:/VIsual Studio Projects/KOR.Drafter-viewer/Kor.Operations.EngineeringTools.Core" && python -I -X utf8 - <<\'EOF\'\n'
     'p = "Plan/Read/Arch/ArchLook.cs"\n'
     's = open(p, encoding="utf-8").read()\n'
     'old_comp_start = s.index("        // Image.alpha_composite(im.convert(\\"RGBA\\"), lay).convert(\\"RGB\\")")\n'
     'old_comp_end = s.index("        Png.WriteRgb(outPng, W, H, outRgb);")\n'
     's = s[:old_comp_start] + "        // Image.alpha_composite(im.convert(\\"RGBA\\"), lay).convert(\\"RGB\\")\\n        var outRgb = lay.CompositeOverOpaque(rgb);\\n" + s[old_comp_end:]\n'
     's = s.replace("    static uint Div255(uint a) => ((a >> 8) + a) >> 8;\\n    static uint Rgba(", "    internal static uint Rgba(")\n'
     's = s.replace("    sealed class Layer\\n    {\\n        public readonly uint[] Px; readonly int W, H;",\n'
     '"""    internal sealed class Layer\n'
     '    {\n'
     '        public readonly uint[] Px; public readonly int W, H;""")\n'
     'anchor = "        void Point(int x, int y, uint ink)"\n'
     'add = \'\'\'        /// <summary>Image.alpha_composite(rgb.convert("RGBA"), this).convert("RGB"): ImagingAlphaComposite\'s integer blend over an opaque\n'
     '        /// RGB image (dst alpha 255), the alpha dropped.</summary>\n'
     '        public byte[] CompositeOverOpaque(byte[] rgb)\n'
     '        {\n'
     '            var outRgb = new byte[W * H * 3];\n'
     '            for (int i = 0, n = W * H; i < n; i++)\n'
     '            {\n'
     '                uint ink = Px[i];\n'
     '                uint sr = ink & 0xff, sg = (ink >> 8) & 0xff, sb = (ink >> 16) & 0xff, sa = ink >> 24;\n'
     '                uint dr = rgb[i * 3], dg = rgb[i * 3 + 1], db = rgb[i * 3 + 2], da = 255;\n'
     '                if (sa == 0) { outRgb[i * 3] = (byte)dr; outRgb[i * 3 + 1] = (byte)dg; outRgb[i * 3 + 2] = (byte)db; continue; }\n'
     '                const int P = 7;\n'
     '                uint blend = da * (255 - sa);\n'
     '                uint outa255 = sa * 255 + blend;\n'
     '                uint coef1 = sa * 255 * 255 * (1u << P) / outa255;\n'
     '                uint coef2 = 255 * (1u << P) - coef1;\n'
     '                outRgb[i * 3] = (byte)(Div255(sr * coef1 + dr * coef2 + (0x80u << P)) >> P);\n'
     '                outRgb[i * 3 + 1] = (byte)(Div255(sg * coef1 + dg * coef2 + (0x80u << P)) >> P);\n'
     '                outRgb[i * 3 + 2] = (byte)(Div255(sb * coef1 + db * coef2 + (0x80u << P)) >> P);\n'
     '            }\n'
     '            return outRgb;\n'
     '        }\n'
     '\n'
     '        static uint Div255(uint a) => ((a >> 8) + a) >> 8;\n'
     '\n'
     "'''\n"
     's = s.replace(anchor, add + anchor, 1)\n'
     'open(p, "w", encoding="utf-8", newline="").write(s)\n'
     'EOF\n'
     'grep -n "outRgb\\|Div255\\|internal sealed class Layer\\|internal static uint Rgba" Plan/Read/Arch/ArchLook.cs; sed -n 40,48p Plan/Read/Arch/ArchLook.cs',
     'allow'),
    # 8929: 780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7, line 2291, 2026-11-20T12:11:44.141Z
    # SHA-256 (UTF-8 command): cf67c6e941b23b326bc246d82ad8d5745caa60bc470a19d2e7e2d7c1bb0afbd7
    ('GuardRefusal 8929',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-quantities" && git commit -q -F - -- docs/port-need-evidence/port-g6-8/HANDOVER-PORT-G6-8.md 2>&1 <<\'EOF\' | grep -v "LF will be replaced"\n'
     "port-g6-8 handover (row 2202): session 2's unproven paths and the review rows, stated\n"
     '\n'
     "The two refusal paths no frozen input takes (the proof's levels_csv without jobs\\, AnnotFingerprint's unfollowable /Annots)\n"
     'are named as unproven; the push and its reviews (2476, 2477, 2486, 2487) recorded. Docs only.\n'
     '\n'
     'Claude-Session: 780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7\n'
     'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n'
     'EOF\n'
     'git show --name-only --format="%h %s" HEAD | cut -c1-140; timeout 1800 python -I "C:/VIsual Studio Projects/Framework/tools/kor_review.py" review > /c/Users/kor-claude/g68-2/review5.txt 2>&1; echo review rc=$?; grep -E "APPROVE|CHANGES|MAJOR" /c/Users/kor-claude/g68-2/review5.txt | cut -c1-200',
     'allow'),
    # 8961: e7287342-6067-4ac2-818a-e923a9744797, line 1162, 2026-11-20T18:29:02.944Z
    # SHA-256 (UTF-8 command): 2b1570b94869e26d3d7311e4887ec31a567f73aa833b200b6953e4fc06c830d2
    ('GuardRefusal 8961',
     'cd "/c/VIsual Studio Projects/KOR.Drafter-step5" && git diff --stat -- tools/SafeOpenCheck/Program.cs && git commit -q -F - -- tools/SafeOpenCheck/Program.cs <<\'EOF\'\n'
     "row 2143 (Ian 20 Nov #135736): SafeOpenCheck 5b keeps and hashes the pre-analysis FDB; 8f writes SAFE's mesh map\n"
     '\n'
     "Codex pass 2 left items 1, 3a, 4, 9 open because the shipped .FDB is the analysis's own later save (31097 B1 L02 set\n"
     'colstiff2: item 5 save 240,094 bytes, shipped 827,925) and no element/joint map existed. With --run, 5b copies\n'
     'safe-check.FDB as item 5 left it to safe-check.pre-analysis.FDB (read sharing read/write), hashes both in full, and only\n'
     'when equal writes identity.sha256 "<64 hex>  safe-check.pre-analysis.FDB" - the line model-audit\'s identity-record reads;\n'
     'its failure is a STOPPED step. With --export-results, 8f writes every table SAFE lists whose key starts "Objects and\n'
     'Elements" into results\\mesh\\ (SAFE 22.7 Table and Field Keys.xml, da7a5a4a, = KOR-210\'s copy: - Joints ElmName ObjType\n'
     'ObjName GlobalX/Y/Z, - Areas ElmJt1-4, - Frames ElmJtI/J); a failure is a line, never the verdict. --self-test 40 of 40\n'
     '(3 new: no FDB, byte-equal copy + regex-valid full-hash line, reused folder). UNPROVEN in SAFE until its KOR-210 run.\n'
     '\n'
     'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n'
     'EOF\n'
     'git log --oneline -1',
     'allow'),
    # 8978: ee8754e5-8d40-4410-94b9-004d09152ab6, line 2158, 2026-11-20T20:03:47.407Z
    # SHA-256 (UTF-8 command): 0c7d1e86dd613a33bbbe321f907f8fcb5adbaa15917da789c6c2b6436a22ae64
    ('GuardRefusal 8978',
     'cd "/c/VIsual Studio Projects/Framework" && date && cat > "$TEMP/state-cleanup.md" <<\'EOF\'\n'
     'Framework lead ee8754e5 (kor-claude), 20 Nov ~13:0x - CLEANUP Ian ordered ("I want this all cleaned up and organized properly"; "go ahead with the worktrees and stashes"). Rule: Office #119316 - save to fs01 archive/<name> before any delete. Row 1975 holds evidence.\n'
     'DONE KOR.Drafter: checkout tracked dirt 27 -> archive/2026-11-20-kordrafter-checkout (d3a48bca); docs/codex 16 + 3 notes committed (c06065cf); worktrees 30 -> 10 (kept: 9 port lanes + step5 + freeze-004c30c1 4.8 GB ignored baseline at C:/kor-suite); 2 dirty ones archived (archive/2026-11-20-wt-agent-a823fa61f9b9a5e9f, -wt-KOR.Drafter-wt-engine0); 5 others\' "dirt" was line endings only. Stashes 9 -> 2: 5 archived (archive/2026-11-20-stash-kd-{1,4,5,7,8}-*), 2 fully covered elsewhere dropped.\n'
     "MISTAKES: git worktree prune also dropped the git records of 5 worktrees in Ian's temp (C:/Users/ilalonde/AppData/Local/Temp/claude/.../scratchpad: drafter, drafter-port3, drafter-port4, wt-head, wt-parent) - unreadable to kor-claude; their HEADs are on fs01; folders untouched; Ian can re-link with git worktree repair or delete. 9 leftover locked .claude/settings.json in deleted worktree folders (C:/VIsual Studio Projects/KOR.Drafter-{pdfspike,port2,wt-*}) - Ian's admin window deletes.\n"
     'WAITING ON IAN: (1) stash@{0} (31097 fix-all partial, flattened 161a1539, 382k diff over review cap) and stash@{1} (PRECHECK scan widen, 440104a6 review CHANGES on the stashed code) - push-exception needs his yes (#134070); (2) FS01 archive grant on \\\\KOR-FS01\\E$\\IT\\archive\\2026-11-20 only (the parent grant was stopped) for KOR.Drafter root scratch 3.2 GB + 01819 lateral runs + freeze-004c30c1.\n'
     "NEXT: Operations 13 worktrees / 33 stashes; Framework 1 stash + 91 untracked (lanes/, _lane*); then branches. Do not touch port worktrees/branches (step 6 on hold) or KOR.Drafter-step5 (2143 in Ian's window). Inspections stashes: Ian's (#120568).\n"
     'EOF\n'
     '"C:/KOR-Operations/kor/kor.exe" state --module Framework --file "$TEMP/state-cleanup.md" 2>&1 | tail -1',
     'allow'),
]

# Rule 3b, 21 Nov: constructed boundaries. These are command TEXT passed to the guard,
# never scripts to execute. Quoted file writes stay verbatim even for source-code files;
# checking a future interpreter/compiler would require another rule, outside this one.
HEREDOC_BOUNDARIES = [
    ("C# word boundary through Python", r'''python - <<'PY'
s = '@"\bLEVEL\b"'
PY''', "deny"),
    ("C# digits through Python", r'''python - <<'PY'
s = '@"\d+"'
PY''', "deny"),
    ("C# whitespace through Python", r'''python - <<'PY'
s = '@"\s+"'
PY''', "deny"),
    ("C# manually doubled regex through Python (4970 class)", r'''python - <<'PY'
s = '@"\\bLEVEL\\s+\\d+"'
PY''', "deny"),
    ("Windows Unicode-looking path", r'''python - <<'PY'
p = 'C:\Users\x'
PY''', "deny"),
    ("Windows octal-looking path", r'''python - <<'PY'
p = 'C:\6\file'
PY''', "deny"),
    ("Windows vertical-tab path", r'''python - <<'PY'
p = 'C:\var\file'
PY''', "deny"),
    ("Windows null path", r'''python - <<'PY'
p = 'C:\0\file'
PY''', "deny"),
    ("UNC path through non-raw Python", r'''python - <<'PY'
p = '\\server\share'
PY''', "deny"),
    ("Relative Windows path", r'''python - <<'PY'
p = 'work\bin\Debug'
PY''', "deny"),
    ("Unquoted cat UNC", r'''cat <<EOF
\\server\share
EOF''', "deny"),
    ("Unquoted tee doubled slash", r'''tee note.txt <<EOF
two \\ slashes
EOF''', "deny"),
    ("Unquoted git message UNC", r'''git commit -F - <<EOF
\\server\share
EOF''', "deny"),
    ("Unquoted Python raw cannot protect against Bash", r'''python - <<PY
p = r'\\server\share'
PY''', "deny"),
    ("Quoted cat Windows paths and regexes", r'''cat > note.txt <<'EOF'
C:\Users\x and ^\b\d+\s*$ and \\server\share
EOF''', "allow"),
    ("Quoted tee Windows paths", r'''tee note.txt <<"EOF"
C:\var\6 and \\server\share
EOF''', "allow"),
    ("Quoted git message regex", r'''git commit -F - <<'EOF'
Fix \b and C:\Users\x
EOF''', "allow"),
    ("Quoted Python-file write has no Python consumer", r'''cat > x.py <<'PY'
s = '@"\bLEVEL\b"'
PY''', "allow"),
    ("Later file execution is outside this rule", r'''cat > x.py <<'PY'
s = '@"\bLEVEL\b"'
PY
python x.py''', "allow"),
    ("Quoted C#-file write", r'''cat > x.cs <<'CS'
var p = @"C:\Users\x"; var regex = @"\bLEVEL\s+\d+";
CS''', "allow"),
    ("Quoted PowerShell-file write", r'''cat > x.ps1 <<'PS'
$p = 'C:\Users\x'; $pattern = '\bLEVEL\d+'
PS''', "allow"),
    ("Raw Python paths, bytes, and regexes", r'''python - <<'PY'
a = r'C:\Users\x'
b = R'\bLEVEL\s+\d+'
c = br'\b\d+\s+'
d = rb'\b\d+\s+'
PY''', "allow"),
    ("Raw f-string including escaped quotes", r'''python - <<'PY'
a = rf"\b{name}\s+[\"']"
b = fr'\b{name}\d+'
PY''', "allow"),
    ("Intentional formatting and Unicode escapes", r'''python - <<'PY'
a = '\n\r\t\x00\x01\u2028\U0001f600\N{SNOWMAN}\a\f\v'
PY''', "allow"),
    ("Intentional JSON data with quotes and slash", r'''python - <<'PY'
a = {'x': '\n\"\\\t\x01\u2028'}
PY''', "allow"),
    ("Comments do not interpret Python escapes", r'''python - <<'PY'
# C:\Users\x, \b and \03 are documentation here.
print('done')
PY''', "allow"),
    ("Raw triple quoted string with internal quotes", r'''python - <<'PY'
a = r"""C:\Users\x and "quoted" \b\d+"""
PY''', "allow"),
    ("Non-raw triple quoted string", r'''python - <<'PY'
a = """var r = @"\bLEVEL\s+\d+";"""
PY''', "deny"),
    ("Non-raw f-string", r'''python - <<'PY'
a = f'@"\b{name}\d+"'
PY''', "deny"),
    ("Raw literal does not mask following non-raw literal", r'''python - <<'PY'
a = r'\b' + '\d+'
PY''', "deny"),
    ("Python then cat use their own bodies", r'''python - <<'EOF'
print('\n')
EOF
cat <<'EOF'
C:\Users\x
EOF''', "allow"),
    ("Cat then Python cannot exempt unsafe Python", r'''cat <<'EOF'
C:\Users\x
EOF
python - <<'EOF'
s = '@"\bLEVEL\b"'
EOF''', "deny"),
    ("Bit shifts inside Python are not nested heredocs", r'''python - <<'PY'
s = '1 << SHIFT; documentation'
x = 1 << SHIFT
print('\x01')
PY''', "allow"),
    ("Heredoc spelling in a quoted argument is not a redirect", r'''printf '%s' "<<EOF
C:\Users\x
EOF"''', "allow"),
    ("Backslash-quoted delimiter", r'''cat <<\EOF
\\server\share
EOF''', "allow"),
    ("Partly quoted delimiter", r'''cat <<E'OF'
\\server\share
EOF''', "allow"),
    ("Tab-stripping quoted heredoc", "cat <<-'EOF'\n\tC:\\Users\\x\n\tEOF", "allow"),
    ("Tab-stripping unquoted heredoc", "cat <<-EOF\n\t\\\\server\\share\n\tEOF", "deny"),
    ("A script's stdin is data, not Python source", r'''python reader.py <<'EOF'
C:\Users\x
EOF''', "allow"),
    ("Python -c reads input as data", r'''python -I -c 'print(input())' <<'EOF'
C:\Users\x
EOF''', "allow"),
    ("Python -m reads input as data", r'''python -m reader <<'EOF'
C:\Users\x
EOF''', "allow"),
    ("Python implicit stdin source", r'''python <<'EOF'
s = '@"\bLEVEL\b"'
EOF''', "deny"),
    ("Python executable path and wrapper", r'''env FLAG=1 timeout 30 "C:/Program Files/Python312/python.exe" -I -X utf8 - <<'EOF'
s = '@"\d+"'
EOF''', "deny"),
    ("Windows Python launcher", r'''py -3 - <<'EOF'
s = '@"\s+"'
EOF''', "deny"),
    ("Quoted non-Python interpreter", r'''pwsh -Command - <<'EOF'
$p = 'C:\Users\x'
EOF''', "allow"),
    ("Unquoted single slash to cat stays literal in Bash", r'''cat <<EOF
C:\Users\x and \P
EOF''', "allow"),
    ("Two same-line heredocs have separate consumers", r'''cat <<'A'; python - <<'B'
C:\Users\x
A
s = '@"\bLEVEL\b"'
B''', "deny"),
    ("Two same-line safe heredocs", r'''python - <<'A'; cat <<'B'
print('\u2028')
A
C:\Users\x
B''', "allow"),
]


# Review of real 8929 found that 2>&1 was being split as a command separator. It did
# not change that git-message verdict, but would hide a Python consumer before '-'.
HEREDOC_BOUNDARIES += [
    ("Descriptor duplication before Python stdin marker", r'''python 2>&1 - <<'PY'
s = '@"\bLEVEL\b"'
PY''', "deny"),
    ("Descriptor duplication on wrapped Python", r'''env FLAG=1 python -I 2>&1 - <<'PY'
p = 'C:\Users\x'
PY''', "deny"),
    ("Descriptor duplication does not make cat Python", r'''cat 2>&1 <<'EOF'
C:\Users\x
EOF''', "allow"),
]


def heredoc_cases():
    return [(label, expected, verdict(command)) for label, command, expected in HEREDOC_REAL + HEREDOC_BOUNDARIES]


# Step 3: text mentioning a protection is not protection of the compiler's status.
CASES += [
    ("dotnet build X | tail; echo '${PIPESTATUS[0]}'", "deny"),
    (r'dotnet build X | tail; echo \$PIPESTATUS', "deny"),
    ('dotnet build X | tail; echo ${PIPESTATUS[1]}', "deny"),
    ('printf x | dotnet build X | tail; echo ${PIPESTATUS[0]}', "deny"),
    ('dotnet build X | tail; # echo ${PIPESTATUS[0]}', "deny"),
    ('dotnet build X | tail; echo "$(true; echo ${PIPESTATUS[0]})"', "deny"),
    ('echo pipefail; dotnet build X | tail', "deny"),
    ('set +o pipefail; dotnet build X | tail', "deny"),
    ('dotnet build X | tail; set -o pipefail', "deny"),
    ('(set -o pipefail); dotnet build X | tail', "deny"),
    ('set -o pipefail; set +o pipefail; dotnet build X | tail', "deny"),
    ('dotnet build X | tail # pipefail', "deny"),
    ('echo "$(dotnet build X | tail)"', "deny"),
    ('dotnet build X & ls | head', "allow"),
    ('dotnet build --help | head -2', "allow"),
]


# Step 3 independent review: executable shell contexts must keep their Python
# Replay 8912 exposed an unrelated earlier pipeline leaking into the element index.
CASES += [('printf x | head; dotnet build X | tail; echo ${PIPESTATUS[0]}', 'allow')]


# Step 3 independent review: executable shell contexts must keep their Python
# consumer. These all allowed on 8d654575; the neighbouring raw/data cases above stay.
HEREDOC_BOUNDARIES += [
    ("review: quoted command substitution", r'''value="$(python - <<'PY'
s = '\bLEVEL\b'
PY
)"''', "deny"),
    ("review: nested quoted substitution", r'''echo "$(echo $(python - <<'PY'
s = '\bLEVEL\b'
PY
))"''', "deny"),
    ("review: arithmetic shift before Python", r'''echo $((1<<2)); python - <<'PY'
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: arithmetic command before Python", r'''((n=1<<2)); python - <<'PY'
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: uv run Python", r'''uv run python - <<'PY'
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: sudo Python", r'''sudo -u worker python - <<'PY'
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: nice Python", r'''nice -n 5 python - <<'PY'
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: cat pipes source to Python", r'''cat <<'PY' | python -
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: tee pipes source to Python", r'''tee copy.py <<'PY' | python -
s = '\bLEVEL\b'
PY''', "deny"),
    ("review: newline in C# verbatim regex", r'''python - <<'PY'
s = '@"\n"'
PY''', "deny"),
    ("review: carriage return in C# verbatim regex", r'''python - <<'PY'
s = '@"\r"'
PY''', "deny"),
]


def busy_tree_cases():
    """W8 (19 Nov): a build is refused while a testhost or dotnet build/test from the SAME git tree runs - any repo, any
    worktree (a .git FILE), the KOR.Drafter slot file; a build of another tree is not. Real temp trees, a fake process
    list (KOR_GUARD_PROCS), so no live testhost is needed. The 19 Nov instance is replayed: the previous coordinator's
    PdfToSafe testhost in KOR.Drafter-portmerge (a worktree), a build there refused."""
    import tempfile
    t = tempfile.mkdtemp(prefix="w8-")
    a, wt, b, drafter = (os.path.join(t, n) for n in ("Ops", "KOR.Drafter-portmerge", "Other", "KOR.Drafter"))
    for d in (a, b, drafter):
        os.makedirs(os.path.join(d, ".git"))
    os.makedirs(wt)
    with open(os.path.join(wt, ".git"), "w") as f:   # a worktree: .git is a FILE
        f.write("gitdir: elsewhere\n")
    for proj in (os.path.join(wt, "Kor.Operations.EngineeringTools.PdfToSafe.Tests"), os.path.join(a, "X.Tests")):
        os.makedirs(proj)   # the tree's OWN project folders: a .csproj in the first folder under the root
        open(os.path.join(proj, "p.csproj"), "w").close()
    os.makedirs(os.path.join(drafter, "work", "_suite", "abc", "Core.Tests", "bin"))   # a suite COPY inside the tree
    procs = os.path.join(t, "procs.json")
    with open(procs, "w", encoding="utf-8") as f:
        json.dump([
            ["13784", "testhost.exe", f"\"{wt}\\Kor.Operations.EngineeringTools.PdfToSafe.Tests\\bin\\Debug\\net8.0-windows\\testhost.exe\" --runtimeconfig x"],
            ["11508", "dotnet.exe", f"\"C:\\Program Files\\dotnet\\dotnet.exe\" test {a}\\X.Tests --no-build"],
            ["532", "dotnet.exe", "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\Program Files\\dotnet\\sdk\\10.0.403\\MSBuild.dll\" /nodemode:1 /nodeReuse:true"],
            ["9001", "testhost.exe", f"\"{drafter}\\work\\_suite\\abc\\Core.Tests\\bin\\testhost.exe\" --runtimeconfig x"],
        ], f)
    empty = os.path.join(t, "none.json")
    with open(empty, "w", encoding="utf-8") as f:
        json.dump([], f)
    slot = os.path.join(t, "slot.txt")
    with open(slot, "w", encoding="utf-8") as f:
        f.write("held by lane X since 12:00")
    nonslot = os.path.join(t, "no-slot.txt")
    P, E = {"KOR_GUARD_PROCS": procs, "KOR_GUARD_SLOT": nonslot}, {"KOR_GUARD_PROCS": empty, "KOR_GUARD_SLOT": nonslot}
    cases = [
        ("RED 19 Nov: a build in the worktree whose testhost still runs", "dotnet build KOR.Drafter.sln", wt, P, "deny"),
        ("a dotnet test in the same tree as a running dotnet test", "dotnet test X.Tests --filter A", a, P, "deny"),
        ("a build of ANOTHER tree is not held", "dotnet build", b, P, "allow"),
        ("cd into the busy tree from elsewhere", f'cd "{a}" && dotnet build', b, P, "deny"),
        ("a project path inside the busy tree, cwd elsewhere", f'dotnet build "{a}\\X.csproj"', b, P, "deny"),
        ("the worktree's main repo is another tree (Ops vs KOR.Drafter-portmerge)", "dotnet build", drafter, P, "allow"),
        ("an MSBuild reuse node names no tree: nothing busy", "dotnet build", b, E, "allow"),
        ("no process at all", "dotnet build", a, E, "allow"),
        ("a mention of dotnet build in a commit message is not a build", 'git commit -m "dotnet build fixed"', a, P, "allow"),
        ("KOR.Drafter's slot file held", "dotnet build", drafter, {"KOR_GUARD_PROCS": empty, "KOR_GUARD_SLOT": slot}, "deny"),
        ("the slot file means nothing to another tree", "dotnet build", b, {"KOR_GUARD_PROCS": empty, "KOR_GUARD_SLOT": slot}, "allow"),
        ("review 2305: a suite COPY's testhost inside the tree (work\\_suite) holds none of its locks", "dotnet build", drafter, P, "allow"),
        ("a PowerShell Set-Location into the busy tree", f'Set-Location "{a}"; dotnet build', b, P, "deny"),
    ]
    out = [(label, exp, verdict(cmd, env=env, cwd=cwd)) for label, cmd, cwd, env, exp in cases]
    # the REAL process reader (bypassed by KOR_GUARD_PROCS above): its first version never split a row and allowed every
    # build - found by the live probe, not by these cases. Its parser, on the two shapes PowerShell writes:
    sys.path.insert(0, os.path.dirname(GUARD))
    import claude_bash_guard as g
    one = g.parse_procs('{"ProcessId":7,"Name":"testhost.exe","CommandLine":"C:\\\\x\\\\testhost.exe"}')
    many = g.parse_procs('[{"ProcessId":7,"Name":"testhost.exe","CommandLine":"a"},{"ProcessId":8,"Name":"dotnet.exe","CommandLine":null}]')
    out.append(("parse_procs: a lone object (one process)", "[('7', 'testhost.exe', 'C:\\\\x\\\\testhost.exe')]", repr(one)))
    out.append(("parse_procs: an array, a null command line", "[('7', 'testhost.exe', 'a'), ('8', 'dotnet.exe', '')]", repr(many)))
    out.append(("parse_procs: nothing / not JSON", "[] []", f"{g.parse_procs('')} {g.parse_procs('oops')}"))
    import shutil
    shutil.rmtree(t, ignore_errors=True)   # review 2305: no w8-* folder left behind
    return out


def preflight_cases():
    """Rule 8: a slow Core run is denied when an input is missing; the fast filter never needs inputs; the
    unfiltered App EngineeringTools suite is denied (it hangs headless), filtered it runs."""
    import tempfile as _t
    empty = _t.mkdtemp()
    core = 'dotnet test "Kor.Operations.EngineeringTools.Core.Tests/Kor.Operations.EngineeringTools.Core.Tests.csproj"'
    app = 'dotnet test "Kor.Operations.App/EngineeringTools.Tests/Kor.Operations.EngineeringTools.Tests.csproj"'
    # Row 1985 (9 Nov): the Core tests moved to KOR.Drafter, so the six-set file the gate names is read there. With
    # every input present in temp mirrors, an unfiltered Core run is ALLOWED - before the fix it was denied because
    # the guard still looked in Operations, where the file no longer is.
    sys.path.insert(0, os.path.dirname(GUARD))
    import claude_bash_guard as g
    import re as _re
    drafter_six = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(GUARD))), "KOR.Drafter",
                               "Kor.Operations.EngineeringTools.Core.Tests", "Intake", "SixSetsBuildAsBankedTests.cs")
    sticks, mirror_root = _t.mkdtemp(), _t.mkdtemp()
    for job in g.STICK_JOBS:
        open(os.path.join(sticks, job + ".pdf"), "wb").close()
    old_root = os.environ.get("KOR_GUARD_MIRROR_ROOT")
    os.environ["KOR_GUARD_MIRROR_ROOT"] = mirror_root
    try:
        sources = _re.findall(r'@"(\\\\Kor-fs01[^"]+\.pdf)"', open(drafter_six, encoding="utf-8").read())
        for remote in sources:
            local = g.mirror_copy_of(remote)
            os.makedirs(os.path.dirname(local), exist_ok=True)
            open(local, "wb").close()
    finally:
        if old_root is None:
            os.environ.pop("KOR_GUARD_MIRROR_ROOT", None)
        else:
            os.environ["KOR_GUARD_MIRROR_ROOT"] = old_root
    present = {"KOR_GUARD_STICKFILES": sticks, "KOR_GUARD_MIRROR_ROOT": mirror_root}
    probes = [
        ("full Core run, all inputs present (KOR.Drafter six-set file)", core, present, "allow"),
        ("full Core run, mirror empty",       core,                                        {"KOR_GUARD_STICKFILES": empty}, "deny"),
        ("SixSets filter, mirror empty",       core + " --filter FullyQualifiedName~SixSets", {"KOR_GUARD_STICKFILES": empty}, "deny"),
        ("fast filter, mirror empty",          core + ' --filter "Speed!=Slow"',             {"KOR_GUARD_STICKFILES": empty}, "allow"),
        ("App engineering suite, unfiltered",  app,                                         None,                            "deny"),
        ("App engineering suite, filtered",    app + ' --filter "FullyQualifiedName~PdfToSafe"', None,                       "allow"),
    ]
    found = [(f"six-set sources found in KOR.Drafter file: {len(sources)}", "some", "some" if sources else "none")]
    return found + [(label, expected, verdict(cmd, env=env)) for label, cmd, env, expected in probes]


def main():
    failed = 0
    for command, expected in CASES:
        got = verdict(command)
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {command[:90]}")
    rate = sql_rate_cases() + duration_cases() + preflight_cases() + busy_tree_cases()
    rate += heredoc_cases()
    for label, expected, got in rate:
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {label}")
    print(f"{len(CASES) + len(rate) - failed} of {len(CASES) + len(rate)} as expected")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
