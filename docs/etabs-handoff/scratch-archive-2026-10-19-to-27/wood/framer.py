r"""framer.py : for every sheet step 184 dropped (arm-on reports), the sheet that framed it and how much of THAT sheet's wall
linework the pipeline tagged as stud partition (layer *PARTITION*) against concrete wall (layers *WALL*, not partition), with
the storey's verdict against the engineer's model from arms.out. The question: is a floor wood exactly when the plan whose
framing it is carries stud walls? COVERS: the DXFs the gate wrote (entities counted per layer). DOES NOT: walls drawn on a
layer named otherwise; a framer sheet carrying both a concrete podium and wood walls (a share in between)."""
import collections, os, re
import ezdxf

HERE = os.path.dirname(os.path.abspath(__file__))
ON = os.path.join(HERE, "arm-on")
LINE = re.compile(r"(S[\w.]+?_\d+_[^;(]+?) \((\S+?): (\d+) outline\(s\), (\d+) opening\(s\); framed by ([^)]+)\)")

# the storey verdicts from arms.out: (job, storey) -> verdict
verdict = {}
job = None
for line in open(os.path.join(HERE, "arms.out"), encoding="utf-8"):
    if m := re.match(r"^(\d{5}-\d\d):", line): job = m.group(1); continue
    if m := re.match(r"\s+(\S+)\s+off\s+([\d,]+) -> on\s+([\d,]+)\s+hers\s+(\S+)\s+(.*)", line):
        verdict[(job, m.group(1))] = (m.group(5).split(":")[0], m.group(2), m.group(3), m.group(4))


def share(dxf):
    if not os.path.exists(dxf): return None
    c = collections.Counter(e.dxf.layer.upper() for e in ezdxf.readfile(dxf).modelspace() if e.dxftype() in ("LINE", "LWPOLYLINE", "POLYLINE"))
    part = sum(n for l, n in c.items() if "PARTITION" in l)
    wall = sum(n for l, n in c.items() if "WALL" in l and "PARTITION" not in l)
    return (part, wall)


rows = collections.Counter()
for job in sorted(os.listdir(ON)):
    rp = os.path.join(ON, job, "report.txt")
    if not os.path.exists(rp): continue
    text = next((l for l in open(rp, encoding="utf-8", errors="replace") if "step 184" in l), None)
    if not text: continue
    for name, storey, outl, opens, framer in LINE.findall(text):
        s = share(os.path.join(ON, job, "dxf", framer.strip() + ".dxf"))
        from_norm = re.sub(r"^L0*", "L", storey)
        v = verdict.get((job, from_norm), ("(plate unchanged on the storey)",))[0]
        sh = "-" if not s or sum(s) == 0 else f"{s[0] / sum(s):.0%} stud ({s[0]}/{sum(s)})"
        print(f"{job} {storey:<5} dropped {name.strip()[:48]:<48} framer {framer.strip()[:44]:<44} framer {sh:<20} {v}")
        rows[(v, "framer mostly stud" if s and sum(s) and s[0] / sum(s) >= 0.5 else "framer mostly concrete wall" if s and sum(s) else "framer: no walls read")] += 1
print("\nby storey verdict and framer:")
for k, n in sorted(rows.items(), key=lambda kv: (kv[0][0], -kv[1])): print(f"   {n:4}  {k[0]:<40} {k[1]}")
