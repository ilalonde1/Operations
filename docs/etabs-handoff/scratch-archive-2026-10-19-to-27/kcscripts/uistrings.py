"""Extract user-visible literal strings from a minified React bundle: children:"..", title:"..", placeholder, label:, aria-label.
Prints each distinct (kind, text) with its first offset, in offset order, plus totals."""
import re, sys
sys.stdout.reconfigure(encoding='utf-8')
t = open(sys.argv[1], encoding='utf-8', errors='replace').read()
kinds = ['children', 'title', 'placeholder', 'label', '"aria-label"', 'header', 'heading', 'text', 'tip', 'hint', 'caption', 'name']
pat = re.compile(r'(?<![A-Za-z0-9_$])(' + '|'.join(re.escape(k) for k in kinds) + r'):\s*"((?:[^"\\]|\\.){2,400})"')
seen = {}
for m in pat.finditer(t):
    k, s = m.group(1), m.group(2)
    if not re.search(r'[A-Za-z]{2}', s):
        continue
    key = (k, s)
    if key not in seen:
        seen[key] = m.start()
items = sorted(seen.items(), key=lambda kv: kv[1])
for (k, s), off in items:
    print(f"{off}\t{k}\t{s}")
print(f"TOTAL distinct: {len(items)}")
