r"""Judge each knob build against the engineer's model: the yardstick's headline numbers per build, so a rule's
move shows whether it brought the model nearer her model or further. Reads sixknob/<job>/<tag>/yardstick.txt and diff.txt.
COVERS: columns both ways within 100 mm, plate area share, openings of hers we have, the diff's change count.
DOES NOT: walls (the yardstick does not pair walls), sections, storey heights."""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sixknob")


def nums(path):
    if not os.path.exists(path):
        return None
    t = open(path, encoding="utf-8", errors="replace").read()
    o = re.search(r"ours -> theirs: (\d+) columns.*?within 100 mm (\d+) \((\d+)%\)", t)
    h = re.search(r"theirs -> ours: (\d+) columns.*?within 100 mm (\d+) \((\d+)%\)", t)
    p = re.search(r"plates on the shared storeys: ours ([\d,]+) sq ft, hers ([\d,]+) \((\d+)%\)", t)
    q = re.search(r"hers we have, by centre or cover.*?: (\d+) of (\d+)", t)
    return dict(ours=f"{o.group(2)}/{o.group(1)}" if o else "?", hers=f"{h.group(2)}/{h.group(1)}" if h else "?",
                plate=p.group(3) + "%" if p else "?", open=f"{q.group(1)}/{q.group(2)}" if q else "?")


def changes(path):
    if not os.path.exists(path):
        return "?"
    t = open(path, encoding="utf-8", errors="replace").read()
    return "0 (identical)" if "byte-identical" in t else str(len(re.findall(r"^\s+(LOST|GAINED|MOVED)", t, re.M)))


for job in sorted(os.listdir(ROOT)):
    if len(sys.argv) > 1 and job not in sys.argv[1].split(","):
        continue
    print(f"== {job}   (ours within 100 mm of hers / hers within 100 mm of ours / our plate as % of hers / her openings we have)")
    tags = sorted(os.listdir(os.path.join(ROOT, job)), key=lambda t: (t != "all-on", not t.startswith("off-149-150-151-152-153-154"), t))
    for tag in tags:
        d = os.path.join(ROOT, job, tag)
        n = nums(os.path.join(d, "yardstick.txt"))
        if n is None:
            continue
        print(f"   {tag[:26]:<26} changes {changes(os.path.join(d, 'diff.txt')):>14}   ours {n['ours']:>9}  hers {n['hers']:>9}  plate {n['plate']:>5}  openings {n['open']:>7}")
