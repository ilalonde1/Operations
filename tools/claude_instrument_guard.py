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

THE RULE. If the text reports a ZERO or a nil result, the instrument is the .py/.ps1 that this session edited and
most recently RAN; that script must have run again after its last edit with KNOWN-ANSWER evidence in its OWN
output - a known-positive, a seeded failure, an expected count. Otherwise the reply is refused and told to test
the instrument first. A zero from a tool this session did not write is not this class and is not touched.

Tightened 2026-10-27 after Virtual Drafter dry-ran the first version over a 73,337-record transcript: it allowed
a nil because the words "self-test" appeared in UNRELATED output that happened to land after a script edit, and
because the claim text itself could say "self-test" and pass. It was catching forgetting, not a word in passing.
Evidence now counts only inside a run of the same script, the claim-text escape is gone, and a script that was
written but never run no longer arms it - it cannot have produced the nil.

What it COVERS: a nil finding claimed in a final reply or a peer message, where the measuring script was authored
or changed in this same session, was run, and was never shown to return an answer already known.
What it does NOT: a zero from an INLINE command rather than a written script (the "0 of 8" from an equality test
against a short sha was typed straight into PowerShell, and this guard would not have seen it); whether the
self-test was a good one; whether a NON-zero number is right - an instrument can be wrong in the other direction;
claims phrased without a nil word; other sessions' instruments.
Same-class fault it would MISS: a scan self-tested on a positive it was always going to match - a self-test that
cannot fail is the class one level up, and no hook here can tell the two apart. It also guesses WHICH script
produced the nil by taking the one most recently run, so a nil from an older instrument, reported after a
different script ran, is attributed to the wrong one.

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

# Evidence that the instrument was exercised on an answer already KNOWN. Checked only in the OUTPUT of a run of
# that same script - not anywhere in the transcript. Virtual Drafter's dry run over 73,337 records found the
# earlier version allowing a nil because the words "self-test" appeared in unrelated output that happened to land
# after a script edit: it caught forgetting, not a word in passing.
SELF_TEST = re.compile(
    r"self-?test|known[- ]positive|known[- ]negative|negative sample|seeded|seedbadrow"
    r"|must fail|expected \d+|expect \d+ of \d+|\d+ of \d+ (?:known|samples?|positives?)", re.I)

SCRIPT = re.compile(r"\.(?:py|ps1)$", re.I)
WRITE_TOOLS = {"Write", "Edit", "NotebookEdit"}
RUN_TOOLS = {"Bash", "PowerShell"}


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


def results_by_id(recs):
    out = {}
    for rec in recs:
        for b in blocks(rec):
            if isinstance(b, dict) and b.get("type") == "tool_result":
                c = b.get("content")
                if isinstance(c, list):
                    c = "\n".join(x.get("text", "") for x in c if isinstance(x, dict))
                out[b.get("tool_use_id")] = c if isinstance(c, str) else ""
    return out


def verdict(recs, claim_text):
    """Returns a refusal string, or None to allow."""
    if not NIL_CLAIM.search(claim_text or ""):
        return None

    outputs = results_by_id(recs)
    edits = {}   # basename -> index of its last edit
    runs = []    # (index, basename, output) for each run of a script, in order
    for i, rec in enumerate(recs):
        for b in blocks(rec):
            if not isinstance(b, dict) or b.get("type") != "tool_use":
                continue
            name, inp = b.get("name"), (b.get("input") or {})
            if name in WRITE_TOOLS:
                p = inp.get("file_path") or ""
                if SCRIPT.search(p):
                    edits[os.path.basename(p)] = i
            elif name in RUN_TOOLS:
                cmd = inp.get("command") or ""
                for base in edits:
                    if base in cmd:
                        runs.append((i, base, outputs.get(b.get("id"), "")))

    # The instrument is the edited script most recently RUN - a script written but never run cannot have
    # produced the nil, and keying on the last RUN is a closer guess at which one did than "any edit".
    armed = [r for r in runs if r[0] > edits[r[1]]]
    if not armed:
        return None
    _, last_script, _ = armed[-1]

    # A self-test counts only when THAT script ran after its last edit and its own output carried the evidence.
    for idx, base, out in armed:
        if base == last_script and idx > edits[base] and SELF_TEST.search(out or ""):
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
