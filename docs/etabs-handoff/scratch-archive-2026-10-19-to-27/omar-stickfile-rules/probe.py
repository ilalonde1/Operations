import fitz, sys
doc = fitz.open(sys.argv[1]); p = doc[5]; k = 96 * 25.4 / 72
for line in open("l35-out.txt").readlines()[:2]:
    name, x, y = line.split(); px, py = float(x) / k, p.rect.height - float(y) / k
    print("==", name, round(px, 1), round(py, 1))
    for d in p.get_drawings():
        r = d["rect"]
        if r.x0 - 3 <= px <= r.x1 + 3 and r.y0 - 3 <= py <= r.y1 + 3 and r.width < 40:
            kinds = "".join(it[0] for it in d["items"])
            print(f"  type={d['type']} fill={d.get('fill')} rect=({r.x0:.1f},{r.y0:.1f},{r.x1:.1f},{r.y1:.1f}) items={kinds} n={len(d['items'])}")
