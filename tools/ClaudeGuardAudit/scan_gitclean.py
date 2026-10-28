"""Across every transcript, find commands that were actually EXECUTED and could remove
untracked files from a working checkout: git clean, or a recursive delete of a repo path.
Mentioning a command in prose is not running it, so only tool_use inputs count."""
import glob
import json
import os
import re

GITCLEAN = re.compile(r"git\s+(-C\s+\S+\s+)?clean", re.I)
RECURSIVE = re.compile(r"(Remove-Item[^|;\n]*-Recurse|rm\s+-[a-z]*r[a-z]*\s|rmdir\s+/s|rd\s+/s)", re.I)
CHECKOUT = re.compile(r"VIsual Studio Projects[\\/]KOR\.Drafter", re.I)

files = sorted(glob.glob("*.jsonl"))
tot_calls = 0
clean_hits = []
checkout_hits = []

for path in files:
    with open(path, encoding="utf-8", errors="replace") as fh:
        for lineno, line in enumerate(fh, 1):
            if "tool_use" not in line:
                continue
            try:
                rec = json.loads(line)
            except Exception:
                continue
            content = (rec.get("message") or {}).get("content")
            if not isinstance(content, list):
                continue
            for b in content:
                if not isinstance(b, dict) or b.get("type") != "tool_use":
                    continue
                tot_calls += 1
                cmd = (b.get("input") or {}).get("command")
                if not isinstance(cmd, str):
                    continue
                ts = rec.get("timestamp", "")[:19]
                if GITCLEAN.search(cmd):
                    clean_hits.append((path, lineno, ts, " ".join(cmd.split())))
                elif RECURSIVE.search(cmd) and CHECKOUT.search(cmd):
                    checkout_hits.append((path, lineno, ts, " ".join(cmd.split())))

print(f"transcripts scanned            : {len(files)}")
print(f"tool calls parsed              : {tot_calls}")
print(f"EXECUTED 'git clean'           : {len(clean_hits)}")
print(f"EXECUTED recursive delete of a KOR.Drafter CHECKOUT path : {len(checkout_hits)}")
print()
for tag, hits in (("git clean", clean_hits), ("recursive delete in checkout", checkout_hits)):
    for path, lineno, ts, cmd in hits:
        print(f"[{tag}] {os.path.basename(path)} line {lineno} {ts}")
        print(f"    {cmd[:300]}")
        print()
