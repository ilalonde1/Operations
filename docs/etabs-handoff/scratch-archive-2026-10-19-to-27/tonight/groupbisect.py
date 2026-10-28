r"""Name the step behind each LOST set by halving the knobs, judged by the gate itself.

For one set: run `takeoff corpus-gate --jobs <set>` with a GROUP of KOR_STEP<n>_OFF knobs switched off. If the set is no
longer lost (gate exit 0), the culprit is in that group; else in the other half. About log2(knobs) rounds per set, the
sets in parallel, each in its own --work dir (a shared work dir would be clobbered). The verdict is the gate's own
judge, the same one that marked the set lost.
COVERS: a single knob that alone restores the set. DOES NOT: two knobs that only together cause the loss (the halving then
names neither and says so), or a loss no knob turns off (a change with no knob, e.g. a reader-wide refactor).
Usage: python groupbisect.py <takeoff.exe> <set,set,...> [first_step]"""
import os, subprocess, sys, re
from concurrent.futures import ThreadPoolExecutor

REPO = r"C:\VIsual Studio Projects\Operations"
EXE = sys.argv[1]
SETS = sys.argv[2].split(",")
FIRST = int(sys.argv[3]) if len(sys.argv) > 3 else 141
BASE = REPO + r"\docs\etabs-handoff\corpus\ledger-sets-2026-09-24-run45-step140.csv"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "groupbisect")
src = open(REPO + r"\Kor.Operations.EngineeringTools.TakeoffCli\Verbs\CorpusGateVerb.cs", encoding="utf-8").read()
KNOBS = sorted({k for k in re.findall(r"KOR_STEP(\d+)_OFF", src) if int(k) >= FIRST}, key=int)
KNOBS = [f"KOR_STEP{k}_OFF" for k in KNOBS]


def lost(job, knobs, tag):
    work = os.path.join(OUT, job, tag)
    os.makedirs(work, exist_ok=True)
    env = dict(os.environ)
    for k in knobs: env[k] = "1"
    r = subprocess.run([EXE, "corpus-gate", "--jobs", job, "--work", work, "--baseline", BASE,
                        "--rules-db", os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"]],
                       env=env, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(work, "gate.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    why = next((l.strip() for l in r.stdout.splitlines() if l.strip().startswith(job + ":")), "")
    return r.returncode != 0, why


def one(job):
    log = []
    still, why = lost(job, KNOBS, "all-off")
    log.append(f"{job}: all {len(KNOBS)} knobs off -> {'STILL LOST (no knob since step %d explains it): %s' % (FIRST, why) if still else 'restored'}")
    if still: return log
    cand, rnd = KNOBS, 0
    while len(cand) > 1:
        rnd += 1
        half = cand[: len(cand) // 2]
        still, why = lost(job, half, f"r{rnd}")
        log.append(f"  round {rnd}: off {half[0][8:11]}..{half[-1][8:11]} ({len(half)}) -> {'still lost' if still else 'restored'}")
        cand = cand[len(cand) // 2:] if still else half
    still, why = lost(job, cand, "confirm")
    log.append(f"  => {cand[0]} {'ALONE restores it' if not still else 'does NOT alone restore it (an interaction) - ' + why}")
    return log


with ThreadPoolExecutor(len(SETS)) as ex:
    for lines in ex.map(one, SETS):
        print("\n".join(lines), flush=True)
