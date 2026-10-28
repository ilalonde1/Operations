"""Which EXECUTED commands deleted something inside the KOR.Drafter working checkout?

Self-tests first: if the known-positive samples are not detected, the scan prints why and
stops, because a scan that silently matches nothing reads exactly like 'nobody did it'.
"""
import glob
import json
import os
import re

DEL = re.compile(r"(Remove-Item|\brm\s+-|rmdir|\brd\s+/s|Directory\.Delete|File\.Delete)", re.I)
CHECKOUT = re.compile(r"VIsual\s+Studio\s+Projects[\\/]+KOR\.Drafter", re.I)

POSITIVES = [
    "Remove-Item 'C:" + chr(92) + "VIsual Studio Projects" + chr(92) + "KOR.Drafter" + chr(92) + "db" + chr(92) + "x.sql' -Force",
    "rm -rf '/c/VIsual Studio Projects/KOR.Drafter/tools/cad/cadtest'",
]
NEGATIVE = "rm -rf \"$S/cli-gate7\"   # mentions nothing"


def statements(cmd):
    return re.split(r"[\n;]|&&|\|\|", cmd)


def hits_in(cmd):
    return [s.strip() for s in statements(cmd) if DEL.search(s) and CHECKOUT.search(s)]


ok = True
for p in POSITIVES:
    found = bool(hits_in(p))
    print(("  OK   " if found else "  FAIL ") + repr(p[:80]))
    ok = ok and found
if hits_in(NEGATIVE):
    print("  FAIL  negative sample matched")
    ok = False
else:
    print("  OK    negative sample ignored")
if not ok:
    print("\nSELF-TEST FAILED - the scan is not trustworthy, so no count is reported.")
    raise SystemExit(2)
print("self-test passed: 2 of 2 positives detected, negative ignored\n")

calls = 0
hits = []
for path in sorted(glob.glob("*.jsonl")):
    with open(path, encoding="utf-8", errors="replace") as fh:
        for ln, line in enumerate(fh, 1):
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
                calls += 1
                cmd = (b.get("input") or {}).get("command")
                if not isinstance(cmd, str):
                    continue
                for h in hits_in(cmd):
                    hits.append((os.path.basename(path), ln, rec.get("timestamp", "")[:19],
                                 " ".join(h.split())))

print("transcripts : " + str(len(glob.glob('*.jsonl'))))
print("tool calls  : " + str(calls))
print("DELETE statements targeting the KOR.Drafter checkout : " + str(len(hits)))
print()
for f, ln, ts, s in hits:
    print("[" + f + " line " + str(ln) + "] " + ts)
    print("    " + s[:240])
    print()
