"""The open proposals for Ian's approval: this year's fees sent and not yet decided, ready to enter the CRM as Submitted.

Step 1 (migration 368, gen_crm_import.py) imported DECIDED tracker pursuits only and left Open / Pending out on purpose, so
the CRM has no Submitted pursuits and the roll-ups' 14-day follow-up and 45-day chase have nothing to run on. This reads the
same reconciled record (winloss-records.csv) and proposes, per open proposal, ONE action for Ian to approve:
  - "update #<id>"  : a live CRM pursuit for the same client already exists -> move it to Submitted with this fee and date;
  - "new"           : no live pursuit -> a new Submitted pursuit with the owner from the lead's initials.
Nothing is written to the database. Output: a review CSV (one row per proposal, biggest fee first).

Selection: new commissions, outcome Open or Pending, last dated within 180 days of --today. Folder and tracker records of the
same proposal are merged when they share a distinctive client word and are within 21 days or carry fees within 10%.
Owners: the lead engineer's first initials (INITIALS below, from opportunities.BdStaff). Merging and matching use the CLIENT name
only (address and project words matched unrelated firms: "Wilson" Ave to Kennedy Wilson); a same-owner match wins.
Usage: python -I open_proposals.py <winloss-records.csv> <live-pursuits.json> <out.csv> [--today yyyy-mm-dd]"""
import csv, json, re, sys
from datetime import date, timedelta

records, live_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
today = date.fromisoformat(sys.argv[sys.argv.index('--today') + 1]) if '--today' in sys.argv else date.today()
INITIALS = {'JM': 'jmarkulin@korstructural.com', 'JD': 'jdesroches@korstructural.com', 'CM': 'cmurtagh@korstructural.com',
            'IS': 'ishabana@korstructural.com', 'OAP': 'omara@korstructural.com', 'RB': 'rbeirne@korstructural.com',
            'JS': 'jstuart@korstructural.com', 'KW': 'kevinw@korstructural.com', 'JB': 'jbryson@korstructural.com',
            'JZ': 'jzickmantel@korstructural.com'}
STOP = {'group', 'developments', 'development', 'properties', 'property', 'homes', 'construction', 'architecture', 'architects',
        'ltd', 'inc', 'corp', 'company', 'projects', 'project', 'management', 'asset', 'holdings', 'street', 'avenue', 'road',
        'vancouver', 'burnaby', 'surrey', 'victoria', 'kelowna', 'calgary', 'richmond', 'coquitlam', 'langley', 'tower', 'towers',
        'rental', 'apartment', 'storey', 'parcel', 'phase', 'consultations', 'communities', 'campus', 'replacement', 'north',
        'south', 'east', 'west', 'with', 'from', 'the', 'and', 'due', 'sept', 'aug', 'july', 'june', 'oct', 'consulting', 'america',
        'pacific', 'studio', 'city', 'village', 'design', 'hotel', 'hotels', 'park', 'sherwood', 'new', 'ltd', 'inc', 'llc', 'corp',
        'investments', 'ventures', 'builders', 'real', 'estate', 'recreation', 'facility', 'affordable', 'housing', 'canada', 'bay'}


def toks(*parts):
    words = set()
    for p in parts:
        words |= {w for w in re.findall(r'[a-z0-9]{3,}', (p or '').lower()) if w not in STOP}
    return words


def d(s):
    try: return date.fromisoformat((s or '')[:10])
    except ValueError: return None


def fee(s):
    try: return float(s) if s else None
    except ValueError: return None


cutoff = today - timedelta(days=180)
cands = []
for r in csv.DictReader(open(records, encoding='utf-8-sig')):
    if r['pursuit_type'] != 'new commission' or r['outcome'] not in ('Open', 'Pending'): continue
    when = d(r['last_date']) or d(r['first_date'])
    if when is None or when < cutoff: continue
    lead = (r['lead_engineer'] or '').split()
    cands.append({'ids': [r['pursuit_id']], 'outcome': r['outcome'], 'sent': when, 'fee': fee(r['fee_proposed']),
                  'currency': r['currency'] or 'CAD', 'client': r['client'], 'project': r['project'],
                  'owner': INITIALS.get(lead[0]) if lead else None, 'lead': r['lead_engineer'],
                  'tracker': 'Fee Proposal Tracking.xlsx!' in (r['sample_path'] or ''), 'toks': toks(r['client'])})

# Merge folder and tracker records of the same proposal.
merged = []
for c in sorted(cands, key=lambda c: (not c['tracker'], c['sent'])):
    twin = next((m for m in merged if m['toks'] & c['toks'] and (abs((m['sent'] - c['sent']).days) <= 21
                 or (m['fee'] and c['fee'] and abs(m['fee'] - c['fee']) <= 0.1 * max(m['fee'], c['fee'])))), None)
    if twin is None:
        merged.append(c); continue
    twin['ids'] += c['ids']; twin['toks'] |= c['toks']
    twin['fee'] = twin['fee'] or c['fee']; twin['owner'] = twin['owner'] or c['owner']; twin['lead'] = twin['lead'] or c['lead']
    if len(c['project']) > len(twin['project']) and not twin['tracker']: twin['project'] = c['project']

live = json.load(open(live_path, encoding='utf-8-sig'))
live = live if isinstance(live, list) else [live]
for p in live: p['toks'] = toks(p.get('Buyer'))

rows = []
for m in merged:
    hits = [p for p in live if p['toks'] & m['toks']]
    hits.sort(key=lambda p: (p.get('Owner') != m['owner'], -len(p['toks'] & m['toks'])))
    hit = hits[0] if hits else None
    days = (today - m['sent']).days
    ask = 'chase the decision (45+ days)' if days >= 45 else 'follow up the submission (14+ days)' if days >= 14 \
        else f"follow-up due {(m['sent'] + timedelta(days=14)).isoformat()}"
    rows.append({'action': f"update #{hit['Id']}" if hit else 'new', 'live_pursuit': f"#{hit['Id']} {hit.get('Buyer') or ''} / {(hit.get('Projects') or '')[:60]}" if hit else '',
                 'owner': m['owner'] or f"? ({m['lead']})", 'client': m['client'], 'project': m['project'][:90], 'sent': m['sent'].isoformat(),
                 'days_since_sent': days, 'fee': f"{m['fee']:.0f}" if m['fee'] else '', 'currency': m['currency'], 'status_on_M': m['outcome'],
                 'roll_up_ask': ask, 'records': ' '.join(m['ids']), 'other_matches': ' '.join(f"#{p['Id']}" for p in hits[1:4])})

rows.sort(key=lambda r: -(float(r['fee']) if r['fee'] else 0))
with open(out_path, 'w', newline='', encoding='utf-8-sig') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
known = [float(r['fee']) for r in rows if r['fee']]
print(f"{len(cands)} open/pending records since {cutoff} -> {len(rows)} proposals after merging folder and tracker twins")
print(f"  update a live pursuit: {sum(r['action'] != 'new' for r in rows)}; new pursuit: {sum(r['action'] == 'new' for r in rows)}; owner unknown: {sum(r['owner'].startswith('?') for r in rows)}")
print(f"  with a fee: {len(known)} of {len(rows)}, total ${sum(known):,.0f}; 45+ days since sent: {sum(r['days_since_sent'] >= 45 for r in rows)}")
