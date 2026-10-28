r"""triage.py <scratchpad> : every file under the scratchpad (recursively) by class, with counts and bytes, so the triage is X of Y.
Classes: BIN (a copied takeoff/probe bin: a dir holding takeoff.dll or Kor.Operations.EngineeringTools.Core.dll), THIRD (griffin/
installer, pkg, unpacked - Griffin's software, never into our repo), MODEL (.e2k .dxf .edb .pdf .png .svg - outputs, regenerable),
TEXT (.txt .log .csv .md .json .out .html - run outputs / notes / ledgers), SCRIPT (.py .ps1 .cs .sql .lsp .sh), OTHER."""
import collections, os, sys
S = sys.argv[1]
bins = set()
for r, ds, fs in os.walk(S):
    if "takeoff.dll" in fs or "Kor.Operations.EngineeringTools.Core.dll" in fs: bins.add(os.path.normpath(r))
count, size, top = collections.Counter(), collections.Counter(), collections.defaultdict(collections.Counter)
for r, ds, fs in os.walk(S):
    nr = os.path.normpath(r)
    for f in fs:
        p = os.path.join(r, f); b = os.path.getsize(p)
        rel = os.path.relpath(p, S); first = rel.split(os.sep)[0]
        if any(nr == x or nr.startswith(x + os.sep) for x in bins): k = "BIN"
        elif rel.startswith(os.path.join("griffin", "installer")) or rel.startswith(os.path.join("griffin", "pkg")) or rel.startswith(os.path.join("griffin", "unpacked")): k = "THIRD"
        else:
            e = os.path.splitext(f)[1].lower()
            k = ("MODEL" if e in (".e2k", ".dxf", ".edb", ".pdf", ".png", ".svg", ".jpg", ".dwg", ".rvt") else
                 "TEXT" if e in (".txt", ".log", ".csv", ".md", ".json", ".out", ".html", ".tsv") else
                 "SCRIPT" if e in (".py", ".ps1", ".cs", ".sql", ".lsp", ".sh", ".bat") else "OTHER")
        count[k] += 1; size[k] += b; top[k][first] += b
tot = sum(count.values())
print(f"{tot} files, {sum(size.values())/1e9:.2f} GB, under {len(os.listdir(S))} top-level entries")
for k in ("BIN", "THIRD", "MODEL", "TEXT", "SCRIPT", "OTHER"):
    print(f"  {k:6s} {count[k]:7d} files  {size[k]/1e6:10.1f} MB   biggest: " + ", ".join(f"{n} {b/1e6:.0f}MB" for n, b in top[k].most_common(4)))
