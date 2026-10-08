"""PreToolUse guard: refuse to WRITE to a SQL table whose columns were never inspected.

Why this exists, 2026-10-08. Ian, after an evening of it: "Why can't you get things RIGHT
on the first try JUST ONCE" -- and then "Why not PREVENT it?"

Every error that night was one move: assume a schema by analogy instead of checking it.
The clearest instance is small and typical. `opportunities.IngestionTriggers.Id` is a
uniqueidentifier, so an insert into `opportunities.JobTriggerRequests` was written with
NEWID(). That column is a bigint IDENTITY. The insert failed with "Operand type clash",
and the failure was only noticed because a polling loop sat there returning nothing.
Four seconds of `sys.columns` would have prevented it. The same move, at larger scale,
produced wrong column names in half a dozen queries the same night.

The repo already proved the remedy. CLAUDE.md rule 12: "a rule that lives only in prose
loses to momentum... the gate is what changes the next action." The two rules with hooks
(7 and 12) held perfectly through that session; the ones in prose failed repeatedly.

WHAT IT BLOCKS: a DML statement (INSERT/UPDATE/DELETE/MERGE) against a schema-qualified
table when nothing in this session has inspected that table's columns.

WHAT COUNTS AS INSPECTION: a prior command -- or this same command -- containing
sys.columns, INFORMATION_SCHEMA.COLUMNS, or sp_help, together with the table's name.

WHAT IT DOES NOT COVER, deliberately:
  - SELECTs. Reading a wrong column name is a loud, immediate error; writing one can be
    silent, or can hit a type clash deep inside a loop.
  - Temp tables (#x) and table variables (@x): declared in the same batch, so their shape
    is already in front of whoever wrote them.
  - Unqualified table names. Too ambiguous to match without false denials, and this repo
    schema-qualifies everything it writes.
  - Whether the columns were UNDERSTOOD. It proves the schema was looked at, nothing more.
  - .sql migration files run with `-i`: the file's content is not on the command line.
    Those get the same error at run time, visibly, against a file under review.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import json
import os
import re
import sys
import tempfile

# Repo rule 7: every pattern is a raw string.
DML = re.compile(
    r"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE(?:\s+INTO)?)\s+"
    r"\[?(?P<schema>[A-Za-z_][\w]*)\]?\.\[?(?P<table>[A-Za-z_][\w]*)\]?",
    re.IGNORECASE)

INSPECTS = re.compile(r"sys\.columns|INFORMATION_SCHEMA\.COLUMNS|sp_help\b", re.IGNORECASE)


def state_path(session):
    d = os.path.join(tempfile.gettempdir(), "claude-schema-seen")
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, (session or "unknown") + ".txt")


def seen_tables(session):
    path = state_path(session)
    if not os.path.exists(path):
        return set()
    with open(path, encoding="utf-8") as f:
        return {ln.strip().lower() for ln in f if ln.strip()}


def remember(session, tables):
    if not tables:
        return
    with open(state_path(session), "a", encoding="utf-8") as f:
        for t in sorted(tables):
            f.write(t + "\n")


def deny(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.exit(0)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)

    command = (payload.get("tool_input") or {}).get("command") or ""
    if not command:
        sys.exit(0)
    session = payload.get("session_id")

    # A schema inspection in this command marks every table it names as "looked at".
    # Deliberately generous -- every quoted literal and every schema-qualified name in
    # the command counts. A false ALLOW costs a run-time error, which is loud; a false
    # DENY costs a blocked command, which is worse than the rule is worth.
    if INSPECTS.search(command):
        names = set(re.findall(r"'([A-Za-z_]\w*)'", command))
        names.update(re.findall(r"\b[A-Za-z_]\w*\.\[?([A-Za-z_]\w*)\]?", command))
        remember(session, {n.lower() for n in names if n})

    writes = {(m.group("schema").lower(), m.group("table").lower())
              for m in DML.finditer(command)}
    if not writes:
        sys.exit(0)

    known = seen_tables(session)
    unchecked = sorted(
        "%s.%s" % (s, t) for s, t in writes
        if not t.startswith("#") and t not in known)

    if unchecked:
        deny(
            "SCHEMA NOT CHECKED: this writes to " + ", ".join(unchecked) +
            " and nothing in this session has read its columns.\n"
            "On 2026-10-08 NEWID() was written into a bigint IDENTITY column because a "
            "SIMILAR table used a GUID -- assumed by analogy, failed at run time.\n"
            "Run this first, then re-issue the write:\n"
            "  SELECT c.name, t2.name AS type, c.is_nullable, c.is_identity\n"
            "  FROM sys.columns c\n"
            "  JOIN sys.tables t ON t.object_id = c.object_id\n"
            "  JOIN sys.types t2 ON t2.user_type_id = c.user_type_id\n"
            "  WHERE t.name = '<Table>' ORDER BY c.column_id;")

    sys.exit(0)


if __name__ == "__main__":
    main()
