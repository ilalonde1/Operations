# Is every file the first-install exe lays down byte-identical to the signed update zip of the same version?
import sys, zipfile, hashlib, os

g = sys.argv[1]
z = zipfile.ZipFile(os.path.join(g, 'pkg', 'KorConcrete-0.134.4.zip'))
zh = {i.filename.replace(os.sep, '/').replace(chr(92), '/'): hashlib.sha256(z.read(i)).hexdigest()
      for i in z.infolist() if not i.is_dir()}
root = os.path.join(g, 'installer', 'x', '{app}')
ih = {}
for d, _, fs in os.walk(root):
    for f in fs:
        p = os.path.join(d, f)
        with open(p, 'rb') as fh:
            ih[os.path.relpath(p, root).replace(chr(92), '/')] = hashlib.sha256(fh.read()).hexdigest()


def key(n):
    return n if n.startswith('launcher/') else 'versions/0.134.4/' + n


zk = {key(n): h for n, h in zh.items()}
same = sum(1 for k in zk if ih.get(k) == zk[k])
print(f"zip files {len(zk)}, installer files {len(ih)}, identical {same}")
print("different:", [k for k in zk if k in ih and ih[k] != zk[k]])
only_zip = [k for k in zk if k not in ih]
print("only in zip:", len(only_zip), only_zip[:10])
print("only in installer:", [k for k in ih if k not in zk])
