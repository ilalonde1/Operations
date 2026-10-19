"""The guard's own tests: python tools/claude_bash_guard_test.py

Each rule is proved by a command it denies and a neighbour it allows. Rule 4 (a kill by process name)
was added 2026-09-19 after six test runs were ended by another session's name-wide kills.
"""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_guard.py")


def verdict(command, session_id=None, timeout=None, description=None, env=None):
    tool_input = {"command": command}
    if timeout is not None:
        tool_input["timeout"] = timeout
    if description is not None:
        tool_input["description"] = description
    payload = json.dumps({"tool_name": "Bash", "session_id": session_id or f"guard-test-{os.getpid()}",
                          "tool_input": tool_input})
    out = subprocess.run([sys.executable, GUARD], input=payload, capture_output=True, text=True, timeout=30,
                         env={**os.environ, **(env or {})}).stdout
    if not out.strip():
        return "allow"
    return json.loads(out)["hookSpecificOutput"]["permissionDecision"]


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
    """Rule 7: a long timeout must come with a duration-first description; short commands are untouched."""
    probes = [
        ("long, no description",         600000, None,                              "deny"),
        ("long, duration not first",     600000, "Run the App suite (~3 min)",     "deny"),
        ("long, duration first",         600000, "~3 min: run the App suite",      "allow"),
        ("long, seconds first",          180000, "90 s: build and test",           "allow"),
        ("short, no description",        60000,  None,                              "allow"),
        ("default timeout",              None,   "Run the Data suite",             "allow"),
    ]
    return [(label, e, verdict("dotnet build -c Release", timeout=t, description=d)) for label, t, d, e in probes]


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
    # rule 2: destroying uncommitted work
    ("git checkout -- src/File.cs", "deny"),
    ("git stash push -- src/File.cs", "allow"),
    # rule 1: a recursive walk over SMB
    ("find //Kor-fs01/Projects -name '*.e2k'", "deny"),
    ("ls //Kor-fs01/Projects/Projects", "allow"),
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
]


def other_repo_cases():
    """The build-lock rule is only for a build OF THIS REPO (2026-09-19): the pattern that says a command
    targets another checkout, proved on both sides without a live testhost."""
    sys.path.insert(0, os.path.dirname(GUARD))
    import claude_bash_guard as g
    yes = [
        'cd "/c/VIsual Studio Projects/JoeBrain" && dotnet build -c Release',
        'Set-Location "C:\\VIsual Studio Projects\\JoeBrain-trader"; dotnet test tests/JoeBrain.Tests',
        'dotnet build "C:\\VIsual Studio Projects\\KOR.Drafter\\KOR.Drafter.sln"',
    ]
    no = [
        'cd "/c/VIsual Studio Projects/Operations" && dotnet test --filter "Speed!=Slow"',
        'dotnet build -c Release',
        'dotnet test "C:\\VIsual Studio Projects\\Operations\\Kor.Operations.sln"',
    ]
    return [(c, "other") for c in yes if g.OTHER_REPO.search(c)] + [(c, "FAIL other") for c in yes if not g.OTHER_REPO.search(c)] \
         + [(c, "ours") for c in no if not g.OTHER_REPO.search(c)] + [(c, "FAIL ours") for c in no if g.OTHER_REPO.search(c)]


def preflight_cases():
    """Rule 8: a slow Core run is denied when an input is missing; the fast filter never needs inputs; the
    unfiltered App EngineeringTools suite is denied (it hangs headless), filtered it runs."""
    import tempfile as _t
    empty = _t.mkdtemp()
    core = 'dotnet test "Kor.Operations.EngineeringTools.Core.Tests/Kor.Operations.EngineeringTools.Core.Tests.csproj"'
    app = 'dotnet test "Kor.Operations.App/EngineeringTools.Tests/Kor.Operations.EngineeringTools.Tests.csproj"'
    probes = [
        ("full Core run, mirror empty",        core,                                        {"KOR_GUARD_STICKFILES": empty}, "deny"),
        ("SixSets filter, mirror empty",       core + " --filter FullyQualifiedName~SixSets", {"KOR_GUARD_STICKFILES": empty}, "deny"),
        ("fast filter, mirror empty",          core + ' --filter "Speed!=Slow"',             {"KOR_GUARD_STICKFILES": empty}, "allow"),
        ("App engineering suite, unfiltered",  app,                                         None,                            "deny"),
        ("App engineering suite, filtered",    app + ' --filter "FullyQualifiedName~PdfToSafe"', None,                       "allow"),
    ]
    return [(label, expected, verdict(cmd, env=env)) for label, cmd, env, expected in probes]


def main():
    failed = 0
    for command, expected in other_repo_cases():
        if expected.startswith("FAIL"):
            failed += 1
        print(f"{'FAIL' if expected.startswith('FAIL') else 'ok  '} {expected:5} | {command[:90]}")
    for command, expected in CASES:
        got = verdict(command)
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {command[:90]}")
    rate = sql_rate_cases() + duration_cases() + preflight_cases()
    for label, expected, got in rate:
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {label}")
    print(f"{len(CASES) + len(rate) - failed} of {len(CASES) + len(rate)} as expected")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
