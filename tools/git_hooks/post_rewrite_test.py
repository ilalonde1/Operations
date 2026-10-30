"""post_rewrite.py, and the measurement its design rests on.

Part A re-runs the probe live in a throwaway repo instead of trusting the paragraph in post_rewrite.py's header.
The claim being held true is a NEGATIVE one - "prepare-commit-msg cannot tell an amend from a plain commit" - and a
negative claim about a tool is exactly the kind that quietly stops being true when the tool is upgraded. If a future
git starts distinguishing them, Part A fails and somebody gets to build the refusing guard that cannot be built today.

Part B runs the hook against REAL commits in the shared KOR.Drafter checkout, including the one that caused it:
2bd0768, the Virtual Drafter's commit that my 23:21 amend rewrote.

COVERS: the probe's findings; foreign / own / unattributed owners; rebase as well as amend; the no-session case;
pairs where old == new (an amend that changed nothing).
DOES NOT COVER: that the hook is INSTALLED anywhere (the shim is a separate step), nor that git really invokes it in
the shared clones - Part A proves git's behaviour in a fresh repo, not that .git/hooks/post-rewrite exists next door.
WOULD NOT CATCH: a trailer that is forged or copied between sessions, and `git reset --hard`, which destroys commits
and uncommitted files with no hook firing at all.

    python -I tools/git_hooks/post_rewrite_test.py [path to a shared checkout]     -> exit 1 on any failure
"""
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
HOOK = HERE / "post_rewrite.py"
DEFAULT_REPO = pathlib.Path(r"C:\VIsual Studio Projects\KOR.Drafter")
THE_AMEND_VICTIM = "2bd0768"        # the Virtual Drafter's commit my 2026-10-29 23:21 amend rewrote

results = []


def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print(f"{'ok  ' if ok else 'FAIL'}  {name}" + (f"\n        {detail}" if detail and not ok else ""))


def git(repo, *args, stdin=None):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, input=stdin)


# ----------------------------------------------------------------------------- Part A: what git actually tells hooks
LOGGER = """#!/bin/sh
echo "$HOOKNAME|$2|$3" >> "$(git rev-parse --git-dir)/probe.log"
"""
REWRITE_LOGGER = """#!/bin/sh
echo "post-rewrite|$1|$(cat | tr '\\n' ';')" >> "$(git rev-parse --git-dir)/probe.log"
"""


def probe():
    d = pathlib.Path(tempfile.mkdtemp(prefix="korhookprobe"))
    try:
        subprocess.run(["git", "init", "-q", str(d)], check=True, capture_output=True)
        git(d, "config", "user.email", "probe@example.com")
        git(d, "config", "user.name", "Probe")
        hooks = d / ".git" / "hooks"
        hooks.mkdir(parents=True, exist_ok=True)
        (hooks / "prepare-commit-msg").write_text(LOGGER.replace("$HOOKNAME", "prepare"), newline="\n")
        (hooks / "post-rewrite").write_text(REWRITE_LOGGER, newline="\n")
        for h in ("prepare-commit-msg", "post-rewrite"):
            os.chmod(hooks / h, 0o755)

        (d / "a.txt").write_text("one\n")
        git(d, "add", "a.txt")
        git(d, "commit", "-q", "-m", "first")
        plain = read_log(d)

        git(d, "commit", "-q", "--amend", "-m", "amended by -m")
        by_m = read_log(d)

        git(d, "commit", "-q", "--amend", "--no-edit")
        no_edit = read_log(d)

        (d / "msg.txt").write_text("from a file\n")
        git(d, "commit", "-q", "--amend", "-F", str(d / "msg.txt"))
        by_f = read_log(d)

        prep = lambda rows: next((r for r in rows if r[0] == "prepare"), None)
        rew = lambda rows: next((r for r in rows if r[0] == "post-rewrite"), None)

        check("probe: a plain commit tells prepare-commit-msg source=message, no sha",
              prep(plain) == ("prepare", "message", ""), str(prep(plain)))
        check("probe: `--amend -m` is INDISTINGUISHABLE from a plain commit there",
              prep(by_m) == prep(plain), f"amend -m gave {prep(by_m)}, plain gave {prep(plain)}")
        check("probe: `--amend -F` (the form this repo uses) is indistinguishable too",
              prep(by_f) == prep(plain), f"amend -F gave {prep(by_f)}")
        check("probe: `--amend --no-edit` looks like `commit -C HEAD`, not like an amend",
              prep(no_edit) == ("prepare", "commit", "HEAD"), str(prep(no_edit)))
        check("probe: post-rewrite fires with 'amend' for -m, --no-edit AND -F",
              all(r and r[1] == "amend" for r in (rew(by_m), rew(no_edit), rew(by_f))),
              f"{rew(by_m)} / {rew(no_edit)} / {rew(by_f)}")
        check("probe: post-rewrite does NOT fire for a plain commit",
              rew(plain) is None, str(rew(plain)))
    finally:
        shutil.rmtree(d, ignore_errors=True)


def read_log(d):
    p = d / ".git" / "probe.log"
    rows = [tuple(line.split("|")) for line in p.read_text().splitlines()] if p.exists() else []
    p.unlink(missing_ok=True)
    return rows


# ------------------------------------------------------------------- Part B: the hook against real shared history
def run_hook(repo, kind, pairs, session):
    env = dict(os.environ)
    env.pop("CLAUDE_CODE_BRIDGE_SESSION_ID", None)
    if session:
        env["CLAUDE_CODE_BRIDGE_SESSION_ID"] = session
    return subprocess.run([sys.executable, "-I", str(HOOK), kind], cwd=str(repo), env=env,
                          capture_output=True, text=True,
                          input="".join(f"{o} {n}\n" for o, n in pairs))


def owner_of(repo, sha):
    r = git(repo, "log", "-1", "--format=%(trailers:key=Claude-Session,valueonly)", sha)
    return r.stdout.strip().rsplit("/", 1)[-1] or None


def against_real_history(repo):
    head = git(repo, "rev-parse", "HEAD").stdout.strip()
    victim = git(repo, "rev-parse", THE_AMEND_VICTIM).stdout.strip()
    victim_owner = owner_of(repo, victim) if victim else None

    # The ACTING session is this process's own, never "whoever owns HEAD" - HEAD belongs to whichever session
    # committed last, which is the proxy mistake this whole hook exists because of. Falling back, pick any session
    # id in recent history that is NOT the victim's owner, so the cross-session case is always real.
    mine = os.environ.get("CLAUDE_CODE_BRIDGE_SESSION_ID", "").strip().rsplit("/", 1)[-1]
    if not mine or mine == victim_owner:
        for line in git(repo, "log", "-80", "--format=%H").stdout.split():
            o = owner_of(repo, line)
            if o and o != victim_owner:
                mine = o
                break
    if not mine:
        check("real history: a session id to act as, distinct from the victim's owner", False,
              f"found none in the last 80 commits; victim owner is {victim_owner!r}")
        return
    print(f"      acting as {mine},  victim {THE_AMEND_VICTIM} owned by {victim_owner}\n")

    if victim_owner and victim_owner != mine:
        out = run_hook(repo, "amend", [(victim, head)], mine)
        check(f"THE REAL CASE: amending {THE_AMEND_VICTIM} (owner {victim_owner}) as {mine[:18]} is called out",
              victim_owner in out.stderr and THE_AMEND_VICTIM[:8] in out.stderr, out.stderr[:200])
        check("  ... and it prints the exact recovery command",
              f"git reset {victim[:8]}" in out.stderr, out.stderr[-300:])
        check("  ... and it warns against --hard in a shared checkout",
              "--hard" in out.stderr, out.stderr[-200:])
        out2 = run_hook(repo, "amend", [(victim, head)], victim_owner)
        check("  ... while the OWNER amending their own commit is silent",
              out2.stderr.strip() == "", out2.stderr[:200])
    else:
        check(f"real history: {THE_AMEND_VICTIM} is present and owned by another session", False,
              f"resolved to {victim!r}, owner {victim_owner!r} (mine is {mine!r})")

    own = None
    for line in git(repo, "log", "-40", "--format=%H").stdout.split():
        if owner_of(repo, line) == mine:
            own = line
            break
    if own:
        out = run_hook(repo, "amend", [(own, head)], mine)
        check("amending my OWN commit says nothing", out.stderr.strip() == "", out.stderr[:200])

    out = run_hook(repo, "amend", [(victim, head)], None)
    check("no CLAUDE_CODE_BRIDGE_SESSION_ID (Ian at a terminal) says nothing", out.stderr.strip() == "", out.stderr[:200])

    out = run_hook(repo, "amend", [(victim, victim)], mine)
    check("an amend that changed nothing (old == new) says nothing", out.stderr.strip() == "", out.stderr[:200])

    if own and victim_owner and victim_owner != mine:
        out = run_hook(repo, "rebase", [(own, head), (victim, head)], mine)
        check("a REBASE over a mix names only the foreign commit",
              victim[:8] in out.stderr and own[:8] not in out.stderr, out.stderr[:300])
        check("  ... and says REBASE, not AMEND", "REBASE" in out.stderr, out.stderr[:160])

    no_trailer = None
    for line in git(repo, "log", "-300", "--format=%H").stdout.split():
        if owner_of(repo, line) is None:
            no_trailer = line
            break
    if no_trailer:
        out = run_hook(repo, "amend", [(no_trailer, head)], mine)
        check("a commit with NO trailer is reported as possibly Ian's, not silently allowed",
              "NO Claude-Session" in out.stderr, out.stderr[:200])
    else:
        print("note  no trailer-less commit in the last 300 - that case is untested here")


def main():
    repo = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_REPO
    print(f"probe: a throwaway repo;  real history: {repo}\n")
    probe()
    print()
    if (repo / ".git").exists():
        against_real_history(repo)
    else:
        check(f"the checkout at {repo} exists", False, "pass one as an argument")
    bad = sum(1 for _, ok, _ in results if not ok)
    print(f"\n{len(results) - bad} of {len(results)} passed")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
