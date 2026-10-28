r"""walk.py <worktree> <commit,commit,...> : for each commit in order, move <worktree> to it (git switch --detach), build the
takeoff CLI in Debug there, and build the six banked sets with it through sixrepair.one (the same PdfOnlyBuild + KorStandards
route as the six-set baseline test), tag walk-<commit>. Appends "commit,job,sha256" to walk.csv beside this file. A commit whose
build fails is recorded as BUILD-FAILED and its sets are not built (an old exe would answer for it). COVERS: the six sets at
each given commit. DOES NOT: decide anything - attribution is walk_attribute.py's."""
import hashlib, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
wt, commits = sys.argv[1], sys.argv[2].split(",")
exe = os.path.join(wt, "Kor.Operations.EngineeringTools.TakeoffCli", "bin", "Debug", "net8.0", "takeoff.exe")
os.environ["SIXKNOB_EXE"] = exe          # sixrepair reads it at import
sys.path.insert(0, HERE)
import sixrepair   # noqa: E402

out = os.path.join(HERE, "walk.csv")
for c in commits:
    sw = subprocess.run(["git", "-C", wt, "switch", "--detach", c], capture_output=True, text=True)
    if sw.returncode != 0:
        open(out, "a").write(f"{c},*,SWITCH-FAILED\n"); print(c, "switch failed", sw.stderr[-300:], flush=True); continue
    b = subprocess.run(["dotnet", "build", os.path.join(wt, "Kor.Operations.EngineeringTools.TakeoffCli"), "-c", "Debug", "-v", "q"],
                       capture_output=True, text=True)
    if b.returncode != 0 or not os.path.exists(exe):
        open(out, "a").write(f"{c},*,BUILD-FAILED\n"); print(c, "build failed", flush=True); continue
    os.environ["SIXKNOB_TAG"] = f"walk-{c}"
    work = [(job, sixrepair.local(share), scale, None) for job, share, scale in sixrepair.SETS]
    with ThreadPoolExecutor(6) as ex:
        list(ex.map(lambda w: sixrepair.one(*w), work))
    for job, _, _ in sixrepair.SETS:
        p = os.path.join(sixrepair.OUT, job, f"walk-{c}", "out.e2k")
        h = hashlib.sha256(open(p, "rb").read()).hexdigest()[:16] if os.path.exists(p) else "NO-OUTPUT"
        open(out, "a").write(f"{c},{job},{h}\n")
    print(c, "done", flush=True)
