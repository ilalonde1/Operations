r"""Instrument guard: a ZERO is only reportable once the instrument has produced a known answer.

The sibling of claude_look_guard.py, for the other class on the oversight board. The look guard proves a look
HAPPENED; it cannot prove the thing that looked was working. Virtual Drafter saw that distinction and took
instrument-not-tested back off the look guard, which is why this exists.

The class (oversight.MistakeClass 'instrument-not-tested', Revit Fleet, 2026-10-27): a measurement was trusted
without first testing the instrument on a known answer, so the method's own artefact was read as a finding - a
false zero, a false failure, or a count that could never have moved.

Three of its instances in one day, each of which read as somebody else's failure:
  - a 62-transcript scan for who deleted a peer's files returned a clean 0; the regex, written through a heredoc,
    silently missed every Windows Remove-Item 'C:\...' form, including a deletion already known to exist.
  - Option C step 7 reported a transaction FAILED, delta 0; the counter read result.items (the field is
    result.elements) against a document of 24,054 notes capped at 500, so the number could never have moved.
  - a publish gate printed "Imperial: 0", which reads as "Imperial drafters get nothing"; the loop broke on the
    first matching unit, so the count was an artefact of the order the units were listed in.

THE RULE. If the text reports a ZERO or a nil result, and this session WROTE OR EDITED a script (.py/.ps1), then
the transcript must show that instrument self-tested AFTER its last edit - a known-positive, a seeded failure, an
expected count. Otherwise the reply is refused and told to test the instrument first. A zero from a tool this
session did not write is not this class and is not touched.

What it COVERS: a nil finding claimed in a final reply or a peer message, where the measuring script was authored
or changed in this same session and never shown to produce a non-nil answer.
What it does NOT: a zero from an INLINE command rather than a written script (the "0 of 8" from an equality test
against a short sha was typed straight into PowerShell, and this guard would not have seen it); whether the
self-test was a good one; whether a NON-zero number is right - an instrument can be wrong in the other direction;
claims phrased without a nil word; other sessions' instruments.
Same-class fault it would MISS: a scan self-tested on a positive it was always going to match - a self-test that
cannot fail is the class one level up, and no hook here can tell the two apart.

stdin  : the Stop payload (transcript_path, stop_hook_active) or the PreToolUse payload (tool_name SendMessage)
stdout : nothing (allow); Stop -> {"decision": "block", ...}; PreToolUse -> permissionDecision "deny"
Fails OPEN on any error: a guard that blocks every reply when a transcript cannot be parsed stops the work.
"""
import json
import os
import re
import sys

# Repo rule 7: raw strings for every regex.
NIL_CLAIM = re.compile(
    r"\b0 of \d+\b"
    r"|\bdelta:?\s*0\b"
    r"|\bnone (?:found|matched|of them|present)\b"
    r"|\bno (?:\w+\s+){0,3}(?:were |was )?(?:found|present|detected|returned|deleted|changed)\b"
    r"|\bnothing (?:was |is |had )?(?:found|changed|deleted|written|returned|touched)\b"
    r"|\bnobody (?:did|had|touched)\b", re.I)

# Evidence that the instrument was exercised on an answer already known.
SELF_TEST = re.compile(
    r"self-?test|known[- ]positive|known[- ]negative|negative sample|seeded|seedbadrow"
    r"|must fail|expected 1|expect 1|expect \d+ of \d+|sanity", re.I)

SCRIPT = re.compile(r"\.(?:py|ps1)$", re.I)
WRITE_TOOLS = {"Write", "Edit", "NotebookEdit"}


def entries(path):
    out = []
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            try:
                out.append(json.loads(line))
            except Exception:
                continue
    return out


def blocks(rec):
    content = (rec.get("message") or {}).get("content")
    return content if isinstance(content, list) else []


def text_of(rec):
    parts = []
    for b in blocks(rec):
        if isinstance(b, dict):
            if b.get("type") == "text" and isinstance(b.get("text"), str):
                parts.append(b["text"])
            elif b.get("type") == "tool_result":
                c = b.get("content")
                if isinstance(c, str):
                    parts.append(c)
                elif isinstance(c, list):
                    parts.extend(x.get("text", "") for x in c if isinstance(x, dict))
    return "\n".join(parts)


def verdict(recs, claim_text):
    """Returns a refusal string, or None to allow."""
    if not NIL_CLAIM.search(claim_text or ""):
        return None

    last_edit_at, last_script = -1, None
    for i, rec in enumerate(recs):
        for b in blocks(rec):
            if not isinstance(b, dict) or b.get("type") != "tool_use":
                continue
            if b.get("name") in WRITE_TOOLS:
                p = (b.get("input") or {}).get("file_path") or ""
                if SCRIPT.search(p):
                    last_edit_at, last_script = i, os.path.basename(p)

    if last_script is None:
        return None  # the measurement is not an instrument this session built

    for rec in recs[last_edit_at + 1:]:
        if SELF_TEST.search(text_of(rec)):
            return None
    if SELF_TEST.search(claim_text):
        return None

    return (
        "INSTRUMENT NOT TESTED. This text reports a nil result, and the instrument that produced it - "
        + last_script + " - was written or changed in this session and has not been shown to return a known "
        "answer since.\n\n"
        "A zero is the one reading that a broken instrument and a clean result produce alike. On 2026-10-27 a "
        "62-transcript scan returned a clean 0 because its regex missed every Windows path; a step reported "
        "delta 0 because it counted the wrong JSON field against a truncated list. Both read as someone else's "
        "failure and would have rolled back working work.\n\n"
        "Before reporting this: run " + last_script + " against a case whose answer is already known - one it "
        "MUST match and one it must NOT - and say what it returned. If the nil is real it survives that, and "
        "the self-test is the evidence. If you cannot construct a known case, say the number is unverified "
        "rather than reporting it as a finding."
    )


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    path = payload.get("transcript_path")
    if not path or not os.path.exists(path):
        return
    try:
        recs = entries(path)
    except Exception:
        return

    if payload.get("tool_name") == "SendMessage":
        claim = ((payload.get("tool_input") or {}).get("message")) or ""
        why = verdict(recs, claim)
        if why:
            json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                              "permissionDecisionReason": why}}, sys.stdout)
        return

    if payload.get("stop_hook_active"):
        return  # blocks once, never loops
    last = ""
    for rec in reversed(recs):
        if (rec.get("message") or {}).get("role") == "assistant":
            last = text_of(rec)
            if last.strip():
                break
    why = verdict(recs, last)
    if why:
        json.dump({"decision": "block", "reason": why}, sys.stdout)


if __name__ == "__main__":
    main()
