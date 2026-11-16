#!/usr/bin/env python3
r"""JM 01819 lateral CAPACITY side, from WHM's drawings only (row 1982; made 13 Nov 2026 as JM-LATERAL-CAP's scratch
calc_capacity.py, brought into the repo by JM-LAT-PREP 14 Nov 2026 - REF-AUDIT item 18: the arithmetic was unreproducible).

INPUTS (independent): S801 header schedule, wall schedule and notes, S101/S111 materials, S201-S203 core pad - typed below
verbatim with their sheet; S802 header geometry from --headers (peer_review_s802_headers.py's output, committed as
jobs/01819-01-renfrew/review/lateral/capacity/s802-headers.csv). Formulas: CSA A23.3-14, each echoed into its CSV row.
OUTPUTS (--out): 1-coupling-beams.csv, 1b-coupling-beams-by-storey.csv, 2-walls.csv, 2b-wall-zones.csv,
3-core-pad-footing-calcs.csv (3-core-pad-footing.csv is hand-written beside them, not by this script).
DOES NOT: read any model output; check that S802's scaled depths are right (peer_review_s802_headers.py says how they are
scaled: door top to the datum line under the level tag - at ROOF the slab is drawn ABOVE that datum, so ROOF depths here
are 10 in less than the concrete WHM draw, a conservative Vr; JM-LAT-PREP looked at S802, 14 Nov).
USAGE: python -I peer_review_lateral_capacity.py --headers <s802-headers.csv> --out <dir>
"""
import argparse
# JM-LATERAL-CAP scratch calc (13 Nov 2026). Inputs read off WHM IFT 2026-09-09 (S101, S111, S201-S203, S801, S802).
# Writes the three capacity tables as CSV into the review/lateral/capacity folder. Every formula is echoed into the CSV.
import csv, math, os, collections
_ap = argparse.ArgumentParser()
_ap.add_argument('--headers', required=True)
_ap.add_argument('--out', required=True)
_a = _ap.parse_args()

OUT = _a.out
os.makedirs(OUT, exist_ok=True)
IN = 25.4
AB = {'10M': (100, 11.3), '15M': (200, 16.0), '20M': (300, 19.5), '25M': (500, 25.2), '30M': (700, 29.9), '35M': (1000, 35.7)}
PHI_S, PHI_C, FY = 0.85, 0.65, 400.0   # cl. 8.4.3(a), 8.4.2; fy 400 per S101 (F13: 500 MPa locations not shown)

STOREYS = ['P3', 'P2', 'GF/P1'] + ['L%d' % i for i in range(1, 32)] + ['ROOF AMENITY', 'ROOF']
def sidx(s):
    s = {'P3/FDN': 'P3', 'P2/FDN': 'P2'}.get(s, s)
    return STOREYS.index(s)

# S801 HEADER SCHEDULE (detail G), verbatim bands. Band level names are storey names (see md: convention).
SCHED = {
 'H1': [('P3', 'L13', 6, '35M'), ('L14', 'L20', 6, '30M'), ('L21', 'L31', 4, '30M'), ('L32', 'ROOF', 4, '25M')],
 'H2': [('P3', 'L11', 8, '35M'), ('L12', 'L20', 6, '35M'), ('L21', 'L25', 6, '30M'), ('L26', 'L32', 4, '30M'), ('L33', 'ROOF', 4, '25M')],
 'H3': [('L1', 'L11', 6, '30M'), ('L12', 'L25', 4, '30M'), ('L26', 'ROOF', 4, '25M')],
}
def band_for(h, storey):
    i = sidx(storey)
    for a, b, n, bar in SCHED[h]:
        ia = sidx(a) if a in STOREYS else None
        ib = sidx(b) if b in STOREYS else None
        if ia is None:   # L32 / L33 (F21): do not exist; read as "the storeys above the previous band"
            ia = sidx('ROOF AMENITY') if a in ('L32', 'L33') else None
        if ib is None and b == 'L32':
            ib = sidx('L31')
        if ia is not None and ib is not None and ia <= i <= ib:
            flag = 'F21: band names L32/L33, which do not exist' if (a in ('L32', 'L33') or b == 'L32') else ''
            return (a, b, n, bar, flag)
    return None

WALL_T_IN = {'CSW1': 24.0, 'CSW4': 32.0, 'CSW5': 32.0}                 # S801 view A, 'Showing L3'
def wall_fc(storey):                                                   # S801 WALL REINF. notes, CSW1-CSW5
    i = sidx(storey)
    return 60 if i <= sidx('L8') else (50 if i <= sidx('L20') else 35)

COVER, STIR, TIE, ROW_HALF = 40.0, 11.3, 11.3, 2 * IN               # S111 beams cover to stirrups 1-1/2"; 10M closed stirrup; 10M cage tie; 2 rows @ 4" (detail G)
def y_cage(bar):
    return COVER + STIR + TIE + AB[bar][1] / 2 + ROW_HALF

def diag(n, bar, h_in, lu_ft):
    As = n * AB[bar][0]
    h = h_in * IN; lu = lu_ft * 304.8
    y = y_cage(bar)
    a = math.atan((h - 2 * y) / lu)
    s = math.sin(a)
    Vr = 2 * PHI_S * As * FY * s / 1000
    Vn = 2 * As * FY * s / 1000
    Vp = 2 * As * 1.25 * FY * s / 1000
    Mr = Vr * lu / 2 / 1000
    return dict(As_per_cage_mm2=As, y_mm=round(y, 1), alpha_deg=round(math.degrees(a), 1), sin_alpha=round(s, 3),
                Vr_kN=round(Vr), Vn_kN=round(Vn), Vp_kN=round(Vp), Mr_kNm=round(Mr))

FORMULA_CB = ('tan(alpha)=(h-2y)/lu; y=40 cover+11.3 stirrup+11.3 tie+db/2+50.8 (half of 4in row spacing); '
              'Vr=2*phi_s*As*fy*sin(alpha), phi_s=0.85, fy=400; Vn same with phi=1; Vp with 1.25fy, phi=1; Mr=Vr*lu/2 '
              '(A23.3-14 cl.21.5.8.2.2: all shear and moment by the diagonals)')

# ---------------- Table 1a: per header and bar band ----------------
OPEN = {'H1': [('CSW4', 3.33, 'S801 view A plan 3\'-4" scaled (30.0 pt); S802 2/S802 3.33 ft'), ('CSW5', 3.33, 'same')],
        'H2': [('CSW4', 5.24, 'S802 2/S802 5.24 ft; S801 plan 5.28 ft (47.5 pt)'), ('CSW5', 5.33, 'S802 3/S802 5.33 ft')],
        'H3': [('CSW1', 3.33, 'S801 plan 3\'-4" (30.0 pt); S802 1/S802 3.33 ft')]}
H_TYP = {('H1', 'CSW4'): 27.9, ('H1', 'CSW5'): 27.9, ('H2', 'CSW4'): 33.0, ('H2', 'CSW5'): 27.9, ('H3', 'CSW1'): 27.9}
rows = []
for h, bands in SCHED.items():
    for (a, b, n, bar) in bands:
        for wall, lu, lu_src in OPEN[h]:
            for label, hin, hsrc in (('WHM minimum', 22.0, 'S801 detail G: "22in DP. MIN."'),
                                     ('typical drawn', H_TYP[(h, wall)], 'S802 scaled, opening top to level above (L2-L31 typical storeys)')):
                r = dict(header=h, wall=wall, band=f'{a} TO {b}', bars_each_cage=f'{n}-{bar}', lu_ft=lu, lu_source=lu_src,
                         depth_case=label, h_in=hin, h_source=hsrc)
                r.update(diag(n, bar, hin, lu))
                t = WALL_T_IN[wall]
                fc = wall_fc(a if a in STOREYS else 'L31')
                r['b_in'] = t
                r['fc_used_MPa'] = fc
                r['note_1.0sqrtfc_b_h_kN'] = round(1.0 * math.sqrt(fc) * t * IN * hin * IN / 1000)
                r['h_le_2lu'] = 'OK' if hin <= 2 * lu * 12 else 'EXCEEDS'
                r['flag'] = 'F21: L32/L33 do not exist' if ('L32' in (a, b) or 'L33' in (a, b)) else ''
                r['formula'] = FORMULA_CB
                rows.append(r)
with open(os.path.join(OUT, '1-coupling-beams.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)

# ---------------- Table 1b: per storey, every header S802 draws ----------------
geo = list(csv.DictReader(open(_a.headers, encoding='utf-8')))
seen = set(); per = []
for g in geo:
    wall = g['wall']; lu = float(g['clear_span_ft'])
    h = 'H3' if wall == 'CSW1' else ('H1' if lu < 4 else 'H2')
    st = g['storey_from'].replace('/FDN', '')
    key = (wall, h, st)
    if key in seen: continue
    seen.add(key)
    per.append((wall, h, st, lu, float(g['header_depth_total_in']), float(g['opening_h_ft']), g['header_at_level'], 'S802 scaled'))
# storeys hidden by the S802 break lines (L11-L15, L24-L29): typical geometry carried, flagged
typ = {(w, h): (lu, d) for (w, h, st, lu, d, oh, lv, s) in per if st == 'L9'}
for (w, h), (lu, d) in typ.items():
    for st in ['L%d' % i for i in list(range(10, 16)) + list(range(23, 30))]:
        if (w, h, st) not in seen:
            nxt = STOREYS[sidx(st) + 1]
            per.append((w, h, st, lu, d, None, nxt, 'ASSUMED typical: storey hidden by break line on S802'))
            seen.add((w, h, st))
per.sort(key=lambda t: (t[0], t[1], sidx(t[2])))
out = []
for wall, h, st, lu, d, oh, lv, src in per:
    bd = band_for(h, st)
    r = dict(wall=wall, header=h, opening_storey=st, header_under_level=lv, lu_ft=lu, h_total_in=d,
             opening_h_ft=oh if oh is not None else '', geometry_source=src)
    if bd is None:
        r.update(band='MISSING: no H-schedule band covers this storey', bars_each_cage='MISSING')
        r['h_le_2lu (cl.21.5.8.2.1a)'] = 'OK' if d <= 2 * lu * 12 else f'EXCEEDS: h/lu={d/(lu*12):.2f}'
        r['flag'] = 'H3 schedule starts at L1; S802 1/S802 draws this header with diagonal bars'
    else:
        a, b, n, bar, flag = bd
        r.update(band=f'{a} TO {b}', bars_each_cage=f'{n}-{bar}')
        r.update(diag(n, bar, d, lu))
        r['h_le_2lu (cl.21.5.8.2.1a)'] = 'OK' if d <= 2 * lu * 12 else f'EXCEEDS: h/lu={d/(lu*12):.2f}'
        r['flag'] = flag
    out.append(r)
fields = []
for r in out:
    for k in r:
        if k not in fields: fields.append(k)
fields.append('formula')
for r in out: r['formula'] = FORMULA_CB
with open(os.path.join(OUT, '1b-coupling-beams-by-storey.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=fields); w.writeheader(); w.writerows(out)

# ---------------- Table 2: walls ----------------
def rho(bar, s_in, t_in, nfaces=2, extra=0):
    return (nfaces + extra) * AB[bar][0] / (s_in * IN * t_in * IN)
def parse(spec):  # '15M @ 10"'
    bar, s = spec.split('@'); return bar.strip(), float(s.replace('"', '').strip())
W = [  # wall, building, levels, t_in, t_src, fc, vert, horiz, extra_h_bars, length_ft, length_src, zones
 ('CSW1','B','P3-L8',24,'S801 A (L3)',60,'15M @ 10"','20M @ 6"',2,32.36,'S801 A scaled outer 291.2 pt; door 3\'-4" scaled at 1579.8-1609.8 pt','Z1A/Z1B corners, Z2A/Z2B each side of door full ht, Z4A P3-L8/Z4B P3-L9'),
 ('CSW1','B','L9-L20',24,'S801 A (L3)',50,'15M @ 10"','20M @ 7"',0,32.36,'as above','Z1, Z2 (Z2A L9 and up, Z2B L10 and up per callout B)'),
 ('CSW1','B','L21-ROOF',24,'S801 A (L3)',35,'15M @ 10"','15M @ 10"',0,32.36,'as above','Z1, Z2'),
 ('CSW2','B','P3-L8',16,'S801 A (L3)',60,'15M @ 12"','20M @ 4"',0,27.0,'S801 A scaled between inner faces of CSW4/CSW5 (243.0 pt)','none tagged on CSW2'),
 ('CSW2','B','L9-L20',16,'S801 A (L3)',50,'15M @ 15"','20M @ 8"',0,27.0,'as above','none tagged'),
 ('CSW2','B','L21-ROOF',16,'S801 A (L3)',35,'15M @ 15"','15M @ 12"',0,27.0,'as above; S801 prints "15M @ 12 HEF" with no inch mark','none tagged'),
 ('CSW3','B','P3-L8',24,'S801 A (L3)',60,'15M @ 10"','20M @ 6"',2,32.36,'S801 A scaled outer 291.2 pt; OPNG AT GF only','Z1, Z8A P3-L8/Z8B P3-L9, Z4A P3-L8/Z4B P3-L9'),
 ('CSW3','B','L9-L20',24,'S801 A (L3)',50,'15M @ 10"','20M @ 8"',0,32.36,'as above','Z1, Z3 (Z3A L9 to roof, Z3B L10 to roof per callout C)'),
 ('CSW3','B','L21-ROOF',24,'S801 A (L3)',35,'15M @ 10"','15M @ 8"',0,32.36,'as above','as above'),
 ('CSW4','B','P3-L8',32,'S801 A (L3)',60,'15M @ 8"','20M @ 4"',2,29.87,'S801 A scaled outer 268.8 pt; doors H2 5\'-3" (627.5-675.2 pt), H1 3\'-4" (756.1-786.1 pt): piers 5.4 / 9.0 / 6.8 ft incl. flanges','Z1A/Z1B, Z2A/Z2B, Z3A/Z3B full ht, Z7A P3-L8/Z7B P3-L9 SW corner'),
 ('CSW4','B','L9-L20',32,'S801 A (L3)',50,'15M @ 8"','20M @ 4"',0,29.87,'as above','Z1, Z2, Z3'),
 ('CSW4','B','L21-ROOF',32,'S801 A (L3)',35,'15M @ 8"','15M @ 6"',0,29.87,'as above','Z1, Z2, Z3'),
 ('CSW5','B','P3-L8',32,'S801 A (L3)',60,'15M @ 8"','20M @ 4"',2,29.87,'as CSW4 (same opening positions on plan)','Z1A/Z1B, Z2A/Z2B, Z3A/Z3B full ht'),
 ('CSW5','B','L9-L20',32,'S801 A (L3)',50,'15M @ 8"','20M @ 4"',0,29.87,'as above','Z1, Z2, Z3'),
 ('CSW5','B','L21-ROOF',32,'S801 A (L3)',35,'15M @ 8"','15M @ 6"',0,29.87,'as above','Z1, Z2, Z3'),
]
# Row 1982 JM-V9-3A (look L1B-01): the wall locations are by WHM's north arrow on S201 (to the sheet's left; S801's key
# plans are drawn the same way round): v9 called the sheet's top north
for wl, ln, src in (('CSW6',16.91,'S801 D scaled 152.1 pt (north wall, between CSW8 and CSW7); OPNG AT L2 TO L9 (not dimensioned)'),
                    ('CSW7',10.35,'S801 D scaled 93.2 pt (west wall, outer)'),
                    ('CSW8',9.35,'S801 D scaled 84.2 pt (east wall)'),
                    ('CSW9',17.91,'S801 D scaled 161.1 pt (south wall)'),
                    ('CSW10',19.5,'S801 E scaled 175.5 pt (three fills 1049.0-1224.5 pt)'),
                    ('CSW11',10.32,'S801 F scaled 92.9 pt'),
                    ('CSW12',18.17,'S801 F scaled 163.6 pt'),
                    ('CSW13',10.32,'S801 F scaled 92.9 pt')):
    tsrc = 'S801 key plan (1\'-0")' if wl != 'CSW8' else 'MISSING on S801 (F10); drawn 9.0 pt = 1\'-0" at 1/8" scale; ASSUMED 1\'-0"'
    hor_lo = '20M @ 4"' if wl == 'CSW12' else '15M @ 4"'
    zl = 'Z5A/Z5B full ht' + ('; Z6A/Z6B P3-L1' if wl == 'CSW8' else ('; Z6A/Z6B P3-L4' if wl in ('CSW9', 'CSW10') else ''))
    W.append((wl,'A','P3-L4',12,tsrc,50,'15M @ 12"',hor_lo,0,ln,src,zl))
    W.append((wl,'A','L5-ROOF',12,tsrc,35,'15M @ 12"','15M @ 8"',0,ln,src,zl))
wr = []
for (wl,bl,lv,t,tsrc,fc,vs,hs,ex,ln,lsrc,zones) in W:
    vb, vsp = parse(vs); hb, hsp = parse(hs)
    rv = rho(vb, vsp, t); rh = rho(hb, hsp, t, extra=ex)
    bw = t * IN
    Avs = (2 + ex) * AB[hb][0] / (hsp * IN)            # mm2 per mm of height
    vs_hinge = PHI_S * Avs * FY * 1.0                  # N/mm of dv: theta 45 deg (cl.21.5.9.5.2c, low axial)
    vs_out = PHI_S * Avs * FY / math.tan(math.radians(35))
    vc_out = PHI_C * 1.0 * 0.18 * min(math.sqrt(fc), 8) * bw
    vmax_h = 0.10 * PHI_C * fc * bw; vmax_o = 0.20 * PHI_C * fc * bw
    wr.append(dict(wall=wl, building=bl, levels=lv, t_in=t, t_source=tsrc, fc_MPa=fc, fc_source='S801 WALL REINF. note',
        vertical_EF=vs, horizontal_EF=hs + (f' + {ex}-{hb} @ {hsp:g}" centred' if ex else ''),
        rho_v_pct=round(100*rv,3), rho_h_pct=round(100*rh,3),
        rho_min_cl_21_5_5_1=('OK' if min(rv, rh) >= 0.0025 else 'BELOW 0.25%: ' + ('vertical ' if rv < 0.0025 else '') + ('horizontal' if rh < 0.0025 else '')),
        length_ft=ln, length_source=lsrc, zones=zones,
        Vr_hinge_kN_per_m_dv=round(min(vs_hinge, vmax_h)), Vrmax_hinge_kN_per_m_dv=round(vmax_h),
        Vr_outside_kN_per_m_dv=round(min(vc_out + vs_out, vmax_o)), Vrmax_outside_kN_per_m_dv=round(vmax_o),
        formula=('rho=n*Ab/(s*t); per mm of dv (dv>=0.8*lw, cl.21.5.9.2): hinge Vr=phi_s*(Av/s)*fy*cot45, beta=0 '
                 '(cl.21.5.9.5.2b), cap 0.1*phi_c*fc*bw (a); outside Vr=phi_c*0.18*sqrt(fc)*bw + phi_s*(Av/s)*fy*cot35 '
                 '(cl.21.5.9.4), cap 0.2*phi_c*fc*bw; kN per m of dv')))
with open(os.path.join(OUT, '2-walls.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=list(wr[0])); w.writeheader(); w.writerows(wr)

ZONES = [('Z1A','P3-L8','9-35M','15M @ 6"'),('Z1A','L9-L20','9-25M','10M @ 6"'),('Z1A','L21-ROOF','9-20M','10M @ 5"'),
         ('Z1B','P3-L9','9-35M','15M @ 6"'),('Z1B','L10-L21','9-25M','10M @ 6"'),('Z1B','L22-ROOF','9-20M','10M @ 5"'),
         ('Z2A','P3-L8','6-30M','10M @ 4"'),('Z2A','L9-L20','6-25M','10M @ 6"'),('Z2A','L21-ROOF','6-20M','10M @ 5"'),
         ('Z2B','P3-L9','6-30M','10M @ 4"'),('Z2B','L10-L21','6-25M','10M @ 6"'),('Z2B','L22-ROOF','6-20M','10M @ 5"'),
         ('Z3A','P3-L8','8-35M','15M @ 6"'),('Z3A','L9-L20','8-30M','10M @ 6"'),('Z3A','L21-ROOF','8-25M','10M @ 6"'),
         ('Z3B','P3-L9','8-35M','15M @ 6"'),('Z3B','L10-L21','8-30M','10M @ 6"'),('Z3B','L22-ROOF','8-25M','10M @ 6"'),
         ('Z4A','P3-L8','9-35M','15M @ 6"'),('Z4B','P3-L9','9-35M','15M @ 6"'),
         ('Z5A','P3-L4','4-30M','10M @ 4"'),('Z5A','L5-ROOF','4-20M','10M @ 4"'),
         ('Z5B','P3-L5','4-30M','10M @ 4"'),('Z5B','L6-ROOF','4-20M','10M @ 4"'),
         ('Z6A','AS PER PLAN','4-20M','10M @ 4"'),('Z6B','AS PER PLAN','4-20M','10M @ 4"'),
         ('Z7A','P3-L8','12-35M','15M @ 6"'),('Z7B','P3-L9','12-35M','15M @ 6"'),
         ('Z8A','P3-L8','9-25M','10M @ 4"'),('Z8B','P3-L9','9-25M','10M @ 4"')]
with open(os.path.join(OUT, '2b-wall-zones.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f); w.writerow(['zone','levels','vertical','ties','As_vertical_mm2','plan_size','source','formula'])
    for z, lv, v, tie in ZONES:
        n, bar = v.split('-')
        w.writerow([z, lv, v, tie, int(n)*AB[bar][0], 'TO SUIT (MISSING, F10); bars at 6in TYP', 'S801 Zone Schedule', 'As=n*Ab'])

# ---------------- Table 3: core pad footing ----------------
h = 80 * IN; fc = 35.0; a1 = 0.85 - 0.0015 * fc; b1 = 0.97 - 0.0025 * fc
def mr(As_per_m, d):
    a = PHI_S * As_per_m * FY / (a1 * PHI_C * fc * 1000)
    return PHI_S * As_per_m * FY * (d - a / 2) / 1e6, a
s6, s12 = 6 * IN, 12 * IN
As6 = 1000 * AB['35M'][0] / s6; As12 = 1000 * AB['35M'][0] / s12
d_in = h - 75 - 35.7 * 2   # inner direction, centroid of 1st+2nd layer: 75 cover + 4 bar layers of 35M -> centroid 2 bars up
fr = []
for case, As in (('bottom, 1st 35M@6 + 2nd 35M@6 (S201 callout at grid 9 B-C)', As6 + As6),
                 ('bottom, 1st 35M@6 + 2nd 35M@12 (S201 callout 9-10 G-H; S203 both)', As6 + As12),
                 ('bottom, 1st 35M@6 + 2nd 35M@12 + S202 extra 35M14.9@12 STAG (2nd layer) at the core', As6 + As12 + As12),
                 ('top, same as bottom (TEW + BEW) - lower of the two readings', As6 + As12)):
    m, a = mr(As, d_in)
    fr.append(dict(case=case, As_mm2_per_m=round(As), d_mm=round(d_in), a_mm=round(a, 1), Mr_kNm_per_m=round(m),
                   formula='a=phi_s*As*fy/(alpha1*phi_c*fc*b), alpha1=0.85-0.0015fc=%.4f; Mr=phi_s*As*fy*(d-a/2); d=h-75-2*35.7 (inner direction, 2-layer centroid); h=80in=2032mm; fc=35 (S111 SHEAR WALL FOOTINGS); fy=400' % a1))
dv = max(0.9 * d_in, 0.72 * h)
beta = 230 / (1000 + dv)
vc = PHI_C * 1.0 * beta * math.sqrt(fc) * 1000 * dv / 1000   # kN per m width
fr.append(dict(case='one-way shear, no stirrups (15M hairpins are support bars)', As_mm2_per_m='', d_mm=round(d_in), a_mm='',
               Mr_kNm_per_m='Vc=%d kN/m (dv=%d mm, beta=%.4f)' % (round(vc), round(dv), beta),
               formula='Vc=phi_c*lambda*beta*sqrt(fc)*bw*dv, beta=230/(1000+dv) (cl.11.3.6.3b, ag 40mm>=20), dv=max(0.9d,0.72h); lambda=1'))
with open(os.path.join(OUT, '3-core-pad-footing-calcs.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=list(fr[0])); w.writeheader(); w.writerows(fr)

print('rows: 1=%d 1b=%d 2=%d zones=%d 3=%d' % (len(rows), len(out), len(wr), len(ZONES), len(fr)))
print('pad: a1=%.4f d=%.0f dv=%.0f beta=%.4f Vc=%.0f' % (a1, d_in, dv, beta, vc))
for r in fr: print(r['case'], r['As_mm2_per_m'], r['Mr_kNm_per_m'])
