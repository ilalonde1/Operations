r"""diff185.py <before-work> <after-work> <legend-check.out> : step 185's differential. Per set: out.e2k identical or not; the sets
that changed against the sets legend_box_plates.py named (prediction: exactly those); for each changed set, takeoff model-diff's
summary line (plates moved / walls lost - expect only the frames and their sample strokes)."""
import hashlib, os, re, subprocess, sys
before, after, named_out = sys.argv[1:4]
EXE = r"C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe"
named = {l.split(":")[0] for l in open(named_out, encoding="utf-8") if re.match(r"^\d{5}-\d{2}:", l)}
h = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()[:16]
same, changed, missing = [], [], []
for job in sorted(os.listdir(after)):
    a, b = os.path.join(before, job, "out.e2k"), os.path.join(after, job, "out.e2k")
    if not os.path.isfile(b): continue
    if not os.path.isfile(a): missing.append(job); continue
    (same if h(a) == h(b) else changed).append(job)
print(f"{len(same)} identical, {len(changed)} changed, {len(missing)} with no before; the check named {len(named)}")
print("changed and named    :", " ".join(j for j in changed if j in named))
print("changed, NOT named   :", " ".join(j for j in changed if j not in named) or "-")
print("named, NOT changed   :", " ".join(sorted(j for j in named if j not in changed)) or "-")
for job in changed:
    out = subprocess.run([EXE, "model-diff", os.path.join(before, job, "out.e2k"), os.path.join(after, job, "out.e2k")],
                         capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    tail = [l for l in out.splitlines() if l.strip().startswith("plates moved")]
    print(f"  {job}: {tail[-1].strip() if tail else '(no summary line)'}")
