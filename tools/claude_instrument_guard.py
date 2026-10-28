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
most recently RAN. It is refused only when NO run of that script since its last edit has ever found anything -
because an instrument that has only ever returned nothing has not been shown to work. Any run that returned a
real answer clears it, whatever words the output used. A zero from a tool this session did not write is not this
class and is not touched.

Rewritten 2026-10-27 18:4x, same day it was built, because it REFUSED THREE OF THE PDF INGESTOR'S SENDS and held
real work. Its net_probe.ps1 had been run on a must-match (two 31087 axes listed) and a must-not (nothing listed
outside the plan) - a textbook known-answer test - and the guard refused anyway, because the first version looked
for ITS OWN VOCABULARY ("self-test", "known-positive", "expected N") in the output instead of looking at what the
instrument did. My 11 fixtures all used that vocabulary, since I wrote them, so an 11-of-11 suite passed over the
defect: a self-test that cannot fail, which is the fault one level up that this file's own header already named.

Three causes were reported by Virtual Drafter and all three measured true, though not with the effects reported:
  (a) the vocabulary problem above - THIS is what refused the sends.
  (b) an indirect invocation (`& $p -Plan 31087`) carries no basename, so the run is not attributed. Measured: it
      DISARMS the guard rather than arming it, so it did not cause the refusals - it is a coverage gap, and such
      a run now explicitly disarms, because a hook that guesses which instrument ran must not refuse on a guess.
  (c) every nil in a message was pinned on the one instrument, including counts from tasklist and a network check
      that it never produced. A nil traceable to another tool's output no longer counts, and the refusal now
      quotes the nils it is about and says plainly that the attribution is a guess.
Then a dry run over a real 15,558-record transcript - the step skipped the first time - found a FOURTH fault the
19-case suite would never have shown: "145 buttons: certain 50 | likely 27 | no match found 68" was read as a nil
because of the words "no match found", in an output reporting 77 matches. A nil marker in PART of an output does
not make the output nil. Both of those real cases are now cases in the suite.

What it COVERS: a nil finding claimed in a final reply or a peer message, where the measuring script was authored
or changed in this same session, was run, and has never once returned a finding since.
What it does NOT: a zero from an INLINE command rather than a written script (the "0 of 8" from an equality test
against a short sha was typed straight into PowerShell, and this guard would not have seen it); a script invoked
only indirectly; whether a NON-zero number is right - an instrument can be wrong in the other direction; claims
phrased without a nil word; other sessions' instruments.
Same-class faults it would MISS, and they are the price of not blocking correct work: a scan self-tested on a
positive it was always going to match - a self-test that cannot fail is the class one level up, and no hook can
tell the two apart. A comparison that reports "33 hashed, 0 mismatches" reads as a finding (33), so a comparison
incapable of failing still passes; that is what the sibling publish gate's -SeedBadRow exists for. The input/
finding distinction is a heuristic on wording: a count of what was looked at, phrased without one of the framing
words in INPUT_FRAMED, reads as a finding. And it still guesses WHICH script produced the nil by taking the one
most recently run.

stdin  : the Stop payload (transcript_path, stop_hook_active) or the PreToolUse payload (tool_name SendMessage)
stdout : nothing (allow); Stop -> {"decision": "block", ...}; PreToolUse -> permissionDecision "deny"
Fails OPEN on any error: a guard that blocks every reply when a transcript cannot be parsed stops the work.
"""
import json
import os
import re
import sys

# Repo rule 7: raw strings for every regex.
#
# Widened 2026-10-27 19:0x, after measuring that this guard NEVER EXAMINED ITS OWN FOUNDING CASE. The cadtest
# answer was written "None of the ~3.9 GB came from that checkout, and nothing I ran removed cadtest" - and
# "none of the" is not "none of them", "nothing I ran removed" is not "nothing was deleted". The arming and the
# exits were measured over 62 transcripts before anyone thought to check whether the detector saw the one claim
# the whole file exists for. A gate's coverage is the narrowest of its stages, not the one that was measured.
NIL_CLAIM = re.compile(
    r"\b0 of \d+\b"
    r"|\bdelta:?\s*0\b"
    r"|\bnone (?:found|matched|of them|present)\b"
    r"|\bnone of (?:the|those|these|it|that)\b"
    r"|\bno (?:\w+\s+){0,3}(?:were |was )?(?:found|present|detected|returned|deleted|changed)\b"
    r"|\bnothing (?:\w+\s+){0,3}(?:found|changed|deleted|removed|written|returned|touched|matched)\b"
    r"|\bnobody (?:did|had|touched)\b", re.I)

# Evidence IN WORDS that the instrument was exercised on an answer already KNOWN. One of two accepted forms, and
# the weaker one: it only fires when the output happens to use this vocabulary. Checked only in the OUTPUT of a
# run of that same script. Virtual Drafter's dry run over 73,337 records found the first version allowing a nil
# because the words "self-test" appeared in unrelated output that landed after a script edit.
SELF_TEST = re.compile(
    r"self-?test|known[- ](?:positive|negative|answer)|negative sample|seeded|seedbadrow"
    r"|must (?:fail|match|not match)|control case|expected \d+|expect \d+ of \d+"
    r"|\d+ of \d+ (?:known|samples?|positives?)", re.I)

# Evidence IN BEHAVIOUR, and the one that matters: an instrument that has returned a real answer has been shown
# to discriminate, whatever words it used. A regex that misses every Windows path, and a counter reading the
# wrong JSON field, return nothing on EVERY input; one run that found something is proof the instrument can see.
#
# "Found something" is a number of at least 1 that reports a FINDING rather than how much was looked at. The
# distinction is the whole difficulty, and a dry run over a real 15,558-record transcript is what forced it: the
# first version read "145 buttons: certain 50 | likely 27 | no match found 68" as a nil, because the words "no
# match found" appeared in an output that plainly reports 77 matches. A nil marker in part of an output does not
# make the output nil. Equally, "scanned 62 files / result: 0 found" must NOT read as a finding - the 62 sizes
# the input, and every result in it is zero. So numbers in input-framing position are skipped.
FINDING_NUM = re.compile(r"\b(\d[\d,]*)\b")
INPUT_FRAMED = re.compile(
    r"(?:\bof|\bout of|\bacross|\bamong|\bin|\bscanned|\bscanning|\bexamined|\bexamining|\bread|\breading"
    r"|\bchecked|\bchecking|\bsearched|\bsearching|\bprocessed|\bprocessing|\bloaded|\bwalked|\btotal"
    r"|\blimit|\bcap|\bover)\W{1,3}$", re.I)

# A result-shaped nil in the instrument's own output. Only consulted once no finding was seen, to decide between
# "returned nothing" (refuse) and "said something this guard cannot read" (allow). Process-health zeros are not
# result nils - "0 errors" says the run was clean, not that the answer was nothing.
OUTPUT_NIL = re.compile(
    r"\b0 (?:of \d+|found|match(?:es|ed)?|rows?|results?|hits?|items?|records?|instances?|mismatch\w*)\b"
    r"|\b(?:found|matched|returned|listed|detected|affected)\s*:?\s*0\b"
    r"|\bresult:?\s*0\b|\(0\)"
    r"|\bnone (?:found|matched|listed|present|returned)\b"
    r"|\bnothing (?:found|matched|listed|returned|to report)\b"
    r"|\bno (?:match|result|hit|row|record|instance)\w*\b", re.I)


def found_something(out):
    """True when this output reports a finding - a count of at least 1 that is not sizing the input."""
    if not out:
        return False
    for m in FINDING_NUM.finditer(out):
        try:
            if int(m.group(1).replace(",", "")) < 1:
                continue
        except ValueError:
            continue
        if not INPUT_FRAMED.search(out[max(0, m.start() - 24):m.start()]):
            return True
    return False

# An invocation the guard CANNOT attribute: the script arrives through a variable, so no basename appears. The
# PDF Ingestor's 18:29:12 run was `& $p -Plan 31087`. This disarms the guard rather than arming it - a hook that
# guesses which instrument ran, then refuses on the guess, blocks correct work.
INDIRECT_RUN = re.compile(
    r"[&.]\s*\$\w+|\$\w+\s*\.(?:ps1|py)\b|-File\s+[\"']?\$"
    r"|(?:python|python3|pwsh|powershell|bash|sh)\s+[\"']?\$\w+", re.I)

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
    nils = [m.group(0) for m in NIL_CLAIM.finditer(claim_text or "")]
    if not nils:
        return None

    outputs = results_by_id(recs)
    edits = {}      # basename -> index of its last edit
    runs = []       # (index, basename, output) for each attributable run of an edited script, in order
    others = []     # (index, output) for every other run - another tool's numbers are not the instrument's
    indirect = []   # indices of runs whose script cannot be identified at all
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
                out = outputs.get(b.get("id"), "")
                hit = [base for base in edits if base in cmd]
                for base in hit:
                    runs.append((i, base, out))
                if not hit:
                    others.append((i, out))
                    if INDIRECT_RUN.search(cmd):
                        indirect.append(i)

    # The instrument is the edited script most recently RUN - a script written but never run cannot have
    # produced the nil, and keying on the last RUN is a closer guess at which one did than "any edit".
    armed = [r for r in runs if r[0] > edits[r[1]]]
    if not armed:
        return None
    _, last_script, _ = armed[-1]
    mine = [(i, out) for i, base, out in armed if base == last_script]

    # (1) BEHAVIOUR. A run of this script that found something has shown it discriminates.
    for _, out in mine:
        if found_something(out):
            return None

    # (2) WORDS. The weaker form, kept for a one-shot harness that states its own known answer.
    for _, out in mine:
        if SELF_TEST.search(out or ""):
            return None

    # (1b) Nothing was found, but nor did any run SAY nothing - the output is something this guard cannot read,
    # and a hook that refuses whatever it cannot read is a hook that blocks correct work. Refuse only when at
    # least one run came back empty or said in so many words that it found nothing.
    if not any(not (out or "").strip() or OUTPUT_NIL.search(out or "") for _, out in mine):
        return None

    # (3) ATTRIBUTION. An unattributable invocation after this script's last edit means the guard does not know
    # what ran - it must not refuse on a guess.
    if any(i > edits[last_script] for i in indirect):
        return None

    # (4) Somebody else's nils. If every nil in the text appears verbatim in the output of a run that is NOT
    # this script's, the number did not come from this instrument. The Ingestor's message carried a tasklist
    # count and a network check alongside its probe result, and all three were pinned on the probe.
    foreign = "\n".join(out or "" for _, out in others).lower()
    if foreign and all(n.lower() in foreign for n in nils):
        return None

    quoted = ", ".join('"' + n + '"' for n in dict.fromkeys(nils))
    return (
        "INSTRUMENT NOT TESTED. This text reports a nil result (" + quoted + "), and every run of "
        + last_script + " since its last edit in this session has itself come back empty - so there is no run "
        "showing the instrument can find anything.\n\n"
        "A zero is the one reading that a broken instrument and a clean result produce alike. On 2026-10-27 a "
        "62-transcript scan returned a clean 0 because its regex missed every Windows path; a step reported "
        "delta 0 because it counted the wrong JSON field against a truncated list. Both read as someone else's "
        "failure and would have rolled back working work.\n\n"
        "Before reporting this: run " + last_script + " against a case whose answer is already known - one it "
        "MUST match and one it must NOT - and let its output show what it found. Any run that returns a real "
        "answer clears this; the words do not matter.\n\n"
        "If the nil above came from something other than " + last_script + ", that is this guard guessing: it "
        "takes the edited script most recently run. Say so and report the number."
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
