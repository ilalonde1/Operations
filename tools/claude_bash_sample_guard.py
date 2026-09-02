"""PostToolUse guard for Bash: a truncated command cannot support a claim about the whole.

WHAT THIS IS FOR, and it is one specific failure that has been paid for.

On 2026-09-01 a report was read with

    grep -oE "implied thickness [0-9.]+ in" report.txt | head -5

and the finding written from it said "all 37 measure 3.1-3.4 in". The command itself said
5. The real distribution had five outlines at 4.3 in -- ABOVE the 4-inch minimum the
argument rested on -- so a defect was marked CLOSED on evidence that did not support it,
and the engineer's own report was telling her 4.3 in for linework she would have gone back
to five locations in the drawings to find. A later audit caught it; nothing in the loop did.

A hook fires at the moment the output is read, which is the only moment that matters. It
cannot stop a sentence being written -- hooks see tool calls, not prose -- so it does the
next best thing and stamps the sample as a sample, in the same text the claim gets written
from.

Silent unless the command truncates. If the command ALSO computes a population -- wc -l,
sort | uniq -c -- nothing is said, because then the claim is supportable.

stdin  : the PostToolUse payload
stdout : nothing, or hookSpecificOutput.additionalContext
"""
import json
import re
import sys

# Repo rule 7: every pattern is a raw string.
TRUNCATES = [
    (re.compile(r"\|\s*head\b"), "head"),
    (re.compile(r"\|\s*tail\b"), "tail"),
    (re.compile(r"(?:^|[;&|]\s*)head\s+-"), "head"),
    (re.compile(r"(?:^|[;&|]\s*)tail\s+-"), "tail"),
    (re.compile(r"grep\b[^|;]*\s-\w*m\s*\d+"), "grep -m"),
    (re.compile(r"sed\s+-n\s+['\"]?\d+\s*,\s*\d+p"), "sed -n range"),
    (re.compile(r"Select-Object\s+-First\b"), "Select-Object -First"),
]

# Asking for the whole population. If the command does this, it can support a claim about it.
COUNTS = re.compile(
    r"\bwc\s+-l\b|\buniq\s+-c\b|\bgrep\b[^|;]*\s-\w*c\b|Measure-Object|\bcount\b",
    re.IGNORECASE)

NOTE = (
    "TRUNCATED OUTPUT ({how}). This is a SAMPLE, not the population.\n"
    "Do not write \"all\", \"every\", \"none\", \"only\", or a range/distribution from it, and do "
    "not close a finding on it.\n"
    "To make a claim about the whole, re-run so the command counts it:  "
    "... | wc -l   or   ... | sort | uniq -c\n"
    "Then state it as X of Y."
)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return

    cmd = (payload.get("tool_input") or {}).get("command") or ""
    if not cmd or COUNTS.search(cmd):
        return

    for pattern, how in TRUNCATES:
        if pattern.search(cmd):
            json.dump({"hookSpecificOutput": {
                "hookEventName": "PostToolUse",
                "additionalContext": NOTE.format(how=how),
            }}, sys.stdout)
            return


if __name__ == "__main__":
    main()
