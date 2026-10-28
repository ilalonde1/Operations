r"""look_thick.py : for the storeys whose thickness the walk moved, the thickness mix (sq ft by inches) at the bank, at the
final build and in the engineer's model - so a step-183 zone (46", 30", 24") can be held against what she modelled."""
import os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "walk_attribute.py"), encoding="utf-8").read()
exec(src[:src.index("def members")])           # TO_MM, state()
YARD = os.path.join(os.environ["TEMP"], "kor-drawings", "yardsticks")
SIX = os.path.join(HERE, "sixrepair")
def norm(n):
    n = re.sub(r"LEVEL\s*", "L", n.upper().strip()); n = re.sub(r"\bL-(\d)", r"L\1", n); return re.sub(r"\bL0+(\d)", r"L\1", n)
def mix(s): return " ".join(f'{k:g}":{v:,.0f}' for k, v in sorted(s["thick"].items(), key=lambda kv: -kv[1]) if v > 50) if s else "-"
for arg in sys.argv[1:]:
    job, sts = arg.split(":")
    b = state(os.path.join(SIX, job, "rb-7345", "out.e2k")); f = state(os.path.join(SIX, job, "walk-86a2e160", "out.e2k"))
    h = {norm(k): v for k, v in state(os.path.join(YARD, job.split("-arch")[0] + ".e2k")).items()}
    for st in sts.split(","):
        print(f"{job} {st}\n   bank  {mix(b.get(st))}\n   final {mix(f.get(st))}\n   hers  {mix(h.get(norm(st)))}")
