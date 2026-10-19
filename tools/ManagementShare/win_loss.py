"""KOR win/loss record from the Management share (no share access; all inputs already local).

Inputs : inventory.csv (FS01 inventory), pulled/Fee Proposal Tracking.xlsx, bundle_proposals.jsonl (text of 7,503 proposal/contract
         docs, extracted ON FS01), dk_pr.json / dk_ld.json / dk_cl.json (Deltek projects, hours by LEFT(WBS1,5), clients; read-only),
         crm.json (opportunities.CrmEngagements; read-only).
Output : winloss-records.csv (one row per pursuit), winloss-ambiguous.csv, winloss-stats.json, printed summary.

Unit   : a pursuit = one proposal folder or loose proposal document group in Project Opportunities (Pending / Submitted to Client /
         Rejected / Word Documents for Awarded Projects), one Project Management job folder, or one tracker row; records that share a
         site key (street number + street word) and fall within 365 days of each other are merged, as are records sharing a job number.
Outcome: Won  = awarded doc, PM job folder, or tracker "Proposal Accepted".  Lost = Rejected folder/doc or tracker "Rejected :(".
         Both  -> "Conflict" (listed).  Submitted-only -> Deltek split: a real Deltek project (WBS1 starts with a digit, not 99, not
         9+letter) whose LEFT(WBS1,5) has labour hours and matches the site (or the job number) and was created from 60 days before to
         540 days after the proposal date -> "Won (Deltek)"; else "Not won (no Deltek job)" when older than 180 days, "Open" when younger.
Does NOT: read scanned PDFs (no OCR), resolve client aliases beyond token overlap, or prove that a Deltek project at the same address
         is the same commission (that is what the confidence column is for)."""
import csv, json, os, re, collections, datetime as dt, warnings, statistics
warnings.filterwarnings('ignore')
import openpyxl
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
D = lambda *p: os.path.join(base, *p)
TODAY = dt.date(2026, 10, 18)

# ---------- parsing helpers ----------
MONTHS = {m: i for i, m in enumerate(['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'], 1)}
INIT = {'JM', 'JB', 'JZ', 'JD', 'JS', 'RB', 'KW', 'CM', 'OA', 'OAP', 'JA', 'CSF', 'JDR', 'IS', 'SG', 'MB', 'AN', 'CF', 'GD', 'SZ'}
PM_INIT = {'Markulin, John': 'JM', 'Stuart, Jason': 'JS', 'DesRoches. Jim': 'JD', 'DesRoches, Jim': 'JD', 'Wurmlinger, Kevin': 'KW',
           'Beirne, Rory': 'RB', 'Zickmantel, John': 'JZ', 'Bryson, John': 'JB', 'Byrson, John': 'JB', 'Murtagh, Conor': 'CM',
           'Atkinson, Jeremy': 'JA', 'Alcazar, Omar': 'OA', 'Omar Alcazar': 'OA', 'Alcazar Pastrana, Omar': 'OA', 'Islam Shabana': 'IS',
           'Shabana, Islam': 'IS', 'Ghaffari, Sadi': 'SG'}
STREET_STOP = {'and', 'the', 'st', 'ave', 'rd', 'street', 'avenue', 'road', 'dr', 'blvd', 'hwy', 'way', 'w', 'e', 'n', 's', 'west', 'east',
               'north', 'south', 'unit', 'suite', 'level', 'floor', 'storey', 'phase', 'ph', 'lot', 'parcel', 'bldg', 'building', 'kor', 'bmz'}
CITIES = ['North Vancouver', 'West Vancouver', 'New Westminster', 'Port Coquitlam', 'Port Moody', 'Maple Ridge', 'Pitt Meadows', 'White Rock',
          'Campbell River', 'North Cowichan', 'West Kelowna', 'Prince George', 'San Diego', 'Los Angeles', 'Red Deer', 'Salt Spring',
          'Vancouver', 'Burnaby', 'Richmond', 'Coquitlam', 'Surrey', 'Langley', 'Delta', 'Tsawwassen', 'Ladner', 'Abbotsford', 'Chilliwack',
          'Mission', 'Kelowna', 'Penticton', 'Vernon', 'Kamloops', 'Victoria', 'Langford', 'Saanich', 'Sidney', 'Nanaimo', 'Parksville',
          'Courtenay', 'Comox', 'Duncan', 'Sooke', 'Squamish', 'Whistler', 'Revelstoke', 'Cranbrook', 'Ashcroft', 'Princeton', 'Calgary',
          'Edmonton', 'Leduc', 'Sacramento', 'Seattle', 'Texas', 'Dallas', 'Arizona', 'Ottawa', 'Oakville', 'Winnipeg', 'Markham', 'Hamilton',
          'UBC', 'Nanoose', 'Gibsons', 'Sechelt', 'Hope', 'Agassiz', 'Esquimalt', 'Colwood', 'Ucluelet', 'Tofino', 'Fort St John']
REGION = {}
for c in ['Vancouver', 'Burnaby', 'Richmond', 'North Vancouver', 'West Vancouver', 'New Westminster', 'Coquitlam', 'Port Coquitlam',
          'Port Moody', 'UBC']: REGION[c] = 'Metro Vancouver (core)'
for c in ['Surrey', 'Langley', 'Delta', 'Tsawwassen', 'Ladner', 'White Rock', 'Maple Ridge', 'Pitt Meadows', 'Abbotsford', 'Chilliwack',
          'Mission', 'Hope', 'Agassiz']: REGION[c] = 'South of Fraser / Fraser Valley'
for c in ['Kelowna', 'West Kelowna', 'Penticton', 'Vernon', 'Kamloops']: REGION[c] = 'Okanagan / Interior'
for c in ['Victoria', 'Langford', 'Saanich', 'Sidney', 'Nanaimo', 'Parksville', 'Courtenay', 'Comox', 'Duncan', 'Sooke', 'Campbell River',
          'North Cowichan', 'Salt Spring', 'Nanoose', 'Esquimalt', 'Colwood', 'Ucluelet', 'Tofino']: REGION[c] = 'Vancouver Island'
for c in ['Squamish', 'Whistler', 'Gibsons', 'Sechelt']: REGION[c] = 'Sea-to-Sky / Sunshine Coast'
for c in ['Calgary', 'Edmonton', 'Leduc', 'Red Deer']: REGION[c] = 'Alberta'
for c in ['San Diego', 'Los Angeles', 'Sacramento', 'Seattle', 'Texas', 'Dallas', 'Arizona']: REGION[c] = 'USA'
for c in ['Ottawa', 'Oakville', 'Winnipeg', 'Markham', 'Hamilton']: REGION[c] = 'Eastern Canada'
for c in ['Revelstoke', 'Cranbrook', 'Ashcroft', 'Princeton', 'Prince George', 'Fort St John']: REGION[c] = 'Other BC'
CITY_RX = re.compile(r'\b(' + '|'.join(re.escape(c) for c in sorted(CITIES, key=len, reverse=True)) + r')\b', re.I)
CITY_CANON = {c.lower(): c for c in CITIES}
ALIAS = {'bby': 'Burnaby', 'van': 'Vancouver', 'nvan': 'North Vancouver', 'north van': 'North Vancouver', 'poco': 'Port Coquitlam',
         'new west': 'New Westminster', 'wvan': 'West Vancouver', 'la': 'Los Angeles'}

def find_city(s):
    if not s: return ''
    ms = list(CITY_RX.finditer(s))          # the city follows the street, so the LAST match wins ("Victoria Avenue White Rock")
    if ms: return CITY_CANON[ms[-1].group(1).lower()]
    for k, v in ALIAS.items():
        if re.search(r'\b' + re.escape(k) + r'\b', s, re.I): return v
    return ''

def to_date(y, m, d):
    try: return dt.date(int(y), int(m), int(d))
    except ValueError: return None

def name_date(s):
    m = re.search(r'(19|20)(\d\d)[-_.](\d\d)[-_.](\d\d)', s)
    if m: return to_date(m.group(1) + m.group(2), m.group(3), m.group(4))
    m = re.search(r'(?<!\d)(0[1-9]|1[0-2])([0-3]\d)(\d\d)\s?(?:[A-Za-z]{1,4}(?:fps?|FP|ltrs?|LTRS?|rpts?|RPTS?)?|\()', s)
    if m:
        y = int(m.group(3)); y += 2000 if y < 50 else 1900
        return to_date(y, m.group(1), m.group(2))
    return None

def due_date(s, fallback_year):
    m = re.search(r'DUE\s*:?\s*([A-Za-z]{3,9})\.?\s*(\d{1,2})', s, re.I)
    if m and m.group(1)[:3].upper() in MONTHS and fallback_year:
        return to_date(fallback_year, MONTHS[m.group(1)[:3].upper()], m.group(2))
    m = re.search(r'Due\s*(20\d\d)-(\d\d)-(\d\d)', s, re.I)
    return to_date(*m.groups()) if m else None

def jobno(s):
    m = re.search(r'(?<![\d-])(\d{5})-(\d\d|##)(?!\d)', s)
    return m.group(1) if m else ''

def initials(s):
    toks = re.findall(r'(?<![A-Za-z])([A-Z]{2,3})(?![A-Za-z])', s.replace('-FP', ' ').replace('FP-', ' '))
    out = []
    for t in toks:
        if t in INIT and t not in out: out.append(t)
    # pre-2018 names glue the initials to the date: "050713jbfps2013", "012814JZFP2014", "062705bfp2005"
    for m in re.finditer(r'\d{6}\s?\(?([A-Za-z]{2,3})\)?(?=fps?|FP|ltrs?|LTRS?|rpts?|RPTS?|\d{4}|\.|$)', s):
        t = m.group(1).upper()
        if t in INIT and t not in out: out.append(t)
    return out

ADDON = re.compile(r'additional services|addendum|change order|\bCO#|extra services|revised drawings|re-?issue|core hole|archived drawings|'
                   r'retrieval|letter of assurance|schedule b|peer review|shop drawing|site visit|inspection over|scope change', re.I)

def site_key(s):
    s = re.sub(r'(?<![\d-])\d{5}-(?:\d\d|##)', ' ', s)            # job numbers
    s = re.sub(r'(19|20)\d\d[-_.]\d\d[-_.]\d\d', ' ', s)           # ISO dates
    for m in re.finditer(r'(?<![\d$.,])(\d{2,6})(?:\s*[-&/]\s*\d{1,6})?\s+([A-Za-z]{3,})', s):
        num, word = m.group(1), m.group(2).lower()
        if word in STREET_STOP or word in ('storey', 'storeys', 'units', 'unit', 'suites', 'sqft', 'sf', 'feet', 'avenue', 'street') : continue
        if len(num) == 4 and num.startswith(('19', '20')) and word in ('rfp', 'fee', 'proposal', 'revised', 'kor', 'bmz'): continue
        return f'{int(num)} {word[:6]}'
    return ''

def client_tokens(s):
    s = re.sub(r'(?<![\d-])\d{5}-(?:\d\d|##)', ' ', s)
    s = re.split(r'\s-\s|,|\s\d', s, maxsplit=1)[0]
    toks = [t.lower() for t in re.findall(r'[A-Za-z][A-Za-z\']+', s)]
    stop = {'ltd', 'inc', 'corp', 'group', 'the', 'and', 'development', 'developments', 'properties', 'property', 'construction', 'homes',
            'architecture', 'architects', 'architect', 'bc', 'co', 'limited', 'partnership', 'projects', 'project', 'company', 'holdings',
            'investment', 'investments', 'contracting', 'living', 'kor', 'bmz', 'proposal', 'fee', 'revised', 'peer', 'review', 'due'}
    return [t for t in toks if t not in stop and len(t) > 2][:3]

def client_label(s):
    s = re.sub(r'^[#\s]*(?:\d{5}-(?:\d\d|##)(?:\s*[&,]\s*\d{5}-\d\d)*)\s*', '', s)
    s = re.split(r'\s-\s|,|\s(?=\d)|\(', s, maxsplit=1)[0]
    return s.strip(' -&')[:60]

SECTOR_RULES = [('Hotel', r'hotel|sandman|inn\b|suites hotel'),
                ('Institutional / Recreation', r'school|community cent|rec(?:reation)? cent|arena|library|church|hospital|health|campus|university|college|daycare|child ?care|museum|pool|curling|gym|first nation|society'),
                ('Industrial', r'industrial|warehouse|distribution|plant\b|manufactur|storage'),
                ('Office / Commercial', r'office|commercial|retail|shopping|mall|restaurant|bank|dealership'),
                ('Renovation / Seismic / Assessment', r'seismic|renovat|upgrad|assessment|condition|repair|retrofit|investigation|feasib'),
                ('Small / Review / TI', r'core hole|tenant improvement|\bTI\b|peer review|shop drawing|signage|canopy|stair|deck|trellis|review'),
                ('Residential / Mixed-use', r'tower|storey|residential|condo|apartment|townhouse|town home|rental|mixed[- ]use|multi[- ]?family|housing|homes|lofts|residences|duplex|units')]
PM_SECTOR = {'01 Small': 'Small / Review / TI', '03 Residential': 'Residential / Mixed-use', '04 Commercial': 'Office / Commercial',
             '05 Office': 'Office / Commercial', '06 Hotel': 'Hotel', '07 Industrial': 'Industrial',
             '08 Institutional, Recreational & Church, etc': 'Institutional / Recreation', '09 Renovations & Seismic Upgrades': 'Renovation / Seismic / Assessment'}

def sector_of(text):
    for name, rx in SECTOR_RULES:
        if re.search(rx, text, re.I): return name
    return 'Unclassified'

MONEY = r'\$\s?((?:\d{1,3}(?:[,.]\d{3})+|\d{3,7})(?:[.,^]\d{2})?)'
def money(s):
    s = re.sub(r'[.,^](\d{2})$', '', s.strip()); return float(re.sub(r'[,.]', '', s))
def fee_of(text):
    t = text[:60000]
    for rx in [r'total\s*(?:base\s*)?(?:structural\s*)?(?:engineering\s*)?fees?\s*(?:\(?(?:cad|usd|excluding gst|plus gst)\)?\s*)?[:=]?\s*[|\s]*' + MONEY,
               r'total\s*(?:lump\s*sum\s*)?(?:fee|cost|price|amount)[^$\n]{0,40}' + MONEY,
               r'(?:proposed|lump sum|fixed|our)\s+fee[^$\n]{0,60}' + MONEY,
               r'total[^$\n]{0,30}' + MONEY]:
        vals = [money(m.group(1)) for m in re.finditer(rx, t, re.I)]
        vals = [v for v in vals if 500 <= v <= 6_000_000]
        if vals: return max(vals), 'doc-total'
    return None, ''

def gfa_of(text):
    m = re.search(r'(\d{1,3}(?:,\d{3})+|\d{4,7})\s*(?:sq\.?\s?ft|square feet|sf\b|ft2)', text[:30000], re.I)
    return float(m.group(1).replace(',', '')) if m else None

# ---------- 1. folder / document records ----------
inv = list(csv.DictReader(open(D('inventory.csv'), encoding='utf-8-sig')))
def P(r): return r['Path'].strip('\\').split('\\')
files = [(P(r), r) for r in inv if r['Dir'] != 'True']
dirs = [P(r) for r in inv if r['Dir'] == 'True']
mod_of_dir = collections.defaultdict(str)
for p, r in files:
    for k in range(3, len(p)):
        key = '\\'.join(p[:k]); mod_of_dir[key] = max(mod_of_dir[key], r['Modified'][:10])

PO = 'Project Opportunities'
PENDING_CATS = {'1. ON HOLD UNTIL FURTHER NOTICE', '3. PENDING PROPOSALS', 'JB Proposals', '4. Fraser Health Authority'}
PENDING_SKIP = {'2. Media Release Forms - Various', 'Client Agreements & Contracts for Review'}
SUBFOLDER_JUNK = re.compile(r'^(to be submitted|to submit|drawings?|addend|obsolete|old|provided|attachments|related|photos|blank|current plans|civil drawings|drawing set)', re.I)
records = []
def add(area, kind, name, path, extra=None):
    rec = {'area': area, 'kind': kind, 'name': name, 'path': path}
    rec.update(extra or {}); records.append(rec)

for p in dirs:
    if len(p) == 3 and p[0] == PO and p[1] in ('Submitted to Client', 'Rejected'):
        add(p[1], 'folder', p[2], '\\'.join(p))
    if len(p) == 3 and p[0] == PO and p[1] == 'Pending' and p[2] not in PENDING_CATS | PENDING_SKIP:
        add('Pending', 'folder', p[2], '\\'.join(p))
    if len(p) == 4 and p[0] == PO and p[1] == 'Pending' and p[2] in PENDING_CATS and not SUBFOLDER_JUNK.match(p[3]):
        add('Pending' + (' (on hold)' if p[2].startswith('1.') else ''), 'folder', p[3], '\\'.join(p))
    if len(p) == 3 and p[0] == 'Project Management' and p[1] in PM_SECTOR and re.match(r'\d{5}', p[2]):
        add('PM job folder', 'folder', p[2], '\\'.join(p), {'pm_sector': PM_SECTOR[p[1]]})
# loose proposal documents grouped by stem (docx + pdf of the same letter are one record)
loose = collections.OrderedDict()
for p, r in files:
    if p[0] != PO or r['Extension'].lower() not in ('.doc', '.docx', '.pdf'): continue
    if p[1] in ('Rejected', 'Submitted to Client', 'Pending') and len(p) == 3: area = p[1]
    elif p[1] == 'Word Documents for Awarded Projects': area = 'Awarded'
    else: continue
    st = re.sub(r'\s*(revised|rev\s?\d*|signed|executed|final)\s*$', '', re.sub(r'\.[^.]+$', '', p[-1]), flags=re.I).strip().lower()
    loose.setdefault((area, st), (p, r))
for (area, st), (p, r) in loose.items():
    add(area, 'document', re.sub(r'\.[^.]+$', '', p[-1]), '\\'.join(p))

for rec in records:
    n = rec['name']
    folder_mod = mod_of_dir.get(rec['path'], '') if rec['kind'] == 'folder' else ''
    rec['jobno'] = jobno(n)
    rec['date'] = name_date(n)
    yr = int(folder_mod[:4]) if folder_mod else (rec['date'].year if rec['date'] else None)
    rec['due'] = due_date(n, yr)
    rec['initials'] = initials(n)
    rec['site'] = site_key(n)
    rec['city'] = find_city(n)
    rec['client'] = client_label(n)
    rec['ctoks'] = client_tokens(n)
    rec['modified'] = folder_mod
    if not rec['date']:
        rec['date'] = rec['due'] or (dt.date.fromisoformat(folder_mod) if folder_mod else None)

# ---------- 2. proposal text: fee, gfa, sector hints, docs per record ----------
docs_by_dir = collections.defaultdict(list); doc_by_path = {}
for l in open(D('bundle_proposals.jsonl'), encoding='utf-8'):
    d = json.loads(l)
    if d['status'] != 'ok': continue
    p = d['path'].split('\\')
    doc_by_path[d['path']] = d
    if p[0] == PO and len(p) >= 4 and p[1] in ('Submitted to Client', 'Rejected', 'Pending'):
        docs_by_dir['\\'.join(p[:3])].append(d)
        if len(p) >= 5: docs_by_dir['\\'.join(p[:4])].append(d)
    if p[0] == 'Project Management' and len(p) >= 4:
        docs_by_dir['\\'.join(p[:3])].append(d)
for rec in records:
    texts = [doc_by_path[rec['path']]] if rec['kind'] == 'document' and rec['path'] in doc_by_path else docs_by_dir.get(rec['path'], [])
    fees = []
    for d in texts:
        f, how = fee_of(d['text'])
        dd = name_date(d['path'].split('\\')[-1])
        if f: fees.append((dd or dt.date(1900, 1, 1), f, d['path']))
        if not rec.get('gfa'): rec['gfa'] = gfa_of(d['text'])
        if not rec['initials']: rec['initials'] = initials(d['path'].split('\\')[-1])
        if not rec['site']: rec['site'] = site_key(d['path'].split('\\')[-1])
        if not rec['city']: rec['city'] = find_city(d['path'].split('\\')[-1]) or find_city(d['text'][:1500])
    if fees:
        fees.sort(); rec['fee_doc'] = fees[-1][1]; rec['fee_doc_path'] = fees[-1][2]   # latest dated proposal's total
        if rec['kind'] == 'folder' and rec['area'] == 'PM job folder':
            first = [x for x in fees if x[0].year > 1900]
            if first and not rec['date']: rec['date'] = first[0][0]
    rec['ndocs'] = len(texts)
    rec['sector_text'] = (rec['name'] + ' ' + ' '.join(d['text'][:2500] for d in texts[:2]))

# ---------- 3. tracker rows ----------
wb = openpyxl.load_workbook(D('pulled', 'Fee Proposal Tracking.xlsx'), data_only=True, read_only=True)
STAT = {'Submitted for Approval': 'Submitted', 'Proposal Accepted': 'Won', 'Rejected :(': 'Lost', 'Pending Draft Review': 'Pending',
        'On Hold': 'On hold'}
def asdate(v):
    if isinstance(v, dt.datetime): return v.date()
    if isinstance(v, dt.date): return v
    return None
def num(v):
    try: return float(v) if v not in (None, '', 'N/A') else None
    except (TypeError, ValueError): return None
trows = []
for sheet, cur in (('CAD Projects', 'CAD'), ('USA Projects', 'USD')):
    rows = list(wb[sheet].iter_rows(values_only=True))
    hdr = [str(h).replace('\n', ' ').strip() if h else '' for h in rows[0]]
    ix = {h: i for i, h in enumerate(hdr)}
    def g(r, *names):
        for n in names:
            if n in ix and ix[n] < len(r): return r[ix[n]]
        return None
    for i, r in enumerate(rows[1:], 2):
        proj = g(r, 'Project'); st = g(r, 'Status')
        if not proj or str(st).strip() not in STAT and not asdate(g(r, 'Due Date')): continue
        if str(proj).strip() in ('Month/Year', 'Monthly Sales'): continue
        addr = str(g(r, 'Address Line 1', 'Address') or '')
        rec = {'area': 'Tracker', 'kind': 'tracker', 'name': str(proj), 'path': f'Fee Proposal Tracking.xlsx!{sheet}!row{i}',
               'tracker_status': STAT.get(str(st).strip(), str(st or '').strip() or '(blank)'),
               'jobno': jobno(str(g(r, 'Project Number') or '')), 'date': asdate(g(r, 'Proposal Submitted', 'Date Proposal Sent')) or asdate(g(r, 'Due Date')),
               'rfp': asdate(g(r, 'RFP Received', 'Date RFP Received')), 'due': asdate(g(r, 'Due Date')),
               'accepted': asdate(g(r, 'Proposal Accepted', 'Date Proposal Accepted')),
               'initials': [x for x in [PM_INIT.get(str(g(r, 'Project Manager') or '').strip()), PM_INIT.get(str(g(r, 'Co-Signer', 'Second Signature') or '').strip())] if x],
               'pm': str(g(r, 'Project Manager') or '').strip(), 'site': site_key(addr) or site_key(str(proj)),
               'city': find_city(str(g(r, 'City') or '')) or find_city(str(proj)), 'client': str(g(r, 'Client / Owner') or '').strip()[:60],
               'architect': str(g(r, 'Architect') or '').strip()[:60], 'ctoks': client_tokens(str(g(r, 'Client / Owner') or '')),
               'gfa': num(g(r, 'Fee GFA (sq.ft.)', 'GFA (sq.ft.)')), 'fee_tracker': num(g(r, 'TotaL Proposed Fee ($cdn)', 'Proposed Fee  ($usd)')),
               'fee_awarded': num(g(r, 'Awarded Fee ($cdn)')), 'currency': cur,
               'reason': ' | '.join(str(x) for x in (g(r, 'Commnents', 'Comments'), g(r, 'Follow up comments')) if x),
               'ndocs': 0, 'sector_text': str(proj), 'initials_src': 'tracker'}
        trows.append(rec)
records += trows

# ---------- 4. merge into pursuits (union-find) ----------
parent = list(range(len(records)))
def find(i):
    while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
    return i
def union(a, b): parent[find(a)] = find(b)
by_site = collections.defaultdict(list); by_job = collections.defaultdict(list)
for i, r in enumerate(records):
    if r['site']: by_site[r['site']].append(i)
    if r['jobno'] and r['area'] in ('PM job folder', 'Awarded', 'Tracker'): by_job[r['jobno']].append(i)
for ids in by_job.values():
    for j in ids[1:]: union(ids[0], j)
for ids in by_site.values():
    for a in range(len(ids)):
        for b in range(a + 1, len(ids)):
            ra, rb = records[ids[a]], records[ids[b]]
            if ra['date'] and rb['date'] and abs((ra['date'] - rb['date']).days) <= 365:
                if ra['area'] == 'PM job folder' and rb['area'] == 'PM job folder': continue
                if not ra['ctoks'] or not rb['ctoks'] or set(ra['ctoks']) & set(rb['ctoks']) or 'Tracker' in (ra['area'], rb['area']):
                    union(ids[a], ids[b])
groups = collections.defaultdict(list)
for i in range(len(records)): groups[find(i)].append(records[i])

# ---------- 5. Deltek: real projects by LEFT(WBS1,5), aggregated before any join ----------
cl = {r['ClientID']: r.get('Name') for r in json.load(open(D('dk_cl.json'), encoding='utf-8'))}
hours = {r['P5']: r for r in json.load(open(D('dk_ld.json'), encoding='utf-8'))}
dk = {}
for r in json.load(open(D('dk_pr.json'), encoding='utf-8')):
    w = (r.get('WBS1') or '').strip()
    if not w or not w[0].isdigit() or w.startswith('99') or re.match(r'^9[A-Za-z]', w): continue
    # 10xxx = ChargeType P (promotional: proposal prep hours land there); 20xxx = the pursuit register (InPursuit/LOST/DNP);
    # H = overhead. None of these is a job, whatever hours they carry.
    if (r.get('ChargeType') or '') in ('P', 'H') or (r.get('Stage') or '') in ('InPursuit', 'LOST', 'DNP'): continue
    p5 = w[:5]
    a = dk.setdefault(p5, {'p5': p5, 'created': None, 'sites': set(), 'clients': set(), 'names': [], 'stages': set(), 'fee': 0.0})
    c = r.get('CreateDate')
    if c and (a['created'] is None or c < a['created']): a['created'] = c
    for s in (r.get('Address1') or '', r.get('LongName') or '', r.get('Name') or ''):
        k = site_key(s)
        if k: a['sites'].add(k)
    if r.get('ClientID') in cl and cl[r['ClientID']]: a['clients'].add(cl[r['ClientID']])
    a['names'].append(r.get('Name') or ''); a['stages'].add(r.get('Stage') or '')
    a['fee'] += float(r.get('Fee') or 0)
for p5, a in dk.items():
    h = hours.get(p5); a['hours'] = float(h['Hrs']) if h else 0.0; a['first_hr'] = h['FirstHr'] if h else None
dk_by_site = collections.defaultdict(list)
for a in dk.values():
    for s in a['sites']: dk_by_site[s].append(a)

def deltek_match(g, first):
    sites = {r['site'] for r in g if r['site']}; jobs = {r['jobno'] for r in g if r['jobno']}
    ctoks = set(t for r in g for t in r['ctoks'])
    cands = {}
    for j in jobs:
        if j in dk: cands[j] = dk[j]
    for s in sites:
        for a in dk_by_site.get(s, []): cands[a['p5']] = a
    scored = []
    for p5, a in cands.items():
        if a['hours'] <= 0: continue
        sc = 0; why = []
        if p5 in jobs: sc += 3; why.append('job number')
        if sites & a['sites']: sc += 2; why.append('site')
        ctext = ' '.join(a['clients']).lower() + ' ' + ' '.join(a['names']).lower()
        if ctoks and any(t in ctext for t in ctoks): sc += 1; why.append('client')
        if first and a['created']:
            delta = (dt.date.fromisoformat(a['created']) - first).days
            if -60 <= delta <= 540: sc += 1; why.append(f'created {delta:+d}d')
            elif delta < -60 and p5 not in jobs: sc -= 2; why.append(f'pre-existing {delta:+d}d')
            elif delta < -60: sc = min(sc, 2); why.append(f'existing job {delta:+d}d: phase outcome unknown')
        scored.append((sc, p5, why, a))
    scored.sort(key=lambda x: (-x[0], 0 if x[1] in jobs else 1, x[1]))   # deterministic: score, own job number, then WBS1
    if not scored: return None
    top = scored[0]; tie = len(scored) > 1 and scored[1][0] == top[0]
    conf = 'high' if top[0] >= 4 else 'medium' if top[0] == 3 else 'low'
    return {'p5': top[1], 'score': top[0], 'why': ', '.join(top[2]), 'conf': conf + (' (tie)' if tie else ''), 'created': top[3]['created'],
            'hours': round(top[3]['hours'], 1), 'clients': '; '.join(sorted(top[3]['clients']))[:80], 'ncands': len(scored)}

# ---------- 6. pursuits ----------
def fee_band(f):
    if f is None: return 'unknown'
    for lim, lab in ((10000, '< $10k'), (25000, '$10-25k'), (50000, '$25-50k'), (100000, '$50-100k'), (250000, '$100-250k')):
        if f < lim: return lab
    return '>= $250k'

pursuits = []
for gid, g in groups.items():
    areas = collections.Counter(r['area'] for r in g)
    tstat = [r['tracker_status'] for r in g if r['area'] == 'Tracker']
    won_ev = [a for a in areas if a in ('Awarded', 'PM job folder')] + (['tracker accepted'] if 'Won' in tstat else [])
    lost_ev = (['Rejected'] if 'Rejected' in areas else []) + (['tracker rejected'] if 'Lost' in tstat else [])
    pend = any(a.startswith('Pending') for a in areas) or any(s in ('Pending', 'On hold') for s in tstat)
    dates = sorted(r['date'] for r in g if r['date'])
    first = dates[0] if dates else None; last = dates[-1] if dates else None
    if won_ev and lost_ev: outcome = 'Conflict'
    elif won_ev: outcome = 'Won'
    elif lost_ev: outcome = 'Lost'
    elif pend and not ('Submitted to Client' in areas or 'Submitted' in tstat): outcome = 'Pending'
    else: outcome = 'Submitted'
    dm = deltek_match(g, first) if outcome in ('Submitted', 'Won', 'Pending', 'Conflict', 'Lost') else None
    basis = '+'.join(sorted(set(won_ev + lost_ev))) or ('pending folder/tracker' if outcome == 'Pending' else 'submitted only')
    final = outcome
    if outcome in ('Submitted', 'Pending'):
        if dm and dm['score'] >= 3: final = 'Won (Deltek)'
        elif outcome == 'Submitted':
            # Recall test (wl_recall.py): 249 of 266 tracker-accepted pursuits have a high Deltek job match, 6 of 478 known losses do.
            # So no Deltek job after 180 days reads as "not won"; younger is still open.
            final = 'Not won (no Deltek job)' if (not last or (TODAY - last).days > 180) else 'Open'
    fee_t = next((r['fee_tracker'] for r in g if r.get('fee_tracker')), None)
    fee_a = next((r['fee_awarded'] for r in g if r.get('fee_awarded')), None)
    fee_d = max((r['fee_doc'] for r in g if r.get('fee_doc')), default=None)
    fee = fee_t or fee_d
    pm_sector = next((r['pm_sector'] for r in g if r.get('pm_sector')), None)
    sector = pm_sector or sector_of(' '.join(r['sector_text'] for r in g)[:20000])
    city = next((r['city'] for r in g if r['area'] == 'Tracker' and r['city']), None) or next((r['city'] for r in g if r['city']), '')
    inits = []
    for r in sorted(g, key=lambda r: r['area'] != 'Tracker'):
        for x in r['initials']:
            if x not in inits: inits.append(x)
    head = sorted(g, key=lambda r: ['Tracker', 'Submitted to Client', 'Rejected', 'Pending', 'Awarded', 'PM job folder'].index(r['area']) if r['area'] in ['Tracker', 'Submitted to Client', 'Rejected', 'Pending', 'Awarded', 'PM job folder'] else 9)[0]
    client = next((r['client'] for r in g if r['area'] == 'Tracker' and r['client']), None) or head['client']
    addon = all(r['area'] == 'Awarded' and (ADDON.search(r['name']) or re.search(r'\d{5}-(?:0[2-9]|[1-9]\d)', r['name'])) for r in g)
    pursuits.append({
        'pursuit_id': f'P{len(pursuits) + 1:05d}', 'outcome': final, 'outcome_basis': basis,
        'pursuit_type': 'add-on to existing job' if addon else 'new commission',
        'client': client, 'architect': next((r['architect'] for r in g if r.get('architect')), ''),
        'project': head['name'][:120], 'site': head['site'] or next((r['site'] for r in g if r['site']), ''), 'city': city,
        'region': REGION.get(city, 'Unknown'), 'sector': sector, 'lead_engineer': inits[0] if inits else '', 'engineers': ' '.join(inits),
        'first_date': first.isoformat() if first else '', 'last_date': last.isoformat() if last else '',
        'year': first.year if first else '', 'due': next((r['due'].isoformat() for r in g if r.get('due')), ''),
        'accepted': next((r['accepted'].isoformat() for r in g if r.get('accepted')), ''),
        'fee_proposed': round(fee) if fee else '', 'fee_source': 'tracker' if fee_t else 'proposal doc' if fee_d else '',
        'fee_awarded': round(fee_a) if fee_a else '', 'fee_band': fee_band(fee), 'currency': next((r['currency'] for r in g if r.get('currency')), 'CAD'),
        'gfa_sf': next((round(r['gfa']) for r in g if r.get('gfa')), ''),
        'tracker_status': ';'.join(tstat), 'areas': ';'.join(f'{a}:{n}' for a, n in sorted(areas.items())),
        'deltek_p5': dm['p5'] if dm else '', 'deltek_conf': dm['conf'] if dm else '', 'deltek_why': dm['why'] if dm else '',
        'deltek_created': dm['created'] if dm else '', 'deltek_hours': dm['hours'] if dm else '', 'deltek_clients': dm['clients'] if dm else '',
        'reason': ' | '.join(r['reason'] for r in g if r.get('reason'))[:400],
        'n_records': len(g), 'n_docs': sum(r.get('ndocs', 0) for r in g), 'sample_path': head['path'][:220],
        'fee_doc_path': next((r['fee_doc_path'] for r in g if r.get('fee_doc_path')), '')[:220]})

# ---------- 7. CRM reconciliation (read-only) ----------
crm = json.load(open(D('crm.json'), encoding='utf-8'))
crm_won = [c for c in crm if c.get('ExternalSource') == 'Deltek.CustomProposal']
p_by_site = collections.defaultdict(list)
for p in pursuits:
    if p['site']: p_by_site[p['site']].append(p)
crm_rows = []
for c in crm_won:
    title = (c.get('Notes') or '').split(' | BD:')[0]
    s = site_key(title); opened = (c.get('OpenedAtUtc') or '')[:10]
    cand = p_by_site.get(s, []) if s else []
    if not cand:
        toks = [t for t in re.findall(r'[a-z]{4,}', title.lower()) if t not in ('vancouver', 'proposal', 'development', 'burnaby', 'surrey', 'street', 'avenue', 'tower', 'building', 'review')]
        cand = [p for p in pursuits if toks and sum(t in p['project'].lower() for t in toks) >= min(2, len(toks)) and p['year'] and opened and abs(int(opened[:4]) - int(p['year'])) <= 1]
    best = sorted(cand, key=lambda p: (p['outcome'].startswith('Won') is False, abs(int(p['year'] or 0) - int(opened[:4] or 0))))[0] if cand else None
    for p in ([best] if best else []):
        p['crm_id'] = c['Id']; p['crm_stage'] = 'Won (CRM)'
    crm_rows.append({'crm_id': c['Id'], 'title': title, 'opened': opened, 'fee': c.get('ProposedFee'), 'site': s,
                     'pursuit_id': best['pursuit_id'] if best else '', 'm_outcome': best['outcome'] if best else 'not found on M:'})
for p in pursuits:
    p.setdefault('crm_id', ''); p.setdefault('crm_stage', '')

# ---------- 8. write outputs ----------
cols = list(pursuits[0].keys())
with open(D('winloss-records.csv'), 'w', newline='', encoding='utf-8-sig') as fh:
    w = csv.DictWriter(fh, fieldnames=cols); w.writeheader(); w.writerows(pursuits)
with open(D('winloss-crm-reconciliation.csv'), 'w', newline='', encoding='utf-8-sig') as fh:
    w = csv.DictWriter(fh, fieldnames=list(crm_rows[0].keys())); w.writeheader(); w.writerows(crm_rows)
amb = [p for p in pursuits if p['outcome'] == 'Conflict' or (p['outcome'].startswith('Won (Deltek)') and ('low' in p['deltek_conf'] or 'tie' in p['deltek_conf'] or 'medium' in p['deltek_conf']))
       or ((p['outcome'].startswith('Not won') or p['outcome'] == 'Open') and p['deltek_p5'])]
with open(D('winloss-ambiguous.csv'), 'w', newline='', encoding='utf-8-sig') as fh:
    w = csv.DictWriter(fh, fieldnames=cols); w.writeheader(); w.writerows(amb)

def rate(rows):
    won = sum(1 for p in rows if p['outcome'].startswith('Won')); lost = sum(1 for p in rows if p['outcome'] == 'Lost')
    und = sum(1 for p in rows if p['outcome'].startswith('Not won'))
    opn = sum(1 for p in rows if p['outcome'] == 'Open')
    return {'n': len(rows), 'won': won, 'lost': lost, 'not_won_inferred': und, 'open': opn,
            'win_rate_decided': round(won / (won + lost), 3) if won + lost else None,
            'win_rate_inferred': round(won / (won + lost + und), 3) if won + lost + und else None}
def by(rows, key, minn=8):
    g = collections.defaultdict(list)
    for p in rows: g[p[key] or '(blank)'].append(p)
    out = {k: rate(v) for k, v in g.items()}
    return dict(sorted(((k, v) for k, v in out.items() if v['won'] + v['lost'] + v['not_won_inferred'] >= minn), key=lambda kv: -(kv[1]['won'] + kv[1]['lost'] + kv[1]['not_won_inferred'])))
def ckey(p): return ' '.join(client_tokens(p['client'])[:2]) or p['client'][:20].lower()
for p in pursuits: p['client_key'] = ckey(p)
outc = collections.Counter(p['outcome'] for p in pursuits)
# Window where both wins and losses are recorded on M: (Rejected exists) and the tracker runs: proposals dated 2017-01-01 or later.
win = [p for p in pursuits if p['year'] and int(p['year']) >= 2017 and p['outcome'] not in ('Pending',) and p['pursuit_type'] == 'new commission']
trk = [p for p in pursuits if p['tracker_status']]
stats = {'pursuits': len(pursuits), 'records': len(records), 'outcomes_all': dict(outc),
         'pursuit_types': dict(collections.Counter(p['pursuit_type'] for p in pursuits)),
         'by_year_tracker': by(trk, 'year', 1), 'by_lead_engineer_tracker': by(trk, 'lead_engineer'), 'by_region_tracker': by(trk, 'region'),
         'by_fee_band_tracker': by(trk, 'fee_band'), 'by_sector_tracker': by(trk, 'sector'), 'by_client_tracker': by(trk, 'client_key', 4),
         'outcomes_by_year': {y: dict(collections.Counter(p['outcome'] for p in pursuits if str(p['year']) == str(y))) for y in range(2010, 2027)},
         'window_2017_plus': rate(win), 'tracker_pursuits': rate(trk),
         'by_year': by(win, 'year', 1), 'by_region': by(win, 'region'), 'by_sector': by(win, 'sector'),
         'by_lead_engineer': by(win, 'lead_engineer'), 'by_fee_band': by(win, 'fee_band'), 'by_client': by(win, 'client_key', 6),
         'deltek_split': dict(collections.Counter(p['deltek_conf'] or '(none)' for p in pursuits if p['outcome'] == 'Won (Deltek)')),
         'crm': {'crm_won_rows': len(crm_won), 'found_on_M': sum(1 for r in crm_rows if r['pursuit_id']),
                 'found_and_won_on_M': sum(1 for r in crm_rows if r['m_outcome'].startswith('Won')),
                 'm_outcome_of_crm_wins': dict(collections.Counter(r['m_outcome'] for r in crm_rows)),
                 'crm_non_backfill_rows': len(crm) - len(crm_won),
                 'm_wins_2023plus_not_in_crm': sum(1 for p in pursuits if p['outcome'].startswith('Won') and p['year'] and int(p['year']) >= 2023 and not p['crm_id'])},
         'fee_known': sum(1 for p in pursuits if p['fee_proposed'] != ''), 'ambiguous': len(amb),
         'median_fee_won': statistics.median([p['fee_proposed'] for p in win if p['outcome'].startswith('Won') and p['fee_proposed'] != ''] or [0]),
         'median_fee_lost': statistics.median([p['fee_proposed'] for p in win if p['outcome'] == 'Lost' and p['fee_proposed'] != ''] or [0])}
json.dump(stats, open(D('winloss-stats.json'), 'w', encoding='utf-8'), indent=1, default=str)
print(json.dumps({k: stats[k] for k in ('pursuits', 'records', 'outcomes_all', 'window_2017_plus', 'tracker_pursuits', 'crm', 'fee_known', 'ambiguous', 'median_fee_won', 'median_fee_lost', 'deltek_split')}, indent=1, default=str))
