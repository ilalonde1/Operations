"""Self-tests for claude_prior_art_guard.py.
Run: python tools/claude_prior_art_guard_test.py

Case 1 is the one that caused the LOOK check: on 2026-10-08 the early-signal wiring plan
was rewritten without finding docs/KOR-EarlySignal-CoverageGap-2026-09-10.md, which had
already measured the gap, probed the municipalities and written the hunt order. Ian found
it, not the search.

Case 8 is the one that caused the FAMILY check, and it has FLIPPED. It used to assert that
listing docs/codex/ was enough to write a brief. On 2026-10-13 that is exactly what
happened: 215 filenames listed, none opened, and a format invented that does not exist in
this repo -- seven "slice" files, a separate protocol, per-file byte budgets, and a ban on
database access, when every real brief there is ONE file scoped in tiers and hands Codex
the connection string. Ian: "When have I ever done 'slices' of codex prompts????????????"
So listing alone must now DENY for a file joining a family, and 8b-8d are the allows.

Cases 7, 9, 10, 12, 13, 16 and 17 are the no-false-denial cases. A guard that blocks real
work gets switched off, and then it protects nothing.
"""
import json
import os
import subprocess
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
GUARD = os.path.join(HERE, "claude_prior_art_guard.py")
REPO = os.path.dirname(HERE).replace("\\", "/")

PLAN = "docs/KOR-EarlySignal-Wiring-Plan-2026-10-08.md"
BRIEF = "docs/codex/CODEX-mpi-sources-have-no-freshness-check.md"
MIGRATION = "Kor.Opportunities.Data/Schema/332_RetireBcMpiFetcher.sql"

# Real siblings, so the family check runs against the actual tree rather than a fixture.
SIBLING_BRIEF = "docs/codex/CODEX-arcgis-frozen-ingest.md"
SIBLING_GUARD = "tools/claude_bash_guard.py"
SIBLING_MIGRATION = "Kor.Opportunities.Data/Schema/331_ViewsRespectDismissal.sql"


def run(tool, inp, session, root=REPO):
    payload = {"session_id": session, "tool_name": tool, "tool_input": inp}
    env = dict(os.environ)
    if root is None:
        env.pop("CLAUDE_PROJECT_DIR", None)
    else:
        env["CLAUDE_PROJECT_DIR"] = root
    r = subprocess.run([sys.executable, GUARD], input=json.dumps(payload),
                       capture_output=True, text=True, env=env)
    out = (r.stdout or "").strip()
    if not out:
        return "allow", ""
    d = json.loads(out)["hookSpecificOutput"]
    return d["permissionDecision"], d["permissionDecisionReason"]


def write(path, session):
    """Write to a repo-relative path, as an absolute one, the way the real payload arrives."""
    return run("Write", {"file_path": REPO + "/" + path, "content": "x"}, session)


def bash(command, session):
    return run("Bash", {"command": command}, session)


def read(path, session):
    return run("Read", {"file_path": REPO + "/" + path}, session)


def check(label, got, want):
    ok = got == want
    print(("  PASS  " if ok else "  FAIL  ") + label + ("" if ok else "  (got %r)" % (got,)))
    return ok


def main():
    r = []

    # 1. THE CASE THAT CAUSED THE LOOK CHECK.
    s = "t-" + uuid.uuid4().hex
    verdict, reason = write(PLAN, s)
    r.append(check("rewriting the wiring plan with no search is DENIED", verdict, "deny"))
    r.append(check("the reason names the prior art it would have found",
                   "CoverageGap" in reason, True))

    # 2. A content search sharing a topic word satisfies the LOOK check. docs/ also holds a
    #    KOR-* family, so the family check may still fire -- what matters here is that the
    #    look check is no longer the objection.
    s = "t-" + uuid.uuid4().hex
    run("Grep", {"pattern": "earlysignal|coverage", "path": "docs"}, s)
    _, reason = write(PLAN, s)
    r.append(check("grepping a shared topic word satisfies the LOOK check",
                   "PRIOR ART NOT CHECKED" in reason, False))

    # 3. Listing the directory satisfies the LOOK check -- the rule 8 check.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/", s)
    _, reason = write(PLAN, s)
    r.append(check("`ls docs/` satisfies the LOOK check",
                   "PRIOR ART NOT CHECKED" in reason, False))

    # 3b. docs/ holds an established KOR-* family, so listing alone is not enough; reading a
    #     sibling clears it. This is the 2026-10-13 rule applied outside docs/codex/.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/", s)
    verdict, reason = write(PLAN, s)
    r.append(check("a KOR-* plan after listing alone is DENIED by the family check",
                   verdict, "deny"))
    r.append(check("...the FAMILY check is what fires", "FAMILY NOT READ" in reason, True))
    bash("cat docs/KOR-EarlySignal-CoverageGap-2026-09-10.md", s)
    verdict, _ = write(PLAN, s)
    r.append(check("...and reading the CoverageGap doc clears it", verdict, "allow"))

    # 4. A Codex brief with nothing looked at: the look check fires first.
    s = "t-" + uuid.uuid4().hex
    verdict, reason = write(BRIEF, s)
    r.append(check("a new Codex brief with no search is DENIED", verdict, "deny"))
    r.append(check("...and the LOOK check is what fires",
                   "PRIOR ART NOT CHECKED" in reason, True))

    # 5. Opening ONE file is not a SEARCH -- the original distinction, still true.
    s = "t-" + uuid.uuid4().hex
    bash("cat " + SIBLING_BRIEF, s)
    verdict, reason = write(BRIEF, s)
    r.append(check("reading one file does NOT satisfy the look check", verdict, "deny"))
    r.append(check("...the LOOK check is what fires", "PRIOR ART NOT CHECKED" in reason, True))

    # 6. Nor does unrelated shell work.
    s = "t-" + uuid.uuid4().hex
    bash("git status --porcelain", s)
    bash("dotnet build -nologo", s)
    verdict, _ = write(BRIEF, s)
    r.append(check("unrelated commands do NOT count as looking", verdict, "deny"))

    # 7. NO FALSE DENIAL: the real 332 migration, after listing Schema/ AND reading a
    #    neighbouring migration. Numbered migrations are a family, so both are required.
    s = "t-" + uuid.uuid4().hex
    bash('ls Kor.Opportunities.Data/Schema/ | grep -oE "^[0-9]+" | sort -n | tail -3', s)
    bash("sed -n '1,40p' " + SIBLING_MIGRATION, s)
    verdict, _ = write(MIGRATION, s)
    r.append(check("migration after listing Schema/ AND reading one is allowed",
                   verdict, "allow"))

    # 7b. The same migration with ONLY the listing is now denied.
    s = "t-" + uuid.uuid4().hex
    bash("ls Kor.Opportunities.Data/Schema/", s)
    verdict, reason = write(MIGRATION, s)
    r.append(check("migration after listing alone is DENIED", verdict, "deny"))
    r.append(check("...labelled as numbered migrations",
                   "numbered migrations" in reason, True))

    # 8. THE CASE THAT CAUSED THE FAMILY CHECK -- FLIPPED.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/ | wc -l", s)
    verdict, reason = write(BRIEF, s)
    r.append(check("listing docs/codex/ alone is now DENIED for a CODEX-* file",
                   verdict, "deny"))
    r.append(check("...the FAMILY check is what fires", "FAMILY NOT READ" in reason, True))
    r.append(check("the reason labels the family as it is spelled on disk",
                   "CODEX-* files" in reason, True))
    r.append(check("the reason suggests a pasteable cat of a real sibling",
                   "cat docs/codex/CODEX-" in reason, True))
    r.append(check("the reason cites the slices incident", "slices" in reason, True))

    # 8b. THE ALLOW: listed the directory AND opened a sibling.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/ | wc -l", s)
    bash("cat " + SIBLING_BRIEF, s)
    verdict, _ = write(BRIEF, s)
    r.append(check("listed AND read a sibling is allowed", verdict, "allow"))

    # 8c. The Read tool satisfies it too, not only a shell cat. This needs the Read matcher
    #     to be registered in .claude/settings.json, which it was not until 2026-10-13 --
    #     the guard recorded Read calls that the harness never sent it.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/", s)
    read(SIBLING_BRIEF, s)
    verdict, _ = write(BRIEF, s)
    r.append(check("the Read tool satisfies the family check", verdict, "allow"))

    # 8d. A MENTION is not a read: grep naming the sibling must not satisfy it.
    s = "t-" + uuid.uuid4().hex
    bash('grep -c "x" ' + SIBLING_BRIEF, s)
    verdict, reason = write(BRIEF, s)
    r.append(check("grep naming a sibling is a mention, not a read", verdict, "deny"))
    r.append(check("...the FAMILY check rejects it", "FAMILY NOT READ" in reason, True))

    # 9. NO FALSE DENIAL: a new tools/ guard after listing AND reading a sibling guard.
    s = "t-" + uuid.uuid4().hex
    bash("ls tools/claude_*.py", s)
    read(SIBLING_GUARD, s)
    verdict, _ = write("tools/claude_new_thing_guard.py", s)
    r.append(check("a new claude_* guard after reading one is allowed", verdict, "allow"))

    # 10. NO FALSE DENIAL: a name with no family prefix joins no family.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/", s)
    verdict, _ = write("docs/codex/Standalone.md", s)
    r.append(check("a name with no prefix separator joins no family", verdict, "allow"))

    # 11. Out-of-scope paths are untouched.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = run("Write", {"file_path": "Kor.Opportunities.Core/Ingestion/Gate.cs"}, s)
    r.append(check("a code file outside the guarded dirs is untouched", verdict, "allow"))
    verdict, _ = run("Write", {"file_path": "C:/Users/x/AppData/Local/Temp/claude/p.py"}, s)
    r.append(check("a scratchpad file is untouched", verdict, "allow"))

    # 12. Edit is deliberately not guarded.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = run("Edit", {"file_path": PLAN, "old_string": "a", "new_string": "b"}, s)
    r.append(check("Edit is not guarded (documented scope)", verdict, "allow"))

    # 13. A name with no topic words cannot demand a search.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = run("Write", {"file_path": "docs/README.md"}, s)
    r.append(check("an all-stopword filename is allowed", verdict, "allow"))

    # 14. The leaf match must not be so loose that any mention of the word counts.
    s = "t-" + uuid.uuid4().hex
    bash("grep -rn 'schema' Kor.Opportunities.Core/", s)
    verdict, _ = run("Write", {"file_path": "db/999_Unrelated.sql"}, s)
    r.append(check("a bare word does not satisfy the directory check", verdict, "deny"))

    # 15. The suggested command must be pasteable. The first live denial printed
    #     `ls c:/visual studio projects/operations/db/` -- lowercased and unquoted.
    s = "t-" + uuid.uuid4().hex
    root = "C:/VIsual Studio Projects/Operations"
    _, reason = run("Write", {"file_path": root + "/db/999_Mandolin.sql"}, s, root=root)
    r.append(check("the suggestion is repo-relative, not an absolute path",
                   "ls db/" in reason, True))
    r.append(check("the suggestion is not lowercased",
                   "visual studio projects" not in reason, True))

    s = "t-" + uuid.uuid4().hex
    _, reason = run("Write", {"file_path": "C:/Some Dir/docs/Zz-Quokka.md"}, s, root=None)
    r.append(check("a path with spaces is quoted in the suggestion",
                   'ls "C:/Some Dir/docs"/' in reason, True))

    # 16. NO FALSE DENIAL: a directory that does not exist has no siblings to read. The grep
    #     is what satisfies the look check -- there is no directory to list.
    s = "t-" + uuid.uuid4().hex
    bash('grep -ril "quokka" docs/', s)
    verdict, _ = write("docs/brand-new-folder/Quokka-First-Of-Its-Kind.md", s)
    r.append(check("a family of zero does not demand a read", verdict, "allow"))

    # 17. NO FALSE DENIAL: rewriting an existing family member you have read.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/", s)
    read(SIBLING_BRIEF, s)
    verdict, _ = write(SIBLING_BRIEF, s)
    r.append(check("rewriting a sibling you read is allowed", verdict, "allow"))

    print("\n%d/%d passed" % (sum(r), len(r)))
    sys.exit(0 if all(r) else 1)


if __name__ == "__main__":
    main()
