r"""Self-test for pair_guard.py, on TODAY'S REAL PAIRS - known-refusals AND known-passes. A guard
never tested on a case it must refuse would be its own first instance, which is what this guard is
about: an answer taken from a name instead of from the thing.

The known answers were established on 2026-10-28 (board 76) BEFORE this guard existed:
  - takeoff-bin-d435 holds b94b8d33, not the d4352fc0 its folder name claims;
  - C:\w\b52 holds 77b56e29 and the string JoinThroughColumns; the d435 bin holds neither;
  - gw-fix2 ran against the share census, gw-fix2m and gw-b52 against the local mirror - which alone
    took 31085-01 from 3 storeys to 7 and was read as the wall-join's doing;
  - gw-fix2m vs gw-b52 is the like-for-like pair, bar the jobs whose view lists differ.
Reads only. Nothing is built, nothing is run, no work directory is written to.

⚠ These are LIVE folders in another session's scratchpad, not fixtures: 80064-01's view list changed
between two runs four minutes apart while gw-fix2m was still being written (gw-b52 finished at 11:12;
gw-fix2m and gw-narrow were the live ones). So this asserts the guard's DECISIONS and, for a refusal,
WHY - and only prints the dropped job names, which are whatever is true at the moment. Asserting the
reason matters more than it looks: gw-before recorded no root when these cases were written and does
now, so the "no root recorded" case would have gone on passing while testing something else.
When the scratchpad is cleared the cases stop being runnable; the honest thing then is to rebuild a
pair, not to weaken what is asserted here.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pair_guard as pg  # noqa: E402

E4 = (r"C:\Users\ilalonde\AppData\Local\Temp\claude"
      r"\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad")
FIX2 = os.path.join(E4, "gw-fix2")                   # the share census
FIX2M = os.path.join(E4, "gw-fix2m")                 # the mirror, d4352fc0-equivalent bin
B52W = os.path.join(E4, "gw-b52")                    # the mirror, with the join
SHAREW = os.path.join(E4, "gw-before")               # the share, recorded after the fact
NOROOT = os.path.join(E4, "gw-pair")                 # a run whose root nobody can now state
MARKER = os.path.join(E4, "gw-marker")               # a real run of corpus-gate d54a76b3, which records both
D435 = os.path.join(E4, "tonight", "takeoff-bin-d435")
B52BIN = r"C:\w\b52\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0"

TRUE_CLAIM = {"before": (D435, "b94b8d33"), "after": (B52BIN, "77b56e29")}

# (name, before work, after work, claims, must refuse?, a phrase the refusal must carry)
# The last column is what stops a case passing for a reason that is not the one it was written for.
CASES = [
    ("the pair that caused this: share census vs mirror", FIX2, B52W, None, True,
     "projects root differs"),
    ("like-for-like, both the mirror", FIX2M, B52W, None, False, None),
    ("a run whose root nobody can state - refused, not guessed", NOROOT, B52W, None, True,
     "root unknown"),
    ("the share, recorded after the fact, against the mirror", SHAREW, B52W, None, True,
     "projects root differs"),
    ("a claimed sha the bin does not carry (d4352fc0 vs b94b8d33)", FIX2M, B52W,
     {"before": (D435, "d4352fc0"), "after": (B52BIN, "77b56e29")}, True, "vs claimed d4352fc0"),
    ("the claim that is true of both bins", FIX2M, B52W, TRUE_CLAIM, False, None),
    ("a marker the bin does carry (+JoinThroughColumns)", FIX2M, B52W,
     {"before": (D435, "b94b8d33"), "after": (B52BIN, "77b56e29+JoinThroughColumns")}, False, None),
    ("a marker the bin does NOT carry", FIX2M, B52W,
     {"before": (D435, "b94b8d33+JoinThroughColumns"), "after": (B52BIN, "77b56e29")}, True,
     "is NOT in"),
    ("a bin folder with no Core.dll", FIX2M, B52W,
     {"before": (E4, "b94b8d33"), "after": (B52BIN, "77b56e29")}, True, "no Kor.Operations"),
    # corpus-gate d54a76b3 records the run's own exit and its own bin; gw-marker is a real run of it
    ("a run that recorded exit 2 - it did not finish its work", MARKER, B52W, None, True,
     "did not finish its work"),
    ("a claim that disagrees with the bin the RUN recorded", MARKER, B52W,
     {"before": (D435, "b94b8d33"), "after": (B52BIN, "77b56e29")}, True, "run was made by"),
]


def run(before, after, claims):
    """(refused?, the lines it printed) - never its exception, which is the caller's business."""
    lines = []
    try:
        pg.check_pair(before, after, claims, log=lines.append)
        return False, lines
    except pg.Refused as ex:
        lines.append(str(ex))
        return True, lines


# ---- the instrument first, on answers established without it
detect = 0
DETECT = [("takeoff-bin-d435", D435, "b94b8d33", "JoinThroughColumns", False),
          ("C:\\w\\b52", B52BIN, "77b56e29", "JoinThroughColumns", True)]
for label, folder, want_sha, marker, marker_in in DETECT:
    dll = os.path.join(folder, pg.CORE)
    found, size = pg.informational_versions(dll)
    hit = marker.encode("latin-1") in io.open(dll, "rb").read()
    ok = len(found) == 1 and found[0].split("+")[1].startswith(want_sha) and hit == marker_in and size > 0
    detect += ok
    print(("  OK   " if ok else "  FAIL ") + f"{label}: {found}, {size:,} bytes, {marker} present={hit}")

print()
passed = 0
for name, before, after, claims, want_refused, because in CASES:
    refused, lines = run(before, after, claims)
    right_reason = because is None or any(because in l for l in lines)
    ok = refused == want_refused and right_reason
    passed += ok
    print(("  OK   " if ok else "  FAIL ") + name + ("   -> REFUSED" if refused else "   -> passed")
          + ("" if right_reason else f"   -> but not for {because!r}"))
    for line in lines:
        if line.startswith("REFUSED") or line.startswith("dropped:"):
            print("           " + line)

# ---- the markers corpus-gate now writes, read as facts rather than inferred
for label, work, want_bin, want_src in (("gw-marker (d54a76b3 run)", MARKER, "1.0.0+59fb132b", "run-done.txt"),
                                        ("gw-b52 (hand-written root)", B52W, None, "not recorded")):
    sha, how = pg.recorded_bin(work)
    ok = (sha or "").startswith(want_bin or "\0") if want_bin else (sha is None and how == want_src)
    passed += ok
    print(("  OK   " if ok else "  FAIL ") + f"{label}: run recorded bin {sha} [{how}]")

# ---- refusal 3, and its measured blind spot. gw-narrow was mid-run at 11:49 and gw-fix2m was still
# judging PAST its end-of-run summary - so one is caught and the other is not, and both are asserted.
# A live folder cannot be asserted forever: a case whose folder has moved on says so instead of failing.
skipped = 0
NARROW = os.path.join(E4, "gw-narrow")
for label, work, want_caught in (("gw-narrow, mid-run", NARROW, True),
                                 ("gw-b52, finished 11:12", B52W, False),
                                 ("gw-fix2m, past its summary and still judging (BLIND SPOT)", FIX2M, False)):
    log = os.path.join(work, "gate.log")
    lines = io.open(log, encoding="utf-8", errors="replace").read().splitlines() if os.path.isfile(log) else []
    progress = sum(1 for l in lines if l.strip().startswith("["))      # read without the guard's own rule
    caught = pg.still_running(work) is not None
    if want_caught and not caught and progress:
        skipped += 1
        print("  SKIP " + label + f": that run has since reached its summary ({len(lines)} lines) - not testable now")
        continue
    ok = caught == want_caught
    passed += ok
    print(("  OK   " if ok else "  FAIL ") + f"{label}: still_running={caught} ({len(lines)} log lines, {progress} progress lines)")

# the refusal has to name BOTH values: "mismatch" costs the next person a rebuild (board 76)
_, lines = run(FIX2M, B52W, {"before": (D435, "d4352fc0"), "after": (B52BIN, "77b56e29")})
both = any("b94b8d33" in l and "d4352fc0" in l for l in lines)
print(("  OK   " if both else "  FAIL ") + "the refusal prints BOTH values, not just 'mismatch'")

# and the like-for-like pair drops jobs by name, so the run goes on with the rest
_, lines = run(FIX2M, B52W, TRUE_CLAIM)
dropped = sorted(l.split("dropped: ")[1].split(" (")[0] for l in lines if l.startswith("dropped: "))
print(f"         like-for-like dropped by name: {dropped}")

print()
WANT = len(CASES) + 3 + 2 - skipped     # cases + the three run-state checks + the two marker reads
print("self-test: {0} of {1} decisions ({2} not runnable now), {3} of {4} detections, both-values {5}".format(
    passed, WANT, skipped, detect, len(DETECT), "yes" if both else "NO"))
sys.exit(0 if (passed == WANT and detect == len(DETECT) and both) else 1)
