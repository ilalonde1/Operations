"""Does rule 9 deny the wrapper-script pipe and leave the safe forms alone?

deny() writes a JSON decision and exits 0, so the exit code says nothing -- the earlier
harness read returncode and reported three false passes. Read the decision.
"""
import json, subprocess, sys

SCRIPT = r"C:\VIsual Studio Projects\Operations\tools\claude_bash_guard.py"
P = "publish" + ".ps1"          # split so this file does not trip the guard it is testing
D = "deploy-networkops-service" + ".ps1"

CASES = [
    (r".\build\%s 2>&1 | Select-String -Pattern 'Signed'" % P,              True,  "what was run today"),
    (r"cd 'C:\x' ; .\build\%s 2>&1 | Select-Object -Last 20" % P,           True,  "piped, no exit code"),
    (r".\%s 2>&1 | Select-Object -Last 25" % D,                             True,  "a deploy script, piped"),
    (r"$out = .\build\%s 2>&1; $c = $LASTEXITCODE; $out | Select-Object -Last 5" % P, False, "captured + LASTEXITCODE"),
    (r".\build\%s" % P,                                                     False, "unpiped"),
    (r"Get-ChildItem . | Select-Object -First 3",                           False, "ordinary pipe"),
    (r"dotnet build x.csproj 2>&1 | tail -5",                               True,  "rule 8 still fires"),
]

fails = 0
for cmd, should_deny, label in CASES:
    p = subprocess.run([sys.executable, "-I", SCRIPT],
                       input=json.dumps({"tool_name": "PowerShell",
                                         "tool_input": {"command": cmd},
                                         "description": "~1 min: test"}),
                       capture_output=True, text=True)
    out = (p.stdout or "").strip()
    denied = False
    if out:
        try:
            denied = json.loads(out)["hookSpecificOutput"]["permissionDecision"] == "deny"
        except Exception:
            denied = False
    ok = denied == should_deny
    fails += 0 if ok else 1
    print(("  OK   " if ok else "  FAIL "), "deny=%-5s expected=%-5s %s" % (denied, should_deny, label))

print("\n%d of %d cases behave as intended" % (len(CASES) - fails, len(CASES)))
sys.exit(1 if fails else 0)
