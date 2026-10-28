r"""Step 3 of tonight's runs: build each of the six banked sets with every rule on, then with each of
steps 149-168 off (KOR_STEP<n>_OFF=1), from the gate's own local mirror copies; diff each build against
Baselines/pdf-only-<job>.e2k and judge it against the engineer's model in the local yardsticks.
COVERS: the same PdfOnlyBuild the gate runs (via takeoff stickfile, rules from KorStandards).
DOES NOT: interactions between two knobs; anything outside the six sets."""
import hashlib, os, subprocess, sys, glob, json
from concurrent.futures import ThreadPoolExecutor

REPO = r"C:\VIsual Studio Projects\Operations"
EXE = os.environ.get("SIXKNOB_EXE") or REPO + r"\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"
BASE = REPO + r"\Kor.Operations.EngineeringTools.Core.Tests\Baselines"
MIRROR = os.path.join(os.environ["TEMP"], "kor-drawings")
YARD = os.path.join(MIRROR, "yardsticks")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sixknob170")
P = r"\\Kor-fs01\Projects\Projects\03 Residential"
SETS = [
    ("31130-01", P + r"\31130-01 (Uplands Area 6 Lot 12 West Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31130-01 UPLANDS LOT 12 2026-05-20 Stick file.pdf", 96),
    ("31138-01", P + r"\31138-01 (2170 W 1st Ave Vancouver BC)\05 Stickfile\06 Old Structural Stickfiles\31138-01 2026-09-01_2170 W 1st Ave_Str Set.pdf", 96),
    ("31065-01", P + r"\31065-01 (5350 5430 Heather Street Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31065-01 - 2026-07-08 - 5380-Heather Street - Stickfile (up to SSI-04)-OAP.pdf", 100),
    ("31202-01", P + r"\31202-01 (1650 N Hotel Circle San Diego)\05 Stickfile\06 Old Structural Stickfiles\31202-01 2026-09-04 Hotel Circle North StickSet  - FULL SET.pdf", 96),
    ("31168-01", P + r"\31168-01 (YMCA Langara Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31168-01 - 2026-04-21- YMCA Langara - Stickfile.pdf", 96),
    ("31170-01-arch", P + r"\31170-01 (2005-2045 West 49 Ave Vancouer)\05 Stickfile\01 Architectural\Obsolete\260723_W49th_75% BP Draft.pdf", 96),
]
KNOBS = [None] + [f"KOR_STEP{n}_OFF" for n in range(149, 173)]


def local(share):
    d, f = os.path.split(share)
    return os.path.join(MIRROR, hashlib.sha1(d.lower().encode("utf-8")).hexdigest().upper()[:16], f)


def yardstick(job):
    for name in (job, job.split("-arch")[0]):
        p = os.path.join(YARD, name + ".e2k")
        if os.path.exists(p):
            return p
    return None


def one(job, pdf, scale, knob):
    # knob: None (every rule on), one KOR_STEP<n>_OFF, or a tuple of them switched off together
    group = knob if isinstance(knob, tuple) else (knob,) if knob else ()
    tag = (os.environ.get("SIXKNOB_TAG") or "all-on") if not group else group[0] if len(group) == 1 else "off-" + "-".join(k[8:11] for k in group)
    d = os.path.join(OUT, job, tag)
    env = dict(os.environ)
    for k in group:
        env[k] = "1"
    r = subprocess.run([EXE, "stickfile", pdf, d, "--scale", str(scale), "--job", job,
                        "--rules-db", os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"], "--no-render", "--no-questions"],
                       env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, "build.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    e2ks = [p for p in glob.glob(os.path.join(d, "*.e2k"))]
    if r.returncode != 0 or not e2ks:
        return (job, tag, f"BUILD FAILED exit {r.returncode}", "")
    e2k = max(e2ks, key=os.path.getsize)
    dr = subprocess.run([EXE, "model-diff", os.path.join(BASE, f"pdf-only-{job}.e2k"), e2k], capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(d, "diff.txt"), "w", encoding="utf-8").write(dr.stdout + dr.stderr)
    ys = yardstick(job)
    yl = ""
    if ys:
        yr = subprocess.run([EXE, "model-yardstick", e2k, ys], capture_output=True, text=True, encoding="utf-8", errors="replace")
        open(os.path.join(d, "yardstick.txt"), "w", encoding="utf-8").write(yr.stdout + yr.stderr)
    return (job, tag, "ok", e2k)


if __name__ == "__main__":
    jobs = [s for s in SETS if len(sys.argv) < 2 or sys.argv[1] == "all" or s[0] in sys.argv[1].split(",")]
    for job, share, _ in jobs:
        if not os.path.exists(local(share)):
            sys.exit(f"MISSING mirror copy for {job}: {local(share)}")
    mode = sys.argv[2] if len(sys.argv) > 2 else "all"
    singles = KNOBS[1:]
    groups = [tuple(singles)] + [tuple(singles[i:i + 5]) for i in range(0, 24, 5)]
    knobs = (KNOBS[:1] if mode == "base" else singles if mode == "knobs" else groups if mode == "groups"
             else [k for k in singles if k[8:11] in mode.split(",")] if mode[:1].isdigit() else KNOBS)
    work = [(job, local(share), sc, k) for job, share, sc in jobs for k in knobs]
    print(f"{len(work)} builds", flush=True)
    with ThreadPoolExecutor(int(os.environ.get("SIXKNOB_PAR", "6"))) as ex:
        for res in ex.map(lambda w: one(*w), work):
            print("\t".join(res[:3]), flush=True)
