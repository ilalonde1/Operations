"""The compiled constants of the PDF intake's readers, as a table: file, name, value, and the sentence
above each — the input to the completion plan's WP5 triage (a drafting CONVENTION becomes a KorStandards
row; a geometric TOLERANCE stays code and says so; a DEAD one goes).

    python tools/list_reader_constants.py [--markdown]

Reads Kor.Operations.EngineeringTools.Core/{Intake,PdfToSafe,Dxf} and the two schedule readers. A
constant's sentence is the last /// summary or // comment line directly above it. Kept in tools/ so the
triage can be re-run when a constant is added; the triage itself is in the plan document.
"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Kor.Operations.EngineeringTools.Core")
CONST = re.compile(r"^\s*(?:public|internal|private)?\s*const\s+(?:double|int|float)\s+(?P<name>[A-Za-z0-9_]+)\s*=\s*(?P<value>[^;]+);")


def scan():
    rows = []
    folders = ["Intake", "PdfToSafe", "Dxf"]
    files = []
    for f in folders:
        d = os.path.join(ROOT, f)
        for name in sorted(os.listdir(d)):
            if name.endswith(".cs"):
                files.append(os.path.join(d, name))
    for extra in ("ScheduleGridReader.cs", "SheetScaleReader.cs", "SheetTitleReader.cs", "ScheduleTableBorder.cs"):
        p = os.path.join(ROOT, extra)
        if os.path.exists(p):
            files.append(p)
    for path in files:
        lines = open(path, encoding="utf-8-sig").read().split("\n")
        for i, line in enumerate(lines):
            m = CONST.match(line)
            if not m:
                continue
            # the sentence above: nearest preceding comment line(s)
            j = i - 1
            sentence = ""
            while j >= 0 and lines[j].strip().startswith(("///", "//")):
                text = re.sub(r"^\s*/{2,3}\s?", "", lines[j]).strip()
                text = re.sub(r"</?summary>", "", text).strip()
                sentence = (text + " " + sentence).strip() if text else sentence
                j -= 1
            rows.append((os.path.relpath(path, os.path.join(ROOT, "..")).replace("\\", "/"), m.group("name"), m.group("value").strip(), sentence[:160]))
    return rows


def main():
    rows = scan()
    md = "--markdown" in sys.argv
    if md:
        print("| file | constant | value | what the code says it is |")
        print("|---|---|---|---|")
        for f, n, v, s in rows:
            print(f"| `{f.split('/')[-1]}` | `{n}` | `{v}` | {s.replace('|', '/')} |")
    else:
        for f, n, v, s in rows:
            print(f"{f.split('/')[-1]:36} {n:34} {v:12} {s}")
    print(f"\n{len(rows)} constants", file=sys.stderr)


if __name__ == "__main__":
    main()
