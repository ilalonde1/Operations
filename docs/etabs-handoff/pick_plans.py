"""Which of the exported views is a structural plan, decided from the export's own reply.

The bridge exported every EngineeringPlan in the model — 140 views — and only some of them are
the plan the generator wants. The rest are key plans, reinforcing plans and the uncropped
MODEL SETTING working views. Version 1.0.33 filters at the source with "viewcontains"; until it
is deployed the same filter is applied here, from the view->file mapping the reply carries, so
nothing is decided by guessing at a filename.
"""
import json, io, sys, shutil, os, collections

reply_path, src_dir, dest_dir = sys.argv[1], sys.argv[2], sys.argv[3]

raw = io.open(reply_path, encoding='utf-8-sig').read()
d = json.loads(raw[raw.index('{'):raw.rindex('}') + 1])
exported = d['result']['exported']

# A view is NOT a structural plan when its name says it is something else. Named rather than
# pattern-guessed, so a new kind of view shows up as unclassified instead of silently included.
DROP = ['KEY PLAN', 'CORE WALL', 'REINFORC', 'REBAR', 'RBR', 'MODEL SETTING',
        'MODEL SEETING', 'ANALYT', 'SHORING', 'DEMO', 'BASE PLAN',
        # A DESIGN LOAD PLAN draws load zones over the floor, not the floor. One of these
        # reached the model on 2026-08-26 and its zone boundary was cut out of B-LEVEL 1's
        # mat as a 10,245 sq ft opening -- 93 per cent of an 11,026 sq ft plate, the exact
        # shape of the fault the engineer rejected on 25 August.
        'LOAD']

keep, dropped = [], collections.Counter()
for e in exported:
    upper = e['view'].upper()
    hit = next((k for k in DROP if k in upper), None)
    if hit:
        dropped[hit] += 1
    else:
        keep.append(e)

os.makedirs(dest_dir, exist_ok=True)
for f in os.listdir(dest_dir):
    os.remove(os.path.join(dest_dir, f))

# One file per level. Where a level has several structural plans the export already wrote them
# side by side; keep them all, because 31168 genuinely draws a level in two halves (BLDG C, and
# WEST BLDG A & B) and dropping either loses half a storey.
copied = 0
for e in keep:
    src = os.path.join(src_dir, e['file'])
    if os.path.exists(src):
        shutil.copy2(src, os.path.join(dest_dir, e['file']))
        copied += 1

levels = sorted({e['level'] for e in keep})
print(f'exported {len(exported)} view(s)')
print(f'  dropped {sum(dropped.values())}: ' + ', '.join(f'{k}={v}' for k, v in dropped.most_common()))
print(f'  kept    {len(keep)} view(s) over {len(levels)} level(s); copied {copied} file(s)')
missing = [e['file'] for e in keep if not os.path.exists(os.path.join(src_dir, e['file']))]
if missing:
    print(f'  MISSING from the export folder: {len(missing)}')
    for m in missing[:5]:
        print('     ', m)
