r"""Prove a built loader DLL carries tonight's new strings.

Searches the raw bytes for the UTF-16LE encoding of each string, because .NET string literals
are UTF-16 in the assembly. NOT by decoding the file as text: Encoding.Unicode.GetString over a
whole DLL only decodes literals that start at an EVEN byte offset, so it reports false negatives
- that exact flaw reported a freshly built loader as lacking its own log strings on 2026-10-28.
Prints the byte count read beside every result, so a pass can never come from an empty read.
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

NEW = [
    "staging the bridge only",
    "old bridge folders KEPT",
    "the stamp is the tools' version, not the bridge's",
]

# Strings to look for can be given on the command line (-s, repeatable); without any, the list
# above is used. Added 2026-10-28 so a later change does not mean editing the instrument, which
# is how an instrument quietly stops matching what it claims to check.
args = sys.argv[1:]
wanted, paths = [], []
i = 0
while i < len(args):
    if args[i] in ("-s", "--string") and i + 1 < len(args):
        wanted.append(args[i + 1])
        i += 2
    else:
        paths.append(args[i])
        i += 1
if wanted:
    NEW = wanted

ok = True
for path in paths:
    if not os.path.isfile(path):
        print("MISSING  %s" % path)
        ok = False
        continue
    data = io.open(path, "rb").read()
    print("%s  (%d bytes, mtime %s)" % (path, len(data), os.path.getmtime(path)))
    if len(data) == 0:
        print("  EMPTY READ - every absence check below would be meaningless")
        ok = False
        continue
    for s in NEW:
        hit = s.encode("utf-16-le") in data
        print("  %-5s %s" % ("FOUND" if hit else "ABSENT", s))
        if not hit:
            ok = False

print("RESULT: %s" % ("all strings present in every file" if ok else "SOMETHING IS MISSING"))
sys.exit(0 if ok else 1)
