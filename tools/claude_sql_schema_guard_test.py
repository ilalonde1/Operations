"""Self-tests for claude_sql_schema_guard.py. Run: python tools/claude_sql_schema_guard_test.py

Case 1 is the one that caused it: NEWID() into a bigint IDENTITY, assumed from a sibling
table that uses a GUID.
"""
import json
import os
import subprocess
import sys
import tempfile
import uuid

GUARD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "claude_sql_schema_guard.py")

INSPECT = ("python oq.py -Q \"SELECT c.name FROM sys.columns c "
           "JOIN sys.tables t ON t.object_id=c.object_id WHERE t.name='JobTriggerRequests';\"")
WRITE = ("python oq.py -Q \"INSERT INTO opportunities.JobTriggerRequests "
         "(JobName, Status) VALUES (N'DataHealthAuditJob', N'Pending');\"")


def run(command, session):
    payload = {"session_id": session, "tool_name": "Bash",
               "tool_input": {"command": command}}
    r = subprocess.run([sys.executable, GUARD], input=json.dumps(payload),
                       capture_output=True, text=True)
    out = (r.stdout or "").strip()
    if not out:
        return "allow", ""
    d = json.loads(out)["hookSpecificOutput"]
    return d["permissionDecision"], d["permissionDecisionReason"]


def check(label, got, want):
    ok = got == want
    print(("  PASS  " if ok else "  FAIL  ") + label + ("" if ok else "  (got %r)" % got))
    return ok


def main():
    results = []

    s = "test-" + uuid.uuid4().hex
    verdict, reason = run(WRITE, s)
    results.append(check("write to an uninspected table is DENIED", verdict, "deny"))
    results.append(check("the reason names the table",
                         "opportunities.jobtriggerrequests" in reason.lower(), True))

    run(INSPECT, s)
    verdict, _ = run(WRITE, s)
    results.append(check("same write AFTER inspecting its columns is allowed", verdict, "allow"))

    s2 = "test-" + uuid.uuid4().hex
    verdict, _ = run(INSPECT + " && " + WRITE, s2)
    results.append(check("inspect + write in ONE command is allowed", verdict, "allow"))

    s3 = "test-" + uuid.uuid4().hex
    verdict, _ = run("python oq.py -Q \"SELECT * FROM opportunities.Opportunities;\"", s3)
    results.append(check("a plain SELECT is never blocked", verdict, "allow"))

    verdict, _ = run("python oq.py -Q \"SELECT * INTO #g FROM x; INSERT INTO #g VALUES (1);\"", s3)
    results.append(check("temp tables are not blocked", verdict, "allow"))

    verdict, _ = run("git status", s3)
    results.append(check("a non-SQL command is untouched", verdict, "allow"))

    s4 = "test-" + uuid.uuid4().hex
    verdict, _ = run("python oq.py -i Kor.Opportunities.Data/Schema/331_Something.sql", s4)
    results.append(check("a migration run by -i is not blocked (documented gap)",
                         verdict, "allow"))

    print("\n%d/%d passed" % (sum(results), len(results)))
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()
