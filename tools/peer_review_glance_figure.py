#!/usr/bin/env python3
"""The 01819 package's design-at-a-glance.png (Appendix D, Figure D1): concrete by storey, wall and column density,
tower column axial use. Moved from a session scratchpad into the repo (row 1982 JM-V9-PREP, 13 Nov 2026): its footer
was typed as "run 17"; the run is now an input.

USAGE
    python -I peer_review_glance_figure.py <design-at-a-glance.csv> <gravity-tower-columns.csv> <out.png>
        (--run "ETABS run 31" | --numbers <v9-numbers.json>)
    --numbers takes the run's date from key G:run.etabs_date (the date the ETABS run logged), so the footer can never
    name a run the numbers file does not.
CHECKS: the storeys plotted and the gravity levels matched are printed (X of Y). DOES NOT: compare the bars to the csv
    totals (read the figure).
"""
import argparse
import json
import csv, sys, statistics as st, collections
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

ap = argparse.ArgumentParser()
ap.add_argument("glance"); ap.add_argument("grav"); ap.add_argument("out")
g = ap.add_mutually_exclusive_group(required=True)
g.add_argument("--run", help="the run the quantities come from, as the footer says it, e.g. 'ETABS run 31'")
g.add_argument("--numbers", help="v9-numbers.json: the footer names the ETABS run of G:run.etabs_date")
a = ap.parse_args()
glance, grav, out = a.glance, a.grav, a.out
if a.numbers:
    v = json.load(open(a.numbers, encoding="utf-8"))["keys"]["G:run.etabs_date"]["value"]
    if not v:
        sys.exit("G:run.etabs_date has no value in " + a.numbers)
    RUN = f"the ETABS run of {v}"
else:
    RUN = a.run
rows = [r for r in csv.DictReader(open(glance, encoding='utf-8')) if r['storey'] != 'TOTAL']
names = {'P1/GF': 'GF', 'ROOF AMENITY': 'Roof amenity', 'ROOF': 'Roof', 'MECHANICAL SERVICE': 'Mech.',
         'ELEVATOR PENTHOUSE': 'Elev. PH'}
lab = [names.get(r['storey'], r['storey']) for r in rows]
y = list(range(len(rows)))
f = lambda c: [float(r[c]) for r in rows]

g = [r for r in csv.DictReader(open(grav, encoding='utf-8'))
     if not r['column'].startswith('TC25') and r['utilisation_status'] == 'numeric']
by = collections.defaultdict(list)
for r in g:
    by[{'RA': 'ROOF AMENITY'}.get(r['level'], r['level'])].append(float(r['utilisation']))

BG = '#fbfaf7'
fig, ax = plt.subplots(1, 3, sharey=True, figsize=(12, 8.2), dpi=150,
                       gridspec_kw={'width_ratios': [34, 26, 26]}, facecolor=BG)
for a in ax:
    a.set_facecolor(BG)
    for s in ('top', 'right'):
        a.spines[s].set_visible(False)
    a.grid(axis='x', color='#dddddd', lw=0.6); a.set_axisbelow(True)

sl, wa, co = f('slab_m3'), f('wall_m3'), f('column_m3')
ax[0].barh(y, sl, color='#2f78d6', edgecolor='white', lw=0.4, label='Slabs')
ax[0].barh(y, wa, left=sl, color='#ea6a3a', edgecolor='white', lw=0.4, label='Walls')
ax[0].barh(y, co, left=[a + b for a, b in zip(sl, wa)], color='#1aab7a', edgecolor='white', lw=0.4, label='Columns')
ax[0].set_yticks(y); ax[0].set_yticklabels(lab, fontsize=7.5)
ax[0].set_xlabel('Concrete per storey (m³)'); ax[0].set_title('Concrete by storey', loc='left', fontsize=11)
ax[0].legend(frameon=False, loc='upper right', fontsize=8)

ax[1].plot(f('wall_pct_floor'), y, '-o', color='#ea6a3a', ms=3.5, lw=1.2, label='Walls')
ax[1].plot(f('column_pct_floor'), y, '-s', color='#1aab7a', ms=3.5, lw=1.2, label='Columns')
ax[1].set_xlim(0, 14); ax[1].set_xticks(range(0, 15, 2))
ax[1].set_xlabel("Plan area as % of\nthe storey's slab area"); ax[1].set_title('Wall and column density', loc='left', fontsize=11)
ax[1].legend(frameon=False, loc='lower right', fontsize=8)

for i, r in enumerate(rows):
    u = by.get(r['storey'])
    if u:
        ax[2].plot([min(u), max(u)], [i, i], color='#2f78d6', lw=3.2, alpha=0.45, solid_capstyle='butt')
        ax[2].plot([st.median(u)], [i], 'o', color='#2f78d6', ms=4)
ax[2].axvline(1.0, ls='--', color='#444444', lw=1)
ax[2].text(1.02, len(rows) - 1.5, 'Pf = Pr,max', fontsize=8, color='#444444')
ax[2].text(0.02, 1, 'GF, P2, P3: not given\n(transferred load not included)', fontsize=7.5, color='#555555')
ax[2].set_xlim(0, 1.3); ax[2].set_xticks([0, 0.2, 0.4, 0.6, 0.8, 1.0, 1.2])
ax[2].set_xlabel('Pf / Pr,max, unreduced live load\n(min - median - max)')
ax[2].set_title('Tower columns TC1-TC21: axial use', loc='left', fontsize=11)
for a in ax[1:]:
    a.tick_params(axis='y', labelleft=False)

fig.suptitle('2480 Renfrew (01819-01): WHM structural design as drawn, at a glance', x=0.01, ha='left', fontsize=13)
fig.text(0.01, 0.01, 'Quantities: our ETABS model of the IFT set, ' + RUN + ' (superstructure; footings and beams not included). '
         'Axial use: KOR hand gravity check (preliminary, tributary areas).', fontsize=7.5, color='#666666')
fig.tight_layout(rect=(0, 0.03, 1, 0.96))
fig.savefig(out, facecolor=BG)
print('storeys', len(rows), 'gravity levels plotted', sum(1 for r in rows if r['storey'] in by))
