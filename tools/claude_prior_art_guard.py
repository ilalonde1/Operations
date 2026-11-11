"""PreToolUse guard: refuse to CREATE a recurring artefact before (1) searching for the one
that already exists and (2) READING one of its siblings.

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

--------------------------------------------------------------------------------
THE SECOND CHECK, added 2026-10-13, because the first one was satisfied and useless.

Asked for a Codex audit of the BD modules, I ran `ls docs/codex/`, saw 215 filenames, and
then invented a format that does not exist in this repo: seven "slice" files, a separate
protocol file, per-file byte budgets, and an explicit ban on database access -- when every
real brief in that directory is ONE file, scoped in tiers, and hands Codex the connection
string. Ian: "WTF is that???? When have I ever done 'slices' of codex prompts????????????"
The convention was proven 215 times and sitting in the directory I had just listed. It took
thirty seconds to get right once I opened one.

This guard ALLOWED all of it, correctly by its old contract, and its own header already
named the hole: "Whether the prior art was READ, or understood, or cited. It proves a look
happened." Listing a directory proves you know a family exists. It does not stop you
inventing a new format inside it, which is the more expensive mistake -- a duplicated
artefact wastes an hour, a wrong format wastes the reviewer's trust.

So for a file joining an ESTABLISHED FAMILY, looking is no longer enough: you must have
opened a member of that family in this session.

--------------------------------------------------------------------------------
WHAT IT BLOCKS, on a Write that creates or wholly rewrites a file under a guarded directory
(`docs/`, `tools/`, any `Schema/*.sql`, `db/*.sql`):

  (1) NOTHING LOOKED FOR -- nothing in this session has searched for the prior art.
  (2) FAMILY NOT READ -- the filename joins a family of >= 3 existing siblings sharing its
      filing prefix (`CODEX-*`, `claude_*`, a numbered migration `341_*`), and nothing in
      this session has READ any of them.

WHAT COUNTS AS LOOKING, either is enough:
  1. LISTING the target's directory -- `ls docs/codex/`, a Glob of `docs/**`, `find tools/`.
  2. A CONTENT SEARCH sharing a significant word with the new filename.

WHAT COUNTS AS READING A SIBLING, either is enough:
  1. The `Read` tool on that file.
  2. A Bash/PowerShell command with a read verb (`cat`, `head`, `tail`, `sed`, `more`,
     `type`, `Get-Content`, `gc`) that names the file. The verb is required, so `grep -c
     "CODEX-FOO.md" x` does not count -- that is a mention, not a read.

WHAT IT DOES NOT COVER, deliberately:
  - `Edit`. Editing a file already read is ordinary work; only creation and wholesale
    rewrite are the moment this fault happens.
  - Whether the sibling was UNDERSTOOD or actually imitated. It proves you opened one. On
    2026-10-13 opening one was the whole difference, so that is the bar -- but a careless
    read still passes.
  - Code files outside the guarded directories.
  - A file written by a redirect inside a Bash heredoc, unless the target is on the
    command line.
  - ⚠ MENTIONING a path satisfies the LOOKING check (not the reading one). The whole
    command is recorded, so `grep -c "docs/" somefile` counts as having looked at `docs/`.
    Left permissive on purpose: tightening it means parsing every shell form of "list a
    directory", and a false DENY is worse than the rule is worth.
  - ⚠ The family key is a PREFIX heuristic. A family that does not share a leading token
    is invisible to it -- forty Codex artefacts once lived under six conventions, and only
    the `CODEX-*` ones would be linked here. It also cannot know that `docs/dossiers/` and
    `docs/codex/` are different KINDS of thing; it only groups by prefix within one
    directory.
  - ⚠ A same-class fault NEITHER check catches: searching the right directory with the
    wrong word, finding nothing, and concluding absence. The guard forces a look and a
    read, not a good one.

--------------------------------------------------------------------------------
THE THIRD CHECK, added 2026-11-10 (Framework OWED lane, mistakes 145 and 141), switched on
by `--content-search` on the Write hook's command line (wired in Framework and Operations;
KOR.Drafter after its reruns - D12).

Mistake 145, 10 Nov 21:4x: OverLORD told Ian the prior Codex audits did not cover a scope,
from a FILENAME search two folders deep whose output was cut to 20 of 333 hits, then launched
a lane to write a new brief. Ian: "that's not really a search", "It's dangerous". The first
check passed it: a listing counts as looking. Mistake 141, 4 Nov: three migrations (382-384)
and a 2-day build were put to Ian while IntelPersistenceService and BdResearchImport already
did it - the guard passed because the Schema folder had been LISTED.

  (4) NO CONTENT SEARCH -- CREATING (the file does not exist yet) one of:
        * anything under docs/codex/
        * a doc under docs/ whose name says brief / plan / audit / proposal / design /
          research / spec / review
        * a new migration (Schema/*.sql, db/*.sql)
      needs a CONTENT search in this session -- the Grep tool, or rg / git grep /
      grep -r|-R / Select-String / findstr -- that is NOT truncated (no `| head`, `| tail`,
      `Select-Object -First/-Last`, `-m N` / `--max-count`, Grep head_limit) and that either
      shares a topic word with the new filename or names the target's directory (or a parent
      of it) as its path. `ls`, `find`, `dir`, Glob and Get-ChildItem are filename listings
      and never count here.

  WHAT IT DOES NOT CATCH (same class): a content search with the wrong words over the right
  place, read and misjudged; a search of the right words over too narrow a path that still
  shares a topic word with the filename; a truncation done some other way (a pager, `awk
  'NR<20'`, a later `Select-Object` in a separate command); writing a brief by `Edit` of an
  existing file, or by a Bash redirect. It proves an untruncated content search happened,
  not that it was the right one.

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

# Verbs that mean "I opened this file and its contents came back".
READ_VERB = re.compile(
    r"\b(?:cat|head|tail|sed|more|type|Get-Content|gc)\b", re.IGNORECASE)

# A family needs this many EXISTING siblings before it counts as an established convention.
# Two files are a coincidence; three are a format somebody will expect you to match.
FAMILY_MIN = 3

# Words that carry no topic: structure, extensions, dates, and the CODEX- filing prefix.
STOP = {
    "codex", "response", "readme", "notes", "index", "the", "and", "for", "with", "not",
    "are", "has", "have", "its", "this", "that", "from", "into", "out", "new", "old",
    "doc", "docs", "test", "tests", "sql", "py", "cs", "ps1", "md", "txt", "json",
    "kor", "prompt", "brief", "audit", "fix", "fixes", "part", "step", "steps", "v1",
    "v2", "v3", "final", "draft", "copy", "temp", "tmp", "all", "any", "one", "two",
}

READ_MARK = "read\t"
CONTENT_MARK = "content\t"

# THE THIRD CHECK (mistakes 145, 141): what is a CONTENT search, what truncates one, and which creations need one.
CONTENT_VERB = re.compile(
    r"(?:^|[\s;&|(])(?:rg|ripgrep|git\s+grep|Select-String|sls|findstr)\b"
    r"|(?:^|[\s;&|(])[ef]?grep\b[^|;&]*\s(?:-[A-Za-z]*[rR][A-Za-z]*|--recursive|--dereference-recursive)\b"
    # a grep over a wildcard of files ("grep -n x db/*.sql") searches the area too (dry run, 10 Nov: migration 181)
    # - an UNQUOTED file glob ("dir/*.sql", "*.cs"), not a '*' inside a quoted regex ('def .*foo')
    r"|(?:^|[\s;&|(])[ef]?grep\b[^|;&]*\s(?![\"'])[^\s|;&\"'*]*(?:/[^\s|;&\"']*\*|\*\.[A-Za-z0-9]+)(?=[\s|;&)]|$)",
    re.IGNORECASE)
TRUNCATED = re.compile(
    r"\|\s*(?:head|tail)\b|\bSelect-Object\s+[^|;]*-(?:First|Last)\b|\bselect\s+-(?:first|last)\b"
    r"|(?:^|\s)(?:-m\s*\d+|--max-count(?:=|\s+)\d+)", re.IGNORECASE)
STRICT_DOC_NAME = re.compile(r"(brief|plan|audit|proposal|design|research|spec|review)", re.IGNORECASE)


def norm(p):
    return (p or "").replace("\\", "/")


def state_path(session):
    d = os.path.join(tempfile.gettempdir(), "claude-priorart-seen")
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, (session or "unknown") + ".txt")


def recorded(session):
    path = state_path(session)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return [ln.rstrip("\n").lower() for ln in f if ln.strip()]


def remember(session, text, kind=""):
    if not text:
        return
    with open(state_path(session), "a", encoding="utf-8") as f:
        f.write(kind + " ".join(text.split())[:4000] + "\n")


def tokens(name):
    """Significant topic words in a filename, lowercased."""
    out = set()
    for raw in re.split(r"[^A-Za-z0-9]+", name or ""):
        t = raw.lower()
        if len(t) < 3 or t in STOP or t.isdigit():
            continue
        out.add(t)
    return out


def family_key(basename):
    """The filing prefix that marks a recurring family, or None.

    `CODEX-BD-AND-BD-REPORTS-AUDIT.md` -> "codex"
    `claude_prior_art_guard.py`        -> "claude"
    `341_BackfillDeadlines.sql`        -> "#numbered"
    `README.md`                        -> None  (no prefix separator, so no family)
    """
    stem = basename.rsplit(".", 1)[0] if "." in basename else basename
    if re.match(r"^\d+[_-]", stem):
        return "#numbered"
    m = re.match(r"^([A-Za-z]{2,})[-_.]", stem)
    return m.group(1).lower() if m else None


def family_members(directory, basename):
    """Existing siblings in the same directory sharing the target's filing prefix."""
    key = family_key(basename)
    if not key:
        return None, []
    try:
        names = os.listdir(directory)
    except OSError:
        return key, []
    out = []
    for n in names:
        if n.lower() == basename.lower():
            continue
        if not os.path.isfile(os.path.join(directory, n)):
            continue
        if family_key(n) == key:
            out.append(n)
    # Most recently touched first: that is the live convention, and the one to imitate.
    out.sort(key=lambda n: os.path.getmtime(os.path.join(directory, n)), reverse=True)
    return key, out


PAYLOAD = {}


def deny(reason):
    try:   # F11 (Framework row 1159): every refusal is a row; logging can never change what this guard does
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        guard_log.record("prior_art", reason.split(":")[0][:200], (PAYLOAD.get("tool_input") or {}).get("file_path"), None, payload=PAYLOAD)
    except Exception:
        pass
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.exit(0)


def record(tool, inp, session):
    """Record a look and/or a read. Generous on purpose for looks: a false ALLOW costs one
    duplicated artefact, a false DENY blocks real work, and the second is worse."""
    if tool in ("Grep", "Glob"):
        remember(session, " ".join(str(inp.get(k) or "") for k in ("pattern", "path", "glob")))
        if tool == "Grep" and not inp.get("head_limit"):
            remember(session, norm(" ".join(str(inp.get(k) or "") for k in ("pattern", "path", "glob")))
                     or ".", kind=CONTENT_MARK)
    elif tool == "Read":
        remember(session, norm(str(inp.get("file_path") or "")), kind=READ_MARK)
    elif tool in ("Bash", "PowerShell"):
        command = str(inp.get("command") or "")
        if SEARCH_VERB.search(command):
            remember(session, command)
        if CONTENT_VERB.search(command) and not TRUNCATED.search(command):
            remember(session, norm(command), kind=CONTENT_MARK)
        if READ_VERB.search(command):
            remember(session, norm(command), kind=READ_MARK)


CONTENT_SEARCH_ON = "--content-search" in sys.argv[1:]


def strict_target(target):
    """A creation that the third check covers: docs/codex/*, a brief/plan-like doc under docs/, a migration."""
    t = norm(target)
    base = t.rsplit("/", 1)[-1]
    if re.search(r"(?:^|/)docs/codex/", t, re.IGNORECASE):
        return True
    if re.search(r"(?:^|/)docs/", t, re.IGNORECASE) and STRICT_DOC_NAME.search(base):
        return True
    return bool(GUARDED[2].search(t) or GUARDED[3].search(t))


def relative(path, root):
    shown = path
    if root and shown.lower().startswith(root.lower()):
        shown = shown[len(root):].lstrip("/") or "."
    return '"' + shown + '"' if " " in shown else shown


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    PAYLOAD.update(payload if isinstance(payload, dict) else {})

    tool = payload.get("tool_name") or ""
    inp = payload.get("tool_input") or {}
    session = payload.get("session_id")

    if tool != "Write":
        record(tool, inp, session)
        sys.exit(0)

    target = norm(inp.get("file_path"))
    if not target or not any(p.search(target) for p in GUARDED):
        sys.exit(0)

    directory_raw = target.rsplit("/", 1)[0]
    directory = directory_raw.lower()
    basename = target.rsplit("/", 1)[-1]
    want = tokens(basename)

    root = norm(os.environ.get("CLAUDE_PROJECT_DIR") or "").rstrip("/")
    shown = relative(directory_raw, root)

    lines = recorded(session)
    haystack = [ln for ln in lines if not ln.startswith(READ_MARK)]
    reads = [ln[len(READ_MARK):] for ln in lines if ln.startswith(READ_MARK)]

    # (1) was the target's own directory listed or searched? Match the full path, or the
    # final segment with a slash on either side -- so `ls docs/` counts for docs/x.md and
    # `ls Kor.Opportunities.Data/Schema/` counts for a migration, without a bare segment
    # like "db" matching any command that happens to contain those two letters.
    leaf = directory.rsplit("/", 1)[-1]
    needles = [directory, leaf + "/", "/" + leaf]
    listed = any(n in h for h in haystack for n in needles)
    # (2) did any content search share a topic word with the filename?
    shared = sorted(t for t in want if any(t in h for h in haystack))

    if not (listed or shared or not want):
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

    # (4) THE THIRD CHECK (mistakes 145, 141): creating a brief/plan/codex doc or a migration needs an untruncated
    #     CONTENT search that shares a topic word or names the target's directory or a parent of it.
    if CONTENT_SEARCH_ON and strict_target(target) and not os.path.exists(target):
        contents = [ln[len(CONTENT_MARK):] for ln in lines if ln.startswith(CONTENT_MARK)]
        # The target's directory and its ancestors up to its git root: each as an absolute path, and each
        # below the root also as its path relative to the root ("docs/codex", "docs").
        gitroot = directory_raw
        while gitroot and not os.path.exists(gitroot + "/.git") and "/" in gitroot:
            gitroot = gitroot.rsplit("/", 1)[0]
        if not os.path.exists(gitroot + "/.git"):
            gitroot = root or directory_raw
        rel = directory_raw[len(gitroot):].lstrip("/") if directory.startswith(gitroot.lower()) else ""
        parts = [p for p in rel.lower().split("/") if p]
        prefixes = ["/".join(parts[:i]) for i in range(len(parts), 0, -1)]
        absolutes = [gitroot.lower()] + [gitroot.lower() + "/" + p for p in prefixes]

        def covers(c):
            # a directory named as the search PATH: it ends there ("docs", "docs/", ".../docs/codex"), not a file in it
            end = r"/?(?:\*[^\s\"')]*)?(?=[\"'\s)]|$)"   # "db", "db/", "db/*.sql"
            if any(re.search(re.escape(a) + end, c) for a in absolutes):
                return True
            return any(re.search(r"(?<![\w.-])" + re.escape(p) + end, c) for p in prefixes)
        hit_word = sorted(t for t in want if any(t in c for c in contents))
        hit_dir = any(covers(c) for c in contents)
        if not (hit_word or hit_dir):
            deny(
                "NO CONTENT SEARCH: creating " + target + " -- a new brief, plan, codex doc or migration -- and "
                "nothing in this session has searched file CONTENTS for the prior work (a listing by name, or a "
                "search cut by head/tail/-m/Select-Object -First, does not count).\n"
                "Mistake 145, 10 Nov: OverLORD told Ian prior Codex audits did not cover a scope, from a FILENAME "
                "search cut to 20 of 333 hits, and launched a lane to write a new brief. Ian: \"that's not really "
                "a search\", \"It's dangerous\". Mistake 141, 4 Nov: three migrations and a 2-day build put to Ian "
                "while the pipeline already did it -- the folder had only been listed.\n"
                "Run an untruncated content search over the area, count it, and say it (\"rg -il 'x|y' over N "
                "files in <dirs>: M hits, read K of M\"), then re-issue the write. For example:\n"
                "  rg -il \"" + "|".join(sorted(want)[:3] or ["topic"]) + "\" " + (prefixes[-1] if prefixes else ".")
                + "/ | wc -l\n"
                "Topic words that would satisfy it: " + (", ".join(sorted(want)) or "(none - search the directory)"))

    # (3) does this filename join an established family, and did we open one of its members?
    #     Rewriting a file you have ALREADY READ is ordinary work -- the same reasoning that
    #     leaves Edit unguarded -- so reading the target itself is enough for the target.
    key, members = family_members(directory_raw, basename)
    target_read = any(basename.lower() in r for r in reads)
    if len(members) >= FAMILY_MIN and not target_read:
        if not any(m.lower() in r for r in reads for m in members):
            # Label it the way the family is actually spelled on disk, so the message reads
            # as "claude_* files" / "CODEX-* files" rather than a guessed separator.
            if key == "#numbered":
                label = "numbered migrations"
            else:
                sep = members[0][len(key):len(key) + 1] if members[0][:len(key)].lower() == key else "-"
                label = members[0][:len(key)] + sep + "* files"
            suggest = members[:3]
            deny(
                "FAMILY NOT READ: " + basename + " joins an established family -- "
                + str(len(members)) + " existing " + label + " in " + shown + " -- and nothing "
                "in this session has opened one of them.\n"
                "Listing the directory proves the family exists. It does not stop you inventing "
                "a new format inside it. On 2026-10-13, asked for a Codex audit, I listed "
                "docs/codex/ (215 files), never opened one, and invented seven 'slice' files, a "
                "separate protocol file and per-file byte budgets -- when every real brief there "
                "is ONE file scoped in tiers. Ian: \"When have I ever done 'slices' of codex "
                "prompts????????????\"\n"
                "CLAUDE.md rule 2: read the artefact you were given. Rule 8: match the convention "
                "the repo already has.\n"
                "Read the most recent member, then re-issue the write:\n"
                + "".join("  cat " + relative(directory_raw + "/" + m, root) + "\n"
                          for m in suggest))

    sys.exit(0)


if __name__ == "__main__":
    main()
