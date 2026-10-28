r"""kebab_dump.py <old|new> <out> : every numeric option of PlanClassificationOptions and ComposeOptions (read from source) with the
rule key CompiledDefaultsAreTheBankedRowsTests derives from its name - under the guard's current Kebab (old) or the one that also
splits a capital starting a word (new). For diffing the two."""
import re, sys
mode, out = sys.argv[1], sys.argv[2]
ROOT = r"C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core\Dxf"
OLD = r"(?<=[a-z0-9])(?=[A-Z])"
NEW = r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])"
rows = []
for cls, fn in (("PlanClassificationOptions", "StructuralPlanClassifier.cs"), ("ComposeOptions", "E2kGeometryComposer.cs")):
    src = open(f"{ROOT}\\{fn}", encoding="utf-8-sig").read()
    m = re.search(r"(?:class|record)\s+" + cls + r"\b", src)
    body, depth, i = "", 0, src.index("{", m.end())
    for j in range(i, len(src)):
        depth += src[j] == "{"; depth -= src[j] == "}"
        if depth == 0: body = src[i:j]; break
    for p in re.finditer(r"public\s+(double|int|bool|long|decimal|float)\??\s+(\w+)\s*\{", body):
        rows.append(f"{cls}.{p.group(2)} -> dxf." + re.sub(OLD if mode == "old" else NEW, "-", p.group(2)).lower())
open(out, "w", encoding="utf-8").write("\n".join(sorted(rows)) + "\n")
print(len(rows), "options")
