r"""Tests for check_plan_doc_current.py. Known-bad first: the 27-28 Oct night Ian caught (Mistake 85), replayed on
frozen real data -- fixtures\halifax-run-2026-10-28.json (run.json runs since 27 Oct 12:00, steps cut to
step/gate/passed/exit) and fixtures\one-machine-plan-rev49-stamps.txt (the plan's dated sentences, rev 49).
Run: python test_check_plan_doc_current.py
"""
import datetime as dt
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_plan_doc_current as c  # noqa: E402

RUNS = json.load(open(os.path.join(HERE, "fixtures", "halifax-run-2026-10-28.json"), encoding="utf-8"))["runs"]
DOC = open(os.path.join(HERE, "fixtures", "one-machine-plan-rev49-stamps.txt"), encoding="utf-8").read()
T = lambda s: dt.datetime.fromisoformat(s)  # noqa: E731


def stale_spans(judge_at, start, end, step_min=5):
    """[(from, to)] where judge_at(t) says STALE, sampled every step_min."""
    spans, t, cur = [], start, None
    while t <= end:
        s = judge_at(t) == 1
        if s and cur is None:
            cur = t
        if not s and cur is not None:
            spans.append((cur, t)); cur = None
        t += dt.timedelta(minutes=step_min)
    if cur is not None:
        spans.append((cur, end))
    return spans


class Stamps(unittest.TestCase):
    def one(self, text):
        found = c.stamps(text, 2026)
        self.assertEqual(len(found), 1, found)
        return found[0][0]

    def test_time_after_date(self):
        self.assertEqual(self.one("28 Oct, 10:15 - Halifax v3"), T("2026-10-28T10:15"))
        self.assertEqual(self.one("DONE 26 Oct 23:31 (VD)"), T("2026-10-26T23:31"))

    def test_time_before_date(self):
        self.assertEqual(self.one("published on your word at 15:15, 27 Oct: all 8"), T("2026-10-27T15:15"))
        self.assertEqual(self.one("done 10:11, 27 Oct."), T("2026-10-27T10:11"))

    def test_no_clock_counts_from_the_end_of_its_period(self):
        self.assertEqual(self.one("27 Oct afternoon."), T("2026-10-27T18:00"))
        self.assertEqual(self.one("27 Oct night: beams"), T("2026-10-27T23:59"))
        self.assertEqual(self.one("DONE 27 Oct (PDF Ingestor)"), T("2026-10-27T23:59"))

    def test_saved_docs_xml_is_read_as_its_text(self):
        xml = "<text id='.22220'><italic>28 <hit n='17'>Oct</hit>, 10:15 - Halifax</italic></text>"
        self.assertEqual(self.one(xml), T("2026-10-28T10:15"))

    def test_not_a_stamp(self):
        self.assertEqual(c.stamps("all 26 pages looked at; 433 of 433; heights 19:10-19:55", 2026), [])

    def test_whole_fixture_parses(self):
        self.assertEqual(len(c.stamps(DOC, 2026)), 29)  # the read said hits='29'


class Doc(unittest.TestCase):
    def test_known_bad_the_night_of_27_28_Oct(self):
        st = c.stamps(DOC, 2026)
        spans = stale_spans(lambda t: c.judge(st, RUNS, t, 60)[0], T("2026-10-27T18:00"), T("2026-10-28T11:00"))
        self.assertEqual(spans, [
            (T("2026-10-27T23:50"), T("2026-10-28T00:00")),   # 22:49 ETABS run uncovered until "27 Oct night"
            (T("2026-10-28T02:20"), T("2026-10-28T10:15")),   # 01:15 Revit 10/10 gates, then 04:59 + 06:52
        ])

    def test_stale_names_what_the_doc_left_out(self):
        code, lines = c.judge(c.stamps(DOC, 2026), RUNS, T("2026-10-28T09:00"), 60)
        self.assertEqual(code, 1)
        self.assertEqual([l for l in lines if l.startswith("  not in the doc")], [
            "  not in the doc: run 2026-10-28T01:05:22 finished 28 Oct 01:15, 10 of 10 gates passed",
            "  not in the doc: run 2026-10-28T03:50:49 finished 28 Oct 04:59, 4 of 4 gates passed",
            "  not in the doc: run 2026-10-28T05:33:55 finished 28 Oct 06:52, 4 of 4 gates passed"])

    def test_grace_runs_from_the_milestone_not_the_stamp(self):
        st = c.stamps("27 Oct night", 2026)
        self.assertEqual(c.judge(st, RUNS, T("2026-10-28T02:15"), 60)[0], 0)  # 01:15 + 60 = not yet
        self.assertEqual(c.judge(st, RUNS, T("2026-10-28T02:16"), 60)[0], 1)

    def test_a_failed_or_unfinished_run_is_not_a_milestone(self):
        failed = [{"started": "2026-10-28T03:00:00", "finished": "2026-10-28T03:05:00", "steps": [{"gate": "g", "passed": False}]},
                  {"started": "2026-10-28T03:10:00", "steps": [{"gate": "g", "passed": True}]},
                  {"started": "2026-10-28T03:20:00", "finished": "2026-10-28T03:21:00", "steps": [{"step": "s", "exit": 0}]}]
        self.assertEqual(c.milestones(failed), [])

    def test_a_future_stamp_does_not_make_the_doc_current(self):
        st = c.stamps("27 Oct night. Next review 29 Oct 09:00.", 2026)
        self.assertEqual(c.judge(st, RUNS, T("2026-10-28T09:00"), 60)[0], 1)

    def test_no_stamp_cannot_judge(self):
        self.assertEqual(c.judge([], RUNS, T("2026-10-28T09:00"), 60)[0], 2)


class Ledger(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.path = os.path.join(self.dir, "doc-owed.jsonl")
        # What virtual_drafter.py would have written last night: one line per milestone in the frozen runs.
        with open(self.path, "w", encoding="utf-8") as f:
            for at, started, gates in c.milestones(RUNS):
                f.write(json.dumps({"kind": "milestone", "at": at.isoformat(), "run_started": started, "gates": gates}) + "\n")

    def run_cli(self, *args):
        p = subprocess.run([sys.executable, os.path.join(HERE, "check_plan_doc_current.py"), *args, "--ledger", self.path],
                           capture_output=True, text=True)
        return p.returncode, p.stdout

    def clear_night(self):
        code, out = self.run_cli("clear", "--doc-stamp", "27 Oct night", "--rev", "47", "--by", "test", "--now", "2026-10-28T00:00")
        self.assertEqual(code, 0, out)

    def test_known_bad_owed_replay(self):
        self.clear_night()
        ms, clears, bad = c.read_ledger(self.path)
        self.assertEqual(bad, [])
        spans = stale_spans(lambda t: c.owed(ms, clears, t, 60)[0], T("2026-10-28T00:00"), T("2026-10-28T10:10"))
        self.assertEqual(spans, [(T("2026-10-28T02:20"), T("2026-10-28T10:10"))])

    def test_never_cleared_is_stale(self):
        self.assertEqual(self.run_cli("owed", "--now", "2026-10-27T23:00")[0], 1)

    def test_owed_within_grace_then_stale_then_cleared(self):
        self.clear_night()
        code, out = self.run_cli("owed", "--now", "2026-10-28T01:30")
        self.assertEqual(code, 0, out)
        self.assertIn("OWED, within grace 60: plan doc owes 1 milestone(s), oldest 28 Oct 01:15", out)
        code, out = self.run_cli("owed", "--now", "2026-10-28T09:00")
        self.assertEqual(code, 1, out)
        self.assertIn("STALE: plan doc owes", out)
        code, out = self.run_cli("clear", "--doc-stamp", "28 Oct, 10:15", "--rev", "49", "--by", "Virtual Drafter",
                                 "--now", "2026-10-28T10:16")
        self.assertEqual(code, 0, out)
        code, out = self.run_cli("owed", "--now", "2026-10-28T10:20")
        self.assertEqual(code, 0, out)
        self.assertIn("owes 0 milestones; cleared through 28 Oct 10:15 by Virtual Drafter (doc rev 49", out)

    def test_clear_refuses_a_future_or_missing_stamp(self):
        self.assertEqual(self.run_cli("clear", "--doc-stamp", "29 Oct 09:00", "--rev", "1", "--by", "t",
                                      "--now", "2026-10-28T10:00")[0], 2)
        self.assertEqual(self.run_cli("clear", "--doc-stamp", "this morning", "--rev", "1", "--by", "t")[0], 2)

    def test_an_unreadable_line_is_reported_not_skipped(self):
        with open(self.path, "a", encoding="utf-8") as f:
            f.write("{not json\n")
        code, out = self.run_cli("owed", "--now", "2026-10-28T09:00")
        self.assertIn("unreadable ledger line(s)", out)


if __name__ == "__main__":
    unittest.main(verbosity=2)
