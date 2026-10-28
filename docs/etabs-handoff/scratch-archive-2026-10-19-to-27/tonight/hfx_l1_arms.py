r"""hfx_l1_arms.py : Halifax LEVEL 1.dxf (VD's 13:58 read) built alone, then with one wall outline removed per arm, to find which
WallNetwork pass closes the 969 mm north/south door and tilts the four corner pieces. Arms: all | no7 (the 12 in cross wall) |
no9_11 (the two 20-24 in west pieces that meet the corners). Writes scratchpad tonight/hfx_l1/<arm>/{dxf/LEVEL 1.dxf, out.e2k}."""
import os, subprocess, sys
import ezdxf
SRC = r"C:\KOR.Drafter-work\31180-01-halifax\2-etabs\dxf\LEVEL 1.dxf"
LEVELS = r"C:\VIsual Studio Projects\KOR.Drafter\jobs\31180-01-halifax\inputs\levels.csv"
HERE = os.path.dirname(os.path.abspath(__file__))
EXE = os.path.join(HERE, "takeoff-bin", "takeoff.exe")
ARMS = {"all": set(), "no7": {7}, "no9_11": {9, 11}}
# The file is R12 and ezdxf's R12 save drops $INSUNITS, so the arms are cut at text level: group-code pairs, and a dropped
# POLYLINE goes with its VERTEXes up to its SEQEND. The wall numbering is the order ezdxf lists them in (same as e2k_walls).
lines = open(SRC, encoding="utf-8", errors="replace").read().splitlines()
pairs = [(lines[i], lines[i + 1]) for i in range(0, len(lines) - 1, 2)]
for arm, drop in ARMS.items():
    kept, n, skipping = [], 0, False
    for k, (code, val) in enumerate(pairs):
        if code.strip() == "0":
            if skipping and val.strip() != "VERTEX" and val.strip() != "SEQEND":
                skipping = False
            if val.strip() == "POLYLINE":
                layer = next(v for c, v in pairs[k + 1:] if c.strip() == "8")
                if layer.strip().upper().endswith("WALL"):
                    n += 1
                    skipping = n in drop
            if skipping and val.strip() == "SEQEND":
                kept_seqend_skip = True
        if not skipping:
            kept.append((code, val))
        elif code.strip() == "0" and val.strip() == "SEQEND":
            pass
    # a SEQEND that closes a dropped POLYLINE is dropped with it; the pair after it starts a new entity
    out = os.path.join(HERE, "hfx_l1", arm)
    os.makedirs(os.path.join(out, "dxf"), exist_ok=True)
    open(os.path.join(out, "dxf", "LEVEL 1.dxf"), "w", encoding="utf-8").write("\n".join(f"{c}\n{v}" for c, v in kept) + "\n")
    check = ezdxf.readfile(os.path.join(out, "dxf", "LEVEL 1.dxf"))
    left = sum(1 for e in check.modelspace() if e.dxf.layer.upper().endswith("WALL"))
    assert left == n - len(drop) and check.header.get("$INSUNITS") == 4, (arm, left, n)
    # the same call as the Halifax check: no reference, the job's levels.csv in feet, no storey rise
    r = subprocess.run([EXE, "dxf-to-etabs", os.path.join(out, "dxf"), "-", os.path.join(out, "out.e2k"), "--levels", LEVELS,
                        "--levels-unit", "ft", "--no-storey-rise"], capture_output=True, text=True)
    open(os.path.join(out, "run.log"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    print(arm, "exit", r.returncode, "walls seen", n)
