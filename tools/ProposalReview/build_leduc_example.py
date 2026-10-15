"""Build the IL draft of the Leduc proposal from Islam's 2026.10.15 Word file.
Every edit is logged; any target that is not found raises, so a silent no-match cannot pass.
Usage: python -I build_v2.py <src.docx> <dst.docx> <orgchart.png>
"""
import sys, copy
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from docx import Document
from docx.shared import Inches
from docx.text.paragraph import Paragraph

SRC, DST, PNG = sys.argv[1:4]
d = Document(SRC)
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
log = []

# ---------- helpers ----------
def single_run_replace(p, old, new, what):
    """Replace inside ONE run only (keeps formatting, never touches drawings)."""
    for r in p.runs:
        if old in r.text:
            r.text = r.text.replace(old, new)
            log.append(f"OK  {what}: '{old}' -> '{new}'")
            return
    raise SystemExit(f"FAIL {what}: '{old}' not found inside a single run; runs={[r.text for r in p.runs]}")

def set_cell(cell, text, what):
    """First paragraph, first run gets the text; every other run/paragraph text is cleared. Cells here hold no drawings."""
    assert not cell._tc.findall('.//' + W + 'drawing'), f"{what}: cell has a drawing"
    before = cell.text.strip().replace('\n', ' / ')
    paras = cell.paragraphs
    if paras[0].runs:
        paras[0].runs[0].text = text
        for r in paras[0].runs[1:]: r.text = ''
    else:
        paras[0].add_run(text)
    for p in paras[1:]:
        for r in p.runs: r.text = ''
    log.append(f"OK  {what}: '{before}' -> '{text}'")

def para_by_text(start, what):
    hits = [p for p in d.paragraphs if p.text.strip().startswith(start)]
    if len(hits) != 1:
        raise SystemExit(f"FAIL {what}: expected 1 paragraph starting '{start}', found {len(hits)}")
    return hits[0]

def new_para_after(anchor_p, style_from_p, what):
    """A fresh paragraph (same pPr as style_from_p, no runs) placed right after anchor_p."""
    el = copy.deepcopy(style_from_p._p)
    for child in list(el):
        if child.tag != W + 'pPr':
            el.remove(child)
    anchor_p._p.addnext(el)
    log.append(f"OK  inserted paragraph: {what}")
    return Paragraph(el, anchor_p._parent)

# ---------- 1. cover: RFP number in the title text box (both the DrawingML and VML copies) ----------
hits = 0
for t in list(d.element.body.iter(W + 't')):
    if t.text and 'RFP 061' in t.text:
        t.text = t.text.replace('RFP 061', '2026-RFP-IP-061')
        hits += 1
        # the longer number wrapped and clipped at 16 pt inside the fixed text box: 12 pt keeps it on one line.
        # Only THIS paragraph's runs (w:t -> w:r -> w:p); the outer anchor paragraph holds the whole text box.
        p = t.getparent().getparent()
        for r in p.findall(W + 'r'):
            rpr = r.find(W + 'rPr')
            if rpr is None:
                rpr = r.makeelement(W + 'rPr', {}); r.insert(0, rpr)
            for tag in ('sz', 'szCs'):
                e = rpr.find(W + tag)
                if e is None:
                    e = rpr.makeelement(W + tag, {}); rpr.append(e)
                e.set(W + 'val', '24')                  # 12 pt
if hits != 2:
    raise SystemExit(f"FAIL cover title: expected 2 copies of 'RFP 061', changed {hits}")
log.append("OK  cover title: 'RFP 061' -> '2026-RFP-IP-061' in both text-box copies, runs set to 13 pt")

# cover: date of submission on the website line of the contact block (a new line overflows the page)
p_contact = para_by_text('contact@korstructural.com', 'cover contact line')
last = p_contact.runs[-1]
r = p_contact.add_run()
src_rpr = last._r.find(W + 'rPr')
if src_rpr is not None:
    r._r.insert(0, copy.deepcopy(src_rpr))
r.add_break()                                           # its own line; two extra lines fit on the cover
r.add_text('Submitted: October 15, 2026')
log.append("OK  cover date added as the last line of the contact block")

def style_runs_like(par, src_run, bold_first=False):
    """Give every run of par the character formatting of src_run (font, size, colour); first run bold if asked."""
    src = src_run._r.find(W + 'rPr')
    for i, rn in enumerate(par.runs):
        old = rn._r.find(W + 'rPr')
        if old is not None: rn._r.remove(old)
        if src is not None: rn._r.insert(0, copy.deepcopy(src))
        if bold_first and i == 0: rn.bold = True

def main_run(par):
    return max(par.runs, key=lambda x: len(x.text))

# ---------- 2. executive summary: AI disclosure, placed before the preparer paragraph ----------
p60 = para_by_text('What distinguishes Kor Structural', 'exec summary para 60')
p_ai = new_para_after(p60, p60, 'AI disclosure')
r1 = p_ai.add_run('Disclosure of Artificial Intelligence (RFP Part III, Section 8). ')
r1.bold = True
p_ai.add_run(
    'Kor Structural operates a proprietary, in-house operations platform (KOR-Operations) that incorporates '
    'AI language-model services. It was used in a supporting role in preparing this proposal: to check the draft '
    'against the submission requirements of this RFP and its Addendum and produce a list of gaps for the authors '
    'to resolve, and to generate the project organization chart in the Project Team section from the team list and '
    'roles prepared by Kor. The proposal was written by the Kor team named in that section. The fee proposal, '
    'project references and all technical content were prepared by Kor staff, and all content has been reviewed '
    'and approved by the signatories below.')
style_runs_like(p_ai, main_run(p60), bold_first=True)

# ---------- 3. project team: organization chart after the "Other team members" table ----------
t0 = d.tables[0]
assert t0.rows[0].cells[0].text.strip() == 'Names', 'T0 is not the team table'
p_other = para_by_text('Other team members include', 'team: other members line')
p_cap = d.add_paragraph()
p_cap._p.insert(0, copy.deepcopy(p_other._p.find(W + 'pPr')))      # same indent and spacing as the body text
p_cap.add_run('The chart below shows how the team is organized and supervised for this project, including the '
              'SMT and PMT representatives, Design Lead, BIM Lead and Construction Contract Administration.')
style_runs_like(p_cap, main_run(p_other))
t0._tbl.addnext(p_cap._p)
p_pic = d.add_paragraph()
p_pic._p.insert(0, copy.deepcopy(p_other._p.find(W + 'pPr')))
p_pic.add_run().add_picture(PNG, width=Inches(6.8))
p_cap._p.addnext(p_pic._p)
log.append("OK  inserted org chart (6.8 in wide, body indent) after the team table")
# the chart leaves the "Resource Strategy" heading orphaned at the foot of the page: start it on the next page
p_rs = para_by_text('Resource Strategy', 'team: Resource Strategy heading')
ppr_rs = p_rs._p.find(W + 'pPr')
if ppr_rs is None:
    ppr_rs = p_rs._p.makeelement(W + 'pPr', {}); p_rs._p.insert(0, ppr_rs)
ppr_rs.insert(0, ppr_rs.makeelement(W + 'pageBreakBefore', {}))
log.append("OK  'Resource Strategy' heading moved to the top of the next page")

# ---------- 4. typos and wrong provinces ----------
single_run_replace(d.tables[1].rows[3].cells[0].paragraphs[0], 'CUMMUNITY', 'COMMUNITY', 'references table')
single_run_replace(para_by_text('Millenium Sports Center', "Jim's project list"), 'Millenium', 'Millennium', "Jim's list")
single_run_replace(para_by_text('MILLLENNIUM SPORTS CENTER', 'Millennium heading'), 'MILLLENNIUM', 'MILLENNIUM', 'Millennium heading')
single_run_replace(para_by_text('BURNABY LAKE RECREATION CENTER, Burnaby (AB', 'Burnaby heading'), '(AB, CAN)', '(BC, CAN)', 'Burnaby province')
single_run_replace(para_by_text('PORT COQUITLAM COMMUNITY CENTER, Port Coquitlam (AB', 'PoCo heading'), '(AB, CAN)', '(BC, CAN)', 'Port Coquitlam province')

# ---------- 5. fee table ----------
t2 = d.tables[2]
assert t2.rows[0].cells[0].text.strip() == 'Personnel', 'T2 is not the fee table'
hdr = t2.rows[0].cells
set_cell(hdr[5], 'Total Hourly Rate (incl. OH)', 'fee header c5 line 1');  hdr[5].paragraphs[1].runs[0].text = '($/hr)'
set_cell(hdr[6], 'Total Hourly Rate (incl. OH & Profit)', 'fee header c6 line 1'); hdr[6].paragraphs[1].runs[0].text = '($/hr)'
# the five scored positions, labelled
set_cell(t2.rows[3].cells[1], 'Design Lead - Senior Design Engineer', 'Islam role')
set_cell(t2.rows[5].cells[1], 'Construction Contract Administration - Structural Engineer', 'Andrea role')
set_cell(t2.rows[6].cells[1], 'BIM Lead - Senior BIM Technician', 'Serban role')
# profit 20% (JM, 2026-10-15): last column = incl.-OH column x 1.20
PROFIT = 0.20
for ri in range(1, len(t2.rows)):
    cells = t2.rows[ri].cells
    name = cells[0].text.strip().split('\n')[0]
    incl_oh = float(cells[5].text.strip())
    set_cell(cells[5], f'{incl_oh:.2f}', f'{name} incl-OH')
    set_cell(cells[6], f'{incl_oh * (1 + PROFIT):.2f}', f'{name} incl-OH-and-profit')
# the two header lines the RFP 7.6 example puts above the table
p_ovh = para_by_text('We propose using an overhead rate', 'fee: overhead para')
p_ovtitle = para_by_text('Overhead Rate Proposed', 'fee: overhead title')
# The fee table is a FLOATING (positioned) table: text flows around it, so anything inserted near it
# rendered below it. Make it inline and set the order explicitly: Pricing information -> OH% -> Profit% -> table.
tblPr = t2._tbl.find(W + 'tblPr')
for tag in ('tblpPr', 'tblOverlap'):
    for e in tblPr.findall(W + tag): tblPr.remove(e)
# inline table starts at the page margin; indent it to the body text's left indent
ind_src = p_ovh._p.find(W + 'pPr').find(W + 'ind') if p_ovh._p.find(W + 'pPr') is not None else None
left = (ind_src.get(W + 'left') or ind_src.get(W + 'start')) if ind_src is not None else None
tind = tblPr.find(W + 'tblInd')
if tind is None:
    tind = tblPr.makeelement(W + 'tblInd', {})
    anchor = tblPr.find(W + 'jc')
    if anchor is None: anchor = tblPr.find(W + 'tblW')
    if anchor is None: anchor = tblPr.find(W + 'tblStyle')
    if anchor is None: tblPr.insert(0, tind)
    else: anchor.addnext(tind)
tind.set(W + 'w', left or '820'); tind.set(W + 'type', 'dxa')
log.append(f"OK  fee table indent set to {left or '820'} twips (body text indent)")
p_pi = para_by_text('Pricing information', 'fee: Pricing information')
el = copy.deepcopy(p_ovh._p)
for child in list(el):
    if child.tag != W + 'pPr': el.remove(child)
p_pi._p.addnext(el); p_ov = Paragraph(el, p_ovh._parent)
p_ov.add_run('Proponent Overhead Rate (%): '); p_ov.add_run('24%'); style_runs_like(p_ov, p_ovh.runs[0], bold_first=True)
p_pr = new_para_after(p_ov, p_ov, 'fee: profit % line')
p_pr.add_run('Proponent Profit (%): '); p_pr.add_run('20%'); style_runs_like(p_pr, p_ovh.runs[0], bold_first=True)
p_pr._p.addnext(t2._tbl)                                 # moves the table to follow the two lines
log.append("OK  fee table made inline; overhead % and profit % lines placed between 'Pricing information' and the table")
# profit paragraphs, cloned BEFORE the page break is added so only "Overhead Rate Proposed" carries it
p_pt = new_para_after(p_ovh, p_ovtitle, 'fee: profit title')
el = copy.deepcopy(p_ovtitle.runs[0]._r); p_pt._p.append(el); p_pt.runs[0].text = 'Profit Proposed'
p_pb = new_para_after(p_pt, p_ovh, 'fee: profit body')
p_pb.add_run('We propose a profit of 20%, carried in the Risk Pool with 100% of profit at risk, in accordance with '
             'CCDC-30 (2025) and the IPD agreement. Overhead and profit are shown separately from burdened labour cost, '
             'as required in Section 7 of the RFP.')
style_runs_like(p_pb, p_ovh.runs[0])
# the four narrative paragraphs move together to the next page instead of stranding three lines
ppr = p_ovtitle._p.find(W + 'pPr')
ppr.insert(0, ppr.makeelement(W + 'pageBreakBefore', {}))

d.save(DST)
print('\n'.join(log))
print('disclosure runs bold flags:', [(r.bold, r.text[:30]) for r in p_ai.runs])
print(f'\nSAVED {DST}')
