"""PostToolUse guard for the Grep TOOL: a result cut off by head_limit cannot support a claim about the whole.

The sibling of claude_bash_sample_guard.py, which stamps `| head`, `| tail`, `grep -m` and friends in a Bash command
but never sees the Grep tool, whose head_limit (250 by default) truncates just as silently.

WHAT IT IS FOR, the failure that was paid for. On 2026-10-27 operations-e6 searched
    Grep  pattern="(?i)amend|retire|supersed"  path=...NetworkOps.Service\\Store  head_limit=15
got 15 lines, all "retire" hits in other files, and told two sessions "nothing in the service implements an amend" --
then offered to build it, and the lead approved the build. The result said 15 of 65 lines; the 50 it did not show
included NetworkOpsStore.Prompts.cs, which implements exactly that (AmendsCardId, db/KorNetworkOps/012).
oversight.Mistake 76. Found by opening the store before coding, not by anything in the loop.

THE SIGNAL. The tool's own result says it was cut: tool_response.appliedLimit is set only when the limit removed
something, and totalLines / numLines give the shown and the whole. So the stamp names both: "showing 15 of 65 lines".
The structured shape is what the transcript records (toolUseResult); no hook in this repo read a Grep payload before
this one, so the text the model sees is accepted too: it ends "[Showing results with pagination = limit: N]".

COVERS: every Grep tool call whose result carries appliedLimit, in every output mode (content, files_with_matches,
count), whether head_limit was given or the default 250 applied.
DRY RUN 2026-10-27 on 4 real transcripts (748 Grep calls): 55 were cut. 52 showed the model a pagination line, and
3 did NOT (40 of 41 files, 30 of 50 files, 250 of 392 lines saved to a file): the model was never told. Structured
form: 33 of the 33 cut calls that have a structured record stamped. Text form: 52 of 52 with a pagination line. 0 of the
693 complete results stamped. The known-bad (15 of 65) stamped in both forms. If the hook is handed only the text, the 3
silent cuts stay invisible, which is the reason the structured form is read first.
DOES NOT COVER:
  - a search that is complete but in the WRONG PLACE or with the WRONG WORD: nothing was cut, so it stays silent,
    and the absence claim it supports is just as false (CLAUDE.md rule 1's second blind spot; claude_absence_guard
    is the check for the claim itself);
  - an `offset` that skips the start of the results (only a cut at the end sets appliedLimit);
  - truncation inside a single over-long line, or a result saved to a file because it was too big for the context.
SAME-CLASS FAULT IT WOULD MISS: a Glob listing cut off at its own result cap -- Glob has no appliedLimit field.

stdin  : the PostToolUse payload
stdout : nothing, or hookSpecificOutput.additionalContext
"""
import json
import re
import sys

# Repo rule 7: raw string. The line the Grep tool appends to a result it cut.
PAGINATED = re.compile(r"\[Showing results with pagination = limit: (\d+)\]")

NOTE = (
    "TRUNCATED GREP RESULT (head_limit {limit}): showing {shown} of {total}. This is a SAMPLE, not the population.\n"
    "Do not write \"all\", \"every\", \"none\", \"only\", \"there is no\", or a range/distribution from it, and do not "
    "close a finding on it.\n"
    "To make a claim about the whole: re-run with head_limit 0, or output_mode \"count\" / \"files_with_matches\" "
    "with head_limit 0, then state it as X of Y."
)


def stamp(payload):
    """The note for a truncated Grep result, or None."""
    if (payload or {}).get("tool_name") != "Grep":
        return None
    r = payload.get("tool_response")
    if isinstance(r, (str, list)):
        text = r if isinstance(r, str) else json.dumps(r)
        m = PAGINATED.search(text)
        return NOTE.format(limit=m.group(1), shown=f"the first {m.group(1)}", total="an unknown total") if m else None
    if not isinstance(r, dict) or r.get("appliedLimit") is None:
        return None
    mode = r.get("mode") or "content"
    unit = "lines" if mode == "content" else ("files" if mode == "files_with_matches" else "entries")
    shown = r.get("numLines") if mode == "content" else r.get("numFiles")
    total = r.get("totalLines") if mode == "content" else r.get("totalFiles")
    if isinstance(shown, int) and isinstance(total, int) and total <= shown:
        return None      # the limit was reached exactly: nothing was cut
    shown_s = f"{shown} {unit}" if isinstance(shown, int) else f"the first {r.get('appliedLimit')} {unit}"
    total_s = f"{total} {unit}" if isinstance(total, int) else "an unknown total"
    return NOTE.format(limit=r.get("appliedLimit"), shown=shown_s, total=total_s)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    note = stamp(payload)
    if note:
        json.dump({"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": note}}, sys.stdout)


if __name__ == "__main__":
    main()
