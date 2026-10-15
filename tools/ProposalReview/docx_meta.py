import sys, zipfile, re, os, glob

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

lock = sys.argv[1]
folder = sys.argv[2]

# --- Word owner/lock file: byte0 = name length, ANSI name follows; UTF-16 copy at offset 0x36
b = open(lock, 'rb').read()
n = b[0]
ansi = b[1:1 + n].decode('latin-1', 'replace')
try:
    u16 = b[0x36:0x36 + 2 * n].decode('utf-16-le', 'replace')
except Exception:
    u16 = '?'
print('LOCK FILE: size', len(b), '| owner (ansi):', repr(ansi), '| owner (utf16):', repr(u16))
print()

# --- core properties of each draft
def tag(xml, name):
    m = re.search(r'<(?:\w+:)?%s[^>]*>([^<]*)</' % name, xml)
    return m.group(1) if m else ''

for p in sorted(glob.glob(os.path.join(folder, '*.docx'))):
    z = zipfile.ZipFile(p)
    core = z.read('docProps/core.xml').decode('utf-8', 'replace')
    app = z.read('docProps/app.xml').decode('utf-8', 'replace') if 'docProps/app.xml' in z.namelist() else ''
    print(os.path.basename(p))
    print('   creator        :', tag(core, 'creator'))
    print('   lastModifiedBy :', tag(core, 'lastModifiedBy'))
    print('   created        :', tag(core, 'created'))
    print('   modified       :', tag(core, 'modified'))
    print('   revision       :', tag(core, 'revision'))
    print('   pages/words    :', tag(app, 'Pages'), '/', tag(app, 'Words'))
    print('   totalTime(min) :', tag(app, 'TotalTime'))
    # tracked changes / comments present?
    doc = z.read('word/document.xml').decode('utf-8', 'replace')
    print('   tracked ins/del:', len(re.findall(r'<w:ins ', doc)), '/', len(re.findall(r'<w:del ', doc)),
          '| comments.xml:', 'word/comments.xml' in z.namelist())
    print()
