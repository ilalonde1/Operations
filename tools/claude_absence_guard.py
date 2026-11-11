"""Stop guard: a reply that claims something is ABSENT must show the check that proves it.

The fault, every time Ian has caught one: a claim about absence built on a look whose scope I chose.
2026-10-08 "there is no Worker deploy script" (looked in tools/*.ps1; it was in the repo root). 2026-10-19
"this PC doesn't have the NetworkOps connection set" (looked at three env-var scopes; KOR-1001 built
NetworkOps and reaches it through the service). The prose rule (feedback_professional_standard 3b) was
loaded both times and did not hold, so it is a gate.

How it decides: on Stop, read the last assistant message from the transcript. If it contains an absence
phrase (ABSENCE below) and no proof marker -- a line or clause starting "Checked:" that names the command
or the scope searched -- block the stop and say why, so the reply is rewritten with its proof or without
the claim. stop_hook_active = true (already blocked once this turn) always allows, so it can never loop.

What it covers: English absence phrasings in my final reply text. What it does NOT cover: absence claims in
tool-call descriptions, in files I write, or phrased in ways the list misses ("zero", "none found" are not
listed because they are usually counts); and a "Checked:" marker naming the WRONG scope passes -- the gate
forces the scope into view, it cannot judge it. Same-class fault it would miss: "the composer can only run
on APP01" (an absence of capability with no absence word).

Row 2010 (10 Nov 2026), HOW-WE-BUILD 7. In the 7 days to 10 Nov it refused 246 replies; 7 were judged wrong, none right.
The class of misfire, in one sentence: the phrase was not a report of what a look found - a truth judgement ("the claim
does not hold"), a modal ("doesn't have to"), a condition in code ("fails when the file is missing"), a check's or the
diff's reach ("this test cannot see", "the commit does not contain"), a quoted mention, or an absence already counted
("shows 0 open rows"). So "hold" is dropped, "have to" is not "have", and each match is excused (excused()) when it is
one of those; the next phrase in the reply is still read. Replay over the stored window of all 246: 85 now allowed,
161 still refused; 7 of 7 judged-wrong allowed; 11 of 11 that RESET-3 judged right (ids 2728 2768 4108 4179 4215 4284
4589 4590 4643 4968 4986) still refused. Every one of the 85 was read. After review row 1419 a bare pronoun (it, this, that, which) excuses only a reach (see,
reach), so "it doesn't have the file" about a share is refused, and a zero count excuses only in its own sentence.
What the excuses do NOT cover, and same-class faults they now MISS: a real absence claim inside a condition clause
("once that reader exists (its files were not there at 09:35)" - refusal 5169, now allowed); a machine's reach with a
check as subject ("its Revit check can't see KOR-302N" - 4525, allowed); a code noun as subject of a wrong claim ("the
commit does not contain X" when it does); and an absence in the same sentence as an unrelated "shows 0". "exist" is never excused by
its subject, so "the SAFE tool doesn't exist" (4643, wrong and corrected) is still refused.

stdin  : the Stop payload (transcript_path, stop_hook_active)
stdout : nothing (allow), or {"decision": "block", "reason": ...}
"""
import json
import re
import sys

# Repo rule 7: raw strings.
# Row 2010: "hold" is gone (19 of 19 refusals in the 7 days to 10 Nov were "the claim does not hold", "I didn't hold the
# run", "it doesn't hold up" - truth or action, never a store's contents) and "have to" is the modal, not possession.
ABSENCE = re.compile(
    r"\b(?:is|are|was|were)\s+(?:not|n't)\s+(?:set|there|available|configured|installed|present|reachable)\b"
    r"|\b(?:isn't|aren't|wasn't|weren't)\s+(?:set|there|available|configured|installed|present|reachable)\b"
    r"|\b(?:doesn't|does not|don't|do not|didn't|did not)\s+(?:have(?!\s+to\b)|exist|contain|carry)\b"
    r"|\b(?:can't|cannot|can not|couldn't|could not)\s+(?:reach|find|see|access|connect)\b"
    r"|\bthere (?:is|are|was|were) no\b|\bno such\b|\b(?:is|are) missing\b|\bnowhere\b|\bnot found\b",
    re.I)
PROOF = re.compile(r"(?:^|[\s(*_])checked:\s*\S", re.I | re.M)
# Row 2010: three shapes that are not a claim about what a look found. Each excuses ONE match; the next is still read.
# (1) A condition: "fails when the file is missing", "if kor is missing", "when there is no row" - a branch of code
#     described, not a search reported. The condition word must be in the same clause, at most 80 characters back.
CONDITION = re.compile(r"\b(?:if|when|whenever|unless|until|once|whose|where|without)\b[^.,;:!?\n]{0,80}$", re.I)
# (2) A subject that is a check, code or a piece of the diff: "this test cannot see", "the commit does not contain",
#     "a lookup can't find it", "so it cannot reach line 337" - a reviewer's reading of code in front of it.
CODE_SUBJECT = re.compile(
    r"\b(?:test|tests|selftest|check|checks|gate|gates|guard|hook|lookup|tool|code|case|cases|"
    r"commit|diff|message|comment|docstring|header|text|function|script|replay|reader|verb|grader|rows)\s+$", re.I)
# A bare pronoun may stand for a share or a machine ("I searched the share and it doesn't have the file" - review row
# 1419), so it excuses only a reach - "so it cannot see this", "it cannot reach line 337" - never have/find/contain.
PRONOUN_SUBJECT = re.compile(r"\b(?:it|this|that|which)\s+$", re.I)
SENTENCE_END = re.compile(r"[.!?\n]")
# (3) A mention in double quotes or backticks on its line: 'written up as "cause not found"'.
QUOTED = re.compile(r"\"[^\"\n]{0,120}\"|“[^”\n]{0,120}”|`[^`\n]{0,120}`")
ZERO_READ = re.compile(r"\b(?:shows?|showed|reads?|returned|returns|finds|found|counts?|counted)\s+(?:0|zero)\b", re.I)


def excused(text, m):
    """True when this absence phrase is a condition, a check's or code's reach, or a quoted mention (row 2010)."""
    before = text[max(0, m.start() - 120):m.start()]
    if CONDITION.search(before):
        return True
    # "exist" stays a claim whatever its subject: 'the SAFE tool doesn't exist' was wrong and corrected (refusal 4644)
    if CODE_SUBJECT.search(before) and re.search(r"(?i)(?:see|find|reach|access|connect|contain|carry|have)$", m.group(0)):
        return True
    if PRONOUN_SUBJECT.search(before) and re.search(r"(?i)(?:see|reach)$", m.group(0)):
        return True
    line_start = text.rfind("\n", 0, m.start()) + 1
    line_end = text.find("\n", m.end()); line_end = len(text) if line_end < 0 else line_end
    for q in QUOTED.finditer(text, line_start, line_end):
        if q.start() < m.start() and m.end() < q.end():
            return True
    # (4) A counted absence: a read that returned zero, in the SAME sentence ("the board read shows 0 open rows, so
    #     there are no row ids"; refusals 52 and 106, judged wrong on 2 Nov). A count is its own check.
    s_start = max([e.end() for e in SENTENCE_END.finditer(text, 0, m.start())] or [0])
    s_end = SENTENCE_END.search(text, m.end()); s_end = s_end.start() if s_end else len(text)
    if ZERO_READ.search(text, s_start, s_end):
        return True
    return False


def last_assistant_text(path):
    text = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") != "assistant":
                continue
            content = entry.get("message", {}).get("content", [])
            parts = [c.get("text", "") for c in content if isinstance(c, dict) and c.get("type") == "text"]
            if parts:
                text = "\n".join(parts)
    return text or ""


def verdict(text):
    """Returns the offending phrase, or None when the reply may stop."""
    m = first_claim(text)
    return m.group(0) if m else None


def first_claim(text):
    """The first absence phrase that is not excused, or None (also None when the reply carries its proof)."""
    if PROOF.search(text):
        return None
    for m in ABSENCE.finditer(text):
        if not excused(text, m):
            return m
    return None


def main():
    payload = json.load(sys.stdin)
    if payload.get("stop_hook_active"):
        return
    path = payload.get("transcript_path")
    if not path:
        return
    try:
        text = last_assistant_text(path)
        phrase = verdict(text)
    except OSError:
        return
    if phrase:
        try:   # F11 (Framework row 1159): every refusal is a row; logging can never change what this guard does
            sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
            m = first_claim(text)
            guard_log.record("absence", phrase, text[max(0, m.start() - 150):m.end() + 150], None, payload=payload)
        except Exception:
            pass
        print(json.dumps({"decision": "block", "reason":
            f"The reply claims an absence (\"{phrase}\") without its proof. Rule 3b: never conclude absence from a "
            "search whose scope you chose. Either run the check that proves it and add a line 'Checked: <the command "
            "or the scope searched>', or rewrite the sentence so it states only what was looked at."}))


if __name__ == "__main__":
    main()
