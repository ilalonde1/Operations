r"""attribute_baseline_drift.py : which commit moved which storey of the six banked sets, and what moved.

  walk      <worktree> <outdir> <commit,commit,...|@file>   build the six sets at each commit, in order
  attribute <outdir> <commit,commit,...|@file> [--yard dir]  the table: storey, first commit, what moved, net, the engineer's plate

WHY (2026-10-26). SixSetsBuildAsBankedTests had been red since 231e0f69 and the bank (Baselines/pdf-only-<job>.e2k) no longer
reproduced from any one knob: switching steps 175-183 off restored it on 0 of 6 sets, and 52 of 67 moved storeys had no step to
name. Re-banking is accepting today's output as the standard, which is never done unread - so the drift was walked commit by
commit instead: 31 commits, 67 storeys, every one attributed. The output is what a reader judges fix / regression / bank's error
from, beside the engineer's own plate and thickness on that storey.

HOW. walk: for each commit, `git switch --detach` the given worktree (your own worktree, never the main checkout), build the
takeoff CLI in Debug there, and run `takeoff stickfile` on each of the six sets from the local drawing mirror with
--rules-db (the PdfOnlyBuild + KorStandards route the test runs; checked identical to it on 6 of 6 sets, 2026-10-26).
A commit whose build fails is written BUILD-FAILED to <outdir>\walk.csv and its sets are not built - an older exe would
answer for it. The six sets and their scales are read from SixSetsBuildAsBankedTests.cs, so this tool cannot drift from it.
attribute: the FIRST commit in the list is the base (put the bank's commit first). Each storey that differs between the base
and the last commit gets the first commit where it moves, what moved there, the net change, and every commit that moved its
plate, thickness or openings - so a move later undone reads as such. Then, per commit, what it changed against the commit
before.

COVERS: per storey - AREA FLOOR plate total (sq ft), plate sq ft by thickness (when the total holds within 1%), OPENING count
and area, and `takeoff model-diff`'s lost + gained walls and columns. The engineer's storey matched by name after
LEVEL 3 / L-3 / L03 -> L3 (a prefix kept: C-LEVEL 3 -> C-L3).
DOES NOT: plate POSITION (a plate moved whole at the same size reads unchanged); member sections; beams, braces, loads;
a storey renamed between commits (reads as one gone and one new); an engineer's storey named another way (shows '-').
SAME-CLASS FAULT IT MISSES: a set with two buildings where the engineer modelled one - the per-storey total is compared
with hers whole, so a second tower's floor closing reads as a plate 'doubling' (31065 L7-L17, 7,662 -> 13,925 against
her 7,834: the right-hand tower's floor, which the bank lacked). Render before judging: takeoff model-render --storey.
Exit 0; 2 on bad arguments.
"""
import collections, glob, hashlib, os, re, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TEST = os.path.join(REPO, "Kor.Operations.EngineeringTools.Core.Tests", "Intake", "SixSetsBuildAsBankedTests.cs")
MIRROR = os.path.join(os.environ.get("TEMP", ""), "kor-drawings")
CLI = os.path.join("Kor.Operations.EngineeringTools.TakeoffCli", "bin", "Debug", "net8.0", "takeoff.exe")
TO_MM = {"MM": 1.0, "IN": 25.4, "FT": 304.8, "M": 1000.0}
EMPTY = {"plate": 0.0, "thick": {}, "open": [0, 0.0]}


def six_sets():
    """(job, share path, scale) as SixSetsBuildAsBankedTests banks them"""
    sets = re.findall(r'new\("([^"]+)",\s*@"([^"]+)",\s*(\d+)\)', open(TEST, encoding="utf-8").read())
    if len(sets) != 6:
        sys.exit(f"expected 6 banked sets in {TEST}, read {len(sets)}")
    return [(j, p, int(s)) for j, p, s in sets]


def mirror_copy(share):
    """DrawingMirror.LocalCopyOf: <mirror>\\<SHA1(lower dir)[:16]>\\<file>"""
    d, f = os.path.split(share)
    return os.path.join(MIRROR, hashlib.sha1(d.lower().encode("utf-8")).hexdigest().upper()[:16], f)


def commits_of(arg):
    if arg.startswith("@"):
        return [c.strip() for c in open(arg[1:]) if c.strip()]
    return [c for c in arg.split(",") if c]


def e2k_in(d):
    found = glob.glob(os.path.join(d, "*.e2k"))
    return max(found, key=os.path.getsize) if found else None


def walk(wt, out, commits):
    exe = os.path.join(wt, CLI)
    sets = six_sets()
    for job, share, _ in sets:
        if not os.path.exists(mirror_copy(share)):
            sys.exit(f"{job}: no local mirror copy {mirror_copy(share)} - never read the share")
    rules = os.environ["KOR_ENGINEERINGTOOLS_STANDARDSDB"]
    ledger = os.path.join(out, "walk.csv")
    os.makedirs(out, exist_ok=True)
    for c in commits:
        if subprocess.run(["git", "-C", wt, "switch", "--detach", c], capture_output=True).returncode != 0:
            open(ledger, "a").write(f"{c},*,SWITCH-FAILED\n"); print(c, "switch failed", flush=True); continue
        b = subprocess.run(["dotnet", "build", os.path.join(wt, "Kor.Operations.EngineeringTools.TakeoffCli"), "-c", "Debug", "-v", "q"],
                           capture_output=True, text=True)
        if b.returncode != 0 or not os.path.exists(exe):
            open(ledger, "a").write(f"{c},*,BUILD-FAILED\n"); print(c, "build failed", flush=True); continue

        def one(s):
            job, share, scale = s
            d = os.path.join(out, job, c)
            r = subprocess.run([exe, "stickfile", mirror_copy(share), d, "--scale", str(scale), "--job", job, "--rules-db", rules,
                                "--no-render", "--no-questions"], capture_output=True, text=True, encoding="utf-8", errors="replace")
            os.makedirs(d, exist_ok=True)
            open(os.path.join(d, "build.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)
            e = e2k_in(d)
            return job, (hashlib.sha256(open(e, "rb").read()).hexdigest()[:16] if r.returncode == 0 and e else "NO-OUTPUT")
        with ThreadPoolExecutor(6) as ex:
            for job, h in ex.map(one, sets):
                open(ledger, "a").write(f"{c},{job},{h}\n")
        print(c, "done", flush=True)


def state(path, drop_ours=False):
    """storey -> {'plate': sq ft, 'thick': {inches: sq ft}, 'open': [n, sq ft]}. drop_ours for the engineer's model: objects this
    tool named (K-letter-digits, ModelYardstick.GeneratedObject) are not hers."""
    u = 1.0; pts = {}; areas = {}; thick = {}
    st = collections.defaultdict(lambda: {"plate": 0.0, "thick": collections.Counter(), "open": [0, 0.0]})
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for s in lines:
        s = s.strip()
        if s.startswith("UNITS"):
            q = re.findall(r'"([^"]+)"', s)
            if len(q) > 1: u = TO_MM.get(q[1].upper(), 1.0)
        elif m := re.match(r'(?:SHELLPROP|SLABPROP|SLAB)\s+"([^"]+)".*?\b(?:SLABTHICKNESS|THICKNESS|TH)\s+(\S+)', s): thick[m.group(1)] = float(m.group(2)) * u
        elif m := re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s): pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u)
        elif m := re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+(\d+)\s+(.*)', s):
            if drop_ours and re.fullmatch(r"K[A-Z]\d+", m.group(1)): continue    # this tool's own plate in a yardstick (31168's KF84)
            areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]

    def sqft(obj):
        p = [pts[q] for q in areas.get(obj, []) if q in pts]
        return abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2 / 92903.04 if len(p) >= 3 else 0.0
    for s in lines:
        if m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+(SECTION\s+"([^"]+)"|OPENING\s+"Yes")', s):
            obj, storey = m.group(1), m.group(2)
            if obj not in areas: continue                       # a wall panel, not a floor area
            a = sqft(obj)
            if m.group(4) is not None:
                st[storey]["plate"] += a; st[storey]["thick"][round(thick.get(m.group(4), 0) / 25.4, 1)] += a
            else:
                st[storey]["open"][0] += 1; st[storey]["open"][1] += a
    return st


def norm(n):
    n = re.sub(r"LEVEL\s*", "L", n.upper().strip()); n = re.sub(r"\bL-(\d)", r"L\1", n); return re.sub(r"\bL0+(\d)", r"L\1", n)


def members(exe, a, b):
    """storey -> (lost+gained walls, lost+gained columns) by takeoff model-diff a -> b"""
    out = subprocess.run([exe, "model-diff", a, b], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    res = {}
    for line in out.splitlines():
        if m := re.match(r"^(\S+): columns .*\[lost columns (\d+), gained columns (\d+), lost walls (\d+), gained walls (\d+)\]", line):
            res[m.group(1)] = (int(m.group(4)) + int(m.group(5)), int(m.group(2)) + int(m.group(3)))
    return res


def what(sa, sb, mem):
    out = []
    pa, pb = sa["plate"], sb["plate"]
    if abs(pa - pb) > max(1.0, 0.01 * max(pa, pb)): out.append(f"plate {pa:,.0f}->{pb:,.0f}")
    elif {k: round(v) for k, v in sa["thick"].items() if v > 1} != {k: round(v) for k, v in sb["thick"].items() if v > 1}:
        top = lambda s: "/".join(f'{k:g}"' for k, v in sorted(s["thick"].items(), key=lambda kv: -kv[1])[:2])
        out.append(f"thickness {top(sa)}->{top(sb)}")
    if sa["open"][0] != sb["open"][0] or abs(sa["open"][1] - sb["open"][1]) > 1: out.append(f"openings {sa['open'][0]}->{sb['open'][0]}")
    if mem and (mem[0] or mem[1]): out.append(f"members walls {mem[0]} cols {mem[1]}")
    return out


def mix(s):
    return " ".join(f'{k:g}":{v:,.0f}' for k, v in sorted(s["thick"].items(), key=lambda kv: -kv[1]) if v > 50) if s else "-"


def attribute(out, commits, yard):
    exe = os.path.join(REPO, CLI)
    subj = {c: subprocess.run(["git", "-C", REPO, "log", "-1", "--format=%s", c], capture_output=True, text=True).stdout.strip()[:90] for c in commits}
    base_c, walked = commits[0], commits[1:]
    table, per_commit = [], collections.defaultdict(list)
    for job, _, _ in six_sets():
        base = e2k_in(os.path.join(out, job, base_c))
        if not base:
            print(f"{job}: no build at the base {base_c}"); continue
        y = os.path.join(yard, job.split("-arch")[0] + ".e2k")
        hers = {norm(k): v for k, v in state(y, drop_ours=True).items()} if os.path.exists(y) else {}
        seq = [(c, e2k_in(os.path.join(out, job, c))) for c in walked]
        seq = [(c, p) for c, p in seq if p]
        if not seq:
            print(f"{job}: no walked builds"); continue
        s0 = state(base); states = {c: state(p) for c, p in seq}
        mem0 = {c: members(exe, base, p) for c, p in seq}
        last, sf = seq[-1][0], states[seq[-1][0]]
        for st in sorted({s for s in set(s0) | set(sf) if what(s0.get(s, EMPTY), sf.get(s, EMPTY), mem0[last].get(s))}, key=lambda s: (len(s), s)):
            first = next((c for c, _ in seq if what(s0.get(st, EMPTY), states[c].get(st, EMPTY), mem0[c].get(st))), None)
            chain, pv = [], s0
            for c, _ in seq:
                if what(pv.get(st, EMPTY), states[c].get(st, EMPTY), None): chain.append(c[:8])
                pv = states[c]
            h = hers.get(norm(st))
            table.append((job, st, first or "NOT FOUND",
                          "; ".join(what(s0.get(st, EMPTY), states[first].get(st, EMPTY), mem0[first].get(st))) if first else "",
                          "; ".join(what(s0.get(st, EMPTY), sf.get(st, EMPTY), mem0[last].get(st))), ",".join(chain),
                          f"{h['plate']:,.0f}" if h else "-", mix(s0.get(st)), mix(sf.get(st)), mix(h)))
        prev, prevp = s0, base
        for c, p in seq:
            mem = members(exe, prevp, p)
            for st in sorted(set(prev) | set(states[c]), key=lambda s: (len(s), s)):
                w = what(prev.get(st, EMPTY), states[c].get(st, EMPTY), mem.get(st))
                if w:
                    h = hers.get(norm(st))
                    per_commit[c].append(f"{job} {st}: {'; '.join(w)} | hers {h['plate']:,.0f}" if h else f"{job} {st}: {'; '.join(w)} | hers -")
            prev, prevp = states[c], p

    print(f"TABLE - storeys that differ between {base_c} (base) and {walked[-1]}")
    for r in table:
        print(f"{r[0]:<14} {r[1]:<10} first {r[2]:<9} {r[3]}   [hers plate {r[6]}]")
        print(f"{'':<25} NET {r[4]}   moved at: {r[5] or '(members only)'}")
        print(f"{'':<25} thickness  base {r[7]} | last {r[8]} | hers {r[9]}")
    nf = sum(1 for r in table if r[2] == "NOT FOUND")
    print(f"\n{len(table)} storeys moved; {len(table) - nf} with a first commit, {nf} NOT FOUND")
    print("\nPER COMMIT - what it changed against the commit before:")
    for c in walked:
        print(f"{c} {subj[c]}: {len(per_commit[c])} storey change(s)")
        for line in per_commit[c]: print("    " + line)


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) == 4 and a[0] == "walk":
        walk(a[1], a[2], commits_of(a[3]))
    elif len(a) in (3, 5) and a[0] == "attribute":
        yard = a[a.index("--yard") + 1] if "--yard" in a else os.path.join(MIRROR, "yardsticks")
        attribute(a[1], commits_of(a[2]), yard)
    else:
        print(__doc__.split("\n\n")[0]); sys.exit(2)
