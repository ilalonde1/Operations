import fitz, sys
doc = fitz.open(sys.argv[1]); p = doc[5]
for d in p.get_drawings():
    r = d["rect"]
    if d["type"] == "f" and abs(r.x0 - 1758.4) < 0.2 and abs(r.y0 - 1568.2) < 0.2:
        for it in d["items"]: print([ (round(q.x, 2), round(q.y, 2)) for q in it[1:] ])
n = sum(1 for d in p.get_drawings() if d["type"] == "f" and len(d["items"]) == 5 and 11 < d["rect"].width < 13 and 11 < d["rect"].height < 13)
print("same-size 5-segment filled shapes on page 6:", n)
