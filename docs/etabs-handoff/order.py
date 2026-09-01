"""Message bodies in file order, so the images can be placed in the thread."""
import re
import sys

blob = open(sys.argv[1], "rb").read()
text = blob.decode("latin-1")

hits = []
for m in re.finditer(r"<p>.{0,400}?</p>", text, re.S):
    hits.append((m.start(), m.group()))
for m in re.finditer(r'itemid="([0-9a-z-]{20,})"', text):
    hits.append((m.start(), "IMAGE " + m.group(1)))

hits.sort()
seen = set()
for pos, s in hits:
    key = s[:120]
    if key in seen:
        continue
    seen.add(key)
    clean = re.sub(r"<[^>]+>", "", s).strip()
    if s.startswith("IMAGE"):
        clean = s
    if not clean:
        continue
    print(f"{pos:>9}  {clean[:300]}")
