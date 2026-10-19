"""Build the two extraction manifests from the inventory (no share access):
  manifest_knowledge.txt  category (a) KOR documents for claims
  manifest_proposals.txt  proposal / contract documents for the win/loss record (fee, scope)
Rules are stated per area so the map can quote them."""
import csv, os, re, collections
base = os.environ.get('KOR_MGMT_WORK') or os.path.dirname(os.path.abspath(__file__))  # work dir: inventory.csv, bundles, Deltek/CRM pulls
rows = [r for r in csv.DictReader(open(os.path.join(base, 'inventory.csv'), encoding='utf-8-sig')) if r['Dir'] != 'True']
TEXT = {'.pdf', '.docx', '.doc', '.msg', '.txt', '.pptx', '.rtf', '.docm', '.dotx'}
def P(r): return r['Path'].strip('\\').split('\\')
def ext(r): return r['Extension'].lower()
def size(r): return int(r['Length'] or 0)
def stem(p): return re.sub(r'\.[^.]+$', '', p[-1]).lower().strip()

def prefer_word(items):
    """Within one folder keep a .docx/.doc and drop the .pdf of the same stem (KOR saves both)."""
    by = collections.defaultdict(list)
    for p, r in items: by[('\\'.join(p[:-1]).lower(), stem(p))].append((p, r))
    out = []
    for k, v in by.items():
        words = [x for x in v if ext(x[1]) in ('.docx', '.doc', '.docm')]
        out += words[:1] if words else v[:1]
    return out

K = []; why = collections.Counter()
def addK(items, tag):
    for p, r in items: K.append('\\'.join(p)); why[tag] += 1

def area(*pre, excl=(), exts=TEXT, maxb=15_000_000):
    pre = list(pre)
    return [(P(r), r) for r in rows if P(r)[:len(pre)] == pre and ext(r) in exts and size(r) <= maxb and size(r) > 0
            and not P(r)[-1].startswith('~$') and not any(e.lower() in '\\'.join(P(r)).lower() for e in excl)]

FP = ['Correspondence', '01 Fee Proposals']
addK(prefer_word([x for x in area(*FP) if len(x[0]) == 3]), 'fee templates + T&Cs (01 Fee Proposals root)')
addK(prefer_word(area(*FP, 'Company Introduction', excl=['\\PAST\\'])), 'company introductions (current)')
addK(prefer_word(area(*FP, 'Hourly Charge Out Rates')), 'charge-out rates (current + history)')
addK(area(*FP, 'Indemnification Notes'), 'indemnification / contract positions')
addK(prefer_word(area(*FP, 'Reference Notes & Templates')), 'fee proposal reference notes')
addK(prefer_word(area(*FP, 'Past Fee Templates')), 'past fee templates')
addK(prefer_word(area(*FP, 'Resumes', excl=['Archive'])), 'partner/staff proposal resumes (marketing)')
addK(prefer_word(area(*FP, 'Project Experience & Portfolios', maxb=8_000_000)), 'project experience & portfolios')
addK([x for x in area(*FP, 'Fraser Valley Health Authority') if 'Addendum' not in x[0][-1]], 'FHA RFPQ (main docs)')
addK(prefer_word(area('Correspondence', 'Wordings')), 'wordings / RFP experience')
addK(prefer_word(area('Correspondence', '07 Notices')), 'site notices')
addK([x for x in area('Correspondence', 'EOR Letters') if len(x[0]) == 3], 'EOR letter templates')
adm_skip = ['Client Contact List', 'Daily Phones', 'Frequent Caller', 'Important Numbers', 'Important Phone', 'Quick Reference Employee',
            'Site Contact List', 'John Bryson.docx', 'Sanovia', 'Reception Contacts', 'office layout', 'PacBlue', 'Invoices to be sent']
addK([x for x in area('Administration') if len(x[0]) == 2 and ext(x[1]) in ('.docx', '.doc')], 'admin guides (root)')
addK(prefer_word(area('Administration', 'Reference', excl=adm_skip)), 'admin reference procedures')
addK(prefer_word(area('Administration', 'Notices')), 'admin notices')
addK(prefer_word([x for x in area('Administration', '1. Docs to be Rebranded') if ext(x[1]) in ('.docx', '.doc')]), 'office templates')
addK(prefer_word(area('Meeting Minutes', excl=['Job Posting'])), 'department meeting minutes')
fr_skip = ['Active Jobs', 'EMAILED BOOKINGS', 'Reviews emails', 'Active Job Sites', 'INSPECTION BY WHO']
addK(prefer_word(area('Field Review Reports', excl=fr_skip)), 'field review procedures')
addK([x for x in area('Business Development', 'BizBrain') if len(x[0]) == 3], 'BizBrain BD reports')
addK([x for x in area('Business Development', 'BizBrain', 'Leduc - 10132026') if 'Appendix B' not in x[0][-1] and 'Appendix C' not in x[0][-1]
      and 'Appendix D' not in x[0][-1] and 'Appendix E' not in x[0][-1] and 'FileSummary' not in x[0][-1]], 'Leduc RFP pursuit')
cb = area('Business Development', 'Corporate Brochure')
addK([x for x in cb if x[0][-1] in ('Kor_Structural_Corporate_Portfolio_2025_LATEST.docx', 'Kor_Structural_Corporate_Portfolio_2025-03-17_USA .docx', 'CALIFORNIA WORDING.docx')], 'corporate portfolio')
addK([x for x in area('Business Development') if len(x[0]) == 2 and ext(x[1]) == '.pptx'], 'BD presentation')
br = {'KOR_Brochure_1Pager_revised_09172025_flat.pdf', 'KOR_Brochure_1Pager_WoodFrame_Final.pdf', 'KOR_Brochure_2Pager_revised_v2_CONOR.pdf', 'Midrise Advertising.pdf', 'KOR_Brochure_1Pager_Okanagan_3.pdf'}
addK([x for x in area('Business Development', 'Brochures') if x[0][-1] in br], 'brochures (final versions)')
addK(prefer_word(area('Project Management', 'Project Specifications', excl=['1. OLD - Archives'])), 'structural specification templates (current)')
addK([x for x in area('Project Management') if len(x[0]) == 2], 'PM root template')

# ---- proposals manifest ----
PR = []; whyp = collections.Counter()
fpname = re.compile(r'(?:\bFP\b|-FP|FP\d{4}|fps\d{4}|fee|proposal|\bKOR\b|\bBMZ|\bJBP|agreement|contract|\bPSA\b|\bCSA\b)', re.I)
draw = re.compile(r'drawing|dwg|arch|plans?\b|survey|geotech|render|photo|addend|spec', re.I)
def addP(items, tag):
    for p, r in items: PR.append('\\'.join(p)); whyp[tag] += 1
PO = 'Project Opportunities'
aw = area(PO, 'Word Documents for Awarded Projects', exts={'.doc', '.docx', '.pdf', '.docm'})
addP(prefer_word(aw), 'awarded proposals')
for a in ('Rejected', 'Submitted to Client', 'Pending'):
    items = [x for x in area(PO, a, exts={'.doc', '.docx', '.pdf'}, maxb=12_000_000)
             if fpname.search(x[0][-1]) and not draw.search(x[0][-1])]
    addP(prefer_word(items), a.lower() + ' proposals')
pm = [x for x in area('Project Management', exts={'.doc', '.docx', '.pdf'}, maxb=12_000_000)
      if x[0][1] not in ('Job Sheets', 'Project Specifications', 'Photos') and re.search(r'\bFP\b|-FP|fps\d{4}|FP\d{4}|fee|proposal|agreement|contract|\bPSA\b|\bCSA\b', x[0][-1], re.I)]
addP(prefer_word(pm), 'project management fee/contract docs')

open(os.path.join(base, 'manifest_knowledge.txt'), 'w', encoding='utf-8').write('\n'.join(K) + '\n')
open(os.path.join(base, 'manifest_proposals.txt'), 'w', encoding='utf-8').write('\n'.join(PR) + '\n')
print('KNOWLEDGE', len(K), 'files'); [print('  %4d %s' % (c, t)) for t, c in why.items()]
print('PROPOSALS', len(PR), 'files'); [print('  %4d %s' % (c, t)) for t, c in whyp.items()]
print('dupes K', len(K) - len(set(K)), 'dupes P', len(PR) - len(set(PR)))
