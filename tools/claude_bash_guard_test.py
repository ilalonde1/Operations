"""The guard's own tests: python tools/claude_bash_guard_test.py

Each rule is proved by a command it denies and a neighbour it allows. Rule 4 (a kill by process name)
was added 2026-09-19 after six test runs were ended by another session's name-wide kills.
"""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_guard.py")


def verdict(command, session_id=None, timeout=None, description=None, env=None, cwd=None):
    tool_input = {"command": command}
    if timeout is not None:
        tool_input["timeout"] = timeout
    if description is not None:
        tool_input["description"] = description
    payload = json.dumps({"tool_name": "Bash", "session_id": session_id or f"guard-test-{os.getpid()}",
                          "tool_input": tool_input, **({"cwd": cwd} if cwd else {})})
    out = subprocess.run([sys.executable, GUARD], input=payload, capture_output=True, text=True, timeout=30,
                         env={**os.environ, **(env or {})}).stdout
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
    procs = os.path.join(t, "procs.json")
    w = lambda p: p.replace("\\", "\\\\")
    with open(procs, "w", encoding="utf-8") as f:
        json.dump([
            ["13784", "testhost.exe", f"\"{wt}\\Kor.Operations.EngineeringTools.PdfToSafe.Tests\\bin\\Debug\\net8.0-windows\\testhost.exe\" --runtimeconfig x"],
            ["11508", "dotnet.exe", f"\"C:\\Program Files\\dotnet\\dotnet.exe\" test {a}\\X.Tests --no-build"],
            ["532", "dotnet.exe", "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\Program Files\\dotnet\\sdk\\10.0.403\\MSBuild.dll\" /nodemode:1 /nodeReuse:true"],
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
    for label, expected, got in rate:
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {label}")
    print(f"{len(CASES) + len(rate) - failed} of {len(CASES) + len(rate)} as expected")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
