import glob, os, re
# same.py: each new-<job>.e2k equals old-<job>.e2k byte for byte once every ANG value is blanked
here = os.path.dirname(os.path.abspath(__file__))
for old in sorted(glob.glob(os.path.join(here, 'old-*.e2k'))):
    new = old.replace('old-', 'new-')
    a, b = open(old, 'rb').read(), open(new, 'rb').read()
    blank = lambda s: re.sub(rb'ANG -?[0-9.]+', b'ANG x', s)
    print(os.path.basename(new), 'same but ANG' if blank(a) == blank(b) else 'DIFFERENT', len(a), len(b), a.count(b'\r'), b.count(b'\r'))
