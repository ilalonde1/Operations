"""post-rewrite guard for the SHARED checkouts: shout when a session rewrites a commit that is not its own.

On 2026-10-29 23:16:39 I committed 981762c in the shared KOR.Drafter checkout, went away to verify two facts, and
ran `git commit --amend` at 23:21:28. The Virtual Drafter had committed 2bd0768 in between. HEAD was no longer mine,
so the amend rewrote THEIR commit: their jobs/31180-01-halifax/job.json change was swallowed into my documentation
commit and their message, author line and Claude-Session trailer were destroyed. An amend takes whatever HEAD IS,
not the commit you remember making. Nothing had been pushed; `git reset` (mixed) restored it.

⚠ WHY THIS IS A post-rewrite HOOK AND NOT A REFUSAL. The obvious design - refuse the amend in prepare-commit-msg -
CANNOT BE BUILT, and I know because I measured it before writing this rather than after (Git 2.x, probe in
scratchpad/hookargs):

    git commit -m ...              prepare-commit-msg: source=[message] sha=[]
    git commit --amend -m ...      prepare-commit-msg: source=[message] sha=[]      <- identical to a plain commit
    git commit --amend -F file     prepare-commit-msg: source=[message] sha=[]      <- the form this repo uses
    git commit --amend --no-edit   prepare-commit-msg: source=[commit]  sha=[HEAD]
    git commit -C HEAD             prepare-commit-msg: source=[commit]  sha=[HEAD]  <- NOT an amend, same signature

The GIT_* environment is byte-identical across all of them (7 variables, none of them about amending). So a
prepare-commit-msg guard would have waved through every amend this repo actually makes and refused ordinary -C
commits: a guard in name only. post-rewrite is the one hook git calls on all three amend forms, with "amend" as its
argument and "<old> <new>" per line on stdin.

WHAT IT COVERS: every `git commit --amend` and every `git rebase` in a clone where this hook is installed, naming
each rewritten commit whose Claude-Session trailer is another session's, with the exact command to put it back.
WHAT IT DOES NOT: it cannot REFUSE - git has already written the new commit and moved HEAD when this runs, and
nothing a post-rewrite hook returns changes that. It is an alarm with a recovery line, not a gate. It also does not
see `git reset --hard`, `git checkout`, a rewrite in another clone, or `--no-verify`.
A SAME-CLASS FAULT IT WOULD MISS: `git reset --hard <older>` in the shared tree, which destroys other sessions'
commits AND their uncommitted files without any hook firing at all. The rule in memory still has to carry that one.

Exit status is always 0: git ignores it, and a non-zero exit here only adds noise to a commit that already happened.
"""
import os
import subprocess
import sys

TRAILER = "Claude-Session"


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True).stdout.strip()


def owner_of(sha):
    """The session that wrote a commit, by its trailer - never %an, which reads 'Ian Lalonde' on all of them."""
    line = git("log", "-1", f"--format=%(trailers:key={TRAILER},valueonly)", sha)
    return line.strip().rsplit("/", 1)[-1] or None


def main():
    kind = sys.argv[1] if len(sys.argv) > 1 else ""
    me = os.environ.get("CLAUDE_CODE_BRIDGE_SESSION_ID", "").strip()
    if not me:
        return 0                                   # Ian at a terminal: not a session, not this hook's business

    pairs = []
    for line in sys.stdin.read().splitlines():
        parts = line.split()
        if len(parts) >= 2 and parts[0] != parts[1]:
            pairs.append((parts[0], parts[1]))
    if not pairs:
        return 0

    foreign, unattributed = [], []
    for old, new in pairs:
        who = owner_of(old)
        if who is None:
            unattributed.append((old, new))
        elif who != me:
            foreign.append((old, new, who))
    if not foreign and not unattributed:
        return 0

    say = sys.stderr.write
    say("\n" + "=" * 100 + "\n")
    # ASCII only: stderr under Windows falls back to backslashreplace, and the first version of this printed a
    # literal "⛔" where a symbol was meant. A guard that looks broken is a guard the next session argues with.
    say(f"  STOP - THAT {kind.upper() or 'REWRITE'} REWROTE {len(foreign) + len(unattributed)} COMMIT(S) THAT ARE NOT YOURS.\n")
    say("=" * 100 + "\n")
    for old, new, who in foreign:
        say(f"\n  {old[:8]} -> {new[:8]}   owner: {who}\n")
        say(f"      {git('log', '-1', '--format=%s', old)[:110]}\n")
    for old, new in unattributed:
        say(f"\n  {old[:8]} -> {new[:8]}   owner: NO {TRAILER} trailer - it may be Ian's own commit\n")
        say(f"      {git('log', '-1', '--format=%s', old)[:110]}\n")
    first_old = (foreign or unattributed)[0][0]
    say("\n  The original commits still exist. To put the oldest one back WITHOUT touching anyone's files:\n")
    say(f"      git reset {first_old[:8]}\n")
    say("  (mixed, NOT --hard: --hard in a shared checkout reverts other sessions' uncommitted work too.)\n")
    say("  Then re-make your own change as a NEW commit, and tell the owner what happened.\n")
    say("\n  An amend takes whatever HEAD IS, not the commit you remember making. In a shared checkout,\n")
    say("  never amend - make a new commit.\n")
    say("=" * 100 + "\n\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
