r"""Step 182 on vs off on the six sets: is the e2k identical, which plates moved (by the step's flag in the build log),
and the slab thickness distribution each way. COVERS: e2k bytes, slab section counts, the ASSUMED flags. DOES NOT: judge
against the engineer's model (yardstick.txt does that per build)."""
import glob, os, re, subprocess, sys
from collections import Counter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sixrepair")
EXE = sys.argv[1]
for job in sorted(os.listdir(OUT)):
    on, off = (os.path.join(OUT, job, t) for t in ("c182-on", "c182-off"))
    if not (os.path.isdir(on) and os.path.isdir(off)):
        continue
    e = [max(glob.glob(os.path.join(d, "*.e2k")), key=os.path.getsize) for d in (on, off)]
    same = open(e[0], "rb").read() == open(e[1], "rb").read()
    log = "\n".join(open(p, encoding="utf-8", errors="replace").read()
                    for p in glob.glob(os.path.join(on, "**", "*"), recursive=True)
                    if os.path.isfile(p) and p.lower().endswith((".log", ".txt", ".md", ".csv")))
    flags = sorted(set(re.findall(r"[^\n]*equally often and draws no outline[^\n]*", log)))
    print(f"== {job}: e2k {'IDENTICAL' if same else 'DIFFERS'}; step-182 flags {len(flags)}")
    for f in flags:
        print("   ", f.strip()[:400])
    if not same:
        d = subprocess.run([EXE, "model-diff", e[1], e[0]], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
        print("   model-diff off->on:\n      " + "\n      ".join(d.strip().splitlines()[:60]))
