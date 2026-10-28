r"""attribute.py : for each of the six banked sets, every storey whose model moved from Baselines/pdf-only-<job>.e2k to today's
build (rb-all-on), and the step it traces to: the knob whose build with that step alone off gives that storey back as banked.
Also whether all seven off together gives back the whole baseline. Compared by takeoff model-diff (by position - object names
renumber between builds). COVERS: the storeys model-diff lists as changed, with its own per-storey line as the storey's state.
DOES NOT: a change no knob of 175-183 carries (named UNATTRIBUTED); two steps that each move a storey (named by every knob
that restores it, or none if only both off together do)."""
import hashlib, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = r"C:\VIsual Studio Projects\Operations"
TAKEOFF = REPO + r"\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"
BASE = REPO + r"\Kor.Operations.EngineeringTools.Core.Tests\Baselines"
SIX = os.path.join(HERE, "sixrepair")
KNOBS = [f"KOR_STEP{n}_OFF" for n in (175, 176, 177, 179, 180, 182, 183)]
ALLOFF = "off-" + "-".join(k[8:11] for k in KNOBS)
STOREY = re.compile(r"^(\S+): columns ")


def moved(a, b):
    """storey -> model-diff's line for it, for every storey model-diff lists as changed (a -> b)"""
    out = subprocess.run([TAKEOFF, "model-diff", a, b], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    rows = {}
    for line in out.splitlines():
        if m := STOREY.match(line): rows[m.group(1)] = line.strip()
    return rows


def h(p): return hashlib.sha256(open(p, "rb").read()).hexdigest()[:16]


for job in ("31065-01", "31130-01", "31138-01", "31168-01", "31170-01-arch", "31202-01"):
    base = os.path.join(BASE, f"pdf-only-{job}.e2k")
    now = os.path.join(SIX, job, "rb-all-on", "out.e2k")
    alloff = os.path.join(SIX, job, ALLOFF, "out.e2k")
    print(f"\n=== {job}: banked {h(base)}, today {h(now)}, all seven off {h(alloff)} "
          f"({'GIVES BACK THE BASELINE byte for byte' if h(alloff) == h(base) else 'does NOT give back the baseline'})")
    drift = moved(base, now)
    left = moved(base, alloff)
    single = {k: moved(base, os.path.join(SIX, job, k, "out.e2k")) for k in KNOBS}
    print(f"   {len(drift)} storey(s) moved banked -> today; {len(left)} still moved with all seven off")
    for st in sorted(drift, key=lambda s: (len(s), s)):
        back = [k[8:11] for k in KNOBS if st not in single[k]]          # this step off alone gives the storey back
        tag = f"step {', '.join(back)}" if back else ("UNATTRIBUTED (moved with all seven off too)" if st in left else "only several off together")
        print(f"   {st:<10} {tag:<32} | {drift[st][len(st) + 2:][:150]}")
    for st in sorted(set(left) - set(drift)): print(f"   {st:<10} moved ONLY with all seven off | {left[st][len(st) + 2:][:120]}")
