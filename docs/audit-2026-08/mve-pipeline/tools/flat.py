"""Flatten an HTML filing to plain text and grep it with context."""
import re, html, sys

path = sys.argv[1]
needle = sys.argv[2] if len(sys.argv) > 2 else None
span = int(sys.argv[3]) if len(sys.argv) > 3 else 400

s = open(path, encoding="utf-8", errors="replace").read()
s = re.sub(r"<(script|style)\b.*?</\1>", " ", s, flags=re.S | re.I)
s = re.sub(r"</(p|div|tr|td|th|li|h[1-6]|table)>", " \n", s, flags=re.I)
t = re.sub(r"<[^>]+>", " ", s)
t = html.unescape(t)
t = re.sub(r"[^\S\n]+", " ", t)
t = re.sub(r"\n\s*\n+", "\n", t)

if not needle:
    sys.stdout.write(t)
    raise SystemExit

pat = re.compile(needle, re.I)
seen = []
for m in pat.finditer(t):
    a = max(0, m.start() - span // 2)
    b = min(len(t), m.start() + span)
    chunk = t[a:b].replace("\n", " ")
    if any(abs(m.start() - p) < span // 2 for p in seen):
        continue
    seen.append(m.start())
    print(f"--- @{m.start()} ---")
    print(chunk)
