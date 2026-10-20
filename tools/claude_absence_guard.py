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

stdin  : the Stop payload (transcript_path, stop_hook_active)
stdout : nothing (allow), or {"decision": "block", "reason": ...}
"""
import json
import re
import sys

# Repo rule 7: raw strings.
ABSENCE = re.compile(
    r"\b(?:is|are|was|were)\s+(?:not|n't)\s+(?:set|there|available|configured|installed|present|reachable)\b"
    r"|\b(?:isn't|aren't|wasn't|weren't)\s+(?:set|there|available|configured|installed|present|reachable)\b"
    r"|\b(?:doesn't|does not|don't|do not|didn't|did not)\s+(?:have|exist|contain|carry|hold)\b"
    r"|\b(?:can't|cannot|can not|couldn't|could not)\s+(?:reach|find|see|access|connect)\b"
    r"|\bthere (?:is|are|was|were) no\b|\bno such\b|\b(?:is|are) missing\b|\bnowhere\b|\bnot found\b",
    re.I)
PROOF = re.compile(r"(?:^|[\s(*_])checked:\s*\S", re.I | re.M)


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
    if PROOF.search(text):
        return None
    m = ABSENCE.search(text)
    return m.group(0) if m else None


def main():
    payload = json.load(sys.stdin)
    if payload.get("stop_hook_active"):
        return
    path = payload.get("transcript_path")
    if not path:
        return
    try:
        phrase = verdict(last_assistant_text(path))
    except OSError:
        return
    if phrase:
        print(json.dumps({"decision": "block", "reason":
            f"The reply claims an absence (\"{phrase}\") without its proof. Rule 3b: never conclude absence from a "
            "search whose scope you chose. Either run the check that proves it and add a line 'Checked: <the command "
            "or the scope searched>', or rewrite the sentence so it states only what was looked at."}))


if __name__ == "__main__":
    main()
