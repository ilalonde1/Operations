import sys, zipfile, re
from xml.etree import ElementTree as ET

NS = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'

def cells(path, sheetname='Questions'):
    z = zipfile.ZipFile(path)
    shared = []
    if 'xl/sharedStrings.xml' in z.namelist():
        for si in ET.fromstring(z.read('xl/sharedStrings.xml')):
            shared.append(''.join(t.text or '' for t in si.iter(NS + 't')))
    wb = ET.fromstring(z.read('xl/workbook.xml'))
    rels = ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
    rid = {r.get('Id'): r.get('Target') for r in rels}
    target = None
    for sh in wb.iter(NS + 'sheet'):
        if sh.get('name') == sheetname:
            r = sh.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id')
            target = rid[r]
    p = 'xl/' + target.lstrip('/').replace('xl/', '', 1) if target else None
    if p not in z.namelist():
        p = 'xl/' + target
    ws = ET.fromstring(z.read(p))
    rows = {}
    for row in ws.iter(NS + 'row'):
        for c in row.iter(NS + 'c'):
            ref = c.get('r')
            col = re.match(r'([A-Z]+)(\d+)', ref)
            v = c.find(NS + 'v')
            isr = c.find(NS + 'is')
            if c.get('t') == 's' and v is not None:
                txt = shared[int(v.text)]
            elif isr is not None:
                txt = ''.join(t.text or '' for t in isr.iter(NS + 't'))
            elif v is not None:
                txt = v.text
            else:
                txt = ''
            rows.setdefault(int(col.group(2)), {})[col.group(1)] = txt
    return rows

for path in sys.argv[1:]:
    rows = cells(path)
    print('=' * 100)
    print(path.split('\\')[-1])
    hdr = rows.get(4, {})
    print('  title:', rows.get(1, {}).get('A', '')[:120])
    for n in sorted(rows):
        if n < 5:
            continue
        r = rows[n]
        ref, status, q = r.get('A', ''), r.get('B', ''), r.get('C', '')
        if not ref:
            continue
        mark = '  >>> ' if status == 'NEEDS YOU' else '      '
        print(f'{mark}{ref:4} {status:10} {q[:150]}')
