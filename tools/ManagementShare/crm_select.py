"""Select the tracker pursuits for the step-1 CRM import (dry run: counts only, nothing written)."""
import csv, os, collections, re, json
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))
P = list(csv.DictReader(open(os.path.join(base, 'winloss-records.csv'), encoding='utf-8-sig')))
trk = [p for p in P if p['tracker_status']]
print('tracker pursuits', len(trk), collections.Counter(p['outcome'] for p in trk))
DECIDED = {'Won': (6, 1), 'Won (Deltek)': (6, 1), 'Lost': (7, 2), 'Not won (no Deltek job)': (7, 2)}
sel, skip = [], collections.Counter()
for p in trk:
    if p['outcome'] not in DECIDED: skip['not decided: ' + p['outcome']] += 1; continue
    if p['crm_id']: skip['linked to a migration-268 win (left untouched)'] += 1; continue
    m = re.search(r'!(CAD Projects|USA Projects)!row(\d+)$', p['sample_path'])
    if not m: skip['no tracker row key'] += 1; continue
    opened = p['first_date'] or p['accepted'] or p['due']
    if not opened: skip['no date at all'] += 1; continue
    sel.append(p)
print('selected', len(sel), collections.Counter(p['outcome'] for p in sel))
print('skipped', dict(skip))
keys = collections.Counter(re.search(r'!(.+)$', p['sample_path'].replace('Fee Proposal Tracking.xlsx!', '')).group(1) for p in sel)
print('duplicate keys', [k for k, v in keys.items() if v > 1])
print('blank client', sum(1 for p in sel if not p['client'].strip()), 'USD rows', sum(1 for p in sel if p['currency'] == 'USD'))
linked = [p for p in trk if p['crm_id']]
print('tracker pursuits linked to a 268 win:', len(linked), collections.Counter(p['outcome'] for p in linked))
json.dump([p['pursuit_id'] for p in sel], open(os.path.join(base, 'crm_selected_ids.json'), 'w'))
