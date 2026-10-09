"""Self-tests for claude_prior_art_guard.py.
Run: python tools/claude_prior_art_guard_test.py

Case 1 is the one that caused it: on 2026-10-08 the early-signal wiring plan was rewritten
without finding docs/KOR-EarlySignal-CoverageGap-2026-09-10.md, which had already measured
the gap, probed the municipalities and written the hunt order. Ian found it, not the search.

Cases 7 and 8 are the no-false-denial cases, taken from the same session: a migration
written straight after listing the Schema directory, and a Codex brief written straight
after listing docs/codex/. Both must be ALLOWED -- a guard that blocks real work gets
switched off, and then it protects nothing.
"""
import json
import os
import subprocess
import sys
import uuid

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_prior_art_guard.py")

PLAN = "docs/KOR-EarlySignal-Wiring-Plan-2026-10-08.md"
BRIEF = "docs/codex/CODEX-mpi-sources-have-no-freshness-check.md"
MIGRATION = "Kor.Opportunities.Data/Schema/332_RetireBcMpiFetcher.sql"


def run(tool, inp, session, root=None):
    payload = {"session_id": session, "tool_name": tool, "tool_input": inp}
    env = dict(os.environ)
    if root is not None:
        env["CLAUDE_PROJECT_DIR"] = root
    else:
        env.pop("CLAUDE_PROJECT_DIR", None)
    r = subprocess.run([sys.executable, GUARD], input=json.dumps(payload),
                       capture_output=True, text=True, env=env)
    out = (r.stdout or "").strip()
    if not out:
        return "allow", ""
    d = json.loads(out)["hookSpecificOutput"]
    return d["permissionDecision"], d["permissionDecisionReason"]


def write(path, session):
    return run("Write", {"file_path": path, "content": "x"}, session)


def bash(command, session):
    return run("Bash", {"command": command}, session)


def check(label, got, want):
    ok = got == want
    print(("  PASS  " if ok else "  FAIL  ") + label + ("" if ok else "  (got %r)" % (got,)))
    return ok


def main():
    r = []

    # 1. THE CASE THAT CAUSED IT.
    s = "t-" + uuid.uuid4().hex
    verdict, reason = write(PLAN, s)
    r.append(check("rewriting the wiring plan with no search is DENIED", verdict, "deny"))
    r.append(check("the reason names the file", PLAN in reason, True))
    r.append(check("the reason names the prior art it would have found",
                   "CoverageGap" in reason, True))

    # 2. A content search sharing a topic word unblocks it.
    s = "t-" + uuid.uuid4().hex
    run("Grep", {"pattern": "earlysignal|coverage", "path": "docs"}, s)
    verdict, _ = write(PLAN, s)
    r.append(check("after grepping a shared topic word, allowed", verdict, "allow"))

    # 3. Listing the directory unblocks it -- the rule 8 check.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/", s)
    verdict, _ = write(PLAN, s)
    r.append(check("after `ls docs/`, allowed", verdict, "allow"))

    # 4. A Codex brief with nothing looked at.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = write(BRIEF, s)
    r.append(check("a new Codex brief with no search is DENIED", verdict, "deny"))

    # 5. Opening ONE file is not a search. This is the distinction the guard exists for.
    s = "t-" + uuid.uuid4().hex
    bash("cat docs/codex/CODEX-arcgis-frozen-ingest.md", s)
    verdict, _ = write(BRIEF, s)
    r.append(check("reading one file does NOT count as looking", verdict, "deny"))

    # 6. Nor does unrelated shell work.
    s = "t-" + uuid.uuid4().hex
    bash("git status --porcelain", s)
    bash("dotnet build -nologo", s)
    verdict, _ = write(BRIEF, s)
    r.append(check("unrelated commands do NOT count as looking", verdict, "deny"))

    # 7. NO FALSE DENIAL: the real 332 migration, written after listing Schema/.
    s = "t-" + uuid.uuid4().hex
    bash('ls Kor.Opportunities.Data/Schema/ | grep -oE "^[0-9]+" | sort -n | tail -3', s)
    verdict, _ = write(MIGRATION, s)
    r.append(check("migration after listing Schema/ is allowed (no false denial)",
                   verdict, "allow"))

    # 8. NO FALSE DENIAL: the real brief, written after listing docs/codex/.
    s = "t-" + uuid.uuid4().hex
    bash("ls docs/codex/ | wc -l", s)
    verdict, _ = write(BRIEF, s)
    r.append(check("brief after listing docs/codex/ is allowed (no false denial)",
                   verdict, "allow"))

    # 9. Glob counts as looking.
    s = "t-" + uuid.uuid4().hex
    run("Glob", {"pattern": "docs/**/*freshness*"}, s)
    verdict, _ = write(BRIEF, s)
    r.append(check("a Glob of the topic counts", verdict, "allow"))

    # 10. Out-of-scope paths are untouched.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = write("Kor.Opportunities.Core/Ingestion/StructuralRelevanceGate.cs", s)
    r.append(check("a code file outside the guarded dirs is untouched", verdict, "allow"))
    verdict, _ = write("C:/Users/x/AppData/Local/Temp/claude/scratch/probe.py", s)
    r.append(check("a scratchpad file is untouched", verdict, "allow"))

    # 11. Edit is deliberately not guarded.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = run("Edit", {"file_path": PLAN, "old_string": "a", "new_string": "b"}, s)
    r.append(check("Edit is not guarded (documented scope)", verdict, "allow"))

    # 12. A name with no topic words cannot demand a search.
    s = "t-" + uuid.uuid4().hex
    verdict, _ = write("docs/README.md", s)
    r.append(check("an all-stopword filename is allowed", verdict, "allow"))

    # 13. The leaf match must not be so loose that any mention of the word counts.
    s = "t-" + uuid.uuid4().hex
    bash("grep -rn 'schema' Kor.Opportunities.Core/", s)
    verdict, _ = write("db/999_Unrelated.sql", s)
    r.append(check("a bare word does not satisfy the directory check", verdict, "deny"))

    # 14. The suggested command must be pasteable. The first live denial printed
    #     `ls c:/visual studio projects/operations/db/` -- lowercased by the matching
    #     code and unquoted, so it would have failed on the spaces.
    s = "t-" + uuid.uuid4().hex
    root = "C:/VIsual Studio Projects/Operations"
    _, reason = run("Write", {"file_path": root + "/db/999_Mandolin.sql"}, s, root=root)
    r.append(check("the suggestion is repo-relative, not an absolute path",
                   "ls db/" in reason, True))
    r.append(check("the suggestion is not lowercased",
                   "visual studio projects" not in reason, True))

    s = "t-" + uuid.uuid4().hex
    _, reason = run("Write", {"file_path": "C:/Some Dir/docs/Zz-Quokka.md"}, s)
    r.append(check("a path with spaces is quoted in the suggestion",
                   'ls "C:/Some Dir/docs"/' in reason, True))

    print("\n%d/%d passed" % (sum(r), len(r)))
    sys.exit(0 if all(r) else 1)


if __name__ == "__main__":
    main()
