r"""pre-push guard for the SHARED Operations clone: a session may push only its own commits.

Five Claude sessions commit into one working copy of develop. On 2026-10-27 18:40 Revit Fleet's push carried the PDF
Ingestor's unsettled d4352fc0 to GitHub, and at 22:1x the local stack held five commits from three sessions, so
whoever pushed first would have published everyone's. A push of any sha carries every ancestor, so `<sha>:develop`
does not isolate anything but a prefix.

How it decides: git hands this hook "<local ref> <local sha> <remote ref> <remote sha>" per ref on stdin. The range is
remote_sha..local_sha (the REMOTE's own sha, never a stale local origin/*); for a new branch, every commit not on any
remote-tracking ref. Each commit's owner is its "Claude-Session:" trailer. The pusher is CLAUDE_CODE_BRIDGE_SESSION_ID,
the same id the trailer carries. A commit whose trailer names another session is refused, listed with its owner.

Allowed through:
  - a push from outside a Claude session (CLAUDE_CODE_BRIDGE_SESSION_ID unset): Ian at a terminal. This ownership check
    is unchanged; the review guard (Framework tools/git_hooks/pre_push_review.py) checks every push whoever runs it.
  - commits listed in EXCEPTIONS_FILE (C:\KOR-Operations\push-exceptions.txt, one full or 7+ hex sha per line, '#' starts
    a comment): the owners' OK in hand, e.g. the lead's one push of a stack every owner approved. Sessions WRITE the
    file (Ian 19 Nov #134752, Framework row 2188), so a listed sha carries past THIS ownership check on the writer's word
    alone; what still holds it is the review guard, which refuses every carried commit without an independent 'approve'
    (a listed one only after `kor_review.py confirm-exceptions` has checked its evidence). A missing or unreadable file
    means none. There is no "carry everything" switch, and no environment variable carries a sha:
    KOR_PUSH_CARRY_OK was removed 18 Nov (gap 1 phase 2) because the session that wanted the carry could set it itself.

What it does NOT catch (stated, so nobody reads more into it):
  - a commit with NO trailer is refused for a session (its owner cannot be known; authorship is "Ian Lalonde" on all
    of them), so it must be listed in the exceptions file (a session writes the line) - it cannot tell Ian's commit
    from a session that forgot its trailer.
  - a forged or copied trailer.
  - a session carrying another session's APPROVED commit by listing it: the file is session-writable by design, and
    the review guard passes an approved commit whoever pushes it. The same-class fault this would miss: a session able
    to write any file this hook trusts (the hook itself, the shim in .git/hooks), or one running as kor\ilalonde.
  - pushes from any other clone, or `--no-verify`.
Exit 1 refuses the push; the message names every foreign commit and its session.
"""
import os, re, subprocess, sys

ZERO = "0" * 40
# The push exceptions (session-written, Ian 19 Nov #134752), shared with the review guard. Not overridable from the environment.
EXCEPTIONS_FILE = r"C:\KOR-Operations\push-exceptions.txt"

def exceptions(path):
    """The shas listed in path: lower-case, 7-40 hex each. Missing or unreadable file: none. UTF-8 or, with its BOM,
    UTF-16 (Windows PowerShell's `>` writes it). A line that is not a sha is named and ignored."""
    try:
        with open(path, "rb") as f: raw = f.read()
    except FileNotFoundError: return set()
    except OSError as e:
        print(f"ownership guard: {path} cannot be read ({e}) - no exceptions apply", file=sys.stderr); return set()
    out = set()
    text = raw.decode("utf-16" if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else "utf-8-sig", errors="replace")
    for n, line in enumerate(text.splitlines(), 1):
        t = line.split("#", 1)[0].strip().lower()
        if not t: continue
        if 7 <= len(t) <= 40 and all(c in "0123456789abcdef" for c in t): out.add(t)
        else: print(f"ownership guard: {path} line {n} is not a 7-40 hex sha, ignored: {line.strip()[:60]}", file=sys.stderr)
    return out

def git(*a):
    return subprocess.run(["git", *a], capture_output=True, text=True, check=True).stdout

def owner(sha):
    body = git("log", "-1", "--format=%B", sha)
    m = re.findall(r"^Claude-Session:\s*\S*?(session_[A-Za-z0-9]+)\s*$", body, re.M)
    return m[-1] if m else None

def main(stdin=sys.stdin, env=os.environ, exceptions_file=EXCEPTIONS_FILE):
    me = env.get("CLAUDE_CODE_BRIDGE_SESSION_ID", "").strip()
    if not me: return 0
    ok = exceptions(exceptions_file)
    foreign = []
    for line in stdin:
        parts = line.split()
        if len(parts) != 4: continue
        _, lsha, rref, rsha = parts
        if lsha == ZERO: continue                     # a delete: nothing carried
        # The remote moved after this clone's last fetch: its tip is not in the local object store, so the range cannot
        # be read (rev-list exits 128 -- until 28 Oct that surfaced as a Python traceback). Refuse, and say how to go on.
        if rsha != ZERO and subprocess.run(["git", "cat-file", "-e", f"{rsha}^{{commit}}"], capture_output=True).returncode != 0:
            print(f"PUSH REFUSED - the remote's {rref} is at {rsha[:8]}, which this clone has not fetched (someone pushed "
                  "since your last fetch): git fetch, put your own commits on top of it, and push again.", file=sys.stderr)
            return 1
        rng = [lsha, "--not", "--remotes"] if rsha == ZERO else [f"{rsha}..{lsha}"]
        for sha in git("rev-list", *rng).split():
            if any(sha.lower().startswith(o) for o in ok): continue
            who = owner(sha)
            if who != me: foreign.append((sha[:8], who or "no Claude-Session trailer", git("log", "-1", "--format=%s", sha).strip()[:70], rref))
    if not foreign: return 0
    print("PUSH REFUSED - this push carries commits that are not this session's:", file=sys.stderr)
    for sha, who, subj, rref in foreign: print(f"  {sha}  {who}  -> {rref}  {subj}", file=sys.stderr)
    print(f"Push only your own commits, or get each owner's OK and list the shas in {exceptions_file}, each with its evidence in a '# ...' comment on its line or just above it; "
          "the review guard still needs each one approved (kor_review.py confirm-exceptions checks a listed one).",
          file=sys.stderr)
    return 1

if __name__ == "__main__":
    sys.exit(main())
