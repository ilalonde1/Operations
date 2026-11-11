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

Row 2010 (10 Nov 2026): also nothing is said when the OUTPUT proves the cut dropped nothing - the cut is the last stage
of its pipeline (no later |, no stdout redirect such as > f or >&2, not closed by a ) or inside backticks), its limit can be read (head/tail -N or -n N, bare head/tail = 10,
sed -n '1,Np', Select-Object -First N), and the command printed fewer lines than that limit. Measured on
1,268 transcripts written 3-10 Nov: 6,387 notices; 1,330 of them were such cuts and are now silent, 5,057 still print
(head 2,227, tail 1,598, sed -n 1,210, grep -m 22). Behind the 6,387: 653 printed 0-1 lines, 1,025 2-5, 2,232 6-20,
2,477 more - so small results were about a quarter, not the bulk, and every one still noticed is a cut that may have
dropped a line. What it does NOT cover: a cut whose limit cannot be read (tail -n +N, grep -m, a sed window not from
line 1, head -c) or that is followed by another stage - noticed as before, even when nothing was cut; a | tail -1
read for a test's own "X of Y passed" line is still noticed (the line counts the population, the guard cannot tell).
Same-class fault it would MISS: a cut whose kept lines END in blank lines that the tool trims from its stdout - `| head
-5` keeping three lines and two blanks reads as three printed, so a cut sixth line goes unnoticed. A payload with no
tool_response falls back to the notice on every cut, as before.

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


# Row 2010: the limit a cut keeps, read off the cut itself - head/tail -N, -n N, bare (10), sed -n '1,Np',
# Select-Object -First N. None when it cannot be read (tail -n +N, grep -m, a sed window not starting at line 1).
LIMIT = [
    (re.compile(r"\|\s*(?:head|tail)(?:\s+-n)?\s*(?:-(\d+)|(\d+))?(?=\s*(?:$|[|;&)\n]))"), 10),
    (re.compile(r"sed\s+-n\s+['\"]?1\s*,\s*(\d+)p"), None),
    (re.compile(r"Select-Object\s+-First\s+(\d+)"), None),
]
STAGE_END = re.compile(r"[;\n)]|&&|\|\|")
REDIRECT = re.compile(r"(?<!2)>")   # every stdout redirect: > f, >> f, >&2, &> f; not 2>&1 or 2>/dev/null


def kept_whole(cmd, start, out_lines):
    """True when the cut starting at `start` provably removed nothing: it is the LAST stage of its pipeline (no later
    | that could filter) and the command printed fewer lines than the cut keeps. A cut that keeps 5 lines of a command
    that printed 3 kept everything; one that printed 5 may have dropped the sixth."""
    for pat, bare in LIMIT:
        m = pat.match(cmd, start) or pat.search(cmd, start, start + 40)
        if not m or m.start() > start + 2:
            continue
        nums = [g for g in m.groups() if g]
        n = int(nums[0]) if nums else bare
        if n is None:
            return False
        end = STAGE_END.search(cmd, m.end())
        rest = cmd[m.end():end.start() if end else len(cmd)]
        # a later stage could filter; a > redirect (a file, or >&2) sends the cut's lines where stdout does not count
        # them; a cut closed by ')' or inside backticks feeds $( ), <( ) or a group whose printing is another command's
        # (review row 1419: `for f in $(ls | head -5); do ...`)
        if "|" in rest or REDIRECT.search(rest) or (end and end.group(0) == ")") or cmd.count("`", 0, start) % 2:
            return False
        return out_lines < n
    return False


def output_lines(payload):
    r = payload.get("tool_response")
    text = r.get("stdout", "") if isinstance(r, dict) else (r if isinstance(r, str) else None)
    if text is None:
        return None
    return len(str(text).splitlines())   # every line, blank ones too: a blank line the cut kept is a line it kept


def first_cut(cmd, out_lines):
    """The name of the first cut that may have dropped something, or None (row 2010: proven-whole cuts are skipped)."""
    for pattern, how in TRUNCATES:
        for m in pattern.finditer(cmd):
            if out_lines is None or not kept_whole(cmd, m.start(), out_lines):
                return how
    return None


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return

    cmd = (payload.get("tool_input") or {}).get("command") or ""
    if not cmd or COUNTS.search(cmd):
        return

    how = first_cut(cmd, output_lines(payload))
    if how:
        json.dump({"hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": NOTE.format(how=how),
        }}, sys.stdout)


if __name__ == "__main__":
    main()
