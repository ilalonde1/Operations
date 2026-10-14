"""The guard's own tests: python tools/claude_bash_guard_test.py

Each rule is proved by a command it denies and a neighbour it allows. Rule 4 (a kill by process name)
was added 2026-09-19 after six test runs were ended by another session's name-wide kills.
"""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_guard.py")


def verdict(command, session_id=None, timeout=None, description=None):
    tool_input = {"command": command}
    if timeout is not None:
        tool_input["timeout"] = timeout
    if description is not None:
        tool_input["description"] = description
    payload = json.dumps({"tool_name": "Bash", "session_id": session_id or f"guard-test-{os.getpid()}",
                          "tool_input": tool_input})
    out = subprocess.run([sys.executable, GUARD], input=payload, capture_output=True, text=True, timeout=30).stdout
    if not out.strip():
        return "allow"
    return json.loads(out)["hookSpecificOutput"]["permissionDecision"]


def sql_rate_cases():
    """Rule 6: the fourth SQL call in ten minutes is denied, the first three are not, and a denied call
    does not count. A fresh session id per run so the test never shares state with a live session."""
    import tempfile
    sid = f"guard-test-sql-{os.getpid()}"
    state = os.path.join(tempfile.gettempdir(), f"claude_bash_guard_sql_{sid}.json")
    if os.path.exists(state):
        os.remove(state)
    try:
        # A MENTION is not a call (2026-10-14: these two were counted and the first real query was
        # denied as "call 4"). Neither may be counted, or the fourth real call below would be the fifth.
        got = [verdict("cp oq.py oqwide.py /tmp/x", sid), verdict("cat oq.py | head -3", sid)]
        got += [verdict('python -I oq.py -q "SELECT 1"', sid) for _ in range(2)]
        got.append(verdict('python -I "C:/x/oq.py" -i a.sql', sid))             # a quoted path is still a call: the 3rd
        got.append(verdict('cd "$S" && python -I oqwide.py -i b.sql', sid))    # the 4th: denied
        got.append(verdict("sqlcmd -S KOR-APP01 -i x.sql", sid))   # still denied: the 4th was not recorded
        got.append(verdict("dotnet build -c Release", sid))        # not a SQL call
    finally:
        if os.path.exists(state):
            os.remove(state)
    expected = ["allow", "allow", "allow", "allow", "allow", "deny", "deny", "allow"]
    return [(f"sql call {i + 1}", e, g) for i, (e, g) in enumerate(zip(expected, got))]


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
    rate = sql_rate_cases() + duration_cases()
    for label, expected, got in rate:
        mark = "ok  " if got == expected else "FAIL"
        if got != expected:
            failed += 1
        print(f"{mark} {expected:5} {got:5} | {label}")
    print(f"{len(CASES) + len(rate) - failed} of {len(CASES) + len(rate)} as expected")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
