"""pre-push guard for the SHARED Operations clone: a session may push only its own commits.

Five Claude sessions commit into one working copy of develop. On 2026-10-27 18:40 Revit Fleet's push carried the PDF
Ingestor's unsettled d4352fc0 to GitHub, and at 22:1x the local stack held five commits from three sessions, so
whoever pushed first would have published everyone's. A push of any sha carries every ancestor, so `<sha>:develop`
does not isolate anything but a prefix.

How it decides: git hands this hook "<local ref> <local sha> <remote ref> <remote sha>" per ref on stdin. The range is
remote_sha..local_sha (the REMOTE's own sha, never a stale local origin/*); for a new branch, every commit not on any
remote-tracking ref. Each commit's owner is its "Claude-Session:" trailer. The pusher is CLAUDE_CODE_BRIDGE_SESSION_ID,
the same id the trailer carries. A commit whose trailer names another session is refused, listed with its owner.

Allowed through:
  - a push from outside a Claude session (CLAUDE_CODE_BRIDGE_SESSION_ID unset): Ian at a terminal.
  - commits listed in KOR_PUSH_CARRY_OK (comma-separated short or full shas): the owners' OK in hand, e.g. the lead's
    one push of a stack every owner approved. Name the shas; there is no "carry everything" switch.

What it does NOT catch (stated, so nobody reads more into it):
  - a commit with NO trailer is refused for a session (its owner cannot be known; authorship is "Ian Lalonde" on all
    of them), so a session must name it in KOR_PUSH_CARRY_OK - it cannot tell Ian's commit from a session that forgot
    its trailer.
  - a forged or copied trailer, or a session that sets the override without the owner's OK.
  - pushes from any other clone, or `--no-verify`.
Exit 1 refuses the push; the message names every foreign commit and its session.
"""
import os, re, subprocess, sys

ZERO = "0" * 40

def git(*a):
    return subprocess.run(["git", *a], capture_output=True, text=True, check=True).stdout

def owner(sha):
    body = git("log", "-1", "--format=%B", sha)
    m = re.findall(r"^Claude-Session:\s*\S*?(session_[A-Za-z0-9]+)\s*$", body, re.M)
    return m[-1] if m else None

def main(stdin=sys.stdin, env=os.environ):
    me = env.get("CLAUDE_CODE_BRIDGE_SESSION_ID", "").strip()
    if not me: return 0
    ok = {s.strip().lower() for s in env.get("KOR_PUSH_CARRY_OK", "").split(",") if s.strip()}
    foreign = []
    for line in stdin:
        parts = line.split()
        if len(parts) != 4: continue
        _, lsha, rref, rsha = parts
        if lsha == ZERO: continue                     # a delete: nothing carried
        rng = [lsha, "--not", "--remotes"] if rsha == ZERO else [f"{rsha}..{lsha}"]
        for sha in git("rev-list", *rng).split():
            if any(sha.lower().startswith(o) for o in ok): continue
            who = owner(sha)
            if who != me: foreign.append((sha[:8], who or "no Claude-Session trailer", git("log", "-1", "--format=%s", sha).strip()[:70], rref))
    if not foreign: return 0
    print("PUSH REFUSED - this push carries commits that are not this session's:", file=sys.stderr)
    for sha, who, subj, rref in foreign: print(f"  {sha}  {who}  -> {rref}  {subj}", file=sys.stderr)
    print("Push only your own commits, or get each owner's OK and name the shas in KOR_PUSH_CARRY_OK.", file=sys.stderr)
    return 1

if __name__ == "__main__":
    sys.exit(main())
