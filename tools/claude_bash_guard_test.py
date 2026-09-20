"""The guard's own tests: python tools/claude_bash_guard_test.py

Each rule is proved by a command it denies and a neighbour it allows. Rule 4 (a kill by process name)
was added 2026-09-19 after six test runs were ended by another session's name-wide kills.
"""
import json
import os
import subprocess
import sys

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_bash_guard.py")


def verdict(command):
    payload = json.dumps({"tool_name": "Bash", "tool_input": {"command": command}})
    out = subprocess.run([sys.executable, GUARD], input=payload, capture_output=True, text=True, timeout=30).stdout
    if not out.strip():
        return "allow"
    return json.loads(out)["hookSpecificOutput"]["permissionDecision"]


CASES = [
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
    print(f"{len(CASES) - failed} of {len(CASES)} as expected")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
