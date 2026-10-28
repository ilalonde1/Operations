"""ctx.py <file> <regex> [before] [after] [max]: print every match with context, and the total count."""
import re, sys
sys.stdout.reconfigure(encoding='utf-8')
path, pat = sys.argv[1], sys.argv[2]
b = int(sys.argv[3]) if len(sys.argv) > 3 else 150
a = int(sys.argv[4]) if len(sys.argv) > 4 else 150
mx = int(sys.argv[5]) if len(sys.argv) > 5 else 50
t = open(path, encoding='utf-8', errors='replace').read()
ms = list(re.finditer(pat, t))
for m in ms[:mx]:
    s = t[max(0, m.start() - b):m.end() + a].replace('\n', ' ')
    print(f"@{m.start()}: {s}\n")
print(f"TOTAL matches: {len(ms)} (shown {min(len(ms), mx)})")
