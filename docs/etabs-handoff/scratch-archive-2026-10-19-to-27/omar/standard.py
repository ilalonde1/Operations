"""KOR's shearwall and column presentation standard: page text mentioning colour words, and every markup's colours and text."""
import fitz, re, sys
doc = fitz.open(sys.argv[1])
for i, p in enumerate(doc):
    text = p.get_text()
    for line in text.splitlines():
        if re.search(r"(?i)yellow|burgundy|maroon|red|green|blue|colou?r|hatch|shade|legend", line):
            print(f"p{i+1} TEXT: {line.strip()[:150]}")
    for a in p.annots():
        f = a.colors.get("fill"); s = a.colors.get("stroke")
        print(f"p{i+1} ANNOT {a.type[1]:9} fill={[round(c,2) for c in f] if f else None} stroke={[round(c,2) for c in s] if s else None} "
              f"{a.info.get('subject','')[:25]!r} {a.info.get('content','')[:90]!r}")
