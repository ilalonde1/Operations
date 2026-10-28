r"""rebank.py : for the six banked sets, the builds that attribute their drift from Baselines/pdf-only-<job>.e2k to a step:
every rule on (tag all-on), the seven knobs of steps 175-183 off together, and each off alone. Uses sixrepair.one() - the
same PdfOnlyBuild route with rules from KorStandards that the six-set baseline test runs. COVERS: knobbed steps 175-183.
DOES NOT: a change between the banking commit (231e0f69) and now that carries no knob - if all seven off does not give
back the baseline, the rest is that, and is reported, not attributed."""
import os, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sixrepair   # noqa: E402

KNOBS = [f"KOR_STEP{n}_OFF" for n in (175, 176, 177, 179, 180, 182, 183)]
work = []
for job, share, scale in sixrepair.SETS:
    pdf = sixrepair.local(share)
    if not os.path.exists(pdf): sys.exit(f"MISSING mirror copy for {job}: {pdf}")
    work += [(job, pdf, scale, None), (job, pdf, scale, tuple(KNOBS))] + [(job, pdf, scale, k) for k in KNOBS]
print(f"{len(work)} builds", flush=True)
with ThreadPoolExecutor(int(os.environ.get("SIXKNOB_PAR", "4"))) as ex:
    for res in ex.map(lambda w: sixrepair.one(*w), work):
        print(res, flush=True)
