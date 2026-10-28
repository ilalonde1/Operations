r"""scratch_refs.py <scratchpad> : for every top-level entry of the scratchpad, how many memory files / repo docs name it, plus its
size, newest write and kind - the triage table. Counts all entries."""
import glob, os, sys, time
S = sys.argv[1]
M = r"C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\memory"
docs = glob.glob(os.path.join(M, "*.md")) + glob.glob(r"C:\VIsual Studio Projects\Operations\docs\*.md") \
    + glob.glob(r"C:\VIsual Studio Projects\KOR.Drafter\docs\*.md")
texts = {d: open(d, encoding="utf-8", errors="replace").read() for d in docs}
rows = []
for e in sorted(os.listdir(S)):
    p = os.path.join(S, e)
    if os.path.isdir(p):
        size = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(p) for f in fs)
        newest = max((os.path.getmtime(os.path.join(r, f)) for r, _, fs in os.walk(p) for f in fs), default=os.path.getmtime(p))
    else:
        size, newest = os.path.getsize(p), os.path.getmtime(p)
    key = e if len(e) > 5 else None
    refs = [os.path.basename(d) for d, t in texts.items() if key and key in t]
    rows.append((e, os.path.isdir(p), size, time.strftime("%m-%d %H:%M", time.localtime(newest)), refs))
print(f"{len(rows)} entries; {sum(1 for r in rows if r[4])} named somewhere")
for e, d, s, t, refs in rows:
    if refs: print(f"REF  {t}  {s/1e6:8.1f} MB  {e}{'/' if d else ''}  <- {', '.join(refs[:3])}")
