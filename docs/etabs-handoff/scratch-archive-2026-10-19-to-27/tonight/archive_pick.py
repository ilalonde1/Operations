r"""archive_pick.py <scratchpad> [--copy <dest>] : the files that go into the repo archive, and the rule that picked them.
PICKED: every SCRIPT (.py .ps1 .cs .sql .lsp .sh .bat) and every TEXT (.txt .log .csv .md .json .out .html .tsv) of at most 1 MB,
at the scratchpad's top level or ONE directory down (a run's ledger, log, report and notes; per-job subfolders two levels down
are the run's regenerable output), outside a copied bin and outside Griffin's material (griffin/installer|pkg|unpacked, kc/,
kc-data-*, kccond/ - his software and what was read out of it).
NOT PICKED: models, renders, bins, per-job outputs, text over 1 MB (ledger-sheets.csv: the per-sheet table each gate re-writes)."""
import os, shutil, sys
S = sys.argv[1]; dest = sys.argv[sys.argv.index("--copy") + 1] if "--copy" in sys.argv else None
THIRD = ("kc", "kccond") ; THIRD_PREFIX = ("kc-data-",)
GRIFFIN_THIRD = ("installer", "pkg", "unpacked")
SCRIPT = (".py", ".ps1", ".cs", ".sql", ".lsp", ".sh", ".bat"); TEXT = (".txt", ".log", ".csv", ".md", ".json", ".out", ".html", ".tsv")
bins = set()
for r, ds, fs in os.walk(S):
    if "takeoff.dll" in fs or "Kor.Operations.EngineeringTools.Core.dll" in fs: bins.add(os.path.normpath(r))
picked, skipped_big = [], 0
for r, ds, fs in os.walk(S):
    rel_dir = os.path.relpath(r, S); parts = [] if rel_dir == "." else rel_dir.split(os.sep)
    if len(parts) > 1: ds[:] = []; continue            # depth limit: top level and one down
    nr = os.path.normpath(r)
    if any(nr == b or nr.startswith(b + os.sep) for b in bins): ds[:] = []; continue
    if parts and (parts[0] in THIRD or parts[0].startswith(THIRD_PREFIX)): ds[:] = []; continue
    if parts == ["griffin"]: ds[:] = [d for d in ds if d not in GRIFFIN_THIRD]
    for f in fs:
        e = os.path.splitext(f)[1].lower(); p = os.path.join(r, f)
        if e in SCRIPT or e in TEXT:
            if e in TEXT and os.path.getsize(p) > 1_000_000: skipped_big += 1; continue
            picked.append(p)
size = sum(os.path.getsize(p) for p in picked)
print(f"picked {len(picked)} files, {size/1e6:.1f} MB; text over 1 MB left: {skipped_big}")
if dest:
    for p in picked:
        t = os.path.join(dest, os.path.relpath(p, S)); os.makedirs(os.path.dirname(t), exist_ok=True); shutil.copy2(p, t)
    print(f"copied to {dest}")
