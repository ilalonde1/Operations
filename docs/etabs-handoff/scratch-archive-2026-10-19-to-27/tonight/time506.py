r"""time506.py : operations-ee's owed timing (2026-10-27) - is 31130's build slower with 506be566 (step 183 "a nest is not an
island") than with its parent 9774f9cf? Six full stickfile builds of 31130-01 from its local PDF, alternating parent / commit
(A B A B A B) so the box's other load falls on both alike; wall time per build, the machine's CPU load sampled before each, and
the e2k hash (the two must differ only if 506be566 moves 31130)."""
import hashlib, os, subprocess, sys, time, glob, shutil
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sixrepair   # noqa: E402  local() -> the PDF mirror path, SCALE
T = os.environ["TEMP"]
EXES = {"A 9774f9cf (parent)": os.path.join(T, "wt-506p", r"Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"),
        "B 506be566": os.path.join(T, "wt-506", r"Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe")}
job = "31130-01"
share, scale = next((s, sc) for j, s, sc in sixrepair.SETS if j == job)
pdf = sixrepair.local(share)
assert os.path.isfile(pdf), pdf
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "time506")
rows = []
for rnd in range(3):
    for name, exe in EXES.items():
        d = os.path.join(OUT, f"{name[0]}{rnd}")
        shutil.rmtree(d, ignore_errors=True)
        load = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average"],
                              capture_output=True, text=True).stdout.strip()
        t0 = time.time()
        r = subprocess.run([exe, "stickfile", pdf, d, "--scale", str(scale), "--job", job,
                            "--rules-db", os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"], "--no-render", "--no-questions"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        dt = time.time() - t0
        e2k = max(glob.glob(os.path.join(d, "*.e2k")), key=os.path.getsize, default=None)
        hsh = hashlib.sha256(open(e2k, "rb").read()).hexdigest()[:12] if e2k else "none"
        line = f"{time.strftime('%H:%M:%S')} round {rnd} {name}: {dt:6.0f} s, load before {load}%, exit {r.returncode}, e2k {hsh}"
        print(line, flush=True); rows.append((name, dt))
for name in EXES:
    ts = sorted(t for n, t in rows if n == name)
    print(f"{name}: {', '.join(f'{t:.0f}' for t in ts)} s - median {ts[1]:.0f}")
