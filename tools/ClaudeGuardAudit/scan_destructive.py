"""Find commands this session actually EXECUTED that could remove untracked files.
Mentioning a command in prose is not running it, so only tool_use inputs count."""
import json
import re
import sys

path = sys.argv[1]
label = sys.argv[2] if len(sys.argv) > 2 else path

DESTRUCTIVE = re.compile(
    r"git\s+clean|rm\s+-[a-z]*[rf]|Remove-Item|rmdir|rd\s+/s|del\s+/|Directory\.Delete|\[IO\.Directory\]::Delete",
    re.I)
DRAFTER = re.compile(r"KOR\.Drafter|cadtest|cad-pdf|tools[\\/]cad", re.I)

executed = []
total_tool_uses = 0

with open(path, encoding="utf-8", errors="replace") as fh:
    for lineno, line in enumerate(fh, 1):
        try:
            rec = json.loads(line)
        except Exception:
            continue
        msg = rec.get("message") or {}
        content = msg.get("content")
        if not isinstance(content, list):
            continue
        for block in content:
            if not isinstance(block, dict) or block.get("type") != "tool_use":
                continue
            total_tool_uses += 1
            inp = block.get("input") or {}
            cmd = inp.get("command")
            if not isinstance(cmd, str):
                continue
            if DESTRUCTIVE.search(cmd):
                executed.append((lineno, rec.get("timestamp", "")[:19], cmd.strip()))

print(f"=== {label} ===")
print(f"tool calls in transcript        : {total_tool_uses}")
print(f"EXECUTED destructive commands   : {len(executed)}")
hit = [e for e in executed if DRAFTER.search(e[2])]
print(f"  ...of those, touching KOR.Drafter / cadtest / cad-pdf : {len(hit)}")
print()
for lineno, ts, cmd in executed:
    flag = "  *** DRAFTER ***" if DRAFTER.search(cmd) else ""
    one = " ".join(cmd.split())
    print(f"[line {lineno}] {ts}{flag}")
    print(f"    {one[:400]}")
    print()
