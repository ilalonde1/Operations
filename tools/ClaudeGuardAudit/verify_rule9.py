"""Prove rule 9's fix against the EXACT command it wrongly denied on 2026-10-25, and
against the forms that must still be denied. Run from the Operations repo root."""
import json
import os
import subprocess
import sys

GUARD = os.path.join("tools", "claude_bash_guard.py")

CASES = [
    # the exact command denied earlier tonight: READING publish.ps1, not running it
    (r"""Select-String -Path 'C:\VIsual Studio Projects\KOR.RevitTools\build\publish.ps1' -Pattern 'Kor-fs01' | ForEach-Object { "line $($_.LineNumber)" }""", "allow"),
    (r"Get-Content .\build\publish.ps1 | Select-Object -First 5", "allow"),
    (r"Get-AuthenticodeSignature .\deploy-bridge.ps1 | Format-List", "allow"),
    # every form that actually RUNS it, piped, must still be denied
    (r".\build\publish.ps1 2>&1 | Select-String 'Signed'", "deny"),
    (r"& 'C:\x\build\publish.ps1' | Out-String", "deny"),
    (r"powershell -File build\publish.ps1 | Select-Object -Last 5", "deny"),
    (r"cd C:\x; .\deploy-opportunities.ps1 | Select-Object -Last 5", "deny"),
    # and the correct way stays allowed
    (r"$out = .\build\publish.ps1 2>&1; $c = $LASTEXITCODE; $out | Select-Object -Last 5", "allow"),
]


def verdict(command):
    payload = json.dumps({"tool_name": "PowerShell", "session_id": "verify-rule9",
                          "tool_input": {"command": command}})
    out = subprocess.run([sys.executable, GUARD], input=payload,
                         capture_output=True, text=True, timeout=30).stdout
    if not out.strip():
        return "allow"
    return json.loads(out)["hookSpecificOutput"]["permissionDecision"]


def main():
    ok = 0
    for command, expected in CASES:
        got = verdict(command)
        mark = "ok  " if got == expected else "FAIL"
        if got == expected:
            ok += 1
        shown = command if len(command) <= 86 else command[:83] + "..."
        print(f"   {mark} want={expected:5} got={got:5} | {shown}")
    print(f"   {ok} of {len(CASES)} as expected")
    return 0 if ok == len(CASES) else 1


if __name__ == "__main__":
    sys.exit(main())
