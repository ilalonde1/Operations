"""PreToolUse guard: refuse to CREATE a recurring artefact before searching for the one
that already exists.

Why this exists, 2026-10-08. Ian: "Should I trust you? The answer is no. So how do we move
forward constructively? Because if I HADN'T pointed out some of this stuff we would be so
far in the woods we couldn't get out."

Every thing he caught that day was a claim about ABSENCE -- something that already existed
and I had not found:

  * "I'm POSITIVE we looked at this" -- `docs/KOR-EarlySignal-CoverageGap-2026-09-10.md`
    had measured the whole municipal gap a month earlier (13 of 56 municipalities, Metro
    Vancouver 4 of 21), probed Richmond, New Westminster, North Vancouver, Burnaby and the
    rest, characterised the zoning-overlay false positive, written the remaining hunt order,
    and promoted three tools into `tools/BdSourceCoverage/`. A whole wiring plan was
    rewritten without citing it, restating settled questions as open.
  * "But why are you building a deploy script. Presumably it was deployed earlier!!!" --
    `publish-opportunities.ps1` was in the repo root, with a test gate. The search had
    covered `tools/*.ps1` only.
  * "US is NOT zero" -- California had been live for weeks; the query filtered
    SourceType IN (20, 21, 22) and never looked at 18.

Nothing SHIPPED in that session was wrong. The gate, the freshness check and the migration
all had tests and before/after evidence. The failures were all upstream of shipping, in
claims about what exists -- so the remedy is not more care, it is a gate at the moment of
creation.

CLAUDE.md rule 12 already says why prose will not do it: "a rule that lives only in prose
loses to momentum... Recognising a mistake afterwards costs nothing and changes nothing;
the gate is what changes the next action." Rules 7 and 12 have hooks and held. Rules 1 and
2 -- search before you build, read the artefact you were given -- are prose, and failed
three times in one session. This is rule 1 and rule 8's half of that.

WHAT IT BLOCKS: a Write that creates or wholly rewrites a file under a guarded directory
(`docs/`, `tools/`, any `Schema/*.sql`, `db/*.sql`) when nothing in this session has looked
for the prior art.

WHAT COUNTS AS LOOKING, either is enough:
  1. LISTING the target's directory -- `ls docs/codex/`, a Glob of `docs/**`, `find tools/`.
     For a recurring artefact this is the real check, and it is what rule 8 demands:
     "find where the previous ones went before writing a new one".
  2. A CONTENT SEARCH sharing a significant word with the new filename -- Grep, rg,
     Select-String, or a grep in a Bash command.

WHAT IT DOES NOT COVER, deliberately:
  - `Edit`. Editing a file already read is ordinary work; only creation and wholesale
    rewrite are the moment this fault happens.
  - Whether the prior art was READ, or understood, or cited. It proves a look happened.
    Reading one file is not a search and does not count -- that is the point.
  - Code files outside the guarded directories. The duplicated artefacts in this repo's
    history are documents, briefs, tools and migrations.
  - A file written by a redirect inside a Bash heredoc is covered only when the redirect
    target is visible on the command line.
  - ⚠ MENTIONING a path satisfies the directory check. The whole command is recorded, so
    `grep -c "docs/" somefile` counts as having looked at `docs/` without listing it. Found
    within a minute of the guard going live, by its own state file. Left permissive on
    purpose -- tightening it to real listings means parsing every shell form of "list a
    directory", and a false DENY is worse than the rule is worth -- but it means the guard
    is a speed bump against momentum, not a proof of diligence.
  - ⚠ A same-class fault it would NOT catch: searching the right directory with the wrong
    word, finding nothing, and concluding absence. The guard forces a look, not a good one.
    `ls docs/` satisfies it and a careless eye still misses the file.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import json
import os
import re
import sys
import tempfile

# Repo rule 7: every pattern is a raw string.
GUARDED = (
    re.compile(r"(?:^|/)docs/", re.IGNORECASE),
    re.compile(r"(?:^|/)tools/", re.IGNORECASE),
    re.compile(r"(?:^|/)Schema/[^/]+\.sql$", re.IGNORECASE),
    re.compile(r"(?:^|/)db/[^/]+\.sql$", re.IGNORECASE),
)

# Verbs that mean "I went looking", as opposed to "I opened one thing I already knew about".
SEARCH_VERB = re.compile(
    r"\b(?:grep|rg|egrep|fgrep|find|ls|dir|gci|Get-ChildItem|Select-String|findstr)\b",
    re.IGNORECASE)

# Words that carry no topic: structure, extensions, dates, and the CODEX- filing prefix.
STOP = {
    "codex", "response", "readme", "notes", "index", "the", "and", "for", "with", "not",
    "are", "has", "have", "its", "this", "that", "from", "into", "out", "new", "old",
    "doc", "docs", "test", "tests", "sql", "py", "cs", "ps1", "md", "txt", "json",
    "kor", "prompt", "brief", "audit", "fix", "fixes", "part", "step", "steps", "v1",
    "v2", "v3", "final", "draft", "copy", "temp", "tmp", "all", "any", "one", "two",
}


def norm(p):
    return (p or "").replace("\\", "/")


def state_path(session):
    d = os.path.join(tempfile.gettempdir(), "claude-priorart-seen")
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, (session or "unknown") + ".txt")


def looked(session):
    path = state_path(session)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return [ln.strip().lower() for ln in f if ln.strip()]


def remember(session, text):
    if not text:
        return
    with open(state_path(session), "a", encoding="utf-8") as f:
        f.write(" ".join(text.split())[:4000] + "\n")


def tokens(name):
    """Significant topic words in a filename, lowercased."""
    out = set()
    for raw in re.split(r"[^A-Za-z0-9]+", name or ""):
        t = raw.lower()
        if len(t) < 3 or t in STOP or t.isdigit():
            continue
        out.add(t)
    return out


def guarded(path):
    return any(p.search(path) for p in GUARDED)


def deny(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.exit(0)


def record_search(tool, inp, session):
    """Record a look. Generous on purpose: a false ALLOW costs one duplicated artefact,
    a false DENY blocks real work, and the second is worse than the rule is worth."""
    if tool in ("Grep", "Glob"):
        remember(session, " ".join(str(inp.get(k) or "") for k in ("pattern", "path", "glob")))
    elif tool in ("Bash", "PowerShell"):
        command = str(inp.get("command") or "")
        if SEARCH_VERB.search(command):
            remember(session, command)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)

    tool = payload.get("tool_name") or ""
    inp = payload.get("tool_input") or {}
    session = payload.get("session_id")

    if tool != "Write":
        record_search(tool, inp, session)
        sys.exit(0)

    target = norm(inp.get("file_path"))
    if not target or not guarded(target):
        sys.exit(0)

    directory_raw = target.rsplit("/", 1)[0]
    directory = directory_raw.lower()
    basename = target.rsplit("/", 1)[-1]
    want = tokens(basename)

    # The suggestion has to be a command the reader can actually paste, so keep the
    # original case, make it repo-relative where we can, and quote a path with spaces.
    # The matching above stays lowercased; only the message uses this.
    root = norm(os.environ.get("CLAUDE_PROJECT_DIR") or "").rstrip("/")
    shown = directory_raw
    if root and shown.lower().startswith(root.lower()):
        shown = shown[len(root):].lstrip("/") or "."
    if " " in shown:
        shown = '"' + shown + '"'

    haystack = looked(session)
    # (1) was the target's own directory listed or searched? Match the full path, or the
    # final segment with a slash on either side -- so `ls docs/` counts for docs/x.md and
    # `ls Kor.Opportunities.Data/Schema/` counts for a migration, without a bare segment
    # like "db" matching any command that happens to contain those two letters.
    leaf = directory.rsplit("/", 1)[-1]
    needles = [directory, leaf + "/", "/" + leaf]
    listed = any(n in h for h in haystack for n in needles)
    # (2) did any content search share a topic word with the filename?
    shared = sorted(t for t in want if any(t in h for h in haystack))

    if listed or shared or not want:
        sys.exit(0)

    deny(
        "PRIOR ART NOT CHECKED: creating " + target + " and nothing in this session has "
        "looked for the one that already exists.\n"
        "On 2026-10-08 a whole early-signal wiring plan was rewritten while "
        "docs/KOR-EarlySignal-CoverageGap-2026-09-10.md -- which had already measured the "
        "gap, probed the municipalities and written the hunt order -- sat unread. Ian found "
        "it, not the search.\n"
        "CLAUDE.md rule 1: say what you searched. Rule 8: find where the previous ones went "
        "before writing a new one.\n"
        "Do ONE of these, then re-issue the write:\n"
        "  ls " + shown + "/\n"
        "  grep -ril \"" + (sorted(want)[0] if want else "topic") + "\" docs/ tools/\n"
        "Topic words that would satisfy it: " + ", ".join(sorted(want)))


if __name__ == "__main__":
    main()
