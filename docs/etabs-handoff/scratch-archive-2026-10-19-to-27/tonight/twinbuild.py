r"""twinbuild.py : 31065-01 and 31170-01-arch built with the current CLI (d92e50b1: sheets.csv carries each view's placement) by the
same stickfile call sixrepair.py makes, into scratchpad\tonight\twinbuild\<job> - for tracing 10-26's untraced WRONG TWIN columns
(31065 L2 x3, 31170-arch L7 x4) to the sheets that drew them."""
import os, shutil, subprocess, sys, time
# REFUSES TO RUN (14:30 10-27, oversight.Mistake): stickfile's sheets.csv is the PAGE list - it never carries the placement
# column; d92e50b1 put placement in the corpus-gate ledger. Use: takeoff corpus-gate --work <dir> --jobs 31065-01,31170-01
sys.exit("twinbuild.py cannot produce placement - run takeoff corpus-gate --jobs instead (see the comment above)")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sixrepair   # noqa: E402
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "twinbuild")
# Never run from the repo's bin: a running takeoff locks Core.dll and a suite build then fails MSB3027 (13:56 10-27,
# oversight.Mistake). Copy the bin aside and run the copy.
if not os.environ.get("SIXKNOB_EXE"):
    BIN = os.path.join(os.path.dirname(os.path.abspath(__file__)), "takeoff-bin")
    shutil.rmtree(BIN, ignore_errors=True)
    shutil.copytree(os.path.dirname(sixrepair.EXE), BIN)
    sixrepair.EXE = os.path.join(BIN, "takeoff.exe")
for job in (sys.argv[1:] or ["31170-01-arch", "31065-01"]):
    share, scale = next((s, sc) for j, s, sc in sixrepair.SETS if j == job)
    d = os.path.join(OUT, job)
    t0 = time.time()
    r = subprocess.run([sixrepair.EXE, "stickfile", sixrepair.local(share), d, "--scale", str(scale), "--job", job,
                        "--rules-db", os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"], "--no-render", "--no-questions"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, "build.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)   # the error text, kept
    print(f"{time.strftime('%H:%M:%S')} {job}: exit {r.returncode}, {time.time() - t0:.0f} s", flush=True)
