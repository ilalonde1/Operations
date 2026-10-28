import sys
BS = chr(92)
app = open(sys.argv[1]).read().split()
rows = [l.rstrip("\n").split("|", 1) for l in open(sys.argv[2], encoding="utf-8", errors="replace")]
nob = [r for r in rows if len(r) > 1 and r[1] and BS not in r[1]]
chk = [r for r in rows if len(r) > 1 and ("checks" + BS) in r[1]]
print(len(rows), "rows")
print(len(nob), "no-backslash:", " ".join(sorted(r[0].replace(".dis.txt", "") for r in nob)))
print(len(chk), "checks pkg")
s = set(r[0] for r in nob + chk)
print("in mine not auto:", [a for a in app if a not in s])
print("auto not mine:", sorted(x for x in s if x not in app))
