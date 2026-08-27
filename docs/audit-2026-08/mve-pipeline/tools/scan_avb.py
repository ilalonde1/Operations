"""Flatten an SEC HTML exhibit to text and dump the development-pipeline tables."""
import re, html, sys

path = sys.argv[1]
s = open(path, encoding="utf-8", errors="replace").read()

# rows -> one line each, cells separated by |
rows = re.findall(r"<tr\b.*?</tr>", s, re.S | re.I)
out = []
for r in rows:
    cells = re.findall(r"<t[dh]\b.*?</t[dh]>", r, re.S | re.I)
    vals = []
    for c in cells:
        v = re.sub(r"<[^>]+>", " ", c)
        v = html.unescape(v)
        v = re.sub(r"[\s ]+", " ", v).strip()
        vals.append(v)
    line = " | ".join(v for v in vals if v)
    line = re.sub(r"\s*\|\s*", " | ", line).strip()
    if line:
        out.append(line)

text = "\n".join(out)
print("TOTAL ROWS:", len(out))
print("=" * 70)

needle = sys.argv[2] if len(sys.argv) > 2 else None
if needle:
    pat = re.compile(needle, re.I)
    for i, line in enumerate(out):
        if pat.search(line):
            print(i, "::", line[:400])
else:
    for i, line in enumerate(out):
        print(i, "::", line[:400])
