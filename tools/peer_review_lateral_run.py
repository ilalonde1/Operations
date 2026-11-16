#!/usr/bin/env python3
r"""JM 01819 lateral run on a final gravity model, end to end, ONE command (row 1982 JM-V9-PREP, 13 Nov 2026).

WHY THIS EXISTS
    r30 (final22) was run by hand from eight scratchpad commands (R30-PIPELINE.md, fill_v9.py, dmass.py, cross.py,
    send30.ps1). When final23's ETABS r31 lands, the lateral run must be one command that nothing new breaks.
    Decisions it carries (records #133656, #133653): buildings cut at L1, not P1/GF (r27 proved W + E = TOTAL to 0.00 %;
    at P1/GF three podium walls cross x = 61,000); seismic weight per building re-read from the run's own output
    (rigid-diaphragm masses in reactions.OUT); Building A's static V at its own modal period when below 2Ta.

STEPS (--steps, default all; each prints what it read and refuses on a fault)
    build   takeoff e2k-lateral <reactions.e2k> basis.txt -> <review>/lateral/<prefix> vN.e2k (+ report). Refuses first
            when the gravity e2k has no SDL load pattern (final22 itself had none). Then REFUSES unless (JM-R37) `takeoff
            lateral-diff` finds every difference from the gravity model under a named allowance AND `takeoff model-audit` on the
            lateral copy has no unstated drawing mismatch (D1 doors on every storey, D2 no overlapping wall panels, ...).
    cross   the walls of the storey above the cut storey (L2 for a cut at L1) whose plan extent crosses split x: must be 0.
            Also prints the cut storey's own count (information: a P1/GF cut would need that one at 0).
    send    send-set.ps1 on KOR-210 - ONLY with --send (default: print the command, -DryRun only with --dry-send).
    fetch   fetch-set.ps1 -Tag <tag> (needs --send or --fetch: it talks to KOR-210)
    copy    work/_runner/results/<tag>/out/etabs/out/*.csv + console.log, lateral.LOG, summary.txt, done.txt
            -> <review>/lateral/<run>  (as run-r27)
    weights rigid-diaphragm masses from the run's reactions.OUT (--out-file, default the fetched one) -> W per building
            (diaphragms above the cut storey split at x; the shared L1 plate (DPOD) shared out by --l1-share-a, REQUIRED:
            Building A's share of the L1 plate area, lateral/independent/LATERAL-PREP-r32.md item 5 - JM-LAT-PREP 14 Nov; the old default,
            the yardsticks' share read off r27, was our model against itself, REF-AUDIT item 10). Each building's W is then
            held to 5 % of the INDEPENDENT hand weight (--hand-weight, independent/hand-weight.csv: drawing areas, WHM's
            walls and columns, S111 loads, VBBL 4.1.8.2), not the yardsticks' model W (REF-AUDIT items 6 and 10)
    check   takeoff lateral-check twice: pass 1 with V scaled to this W, reads Building A's modal T; pass 2 with
            V_A at min(T_A, 2Ta) (S(T)Mv interpolated as the yardsticks do; self-tested against them). The run's basis
            is written to <run>/check-basis.txt; the job's check.txt is never edited.
    fill    v9-numbers.json lateral keys from <run>/check (dry unless --write).

WHAT IT CHECKS / DOES NOT
    checks:  SDL pattern present; 0 walls across the split above the cut; W per building > 0 and EACH building's W and
             the total within 5 % of the independent hand weight (else REFUSED - read the mass source before any V is set);
             the V(T) interpolation reproduces the yardsticks' V/W at Ta and 2Ta to 0.5 %; every v9 key the fill sets
             exists (W and V per building are set only where v9-numbers.json has the key, else printed to type by hand).
             V itself = the yardsticks' code V/W (VBBL 4.1.8.11, INDEPENDENT: code formulas) x this run's W.
    does NOT check: that the hand weight itself is right (it is the independent side: its own method and limits are in
             lateral/independent/HAND-WEIGHT.md); a mass error inside 5 % of W (e.g. 0.5 kPa on a roof where 25 % snow
             belongs); that the cut plane is right for final26 beyond the wall crossing (columns and beams are not tested -
             same-class fault it would miss: a transfer beam spanning x = 61,000 at L2); Building B's V when its modal T
             is under 2Ta (REFUSED: done by hand); drift V (vdrift_*) is the yardsticks' scaled by W only.
    JM-V9-3B (16 Nov 2026, V9-09 / V9-13): beside that published drift V the check step writes a SECOND reading, V for
             deflection at each building's own modal period (not above 4.0 s), as vdrift_own_* / period_own_* / period_drift_*
             in check-basis.txt; lateral-check writes it and the pad bearing with the gravity moment to their own files, and
             the published files stay byte for byte as before (proved on r41: 8 of 8). Which reading governs is JM's question.

USAGE (from anywhere; paths absolute or relative to KOR.Drafter)
    python -I peer_review_lateral_run.py --reactions <r31 reactions.e2k> --version v6 --tag r32-lateral-v6-final23
        --set <r31 gravity set name> --run run-r32 --date "14 Nov 2026" --l1-share-a <LATERAL-PREP-r32.md item 5> [--send] [--write]
    the exact r32 command: scratchpad latprep/RUN.md of JM-LAT-PREP (14 Nov), copied to lateral/independent/RUN-r32.md
    offline test on r29/r30/r27 files: see the commit that added this file.
"""
from __future__ import annotations

import argparse
import collections
import csv
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

KD = Path(r"C:\VIsual Studio Projects\KOR.Drafter")
PREFIX = "01819-01 Renfrew - ETABS lateral"
CUTS = ("BASE-ALL=0..110000,30000..90000@P1/GF;GF-W=0..61000,30000..90000@P1/GF;GF-E=61000..110000,30000..90000@P1/GF;"
        "L1-W=0..61000,30000..90000@L1;L1-E=61000..110000,30000..90000@L1;L1-TOTAL=0..110000,30000..90000@L1;"
        "CORE-GF=74500..85100,49700..59700@P1/GF@79821,54773;PAD-A=74500..85100,49700..59700@P3@77991,52893;"
        "PAD-B=74500..85100,49700..59700@P3@77991,55618")
LATERAL_CASES = "Modal,RSX,RSY,EQX+E,EQX-E,EQY+E,EQY-E,WX-PLACEHOLDER,WY-PLACEHOLDER,KOR-Dead,SDL,LIVE"
G_LB_MM = 9806.65 * 0.00444822      # model LB / MM: mass lb-s2/mm x g (mm/s2) = lb -> kN


def die(msg: str) -> None:
    print(f"REFUSED: {msg}")
    sys.exit(2)


def step(name: str) -> None:
    print(f"\n== {name}")


# ---------------------------------------------------------------- e2k reading (cross.py, gaps.py)
def e2k_walls(path: Path):
    pts, areas, assign, stories = {}, {}, collections.defaultdict(list), []
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r'\s+STORY "([^"]+)"', line)
        if m and " HEIGHT " in line:
            stories.append(m[1]); continue
        m = re.match(r'\s+POINT "([^"]+)"\s+([-\d.E+]+)\s+([-\d.E+]+)', line)
        if m:
            pts[m[1]] = (float(m[2]), float(m[3])); continue
        m = re.match(r'\s+AREA "([^"]+)"\s+PANEL\s+(\d+)\s+(.*)', line)
        if m:
            areas[m[1]] = re.findall(r'"([^"]+)"', m[3]); continue
        m = re.match(r'\s+AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"', line)
        if m:
            assign[m[2]].append(m[1])
    return pts, areas, assign, stories          # stories top-down, as the e2k lists them


def crossing(path: Path, storey: str, x: float):
    pts, areas, assign, _ = e2k_walls(path)
    hits, n = [], 0
    for a in assign[storey]:
        if a not in areas:
            continue
        n += 1
        xs = [pts[p][0] for p in areas[a]]
        if min(xs) < x < max(xs):
            hits.append(f"{a} x {min(xs) / 1000:.1f}-{max(xs) / 1000:.1f} m")
    return n, hits


# ---------------------------------------------------------------- diaphragm masses (dmass.py)
def diaphragms(out_file: Path):
    rows, lines = [], open(out_file, encoding="latin-1").read().splitlines()
    i = 0
    while i < len(lines):
        m = re.match(r"\s*(\S+), CONSTR = RIGID", lines[i])
        if m:
            j = i
            while "TRANSLATIONAL MASS" not in lines[j]:
                j += 1
            u1 = float(lines[j + 2].split()[0])
            while "CENTER OF MASS" not in lines[j]:
                j += 1
            x, y, z = (float(lines[j + k].split()[1]) for k in (2, 3, 4))
            rows.append((m[1], u1 * G_LB_MM, x, y, z))
            i = j
        i += 1
    return rows


def storey_elev(e2k: Path, name: str) -> float:
    """Elevation (mm) of a storey's top: BASE elevation + the heights of every storey up to it (e2k lists top-down)."""
    base, hs = None, []
    for line in open(e2k, encoding="utf-8", errors="replace"):
        m = re.match(r'\s+STORY "([^"]+)"\s+(HEIGHT|ELEV)\s+([-\d.E+]+)', line)
        if m:
            if m[2] == "ELEV":
                base = float(m[3])
            else:
                hs.append((m[1], float(m[3])))
    if base is None:
        die(f"{e2k}: no BASE ELEV line")
    names = [n for n, _ in hs]
    if name not in names:
        die(f"{e2k}: no storey {name}")
    return base + sum(h for _, h in hs[names.index(name):])


def weights(out_file: Path, z_cut: float, split_x: float):
    r = diaphragms(out_file)
    if not r:
        die(f"{out_file}: 0 rigid diaphragms (a gravity-only run has none: read the LATERAL run's reactions.OUT)")
    above = [t for t in r if t[4] > z_cut + 1]
    at = [t for t in r if abs(t[4] - z_cut) <= 1]
    a = sum(w for _, w, x, *_ in above if x < split_x)
    b = sum(w for _, w, x, *_ in above if x > split_x)
    below = sum(t[1] for t in r if t[4] < z_cut - 1)
    return a, b, at, below, sum(t[1] for t in r), len(r)


# ---------------------------------------------------------------- V(T) as the yardsticks compute it
def yard(review: Path):
    y = collections.defaultdict(dict)
    for r in csv.DictReader(open(review / "lateral/yardsticks/yardsticks.csv", encoding="utf-8")):
        y[(r["building"], r["direction"][0])][r["item"]] = float(r["value"]) if re.fullmatch(r"[-\d.]+", r["value"]) else r["value"]
    return y


def hand_weight(path: Path, storeys_top_down: list, cut: str) -> dict:
    """Independent hand W per building (A, B) for the cut storey and every storey above it, from hand-weight.csv
    (columns storey, building, W_kN). Refuses on a storey the e2k does not have, or no row at or above the cut."""
    if not path.is_file():
        die(f"no independent hand weight {path} (JM-LAT-PREP: drawing areas, WHM's walls/columns, S111 loads)")
    above = set(storeys_top_down[:storeys_top_down.index(cut) + 1])
    w, n = {"A": 0.0, "B": 0.0}, 0
    for r in csv.DictReader(ln for ln in open(path, encoding="utf-8-sig") if not ln.startswith("#")):
        if r["storey"] not in storeys_top_down:
            die(f"{path.name}: storey '{r['storey']}' is not in the e2k")
        if r["storey"] in above and r["building"] in w:
            w[r["building"]] += float(r["W_kN"]); n += 1
    if n == 0 or min(w.values()) <= 0:
        die(f"{path.name}: no W rows at or above {cut} for both buildings")
    return w


def basis(path: Path) -> dict:
    d = {}
    for ln in open(path, encoding="utf-8"):
        ln = ln.split("#", 1)[0].strip()
        if "=" in ln:
            k, v = (s.strip() for s in ln.split("=", 1))
            d[k] = v
    return d


def s_mv(T: float, sa: dict, mv2: float) -> float:
    """S(T)Mv for ductile walls with Mv 1.0 at 0.5 and 1.0 s and mv2 at 2.0 s, linear between (Table 4.1.8.11 Note 2).
    Valid 0.5 <= T <= 2.0 s; Building A's Ta (0.60) and 2Ta (1.20) lie inside."""
    pts = [(0.5, sa["sa0.5"]), (1.0, sa["sa1.0"]), (2.0, sa["sa2.0"] * mv2)]
    if not 0.5 <= T <= 2.0:
        die(f"S(T)Mv asked at T = {T:.3f} s, outside 0.5-2.0 s: compute it by hand")
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t0 <= T <= t1:
            return v0 + (T - t0) / (t1 - t0) * (v1 - v0)
    raise AssertionError


def a_ratio(T: float, ya: dict, sa: dict, rdro: float, ie: float) -> tuple[float, float]:
    """Building A's V/W at period T, bounded as the yardsticks bound it; returns (V/W, mv2 used)."""
    mv2_at_2ta = ya["Mv at 2Ta"]
    # back-solve Mv(2.0) from the yardsticks' Mv at 2Ta: S(2Ta)Mv(2Ta) = interpolated S(T)Mv at 2Ta
    t2 = 2 * TA_A
    s2ta = sa["sa1.0"] + (t2 - 1.0) * (sa["sa2.0"] - sa["sa1.0"])
    mv2 = (s2ta * mv2_at_2ta - sa["sa1.0"]) / (t2 - 1.0) / sa["sa2.0"] + sa["sa1.0"] / sa["sa2.0"]
    v = s_mv(T, sa, mv2) * ie / rdro
    lo, hi = ya["V/W lower limit (walls)"], ya.get("V/W upper limit", 9)
    return min(max(v, lo), hi), mv2


TA_A = None   # set from the yardsticks (Ta of Building A)


def tower_ratio(T: float, sa: dict, mv2: float, mv5: float, rdro: float, ie: float, lo: float, hi: float) -> float:
    """The tower's V/W at period T (2.0 <= T <= 5.0 s): S(T)Mv linear between (2.0, S(2.0) Mv(2.0)) and (5.0, S(5.0) Mv(5.0)),
    Table 4.1.8.11 Note (2) as the yardsticks apply it (Mv(2.0), Mv(5.0) at S(0.2)/S(5.0) = 10.6: LATERAL-BASIS.md section on Mv),
    bounded by the yardsticks' lower and upper limits. JM-V9-3B (16 Nov 2026, V9-09): for the second drift reading only; the
    check step self-tests it against the yardsticks' V/W at 2Ta and their lower limit (= V/W at 4.0 s) before using it."""
    if not 2.0 <= T <= 5.0:
        die(f"tower S(T)Mv asked at T = {T:.3f} s, outside 2.0-5.0 s: compute it by hand")
    p0, p1 = sa["sa2.0"] * mv2, sa["sa5.0"] * mv5
    v = (p0 + (T - 2.0) / 3.0 * (p1 - p0)) * ie / rdro
    return min(max(v, lo), hi)


DRIFT_T_CAP = 4.0   # s: VBBL 4.1.8.11.(3)(d)(v) - the computed period used for deflections, not above 4.0 s (walls); yardsticks.csv row 7


# ---------------------------------------------------------------- the v9 fill (fill_v9.py, its e2k name an argument)
def fill(review: Path, run: str, run_date: str, e2k_name: str, write: bool) -> None:
    rd = review / "lateral" / run
    ck = rd / "check"
    rows = lambda p: list(csv.DictReader(open(p, encoding="utf-8-sig")))
    npath = review / "v9-numbers.json"
    d = json.load(open(npath, encoding="utf-8"))
    K = d["keys"]
    set_ = {}

    def put(k, v):
        if k not in K:
            die(f"v9-numbers.json has no key {k}")
        set_[k] = v
    bmap = {"A": "A", "B (tower)": "B"}
    for r in rows(ck / "rsa-scaling.csv"):
        b, x = bmap[r["building"]], r["direction"]
        tgt = float(r["target_kN"]); vd = max(float(r["Vd_kN"]), tgt)
        put(f"R23:Ve_{b}_{x}", f"{float(r['Ve_kN']):,.0f}"); put(f"R23:target_{b}_{x}", f"{tgt:,.0f}")
        put(f"R23:Vd_{b}_{x}", f"{vd:,.0f}"); put(f"R23:scale_{b}_{x}", f"{float(r['scale_forces']):.2f}")
        if b == "A":
            put("R23:V_A", f"{float(r['V_kN']):,.0f}")
        if b == "B":
            put("R23:tower_scale_fraction", f"{float(r['fraction']):.1f}")   # "1.0 V" / "0.8 V" in the text
    txt = (ck / "lateral-check.txt").read_text(encoding="utf-8")
    ta = [float(t) for t in re.findall(r"^A [XY]: the mode .* T = ([\d.]+) s", txt, re.M)]
    put("R23:T_A_s", f"{max(ta):.2f}")
    best = {}
    for r in rows(ck / "drifts-scaled.csv"):
        k = (bmap[r["building"]], r["direction"])
        if k not in best or float(r["realistic_drift"]) > float(best[k]["realistic_drift"]):
            best[k] = r
    for (b, x), r in best.items():
        put(f"R23:drift_{b}_{x}", f"{float(r['realistic_drift']) * 100:.2f}")
        put(f"R23:drift_{b}_{x}_storey", r["story"])
        put(f"R23:drift_{b}_{x}_ratio", f"{float(r['realistic_drift']) / float(r['limit']):.2f}")
    cb_all = rows(ck / "coupling-beams-dc.csv")
    cap = rows(review / "lateral/capacity/1b-coupling-beams-by-storey.csv")
    # JM-R34 (row 1982, 14 Nov): an ETABS storey's header is the capacity row whose header_under_level is that storey (ETABS
    # names a storey by its top; the capacity table by the opening's floor): P1/GF <-> 'GF/P1' (opening P2), P2 <-> 'P2/FDN'.
    # The old lookup matched opening_storey == storey: one storey off (r32c: H1_CSW4 at P1/GF found no row; elsewhere it
    # took the header ABOVE's Vp). A header deeper than 2.0 lu (h_le_2lu EXCEEDS: CSW1 H3 and CSW4 H1 over the 3'-4 doors under
    # GF/P1, F35) is a deep beam, not a coupling beam of cl. 21.5.8.2: its diagonal-cage Vr does not apply, so it is left out
    # of the worst / count / table keys and printed (the text says so).
    lvl = {"GF/P1": "P1/GF", "P2/FDN": "P2"}
    def cap_rows(wall, h, storey):
        return [c for c in cap if c["wall"] == wall and c["header"] == h and lvl.get(c["header_under_level"], c["header_under_level"]) == storey]
    def deep(r):
        wall, h = r["wall_header"].split("/")
        return any(c["h_le_2lu (cl.21.5.8.2.1a)"].startswith("EXCEEDS") for c in cap_rows(wall, h, r["storey"]))
    cb = [r for r in cb_all if r["Vf_over_Vr"] and not deep(r)]
    for r in cb_all:
        if r["Vf_over_Vr"] and deep(r):
            print(f"  left out (deeper than 2.0 lu, F35): {r['wall_header']} {r['storey']} {r['case']} Vf {r['Vf_kN']} / Vr {r['Vr_kN']} = {r['Vf_over_Vr']}")
    print(f"  coupling-beam rows with a ratio, deep headers left out: {len(cb)} of {len(cb_all)}")
    w = max(cb, key=lambda r: float(r["Vf_over_Vr"]))
    put("R23:CB_worst_header", w["wall_header"]); put("R23:CB_worst_storey", w["storey"])
    put("R23:CB_worst_Vf_kN", f"{float(w['Vf_kN']):,.0f}"); put("R23:CB_worst_Vr_kN", f"{float(w['Vr_kN']):,.0f}")
    put("R23:CB_worst_ratio", f"{float(w['Vf_over_Vr']):.2f}")
    put("R23:CB_count_over", str(len({(r["wall_header"], r["storey"]) for r in cb if float(r["Vf_over_Vr"]) > 1.0})))
    for wh in ["CSW4/H1", "CSW5/H1", "CSW4/H2", "CSW5/H2", "CSW1/H3"]:
        wall, h = wh.split("/")
        sel = [r for r in cb if r["wall_header"] == wh]
        if not sel:
            print(f"  {wh}: no coupling-beam row with a ratio - its keys NOT SET"); continue
        m = max(sel, key=lambda r: float(r["Vf_over_Vr"]))
        k = f"R23:{h}_{wall}"
        put(k + "_Vf", f"{float(m['Vf_kN']):,.0f}"); put(k + "_Vr", f"{float(m['Vr_kN']):,.0f}")
        put(k + "_ratio", f"{float(m['Vf_over_Vr']):.2f}"); put(k + "_storey", m["storey"])
        c = cap_rows(wall, h, m["storey"])
        if len(c) == 1 and c[0]["Vp_kN"]:
            put(k + "_Vp", f"{float(c[0]['Vp_kN']):,.0f}"); put(k + "_bars", c[0]["bars_each_cage"])
        else:
            print(f"  {k}_Vp/_bars NOT SET: {len(c)} capacity row(s) for {wall} {h} {m['storey']}")
    cw = [r for r in rows(ck / "core-walls-dc.csv") if r["Vf_over_Vr"]]
    m = max(cw, key=lambda r: float(r["Vf_over_Vr"]))
    put("R23:wall_worst", f"{m['wall']} {m['storey']} ({m['pier']}, {m['case']})")
    put("R23:wall_Vf_kN", f"{float(m['Vf_kN']):,.0f}"); put("R23:wall_Vr_kN", f"{float(m['Vr_kN']):,.0f}")
    pads = rows(ck / "core-pad-bearing.csv")
    put("R23:pad_q_short_kPa", f"{max(float(r['q_max_kPa']) for r in pads if r['pad'] == 'PAD-A'):,.0f}")
    put("R23:pad_q_long_kPa", f"{max(float(r['q_max_kPa']) for r in pads if r['pad'] == 'PAD-B'):,.0f}")
    # W and V per building (JM-LAT-PREP 14 Nov): section 6 printed the yardsticks' model W and V at 2Ta as fixed text;
    # set here only where v9-numbers.json has the key, else printed for the hand fill (REFILL-KEYS.md)
    cb_ = (rd / "check-basis.txt").read_text(encoding="utf-8")
    mw = re.search(r"^# W_kN A = (\d+), B = (\d+)", cb_, re.M)
    vv = dict(re.findall(r"^(v_[ew]_[xy])\s*=\s*([\d.]+)", cb_, re.M))
    soft = {"R23:W_A": mw and f"{int(mw[1]):,}", "R23:W_B": mw and f"{int(mw[2]):,}"}
    for k, b, x in (("v_e_x", "B", "X"), ("v_e_y", "B", "Y"), ("v_w_x", "A", "X"), ("v_w_y", "A", "Y")):
        soft[f"R23:V_{b}_{x}"] = f"{float(vv[k]):,.0f}" if k in vv else None
    for k, v in soft.items():
        if v is None:
            print(f"  {k}: no value in check-basis.txt")
        elif k in K:
            put(k, v)
        else:
            print(f"  {k} = {v}  (NOT A KEY of v9-numbers.json: type it by hand where REFILL-KEYS.md says)")
    # JM-V9-3B (row 1982, 16 Nov 2026; V9-09, V9-13): the SECOND readings beside the published ones - set only where
    # v9-numbers.json has the key (the text owner adds them), else printed. Published keys above are unchanged.
    for r in (rows(ck / "rsa-scaling-own-period.csv") if (ck / "rsa-scaling-own-period.csv").is_file() else []):
        b, x = bmap[r["building"]], r["direction"]
        soft[f"R23:T_own_{b}_{x}_s"] = f"{float(r['T_own_s']):.2f}"; soft[f"R23:T_drift_{b}_{x}_s"] = f"{float(r['T_used_s']):.2f}"
        soft[f"R23:V_drift_own_{b}_{x}"] = f"{float(r['V_drift_own_kN']):,.0f}"; soft[f"R23:scale_drifts_own_{b}_{x}"] = f"{float(r['scale_drifts_own']):.3f}"
        soft[f"R23:V_drift_{b}_{x}"] = f"{float(r['V_drift_published_kN']):,.0f}"; soft[f"R23:scale_drifts_{b}_{x}"] = f"{float(r['scale_drifts_published']):.3f}"
    best_o = {}
    for r in (rows(ck / "drifts-scaled-own-period.csv") if (ck / "drifts-scaled-own-period.csv").is_file() else []):
        k_ = (bmap[r["building"]], r["direction"])
        if k_ not in best_o or float(r["realistic_drift"]) > float(best_o[k_]["realistic_drift"]):
            best_o[k_] = r
    for (b, x), r in best_o.items():
        soft[f"R23:drift_own_{b}_{x}"] = f"{float(r['realistic_drift']) * 100:.2f}"
        soft[f"R23:drift_own_{b}_{x}_storey"] = r["story"]
        soft[f"R23:drift_own_{b}_{x}_ratio"] = f"{float(r['realistic_drift']) / float(r['limit']):.2f}"
    gmp = ck / "core-pad-bearing-gravity-moment.csv"
    for pad, nm in (("PAD-A", "short"), ("PAD-B", "long")):
        sel = [r for r in (rows(gmp) if gmp.is_file() else []) if r["pad"] == pad]
        if sel:
            w_ = max(sel, key=lambda r: float(r["q_max_kPa"]) if r["q_max_kPa"] else float("inf"))
            soft[f"R23:pad_q_{nm}_gm_kPa"] = f"{float(w_['q_max_kPa']):,.0f}" if w_["q_max_kPa"] else "no bearing solution (e >= L/2)"
            soft[f"R23:pad_M_gravity_{nm}_kNm"] = f"{float(w_['M_gravity_kNm']):,.0f}"
            # JM-V9-4 (row 1982, 16 Nov): the letter's Q7 quotes the seismic moment beside the gravity one, from the same row.
            soft[f"R23:pad_M_seismic_{nm}_kNm"] = f"{float(w_['M_seismic_kNm']):,.0f}"
            soft[f"R23:pad_e_{nm}_gm_m"] = f"{float(w_['e_m']):.2f}"
            soft[f"R23:pad_dir_{nm}_gm"] = w_["direction"]
    for k, v in soft.items():
        if k in set_ or v is None:
            continue
        if k in K:
            put(k, v)
        elif k.startswith(("R23:T_own", "R23:T_drift", "R23:V_drift", "R23:scale_drifts", "R23:drift_own", "R23:pad_")):
            print(f"  {k} = {v}  (second reading; NOT A KEY of v9-numbers.json yet)")
    put("R23:run_date", run_date)
    put("R23:modal_type", "Ritz")
    put("R23:base_condition", "pinned at P3; every joint of P2 and P1/GF held UX UY; base of the structure P1/GF")
    left = sorted(k for k, e in K.items() if k.startswith("R23:") and e.get("value") in (None, "") and k not in set_)
    print(f"  {len(set_)} keys set; R23 keys still without a value after this fill: {len(left)}"
          + (f" ({', '.join(left)})" if left else ""))
    if not write:
        for k, v in sorted(set_.items()):
            print(f"  {k} = {v}")
        print("  (dry: --write to write v9-numbers.json)")
        return
    for k, v in set_.items():
        K[k]["value"] = v
    d["lateral_run_dir"] = f"lateral/{run}"
    ship = d["lateral_ship"]
    sv = rd / "ETABS lateral save.e2k"
    ship["03 Models/ETABS"] = [f"lateral/{run}/{sv.name}" if sv.is_file() else f"lateral/{e2k_name}"]
    ship["04 Results/ETABS lateral"] = [re.sub(r"lateral/run-[^/]+/", f"lateral/{run}/", p) for p in ship["04 Results/ETABS lateral"]]
    K["ISSUE:lateral_e2k_name"]["value"] = e2k_name
    d["status"] = f"LATERAL FILLED from {run} ({run_date}); still TO FILL: hand keys left null and ISSUE / AUDIT-after values"
    with open(npath, "w", encoding="utf-8", newline="\n") as f:
        json.dump(d, f, ensure_ascii=False, indent=1); f.write("\n")
    print(f"  written {npath}")


# ---------------------------------------------------------------- main
def main() -> int:
    global TA_A
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--review", type=Path, default=KD / "jobs/01819-01-renfrew/review", help="the job's review folder (a scratch copy for a test)")
    ap.add_argument("--reactions", type=Path, help="the gravity run's analysed model (work/.../etabsNN/reactions.e2k)")
    ap.add_argument("--version", help="lateral e2k version, e.g. v6")
    ap.add_argument("--e2k", type=Path, help="an existing lateral e2k (skips build; default <review>/lateral/<prefix> <version>.e2k)")
    ap.add_argument("--tag", help="runner set tag, e.g. r32-lateral-v6-final23")
    ap.add_argument("--set", dest="gset", help="the gravity set whose uses.csv / transfers.csv the lateral run takes")
    ap.add_argument("--run", help="run folder name under <review>/lateral, e.g. run-r32")
    ap.add_argument("--date", help="run date for the letter, 'dd Mon yyyy'")
    ap.add_argument("--steps", default="build,cross,send,fetch,copy,weights,check,fill")
    ap.add_argument("--split-x", type=float, default=61000)
    ap.add_argument("--cut-storey", default="L1")
    ap.add_argument("--out-file", type=Path, help="reactions.OUT with the rigid-diaphragm masses (default the fetched run's)")
    ap.add_argument("--l1-share-a", type=float, help="Building A's share of the cut storey's shared plate: REQUIRED for weights "
                    "(the L1 plate area west of split x over the whole, lateral/independent/LATERAL-PREP-r32.md item 5)")
    ap.add_argument("--hand-weight", type=Path, help="independent hand W per storey and building (default <review>/lateral/independent/hand-weight.csv)")
    ap.add_argument("--takeoff", type=Path, default=KD / "Kor.Operations.EngineeringTools.TakeoffCli/bin/Debug/net8.0/takeoff.exe")
    ap.add_argument("--send", action="store_true", help="really send to KOR-210 and fetch (default: print the command)")
    ap.add_argument("--dry-send", action="store_true", help="send-set.ps1 -DryRun only (stages locally, sends nothing)")
    ap.add_argument("--write", action="store_true", help="write v9-numbers.json (default: dry)")
    a = ap.parse_args()
    steps = a.steps.split(",")
    review = a.review.resolve()
    lat = review / "lateral"
    e2k = a.e2k or (lat / f"{PREFIX} {a.version}.e2k" if a.version else None)
    run_dir = lat / a.run if a.run else None
    results = KD / "work/_runner/results" / a.tag if a.tag else None

    if "build" in steps:
        step("build: takeoff e2k-lateral")
        if not (a.reactions and a.version):
            die("build needs --reactions and --version")
        if not re.search(r'LOADPATTERN "SDL"', a.reactions.read_text(encoding="utf-8", errors="replace")):
            die(f"{a.reactions} has no SDL load pattern (e2k-lateral needs one; final22 itself had none)")
        if e2k.exists():
            die(f"{e2k} exists: a lateral copy is written once (new --version)")
        rep = lat / f"e2k-lateral-report-{a.version}.txt"
        r = subprocess.run([str(a.takeoff), "e2k-lateral", str(a.reactions), str(lat / "basis.txt"), str(e2k), "--report", str(rep)],
                           capture_output=True, text=True)
        print(r.stdout[-1500:], r.stderr[-800:])
        if r.returncode != 0 or not e2k.is_file():
            die(f"e2k-lateral exit {r.returncode}")
        print(f"  {e2k.name} written ({e2k.stat().st_size:,} bytes); report {rep.name}")
        # JM-R37 (14 Nov 2026, framework-ca): anything the build adds or changes is checked against WHM's drawings or allowed by
        # name, before the copy goes anywhere. lateral v8 carried a copied header (KS201 @L10) over a solid wall and nothing saw it.
        #   (1) takeoff lateral-diff: the lateral copy differs from the gravity model only by the named allowances, else exit 1;
        #   (2) takeoff model-audit on the LATERAL copy: every drawing check (D1 doors on every storey, D2 no overlapping panels, ...)
        #       with no unstated mismatch, else exit 1.
        job = review.parent
        diff_rep = lat / f"lateral-diff-{a.version}.txt"
        r = subprocess.run([str(a.takeoff), "lateral-diff", str(a.reactions), str(e2k), "--basis", str(lat / "basis.txt"), "--report", str(diff_rep)],
                           capture_output=True, text=True)
        print(r.stdout[-1500:], r.stderr[-500:])
        if r.returncode != 0:
            die(f"lateral-diff exit {r.returncode}: the lateral copy changes something no allowance names ({diff_rep.name}) - REFUSED")
        aud_rep = lat / f"model-audit-{a.version}.txt"
        r = subprocess.run([str(a.takeoff), "model-audit", str(e2k), str(job / "answer-key.csv"), str(job / "model-audit"),
                            "--uses", str(job / "model-audit" / "uses-final26.csv"), "--report", str(aud_rep)], capture_output=True, text=True)
        tail = [l for l in aud_rep.read_text(encoding="utf-8", errors="replace").splitlines() if l.startswith("MISMATCHES")] if aud_rep.is_file() else []
        print(f"  model-audit on {e2k.name}: exit {r.returncode}; {tail}")
        if r.returncode != 0:
            die(f"model-audit exit {r.returncode} on the lateral copy: an unstated drawing mismatch ({aud_rep.name}) - REFUSED")

    if "cross" in steps:
        step(f"cross: walls across x = {a.split_x:,.0f} at the cut")
        if not e2k or not e2k.is_file():
            die("cross needs the lateral e2k (--version or --e2k)")
        stories = e2k_walls(e2k)[3]
        if a.cut_storey not in stories or stories.index(a.cut_storey) == 0:
            die(f"no storey above {a.cut_storey} in {e2k.name}")
        above = stories[stories.index(a.cut_storey) - 1]
        n0, h0 = crossing(e2k, a.cut_storey, a.split_x)
        n1, h1 = crossing(e2k, above, a.split_x)
        print(f"  storey {a.cut_storey} (below the cut, information): {len(h0)} of {n0} wall panels cross {h0}")
        print(f"  storey {above} (the cut plane at {a.cut_storey} + 0.1 m passes through it): {len(h1)} of {n1} wall panels cross {h1}")
        if h1:
            die(f"{len(h1)} wall(s) of {above} cross x = {a.split_x:,.0f}: the per-building cuts at {a.cut_storey} cannot split the buildings")

    if "send" in steps:
        step("send: KOR-210 (send-set.ps1)")
        if not (a.tag and a.gset):
            die("send needs --tag and --set")
        sets = KD / "work/_runner/sets" / a.gset / "etabs"
        for f in ("uses.csv", "transfers.csv"):
            if not (sets / f).is_file():
                die(f"missing {sets / f}")
        args = ["pwsh", "-NoProfile", "-File", str(KD / "docs/one-tool/run-on-210/send-set.ps1"), "-Job", "01819-01", "-Tag", a.tag,
                "-Holder", "JM lateral (peer_review_lateral_run.py)", "-EtabsModel", str(e2k), "-Uses", str(sets / "uses.csv"),
                "-Transfers", str(sets / "transfers.csv"), "-SupportStorey", "P3", "-PinStorey", "P3", "-StandardSolver",
                "-EtabsTimeoutMin", "180", "-Lateral", LATERAL_CASES, "-SplitX", f"{a.split_x:.0f}", "-Cuts", CUTS]
        if a.send or a.dry_send:
            r = subprocess.run(args + (["-DryRun"] if a.dry_send else []))
            if r.returncode != 0:
                die(f"send-set exit {r.returncode}")
            if a.dry_send:
                print("  DRY RUN only: delete the local dry set folder it made before a real send (the real send refuses otherwise)")
                return 0
        else:
            print("  NOT SENT (no --send). The command:\n  " + " ".join(f'"{x}"' if " " in x or ";" in x else x for x in args))
            print("  Hang rule: no file under out\\etabs written for 15 min -> the runner says HUNG but does not end ETABS;"
                  " end it by pid (EtabsOpenCheck with the set name in its command line + its ETABS child) through netops --direct.")
            steps = [s for s in steps if s not in ("fetch",)]
            if "copy" in steps and not (results and results.is_dir()):
                print("  stopping: nothing fetched yet")
                return 0

    if "fetch" in steps:
        step("fetch: fetch-set.ps1")
        if not a.send:
            die("fetch talks to KOR-210: needs --send")
        r = subprocess.run(["pwsh", "-NoProfile", "-File", str(KD / "docs/one-tool/run-on-210/fetch-set.ps1"), "-Tag", a.tag])
        if r.returncode != 0:
            die(f"fetch-set exit {r.returncode}")

    if "copy" in steps:
        step(f"copy: {results} -> {run_dir}")
        src = results / "out/etabs/out" if results else None
        if not src or not src.is_dir():
            die(f"no fetched run at {src}")
        if run_dir.exists():
            die(f"{run_dir} exists")
        run_dir.mkdir(parents=True)
        n = 0
        for f in list(src.glob("*.csv")) + [src / "lateral.LOG", results / "out/etabs/console.log",
                                             results / "out/summary.txt", results / "done.txt"]:
            if f.is_file():
                shutil.copy2(f, run_dir / f.name); n += 1
            else:
                print(f"  NOTE: {f} not there")
        print(f"  {n} files copied")
        save = src / "reactions.$et"          # ETABS's own save of the analysed lateral model (the package ships it, 1.6)
        if save.is_file():
            shutil.copy2(save, run_dir / "ETABS lateral save.e2k")
            print(f"  ETABS's save {save.name} -> {run_dir.name}/ETABS lateral save.e2k")
        else:
            print(f"  NOTE: no ETABS save {save}")

    W = None
    if "weights" in steps or "check" in steps:
        step("weights: seismic weight per building off the run's rigid-diaphragm masses")
        out_file = a.out_file or (results / "out/etabs/out/reactions.OUT" if results else None)
        if not out_file or not out_file.is_file():
            die(f"no reactions.OUT ({out_file})")
        y = yard(review)
        z_cut = storey_elev(e2k, a.cut_storey)
        A, B, at, below, tot, n = weights(out_file, z_cut, a.split_x)
        print(f"  {out_file}: {n} rigid diaphragms, {tot:,.0f} kN; above {a.cut_storey} (z {z_cut / 1000:.3f} m): "
              f"A (x < {a.split_x:,.0f}) {A:,.0f} kN, B {B:,.0f} kN; at {a.cut_storey}: "
              + ", ".join(f"{nm} {w:,.0f} kN at x {x / 1000:.1f} m" for nm, w, x, *_ in at) + f"; below: {below:,.0f} kN")
        share = a.l1_share_a
        if share is None or not 0 < share < 1:
            die(f"--l1-share-a is required (0..1): Building A's share of the {a.cut_storey} plate area "
                "(lateral/independent/LATERAL-PREP-r32.md item 5); the r27 model share is no longer a default (REF-AUDIT item 10)")
        print(f"  ASSUMPTION (for the engineer): Building A takes {share:.3f} of the {a.cut_storey} plate's mass "
              f"(as given: lateral/independent/LATERAL-PREP-r32.md item 5)")
        pl = sum(w for _, w, *_ in at)
        W = {"A": A + share * pl, "B": B + (1 - share) * pl}
        hw = hand_weight(a.hand_weight or lat / "independent/hand-weight.csv", e2k_walls(e2k)[3], a.cut_storey)
        bad = []
        for b in ("A", "B"):
            d = W[b] / hw[b] - 1
            print(f"  W {b}: run {W[b]:,.0f} kN vs independent hand weight {hw[b]:,.0f} kN ({d * 100:+.1f} %)")
            bad += [b] if abs(d) > 0.05 else []
        dt = sum(W.values()) / sum(hw.values()) - 1
        print(f"  W total: run {sum(W.values()):,.0f} kN vs hand {sum(hw.values()):,.0f} kN ({dt * 100:+.1f} %)")
        if min(W.values()) <= 0 or bad or abs(dt) > 0.05:
            die(f"W off the independent hand weight by more than 5 % ({', '.join(bad) or 'total'}): read the mass source "
                "and the hand weight before any V is set")

    if "check" in steps:
        step("check: takeoff lateral-check (V on this W; Building A at its own period below 2Ta)")
        if not run_dir or not run_dir.is_dir():
            die(f"no run folder {run_dir}")
        y = yard(review)
        bas = basis(lat / "check.txt")
        sa = {k: float(bas[k]) for k in ("sa0.5", "sa1.0", "sa2.0")}
        ie = float(bas["ie"])
        TA_A = float(y[("A", "X")]["Ta empirical"])
        # self-test: reproduce the yardsticks' V/W for A at Ta and 2Ta
        for T, item in ((TA_A, "V/W at Ta"), (2 * TA_A, "V/W at 2Ta")):
            v, _ = a_ratio(T, y[("A", "X")], sa, float(bas["rdro_w_x"]), ie)
            ref = y[("A", "X")][item]
            ok = abs(v / ref - 1) < 0.005
            print(f"  self-test A {item}: {v:.5f} vs yardsticks {ref:.5f} {'ok' if ok else 'FAIL'}")
            if not ok:
                die("the V(T) interpolation does not reproduce the yardsticks")
        wy = {b: y[(b, "X")]["W (seismic weight above P1/GF)"] for b in ("A", "B")}
        k = {b: W[b] / wy[b] for b in ("A", "B")}
        base = {kk: float(bas[kk]) for kk in ("v_e_x", "v_e_y", "v_w_x", "v_w_y", "vdrift_e_x", "vdrift_e_y", "vdrift_w_x", "vdrift_w_y")}

        def write_basis(vals: dict, note: str, extra: list | None = None) -> Path:
            p = run_dir / "check-basis.txt"
            src = (lat / "check.txt").read_text(encoding="utf-8").splitlines()
            out = [f"# {run_dir.name}: written by peer_review_lateral_run.py from ../check.txt - {note}",
                   f"# W_kN A = {W['A']:.0f}, B = {W['B']:.0f} (this run's rigid-diaphragm masses; L1 share A {share:.3f})"]
            for ln in src:
                key = ln.split("=", 1)[0].strip()
                out.append(f"{key} = {vals[key]:.0f}" if key in vals and not ln.lstrip().startswith("#") else ln)
            p.write_text("\n".join(out + (extra or [])) + "\n", encoding="utf-8")
            return p

        def run_check(p: Path):
            ckd = run_dir / "check"
            r = subprocess.run([str(a.takeoff), "lateral-check", str(run_dir), str(e2k), str(p), str(lat / "capacity"), str(ckd)],
                               capture_output=True, text=True)
            if r.returncode != 0 or not (ckd / "lateral-check.txt").is_file():
                print(r.stdout[-2000:], r.stderr[-1000:])
                die(f"lateral-check exit {r.returncode}")
            return (ckd / "lateral-check.txt").read_text(encoding="utf-8")

        v1 = {kk: base[kk] * k["B" if "_e_" in kk else "A"] for kk in base}
        txt = run_check(write_basis(v1, f"pass 1: V and V-for-drift scaled by W (A x {k['A']:.4f}, B x {k['B']:.4f})"))
        tb = [float(t) for t in re.findall(r"^B \(tower\) [XY]: the mode .* T = ([\d.]+) s", txt, re.M)]
        ta = {d: float(t) for d, t in re.findall(r"^A ([XY]): the mode .* T = ([\d.]+) s", txt, re.M)}
        ta_b = float(y[("B", "X")]["Ta empirical"])
        print(f"  pass 1: A modal T {ta}; B modal T {tb} (2Ta B {2 * ta_b:.2f} s)")
        if not ta or len(tb) != 2:
            die("lateral-check named no Building A / B modes")
        if min(tb) < 2 * ta_b:
            die(f"Building B's modal T {min(tb):.2f} s is under 2Ta {2 * ta_b:.2f} s: its V at its own period is done by hand")
        v2 = dict(v1)
        for d in ("x", "y"):
            T = min(ta[d.upper()], 2 * TA_A)
            r_, mv2 = a_ratio(T, y[("A", d.upper())], sa, float(bas[f"rdro_w_{d}"]), ie)
            v2[f"v_w_{d}"] = r_ * W["A"]
            print(f"  A {d.upper()}: T {ta[d.upper()]:.2f} s -> V at {T:.2f} s = {r_:.5f} W = {v2[f'v_w_{d}']:,.0f} kN "
                  f"(Mv(2.0) {mv2:.4f} back-solved from the yardsticks; V at 2Ta on this W would be {v1[f'v_w_{d}']:,.0f})")
        # JM-V9-3B (row 1982, 16 Nov 2026, Codex V9-09; Ian #133818): the SECOND drift reading, beside the published one (which
        # stays vdrift_* above, the yardsticks' V at the 4.0 s floor scaled by W - not changed here). Reading (b) is the rule the
        # letter states: V for deflection at each building's OWN modal period (pass 1's), that period not above 4.0 s
        # (DRIFT_T_CAP). Building A by a_ratio (as pass 2), the tower by tower_ratio (self-tested below). lateral-check writes it to
        # rsa-scaling-own-period.csv, drifts-scaled-own-period.csv and lateral-check-second-readings.txt.
        tbd = {d: float(t) for d, t in re.findall(r"^B \(tower\) ([XY]): the mode .* T = ([\d.]+) s", txt, re.M)}
        if len(tbd) != 2 or len(ta) != 2:
            die(f"pass 1 named the modes of A {sorted(ta)} and the tower {sorted(tbd)}: both directions of each are needed for the second drift reading")
        for d in ("x", "y"):
            yd = y[("B", d.upper())]
            for T, ref in ((2 * float(yd["Ta empirical"]), yd["V/W at 2Ta"]), (DRIFT_T_CAP, yd["V/W lower limit (walls)"])):
                v = tower_ratio(T, sa | {"sa5.0": float(bas["sa5.0"])}, float(bas[f"mv_tower_2s_{d}"]), float(bas[f"mv_tower_5s_{d}"]),
                                float(bas[f"rdro_e_{d}"]), ie, 0.0, 9.0)
                ok = abs(v / ref - 1) < 0.005
                print(f"  self-test tower {d.upper()} V/W at {T:.2f} s: {v:.5f} vs yardsticks {ref:.5f} {'ok' if ok else 'FAIL'}")
                if not ok:
                    die("the tower's S(T)Mv interpolation does not reproduce the yardsticks")
        extra = ["# JM-V9-3B (V9-09): SECOND drift reading - V for deflection at the building's own modal period (pass 1), not above "
                 f"{DRIFT_T_CAP} s (VBBL 4.1.8.11.(3)(d)(v)); the published reading stays vdrift_* above"]
        for b, side, tmap in (("A", "w", ta), ("B", "e", tbd)):
            for d in ("x", "y"):
                T = tmap[d.upper()]; Tu = min(T, DRIFT_T_CAP); yd = y[(b, d.upper())]
                if b == "A":
                    r_, _ = a_ratio(Tu, yd, sa, float(bas[f"rdro_w_{d}"]), ie)
                else:
                    r_ = tower_ratio(Tu, sa | {"sa5.0": float(bas["sa5.0"])}, float(bas[f"mv_tower_2s_{d}"]), float(bas[f"mv_tower_5s_{d}"]),
                                     float(bas[f"rdro_e_{d}"]), ie, yd["V/W lower limit (walls)"], yd.get("V/W upper limit", 9))
                vo = r_ * W[b]
                extra += [f"period_own_{side}_{d} = {T:.2f}", f"period_drift_{side}_{d} = {Tu:.2f}", f"vdrift_own_{side}_{d} = {vo:.0f}"]
                print(f"  second drift reading {b} {d.upper()}: T {T:.2f} s -> used {Tu:.2f} s: V/W {r_:.5f} x W {W[b]:,.0f} = {vo:,.0f} kN "
                      f"(published V for deflection {v2[f'vdrift_{side}_{d}']:,.0f})")
        txt = run_check(write_basis(v2, "pass 2: Building A's V at its own modal period (below 2Ta)", extra))
        for ln in txt.splitlines():
            if ln.startswith(("GATE", "A ", "B (tower) ", "drift")):
                print("  " + ln[:200])
        sec = run_dir / "check" / "lateral-check-second-readings.txt"
        for ln in (sec.read_text(encoding="utf-8").splitlines() if sec.is_file() else ["NO second readings written"]):
            print("  " + ln[:220])

    if "fill" in steps:
        step("fill: v9-numbers.json lateral keys")
        if not (a.run and a.date and e2k):
            die("fill needs --run, --date and the lateral e2k")
        fill(review, a.run, a.date, e2k.name, a.write)
    return 0


if __name__ == "__main__":
    sys.exit(main())
