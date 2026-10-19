"""Second de-dup guard: selected tracker rows vs ALL 177 migration-268 CRM wins, beyond the site-key link."""
import csv, os, json, re, datetime as dt, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))
P = {p['pursuit_id']: p for p in csv.DictReader(open(os.path.join(base, 'winloss-records.csv'), encoding='utf-8-sig'))}
sel = [P[i] for i in json.load(open(os.path.join(base, 'crm_selected_ids.json')))]
crm = [c for c in json.load(open(os.path.join(base, 'crm.json'), encoding='utf-8')) if c.get('ExternalSource') == 'Deltek.CustomProposal']
STOP = {'vancouver', 'burnaby', 'surrey', 'richmond', 'coquitlam', 'street', 'avenue', 'road', 'fee', 'proposal', 'tower', 'building', 'development',
        'north', 'west', 'east', 'south', 'phase', 'project', 'the', 'and', 'kelowna', 'langley', 'victoria', 'residential', 'apartments', 'ave'}
def toks(s): return {t for t in re.findall(r'[a-z0-9]{3,}', (s or '').lower()) if t not in STOP}
def d(s):
    try: return dt.date.fromisoformat(s[:10])
    except Exception: return None
held = []
for p in sel:
    pd = d(p['first_date']); pf = float(p['fee_proposed']) if p['fee_proposed'] else None
    pt = toks(p['project'] + ' ' + p['client'])
    for c in crm:
        cd = d(c.get('OpenedAtUtc') or ''); cf = c.get('ProposedFee')
        title = (c.get('Notes') or '').split(' | BD:')[0]
        delta = abs((pd - cd).days) if pd and cd else 9999
        fee_eq = pf and cf and abs(pf - float(cf)) < 1
        tok = len(pt & toks(title)) >= 1
        won = p['outcome'].startswith('Won')
        # Won rows: shared distinctive title word within 120 days, or equal fee within 30 days (double-counting a win is worse
        # than leaving one out). Lost/not-won rows: only a shared title word within 120 days (same project, conflicting outcome).
        if (tok and delta <= 120) or (won and fee_eq and delta <= 30):
            held.append((p['pursuit_id'], p['outcome'], p['project'][:50], p['first_date'], pf, c['Id'], title[:50], (c.get('OpenedAtUtc') or '')[:10], cf, 'fee' if fee_eq else 'title'))
            break
print('selected', len(sel), 'held out as possible duplicates of a 268 win:', len(held), collections.Counter(h[1] for h in held))
for h in held: print('  ', h)
json.dump([h[0] for h in held], open(os.path.join(base, 'crm_held_ids.json'), 'w'))
keys = collections.Counter(p['sample_path'].split('Fee Proposal Tracking.xlsx!')[1] for p in sel)
print('true duplicate keys:', [k for k, v in keys.items() if v > 1])
