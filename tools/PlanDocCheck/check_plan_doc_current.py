r"""check_plan_doc_current.py -- READ-ONLY. Says STALE when a plan document is behind the pipeline it reports on.

Board: KorStandards oversight.Mistake 85 (28 Oct): the One Machine plan and the Jim findings doc fell hours behind
the Halifax pipeline overnight (the Revit model passed every gate at 01:15; the plan's next entry is 10:15) and
nothing noticed but Ian.

WHAT THE DOC'S TIME IS -- AND WHY IT IS A PROXY. A Claude Docs read gives the doc's revision number (rev) but NO
edit time: the doc read, a tab search and an outline all carry rev/h attributes and no timestamps (checked 28 Oct
10:3x on rev 49). The Artifact list's "updated" is the PUBLISH date, not the docs edit (it said 2026-10-26 for a
doc edited at 10:15 on 28 Oct). And a script cannot read a Claude Doc at all -- only a session's Docs tools can.
So the nearest honest proxy is the NEWEST DATED STAMP WRITTEN IN THE DOC'S OWN TEXT ("28 Oct, 10:15 -",
"DONE 27 Oct 05:42", "at 15:15, 27 Oct", "27 Oct night"). The session that writes the doc saves a Docs read of it
(a search for the month, e.g. "Oct", returns every stamped block) to a file and passes it as --doc-text, or passes
one stamp with --doc-stamp.
  A stamp with no clock time counts from the END of the period it names: a bare date from 23:59 that day,
  morning 12:00, afternoon 18:00, evening 21:00, night 23:59. So a stamp never claims more currency than it states.
  Stamps later than --now are ignored (a "due 29 Oct" is a plan, not an edit). The year is --now's year.

WHAT THE PIPELINE'S TIME IS. A MILESTONE = a run in run.json (KOR.Drafter tools/virtual_drafter.py writes one entry
per run) that FINISHED (has "finished") with at least one gate and every gate passed. Its time is "finished".

VERDICT: STALE when a milestone later than the doc's newest stamp has gone uncovered for more than --grace-min
(default 60) as at --now: the writer gets the grace from the OLDEST uncovered milestone.
Exit 0 = CURRENT, 1 = STALE, 2 = cannot judge (no stamp found, or run.json unreadable).

COVERS: every dated stamp in the text given (day + month name, with an optional HH:MM before or after it, or a
morning/afternoon/evening/night word after it); every run in run.json; the gap between the two.
DOES NOT COVER:
  - what the doc SAYS: a stamp on any item makes the whole doc current. An Edgar line stamped "27 Oct night" made the
    plan look current for Halifax too -- the check is doc-wide, not per project;
  - an edit with no stamp in it (it does not count), or an old stamp left in place on a rewritten line;
  - a FAILED stage that changed the state (a gate failing at 02:10 is news too) -- only passed, finished runs count;
  - a pipeline other than the run.json given (one --run-json per call; run it once per job);
  - reading the doc itself (see above) -- if nobody saves a fresh read, the check judges an old copy.
SAME-CLASS FAULT IT WOULD MISS: the doc updated on time with a stamp, but the update is WRONG or leaves out the
milestone (e.g. "10:15 - v3 not delivered" written while v3 had in fact been promoted): stamp fresh, content stale.

THE LEDGER (doc-owed.jsonl, beside run.json in the job's work folder) -- so nobody has to remember to look.
The pipeline appends one line per milestone; the doc's writer appends one line after each doc update. `owed` reads
only this file, no doc read, and says what the doc owes. One JSON object per line, UTF-8, local times without zone:
  {"kind":"milestone","at":"2026-10-28T01:15:11","run_started":"2026-10-28T01:05:22","gates":10,"what":"<short label>"}
      written by KOR.Drafter tools/virtual_drafter.py when a run finishes with >= 1 gate and every gate passed
      (the same test as milestones() below); "at" = the run's "finished"; "what" optional (e.g. the last gate's name).
  {"kind":"cleared","through":"2026-10-28T10:15:00","stamp":"28 Oct, 10:15","rev":49,"by":"Virtual Drafter","recorded":"..."}
      written by `clear` (never by hand): the stamp just written in the doc, its rev from the write's ack, who.
A milestone later than the newest "cleared ... through" is OWED; OWED longer than --grace-min is STALE.
The ledger's "cleared" is the writer's own word; `doc` is the check of that word against the doc's real text.

Usage (exit 0 = current or owed within grace, 1 = STALE, 2 = cannot judge / refused):
  python check_plan_doc_current.py owed  [--ledger <work>\doc-owed.jsonl] [--grace-min 60] [--now ...]
  python check_plan_doc_current.py clear --doc-stamp "28 Oct, 10:15" --rev 49 --by "Virtual Drafter" [--ledger ...]
  python check_plan_doc_current.py doc   --doc-text plan-read.txt [--run-json <work>\run.json] [--now 2026-10-28T09:00]
  python check_plan_doc_current.py doc   --doc-stamp "28 Oct, 10:15" ...
Tests: python test_check_plan_doc_current.py (replays 27-28 Oct on frozen real data in fixtures\).
"""
import argparse
import datetime as dt
import json
import re
import sys

MONTHS = {m: i + 1 for i, m in enumerate("Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec".split())}
PART_END = {"morning": (12, 0), "afternoon": (18, 0), "evening": (21, 0), "night": (23, 59)}
DEFAULT_RUN_JSON = r"C:\KOR.Drafter-work\31180-01-halifax\run.json"
DEFAULT_LEDGER = r"C:\KOR.Drafter-work\31180-01-halifax\doc-owed.jsonl"

# "[HH:MM, ]D Mon[th][, | at ]HH:MM | D Mon morning/afternoon/evening/night"
STAMP = re.compile(
    r"(?:\b(?P<bh>\d{1,2}):(?P<bm>\d{2}),\s*)?"
    r"\b(?P<d>\d{1,2})\s+(?P<mon>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\b"
    r"(?:,?\s*(?:at\s+)?(?P<ah>\d{1,2}):(?P<am>\d{2})\b|\s+(?P<part>morning|afternoon|evening|night)\b)?")


def stamps(text, year):
    """Every dated stamp in the text, as (datetime, the matched words)."""
    text = re.sub(r"<[^>]+>", "", text)  # a saved Docs read is XML; its text runs are what is written
    found = []
    for m in STAMP.finditer(text):
        try:
            day = dt.date(year, MONTHS[m["mon"]], int(m["d"]))
        except ValueError:
            continue
        if m["ah"] is not None:
            h, mi = int(m["ah"]), int(m["am"])
        elif m["bh"] is not None:
            h, mi = int(m["bh"]), int(m["bm"])
        elif m["part"]:
            h, mi = PART_END[m["part"]]
        else:
            h, mi = 23, 59
        if h > 23 or mi > 59:
            continue
        found.append((dt.datetime.combine(day, dt.time(h, mi)), m.group(0).strip()))
    return found


def milestones(runs):
    """(finished, started, gates passed) for every run that finished with at least one gate and every gate passed."""
    out = []
    for r in runs:
        gates = [s for s in r.get("steps", []) if "gate" in s]
        if r.get("finished") and gates and all(s.get("passed") for s in gates):
            out.append((dt.datetime.fromisoformat(r["finished"]), r["started"], len(gates)))
    return sorted(out)


def judge(doc_stamps, runs, now, grace_min):
    """Returns (exit code, lines). Pure: the test replays it at any clock time."""
    seen = sorted(s for s in doc_stamps if s[0] <= now)
    done = [m for m in milestones(runs) if m[0] <= now]
    if not seen:
        return 2, [f"CANNOT JUDGE: no dated stamp at or before {now:%d %b %H:%M} in the doc text"]
    doc_t, doc_words = seen[-1]
    if not done:
        return 0, [f"CURRENT: no finished, all-gates-passed run at or before {now:%d %b %H:%M}; doc stamp {doc_t:%d %b %H:%M} ('{doc_words}')"]
    last = done[-1]
    behind = [m for m in done if m[0] > doc_t]
    head = (f"doc's newest stamp {doc_t:%d %b %H:%M} ('{doc_words}'); newest milestone {last[0]:%d %b %H:%M} "
            f"(run started {last[1]}, {last[2]} of {last[2]} gates passed); {len(behind)} milestone(s) after the stamp")
    # The writer gets --grace-min from the OLDEST milestone the doc does not cover, not from the stamp.
    owed_min = (now - behind[0][0]).total_seconds() / 60 if behind else 0
    if owed_min > grace_min:
        return 1, [f"STALE: owed {owed_min:.0f} min (grace {grace_min}): {head}"] + [
            f"  not in the doc: run {m[1]} finished {m[0]:%d %b %H:%M}, {m[2]} of {m[2]} gates passed" for m in behind]
    return 0, [f"CURRENT: {head}"]


def read_ledger(path):
    """(milestones, clears) from doc-owed.jsonl. A line that is not a known kind is reported, never skipped silently."""
    ms, clears, bad = [], [], []
    with open(path, encoding="utf-8-sig") as f:
        for n, line in enumerate(f, 1):
            if not line.strip():
                continue
            try:
                r = json.loads(line)
                if r["kind"] == "milestone":
                    ms.append((dt.datetime.fromisoformat(r["at"]), r["run_started"], int(r["gates"]), r.get("what", "")))
                elif r["kind"] == "cleared":
                    clears.append((dt.datetime.fromisoformat(r["through"]), r.get("by", ""), r.get("rev"), r.get("stamp", "")))
                else:
                    bad.append(n)
            except (ValueError, KeyError, TypeError):
                bad.append(n)
    return sorted(ms), sorted(clears), bad


def owed(ms, clears, now, grace_min, bad=()):
    """Returns (exit code, lines): the milestones no 'cleared' line covers, judged at now. Pure, like judge()."""
    through = max((c for c in clears if c[0] <= now), default=None)
    t0 = through[0] if through else dt.datetime.min
    due = [m for m in ms if t0 < m[0] <= now]
    warn = [f"  {len(bad)} unreadable ledger line(s): {', '.join(map(str, bad))}"] if bad else []
    cl = (f"cleared through {through[0]:%d %b %H:%M} by {through[1] or '?'} (doc rev {through[2]}, stamp '{through[3]}')"
          if through else "never cleared")
    if not due:
        return (2 if bad else 0), [f"CURRENT: plan doc owes 0 milestones; {cl}"] + warn
    age = (now - due[0][0]).total_seconds() / 60
    lines = [f"plan doc owes {len(due)} milestone(s), oldest {due[0][0]:%d %b %H:%M} ({age:.0f} min ago); {cl}"] + [
        f"  owed: {m[0]:%d %b %H:%M} run {m[1]}, {m[2]} of {m[2]} gates passed{' - ' + m[3] if m[3] else ''}" for m in due] + warn
    if age > grace_min:
        return 1, [f"STALE: {lines[0]} (grace {grace_min})"] + lines[1:]
    return 0, [f"OWED, within grace {grace_min}: {lines[0]}"] + lines[1:]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("doc", help="judge a saved doc read against run.json")
    src = d.add_mutually_exclusive_group(required=True)
    src.add_argument("--doc-text", help="a file holding the doc's text (a saved Docs read is fine)")
    src.add_argument("--doc-stamp", help='one stamp, e.g. "28 Oct, 10:15"')
    d.add_argument("--run-json", default=DEFAULT_RUN_JSON)
    o = sub.add_parser("owed", help="what the plan doc owes, from doc-owed.jsonl (no doc read)")
    o.add_argument("--ledger", default=DEFAULT_LEDGER)
    c = sub.add_parser("clear", help="after writing the doc: record that it covers everything up to its stamp")
    c.add_argument("--ledger", default=DEFAULT_LEDGER)
    c.add_argument("--doc-stamp", required=True, help='the stamp just written in the doc, e.g. "28 Oct, 10:15"')
    c.add_argument("--rev", type=int, required=True, help="the doc's rev from the write's ack")
    c.add_argument("--by", required=True, help="the session that wrote it")
    for p in (d, o, c):
        p.add_argument("--grace-min", type=int, default=60)
        p.add_argument("--now", help="judge as at this local time (ISO); default the clock")
    a = ap.parse_args(argv)
    now = dt.datetime.fromisoformat(a.now) if a.now else dt.datetime.now().replace(microsecond=0)

    if a.cmd == "clear":
        found = [s for s in stamps(a.doc_stamp, now.year) if s[0] <= now]
        if len(found) != 1:
            print(f"REFUSED: --doc-stamp must hold exactly one stamp at or before now; found {len(found)} in '{a.doc_stamp}'")
            return 2
        rec = {"kind": "cleared", "through": found[0][0].isoformat(timespec="seconds"), "stamp": found[0][1],
               "rev": a.rev, "by": a.by, "recorded": now.isoformat(timespec="seconds")}
        with open(a.ledger, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
        print(f"cleared through {found[0][0]:%d %b %H:%M} (doc rev {a.rev}, by {a.by}) -> {a.ledger}")
        return 0

    if a.cmd == "owed":
        try:
            ms, clears, bad = read_ledger(a.ledger)
        except OSError as e:
            print(f"CANNOT JUDGE: ledger unreadable ({a.ledger}): {e}")
            return 2
        code, lines = owed(ms, clears, now, a.grace_min, bad)
        print("\n".join(lines))
        return code

    text = a.doc_stamp if a.doc_stamp else open(a.doc_text, encoding="utf-8-sig").read()
    try:
        runs = json.load(open(a.run_json, encoding="utf-8-sig"))["runs"]
    except (OSError, ValueError, KeyError) as e:
        print(f"CANNOT JUDGE: run.json unreadable ({a.run_json}): {e}")
        return 2
    code, lines = judge(stamps(text, now.year), runs, now, a.grace_min)
    print("\n".join(lines))
    return code


if __name__ == "__main__":
    sys.exit(main())
