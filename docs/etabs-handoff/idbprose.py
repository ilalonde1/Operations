"""Prose-looking strings from one LevelDB file, in file order."""
import re
import sys

ASCII_RUN = re.compile(rb"[\x20-\x7e\t]{14,}")
UTF16_RUN = re.compile(rb"(?:[\x20-\x7e]\x00){14,}")

blob = open(sys.argv[1], "rb").read()
minwords = int(sys.argv[2]) if len(sys.argv) > 2 else 3

items = []
for m in ASCII_RUN.finditer(blob):
    items.append((m.start(), m.group().decode("ascii", "replace")))
for m in UTF16_RUN.finditer(blob):
    items.append((m.start(), m.group().decode("utf-16-le", "replace")))
items.sort()

seen = set()
for pos, s in items:
    t = s.strip()
    # prose: several words, mostly letters, not a GUID or a base64 blob
    words = [w for w in re.split(r"\s+", t) if w]
    if len(words) < minwords:
        continue
    letters = sum(c.isalpha() or c.isspace() for c in t)
    if letters / max(len(t), 1) < 0.72:
        continue
    if re.fullmatch(r"[0-9a-fA-F-]{20,}", t.replace(" ", "")):
        continue
    if t in seen:
        continue
    seen.add(t)
    print(f"{pos:>9}  {t[:400]}")
