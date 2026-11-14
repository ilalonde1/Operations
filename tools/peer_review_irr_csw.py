# JM 01819 irregularity screen (row 1982, JM-LAT-PREP 14 Nov 2026; scratch irr_csw.py brought into the repo). Reads WHM's drawn walls only
# (model-audit/drawn-walls.csv), never a model. Output to stdout; independent/irregularity/csw.txt is its committed output. See IRREGULARITY.md.
# SCREEN from WHM's drawn walls: per CSW line and storey, clipped length (pieces), thickness, centreline offset from
# the line, and max angle off axis; then SFRS-only sum t*L and sum t*L^3 per building+direction, CLIPPED to the
# S801 extent of each CSW (pieces summed individually for t*L^3). Usage: irr_csw.py drawn-walls.csv
import csv, sys, math, collections

rows = [r for r in csv.DictReader(l for l in open(sys.argv[1]) if not l.startswith('#')) if 'COMPACT' not in r['how']]
order = ['P2','P1/GF','L1','L2'] + [f'L{i}' for i in range(3,32)] + ['ROOF AMENITY','ROOF','MECHANICAL SERVICE','ELEVATOR PENTHOUSE']
CSW = [('CSW1','H',59070,74891,84751,'B'),('CSW2','H',53576,74891,84751,'B'),('CSW3','H',50577,74891,84751,'B'),
       ('CSW4','V',75298,50171,59375,'B'),('CSW5','V',84344,50171,59375,'B'),
       ('CSW6','V',24772,54196,59653,'A'),('CSW9','V',27617,54196,59806,'A'),('CSW8','H',59653,24620,27769,'A'),
       ('CSW7','H',54196,24620,27769,'A'),('CSW10','V',52936,54222,59432,'A'),('CSW11','V',40081,52706,57189,'A'),
       ('CSW12','H',57036,40081,45925,'A'),('CSW13','V',45925,52706,57189,'A')]
maxoff = collections.defaultdict(float); maxang = 0
print('storey | ' + ' | '.join(f'{b}{d} tL tL3' for b in 'AB' for d in 'HV') + ' | pieces per CSW (mm)')
for st in order:
    acc = collections.defaultdict(lambda: [0.0, 0.0]); pcs = {}
    for name,d,c,lo,hi,b in CSW:
        pcs[name] = []
        for r in rows:
            if r['storey'] != st: continue
            x1,y1,x2,y2 = (float(r[k]) for k in ('x1','y1','x2','y2'))
            dd = 'H' if abs(x2-x1) >= abs(y2-y1) else 'V'
            if dd != d: continue
            cc = (y1+y2)/2 if d=='H' else (x1+x2)/2
            a,bb = sorted((x1,x2)) if d=='H' else sorted((y1,y2))
            L = min(bb,hi) - max(a,lo)
            if abs(cc-c) <= 450 and L > 300:
                t = float(r['thk_mm'])
                if t < 100: continue
                pcs[name].append(round(L)); acc[(b,d)][0] += t/1000*L/1000; acc[(b,d)][1] += t/1000*(L/1000)**3
                maxoff[name] = max(maxoff[name], abs(cc-c))
                ang = math.degrees(math.atan2(abs(y2-y1), abs(x2-x1))); ang = ang if d=='H' else 90-ang
                maxang = max(maxang, ang)
    print(st, '|', ' | '.join(f"{acc[(b,d)][0]:.2f} {acc[(b,d)][1]:.0f}" for b in 'AB' for d in 'HV'), '|', ' '.join(f"{n}={'+'.join(map(str,p)) or '-'}" for n,p in pcs.items()))
print('max centreline offset from the line (mm):', {k: round(v) for k,v in maxoff.items()}, ' max angle off axis (deg):', round(maxang,2))
