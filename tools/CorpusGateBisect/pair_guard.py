r"""pair_guard.py <before-work> <after-work> [--before-bin DIR --after-bin DIR] [--claim before=<sha>[+<marker>],after=<sha>[+<marker>]]
: refuse a gate pair whose two sides are not comparable, BEFORE anything is read from them.

WHY (2026-10-28). A pair was run as "d4352fc0 vs the board-52 join" and its difference - L3 gaining a
floor and losing a column on 31085-01 - was taken for the join's doing and held the branch. Rebuilt
side by side it was not: the plate's outline is character-identical with the join on and off, and the
columns are 57 both ways. The two sides had been built against DIFFERENT PROJECTS ROOTS, a share census
one night and the local mirror the next, which alone takes 31085 from 3 storeys to 7. The bin was
mislabelled as well - the folder reads takeoff-bin-d435, the assembly reads 1.0.0+b94b8d33 - though that
one was harmless, b94b8d33 being d4352fc0 plus a comment and a Python guard.

Both faults are the same one: IDENTITY TAKEN FROM A NAME BESIDE THE THING INSTEAD OF FROM THE THING.
A folder name is not a commit, and a work directory is not a statement of what it was built against. So
this asks the artefacts.

THE THREE REFUSALS
  1. BIN IDENTITY (whole run). Each side's Kor.Operations.EngineeringTools.Core.dll must carry the commit
     the comparison claims. Read as the AssemblyInformationalVersion stamped in the assembly's own bytes
     ("1.0.0+<40 hex>"), never FileVersion, which reads 1.0.0.0 on every build here and would agree with
     any claim. A claim may be "<sha>+<marker>": a build from a working tree stamps HEAD and not the
     change, so the change is named by a string that must be in the DLL (JoinThroughColumns, HoldsColumn).
  2. PROJECTS ROOT (whole run). Both sides must name the same root: run-root.txt in the work dir, or
     failing that the job count on gate.log's census line, which is an INFERENCE and says so. Neither
     present on a side = refused, not guessed.
  3. VIEWS (per job). A job whose <job>/dxf/ file lists differ between the sides is DROPPED by name with
     its counts, and the rest of the run carries on.

A refusal RAISES (SystemExit) rather than returning a flag, so a caller cannot carry on past one by
ignoring a return value - this morning's lock guard was inert for exactly that shape of reason. Pass
raise_on_refuse=False to get (ok_jobs, dropped) back instead and decide for yourself.

COVERS: which commit each side's geometry assembly carries, whether a named marker is in it, the recorded
projects root on each side, and the per-job view file NAMES.
DOES NOT COVER: the views' CONTENT (names equal, bytes different is invisible here - hash them if that
matters), the rules database each run read, KorStandards settings, the baseline ledger, anything about the
models themselves, or a bin folder holding a Core.dll that is not the one the exe loaded.
SAME-CLASS FAULT IT WOULD MISS: two runs of the SAME bin and root where one read a job from an older
drawing set that happens to carry the same file names - the gate's own "re-read from older drawings"
case. The names match, so this passes it.

Exit 0 when the pair is comparable (any dropped jobs are listed), 1 on a refusal, 2 on bad arguments.
"""
import io, os, re, sys

VERSION = re.compile(rb"\d+\.\d+\.\d+\+[0-9a-fA-F]{40}")
CORE = "Kor.Operations.EngineeringTools.Core.dll"
CENSUS = re.compile(r"census[^\n]*?\((\d+) job folders", re.I)


class Refused(SystemExit):
    """A pair that must not be compared. SystemExit so an unhandled one stops the caller."""


def informational_versions(dll):
    """Every "<n.n.n>+<sha>" the assembly carries, from its own bytes. More than one is reported, never
    silently reduced to the first: that choice is how a wrong identity gets made twice."""
    data = io.open(dll, "rb").read()
    return sorted({m.decode("ascii") for m in VERSION.findall(data)}), len(data)


def bin_identity(bin_dir, claim, side):
    """(what the assembly says, what was claimed, [problems]) for one side."""
    dll = os.path.join(bin_dir, CORE)
    sha, _, marker = claim.partition("+")
    if not os.path.isfile(dll):
        return None, claim, [f"{side} bin: no {CORE} in {bin_dir}"]
    found, size = informational_versions(dll)
    problems = []
    if len(found) != 1:
        problems.append(f"{side} bin: {len(found)} informational version(s) in {CORE} ({found}) - expected exactly 1")
    stamped = found[0] if found else "(none)"
    if found and not stamped.split("+", 1)[1].startswith(sha.lower()):
        problems.append(f"{side} bin: ProductVersion {stamped} vs claimed {claim}")
    if marker:
        data = io.open(dll, "rb").read()
        if marker.encode("latin-1") not in data:
            problems.append(f"{side} bin: marker {marker!r} is NOT in {CORE} ({size:,} bytes read), "
                            f"though the claim says this build carries it")
    return stamped, claim, problems


def recorded_root(work):
    """(root, how it was established) - run-root.txt first, then gate.log's census count as an inference."""
    p = os.path.join(work, "run-root.txt")
    if os.path.isfile(p):
        line = io.open(p, encoding="utf-8", errors="replace").read().strip().splitlines()[0].strip()
        return line.split(" (")[0].strip(), "run-root.txt"      # the note after the path is a note
    g = os.path.join(work, "gate.log")
    if os.path.isfile(g):
        m = CENSUS.search(io.open(g, encoding="utf-8", errors="replace").read())
        if m:
            return f"census of {m.group(1)} job folders", "gate.log census count (INFERRED, not the root itself)"
    return None, "nothing recorded"


def views(work, job):
    d = os.path.join(work, job, "dxf")
    return sorted(os.listdir(d)) if os.path.isdir(d) else []


def check_pair(before_work, after_work, claims=None, raise_on_refuse=True, log=print):
    """claims: {"before": (bin_dir, "<sha>[+marker]"), "after": (...)} - omit to skip refusal 1.
    Returns (ok_jobs, dropped) where dropped is [(job, reason)]."""
    refusals = []

    if claims:
        for side, work in (("before", before_work), ("after", after_work)):
            if side not in claims:
                refusals.append(f"{side} bin: no claim given for this side, while the other side has one")
                continue
            bin_dir, claim = claims[side]
            stamped, claimed, problems = bin_identity(bin_dir, claim, side)
            refusals += problems
            if not problems:
                log(f"  {side} bin: {stamped} - matches the claim {claimed}")

    roots = {}
    for side, work in (("before", before_work), ("after", after_work)):
        root, how = recorded_root(work)
        roots[side] = root
        if root is None:
            refusals.append(f"root unknown on {side} ({work}): no run-root.txt and no census line in gate.log")
        else:
            log(f"  {side} root: {root}   [{how}]")
    if roots["before"] and roots["after"] and roots["before"] != roots["after"]:
        refusals.append(f"projects root differs: before {roots['before']} vs after {roots['after']}")

    if refusals:
        for r in refusals:
            log("REFUSED: " + r)
        if raise_on_refuse:
            raise Refused(f"pair_guard: {len(refusals)} refusal(s) - the two sides are not comparable")

    jobs = sorted(j for j in os.listdir(after_work)
                  if os.path.isdir(os.path.join(after_work, j)) and os.path.isdir(os.path.join(before_work, j)))
    ok, dropped = [], []
    for job in jobs:
        a, b = views(before_work, job), views(after_work, job)
        if a == b:
            ok.append(job); continue
        only_b, only_a = len(set(a) - set(b)), len(set(b) - set(a))
        names = sorted(set(a) ^ set(b))
        dropped.append((job, f"views differ: {only_b} only before, {only_a} only after"))
        log(f"dropped: {job} (views differ: {only_b} only before, {only_a} only after) {names[:4]}")
    log(f"{len(ok)} of {len(jobs)} job(s) comparable; {len(dropped)} dropped")
    return ok, dropped


def main():
    a = sys.argv[1:]
    if len(a) < 2:
        print(__doc__); return 2
    before_work, after_work = a[0], a[1]
    o = {}
    i = 2
    while i < len(a):
        o[a[i]] = a[i + 1]; i += 2
    claims = None
    if "--claim" in o:
        parts = dict(p.split("=", 1) for p in o["--claim"].split(",") if "=" in p)
        if not ("--before-bin" in o and "--after-bin" in o):
            print("pair_guard: --claim needs --before-bin and --after-bin"); return 2
        claims = {"before": (o["--before-bin"], parts.get("before", "")),
                  "after": (o["--after-bin"], parts.get("after", ""))}
    try:
        check_pair(before_work, after_work, claims)
    except Refused as ex:
        print(ex); return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
