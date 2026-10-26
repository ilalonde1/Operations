"""Name the intake step behind each set the corpus gate marks LOST, by halving the step knobs - judged by the gate itself.

    python tools/CorpusGateBisect/group_bisect.py <takeoff.exe> <set,set,...> [--first 141] [--baseline <ledger-sets-*.csv>]

Why: `takeoff corpus-gate --bisect` tries ONE knob at a time, one set at a time, sequentially. With the 35 knobs since
the run45 bank over five lost sets that is ~175 set builds - many hours (2026-10-26). Halving is ~log2(knobs) rounds per
set, the sets in parallel.

How: for one set, run `takeoff corpus-gate --jobs <set>` with a GROUP of KOR_STEP<n>_OFF knobs switched off. Exit 0 = the
set is no longer lost, so the step is in that group; exit 1 = it is in the other half. Each set builds in its own --work
dir (a shared work dir is clobbered by parallel runs). The knobs are read from CorpusGateVerb.cs, the same list the gate
advertises, from --first on. The verdict is the gate's own judge - the one that marked the set lost.

WHAT IT CAN CONCLUDE:
- "<knob> ALONE restores it" - one step, switched off alone, un-loses the set.
- "does NOT alone restore it (an interaction)" - the halving converged on a knob that only restores with others off.
- "STILL LOST with every knob off" - no step since --first explains it: a change with no knob (a refactor, a rule value,
  a drawing re-issued since the bank), or a loss older than --first.
WHAT IT CANNOT: tell two steps that each alone cause the loss apart (it names one); see a knob missing from the gate's
list (ABisectKnobActuallyTurnsItsRuleOffTests guards that list); say whether the loss is a fault - a lost set can be
the bank's own error. LOOK at the set before and after before calling it either.

Runs write under %TEMP%\\kor-intake-runs\\group-bisect\\<set>\\<round>\\ (gate.log per run). Local mirror only: the gate
reads the cached census and the drawing mirror, never the share.
"""
import os
import re
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
args = sys.argv[1:]
if len(args) < 2:
    sys.exit(__doc__)
EXE, SETS = args[0], args[1].split(",")
FIRST = int(args[args.index("--first") + 1]) if "--first" in args else 141
BASE = args[args.index("--baseline") + 1] if "--baseline" in args else os.path.join(
    REPO, "docs", "etabs-handoff", "corpus", "ledger-sets-2026-09-24-run45-step140.csv")
OUT = os.path.join(os.environ["TEMP"], "kor-intake-runs", "group-bisect")
src = open(os.path.join(REPO, "Kor.Operations.EngineeringTools.TakeoffCli", "Verbs", "CorpusGateVerb.cs"), encoding="utf-8").read()
KNOBS = [f"KOR_STEP{k}_OFF" for k in sorted({k for k in re.findall(r"KOR_STEP(\d+)_OFF", src) if int(k) >= FIRST}, key=int)]


def lost(job, knobs, tag):
    work = os.path.join(OUT, job, tag)
    os.makedirs(work, exist_ok=True)
    env = dict(os.environ)
    for k in knobs:
        env[k] = "1"
    r = subprocess.run([EXE, "corpus-gate", "--jobs", job, "--work", work, "--baseline", BASE,
                        "--rules-db", os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"]],
                       env=env, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(work, "gate.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    why = next((l.strip() for l in r.stdout.splitlines() if l.strip().startswith(job + ":")), "")
    return r.returncode != 0, why


def one(job):
    log = []
    still, why = lost(job, KNOBS, "all-off")
    if still:
        return [f"{job}: STILL LOST with all {len(KNOBS)} knobs from step {FIRST} off - no step since then explains it: {why}"]
    log.append(f"{job}: all {len(KNOBS)} knobs off -> restored")
    cand, rnd = KNOBS, 0
    while len(cand) > 1:
        rnd += 1
        half = cand[: len(cand) // 2]
        still, why = lost(job, half, f"r{rnd}")
        log.append(f"  round {rnd}: off {half[0][8:-4]}..{half[-1][8:-4]} ({len(half)}) -> {'still lost' if still else 'restored'}")
        cand = cand[len(cand) // 2:] if still else half
    still, why = lost(job, cand, "confirm")
    log.append(f"  => {cand[0]} {'ALONE restores it' if not still else 'does NOT alone restore it (an interaction): ' + why}")
    return log


if __name__ == "__main__":
    with ThreadPoolExecutor(len(SETS)) as ex:
        for lines in ex.map(one, SETS):
            print("\n".join(lines), flush=True)
