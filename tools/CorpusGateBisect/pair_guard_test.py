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

⚠ These are LIVE folders in another session's scratchpad, not fixtures: gw-b52's view list for 80064-01
changed between two runs four minutes apart while that session re-ran it. So this asserts the guard's
DECISIONS - which pairs it refuses and which it passes - and only prints the dropped job names, which
are whatever is true at the moment. When the scratchpad is cleared the cases stop being runnable; at
that point the honest thing is to rebuild a pair rather than to weaken what is asserted here.
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
NOROOT = os.path.join(E4, "gw-before")               # a run that recorded nothing
D435 = os.path.join(E4, "tonight", "takeoff-bin-d435")
B52BIN = r"C:\w\b52\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0"

TRUE_CLAIM = {"before": (D435, "b94b8d33"), "after": (B52BIN, "77b56e29")}

# name -> (before work, after work, claims, must refuse?)
CASES = [
    ("the pair that caused this: share census vs mirror", FIX2, B52W, None, True),
    ("like-for-like, both the mirror", FIX2M, B52W, None, False),
    ("a side that recorded no root at all - refused, not guessed", NOROOT, B52W, None, True),
    ("a claimed sha the bin does not carry (d4352fc0 vs b94b8d33)", FIX2M, B52W,
     {"before": (D435, "d4352fc0"), "after": (B52BIN, "77b56e29")}, True),
    ("the claim that is true of both bins", FIX2M, B52W, TRUE_CLAIM, False),
    ("a marker the bin does carry (+JoinThroughColumns)", FIX2M, B52W,
     {"before": (D435, "b94b8d33"), "after": (B52BIN, "77b56e29+JoinThroughColumns")}, False),
    ("a marker the bin does NOT carry", FIX2M, B52W,
     {"before": (D435, "b94b8d33+JoinThroughColumns"), "after": (B52BIN, "77b56e29")}, True),
    ("a bin folder with no Core.dll", FIX2M, B52W,
     {"before": (E4, "b94b8d33"), "after": (B52BIN, "77b56e29")}, True),
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
for name, before, after, claims, want_refused in CASES:
    refused, lines = run(before, after, claims)
    ok = refused == want_refused
    passed += ok
    print(("  OK   " if ok else "  FAIL ") + name + ("   -> REFUSED" if refused else "   -> passed"))
    for line in lines:
        if line.startswith("REFUSED") or line.startswith("dropped:"):
            print("           " + line)

# the refusal has to name BOTH values: "mismatch" costs the next person a rebuild (board 76)
_, lines = run(FIX2M, B52W, {"before": (D435, "d4352fc0"), "after": (B52BIN, "77b56e29")})
both = any("b94b8d33" in l and "d4352fc0" in l for l in lines)
print(("  OK   " if both else "  FAIL ") + "the refusal prints BOTH values, not just 'mismatch'")

# and the like-for-like pair drops jobs by name, so the run goes on with the rest
_, lines = run(FIX2M, B52W, TRUE_CLAIM)
dropped = sorted(l.split("dropped: ")[1].split(" (")[0] for l in lines if l.startswith("dropped: "))
print(f"         like-for-like dropped by name: {dropped}")

print()
print("self-test: {0} of {1} decisions, {2} of {3} detections, both-values {4}".format(
    passed, len(CASES), detect, len(DETECT), "yes" if both else "NO"))
sys.exit(0 if (passed == len(CASES) and detect == len(DETECT) and both) else 1)
