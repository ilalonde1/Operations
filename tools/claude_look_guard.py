r"""Look guard: a claim that something is done needs a COMPLETE LOOK at what changed.

Ian, 2026-10-27: "I think the hook should be a COMPLETE LOOK." / "Yes - build the complete look hook".
Why: today the gates caught counts; only LOOKING caught Jim's inverted clouds, the closed core door and the turned
columns, and the faults Ian keeps catching are claims made from a command's silence or a summary line.

Two doors, one rule:
  Stop        - my final reply to Ian. Blocks ONCE (stop_hook_active then allows, so it can never loop), like
                claude_absence_guard.py.
  PreToolUse  - SendMessage to a peer session. Denied until the look exists or the claim is reworded.

The rule: if the text CLAIMS completion (CLAIM below), then for every kind of change in KorStandards
oversight.LookRule (migration 138) that happened anywhere in this session's transcript, a LOOK of that kind must
come after its last instance. A tool call is a change when its tool is in ChangeTools and ChangePattern matches its
command (Bash/PowerShell) or file path (Edit/Write); a look likewise with LookTools/LookPattern (Read: the file
path). A command that both changes and then looks (a write followed by its SELECT) counts as looked when the look
match comes after the change match in its text. The refusal names each unlooked kind, its last change, and the
rule's COMPLETE text - what a complete look is.

Rules come from KorStandards through KOR.Drafter tools/standards_db.py (env KOR_ENGINEERINGTOOLS_STANDARDSDB),
cached for CACHE_S at %LOCALAPPDATA%\KOR\look_rules_cache.json (a cache of the DB, never the source). DB down and
no cache = allow (fail open), because a gate that blocks every reply when the server reboots stops the work.

What it COVERS: that a look OF THE RIGHT KIND happened after each kind of change, before a completion claim, in
replies and peer messages. What it does NOT: whether the look covered everything (every page, every storey) - the
Complete text tells the session what everything is, the hook cannot count pages it does not know; whether the
look was at the RIGHT file; whether it was understood (10-27: I opened the final model's Level 1, saw P1's solid
west wall and nearly called it a fault); claims phrased without a CLAIM word; changes made by other sessions or
by tools that are not Bash/PowerShell/Edit/Write; a DB write by a script FILE that does NOT go through
KOR.Drafter tools/standards_db.py (raw sqlcmd, its own pyodbc) - nothing in its command or output says it writes.
Writes through standards_db ARE seen: in the command (db.run/record/withdraw, migration 140) and, from any script,
in the OUTPUT line standards_db.run() prints, "KORSTANDARDS WRITE <table> (<n> statements)" (KOR.Drafter 91007ba,
migrations 141-143, Standard Details' case; a rolled-back write prints it too - the line says the batch held writes). Same-class fault it would miss: a reply saying "the drawings are
right" after rendering page 1 of 26 - one Read of a PNG satisfies the drawing rule.

Framework row 1203 (3 Nov 2026; GuardRefusal 440, 441, 893, 898 judged wrong). Three changes:
- A LOOK is also read AS THE SHELL RUNS IT: printf/echo -e "\n" and PowerShell `n are line breaks. The lead read
  every row back with `printf "USE KorStandards;\nSELECT ... FROM oversight.Mistake ..." > rb.sql`, and \bSELECT\b
  saw no SELECT (no word boundary between the n and the S), so the look could never be satisfied. That, not a
  missing oversight.Surface row, was the cause: this guard never read Surface. A CHANGE is still matched on the raw
  text only (a Windows path is full of \t and \n pairs), so a write spelled "\nUPDATE x SET" inside a printf is
  still not seen - as before this fix.
- A db-write's look, as before, is any look (LookPattern) after its last write; now also OUTPUT inserted.*/deleted.*
  in the write itself (every row it touched, as its own result - refusal 440's write) when that command's output is
  not cut by | head, | tail (`tail -n +N`, which drops a fixed head such as sqlcmd's "Changed database context" and
  shows every line after it, is not a cut, nor is Select-Object -Skip N for the same reason), Select-Object -First/-Last
  or grep -m, AFTER the write in that command (review row 284: not anywhere in it). A SELECT look whose output
  is cut STILL counts, as before: refusing it was tried and, on a second real transcript (Operations f6ff24dd, 839
  moments with a claim word), took refusals from 555 to 575 - 41 of the 98 moments that gained one behind greps of
  migration files the ChangePattern misreads as writes ("grep ... 'INSERT INTO ...' | head"). The refusal names the
  TABLES the last write wrote (INSERT INTO / UPDATE..SET / DELETE FROM / MERGE INTO / ALTER TABLE <schema.table>,
  or the qualified name in a "KORSTANDARDS WRITE" line) and, for each one oversight.Surface names, that Surface's
  code and its consumers' check; with no Surface row, its rows read back ARE the whole look.
  Tried and NOT done: requiring the read-back to NAME the written table. The dry run over the lead's 3 Nov
  transcript (196 replies and sends with a claim word) took refusals from 28 to 46 that way, 17 of the 18 more from
  one `grep -E "...UPDATE brain.Memory SET RetiredAtUtc"` the ChangePattern misreads as a write, and 1 from an
  ALTER TABLE in a LocalDB rehearsal whose database was then dropped - a look that could never be done.
  What it does NOT see: a SELECT read-back cut by | head or | tail (the miss above, kept so a misread grep's own
  SELECT goes on counting); whether the look read the table that was WRITTEN (a SELECT of any table, or SELECT
  @@ROWCOUNT, still satisfies - the miss above); whether it covered every row written (a SELECT of one Id satisfies
  a write of ten); whether a Surface's check RAN (Check is prose and CheckSql was NULL in 8 of 8 rows on 3 Nov - the
  refusal names the check, it cannot require it); a write whose own result was cut by a tail and then read back
  by a later SELECT (allowed: the rows were read, the statement's own error may not have been).
- A claim word wrapped in quotes ("passes", 'done', `fixed`) is a MENTION, not a claim, and is skipped: 898 read the
  guard's own misfire, `misreading "passes" as a completion claim`. NOT decided, and still refused when a look is
  missing: a claim word used as another part of speech in an unquoted sentence - 893 was "in three passes" (a noun)
  - and a sentence ABOUT the guard that uses the word unquoted. Deciding either needs the sentence understood; a
  regex guess would also wave through real claims ("all three passes"), so the miss stays named here.

Framework row 1936 (6 Nov 2026): a main session may not hold a big picture (kor context, ReadGuard), so a HELPER'S LOOK COUNTS,
or the two rules deadlock. Ian's rule that every sheet is looked at stands: a helper's Read calls, read from its own transcript
(<session>/subagents/agent-<id>.jsonl), count as Reads at the place it was LAUNCHED, once its finish is in the main transcript
(a queued_command attachment holding <status>completed</status>, or a foreground result with status completed). So a helper
launched before the last render earns nothing, one still running earns nothing, and one that failed earns nothing. It credits
Read calls only: the other kinds' looks are Bash and PowerShell, which a main session may still run. A Workflow's agents are not
credited, and a helper resumed with SendMessage after a render earns nothing: launch a fresh one. Same-class fault it would miss:
a helper sent to do something else that reads one unrelated picture satisfies the drawing rule, and the helper's finding may
never mention what it saw, exactly as one Read of page 1 of 26 already does.

stdin  : the Stop payload (transcript_path, stop_hook_active) or the PreToolUse payload (tool_name SendMessage)
stdout : nothing (allow); Stop -> {"decision": "block", ...}; PreToolUse -> permissionDecision "deny"
"""
import json
import os
import re
import sys
import time

# Repo rule 7: raw strings for every regex.
# "done(?!-when\b| when\b)": exactly "done-when" / "done when" names the board's finish-line column, not a claim
# (Framework row 151, 2 Nov 2026). Only those two forms: "done - when ..." or "done" + a line break + "When" is still a
# claim (review row 24: the first version, [\s-]+, let "All done - when you're ready" through).
CLAIM = re.compile(
    r"\b(?:done(?!-when\b| when\b)|fixed|built|rebuilt|published|deployed|applied|verified|landed|shipped|works|passes|passed"
    r"|complete|completed|ready to (?:send|issue|go)|all good|confirmed working)\b", re.I)
COMMIT_MSG = re.compile(r"""-m\s+(?:"(?:[^"\\]|\\.)*"|'[^']*'|@'.*?'@)""", re.S)
# row 1203: the shell's line-break escapes, so "\nSELECT" in a printf reads as a SELECT
ESCAPES = re.compile(r"\\[nrt]|`[nrt]")
# row 1203: a db-write's table, schema-qualified (an optional database part before it); brackets allowed
_NAME = r"(?:\[[^\]]+\]|\w+)"
QUALIFIED = _NAME + r"(?:\s*\.\s*" + _NAME + r")+"
WRITE_TABLE = re.compile(r"\b(?:INSERT\s+INTO|DELETE\s+FROM|MERGE\s+INTO|ALTER\s+TABLE)\s+(" + QUALIFIED + r")"
                         r"|\bUPDATE\s+(" + QUALIFIED + r")\s+SET\b", re.I)
# output cut to its first or last lines. `tail -n +N` is NOT a cut: it drops a fixed head (sqlcmd's "Changed database
# context" line) and shows every line after it
TRUNCATED = re.compile(r"\|\s*head\b|\|\s*tail\b(?!\s+(?:-n\s*)?\+\d)|Select-Object\s+[^|;\n]*-(?:First|Last)\b"
                       r"|\bgrep\s+(?:-\w+\s+)*-m\s*\d", re.I)
# the written rows returned by the write itself: each row the statement wrote, as its own result
OUTPUT_ROWS = re.compile(r"\bOUTPUT\s+(?:inserted|deleted)\.", re.I)
# row 1936: a background helper's end, as the main transcript holds it (an attachment of type queued_command)
HELPER_DONE = re.compile(r"<task-notification>\s*<task-id>([\w-]+)</task-id>(?:(?!</task-notification>).)*?<status>completed</status>", re.S)
QUOTES = "\"'`‘’“”"
CACHE_S = 600
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "KOR", "look_rules_cache.json")
SURFACE_CACHE = os.path.join(os.path.dirname(CACHE), "look_surface_cache.json")
DRAFTER_TOOLS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "KOR.Drafter", "tools"))


def _cached_rows(cache, sql):
    """Rows of `sql` from KorStandards, cached CACHE_S at `cache`; the stale cache when the DB is down; else []."""
    try:
        st = os.stat(cache)
        if time.time() - st.st_mtime < CACHE_S:
            return json.load(open(cache, encoding="utf-8"))
    except (OSError, ValueError):
        pass
    try:
        sys.path.insert(0, DRAFTER_TOOLS)
        import standards_db  # noqa: E402
        rows = standards_db.rows(sql)
        if rows:
            os.makedirs(os.path.dirname(cache), exist_ok=True)
            json.dump(rows, open(cache, "w", encoding="utf-8"))
            return rows
    except BaseException:   # SystemExit from standards_db when the env var is missing, sqlcmd errors, timeouts
        pass
    try:
        return json.load(open(cache, encoding="utf-8"))
    except (OSError, ValueError):
        return []


def load_rules():
    """[{Kind, ChangeTools, ChangePattern, LookTools, LookPattern, Complete}], or [] when neither DB nor cache."""
    return _cached_rows(CACHE, "SELECT Kind, ChangeTools, ChangePattern, LookTools, LookPattern, OutputPattern, Complete "
                               "FROM oversight.LookRule WHERE RetiredAtUtc IS NULL")


def load_surfaces():
    """[{Code, Name, Check}] from oversight.Surface (row 1203), or [] - only ever used to NAME a check in a refusal."""
    return _cached_rows(SURFACE_CACHE, "SELECT Code, Name, [Check] FROM oversight.Surface")


def as_read(text):
    """A command as the shell runs it: the escapes printf, echo -e and PowerShell turn into line breaks become one,
    padded to the escape's two characters so every position in it is the same position in `text`. Used for LOOKS
    only: a change is matched on the raw text, because a Windows path is full of these pairs ("\\takeoff\\takeoff.exe
    model-render" is a drawing run, and read this way it is not - the first version of this fix lost exactly that)."""
    return ESCAPES.sub("\n ", text)


def look_in(lpat, raw, read, start=0):
    """The look pattern's match in a command, on its raw text or as the shell reads it, from `start`."""
    return lpat.search(raw, start) or lpat.search(read, start)


def table_key(name):
    """'KorStandards.[oversight].Commitment' -> 'oversight.commitment' (schema.table, lower case)."""
    parts = [p.strip().strip("[]").strip() for p in name.split(".")]
    return ".".join(parts[-2:]).lower()


def names_table(text, key):
    """Does `text` name the table `key` (schema.table), with or without brackets or a database part before it?"""
    for m in re.finditer(QUALIFIED, text):
        if table_key(m.group(0)) == key:
            return True
    return False


def written_tables(text, cpat):
    """[(table key or '*', end of the write in text)] - '*' for a write cpat sees that names no qualified table (db.run,
    Invoke-KorSqlScript, an unqualified name). The refusal names the keys; '*' is never named."""
    out = []; starts = set()
    for m in WRITE_TABLE.finditer(text):
        out.append((table_key(m.group(1) or m.group(2)), m.end())); starts.add(m.start())
    for m in cpat.finditer(text):
        if m.start() not in starts:
            out.append(("*", m.end()))
    return out


def result_text(c):
    """A tool_result's text: a string, or a list of text parts."""
    cc = c.get("content")
    if isinstance(cc, str):
        return cc
    if isinstance(cc, list):
        return "\n".join(p.get("text", "") for p in cc if isinstance(p, dict) and p.get("type") == "text")
    return ""


def helper_reads(path, agent):
    """The Read calls in a helper's own transcript, beside its session's: <session>/subagents/agent-<id>.jsonl."""
    reads = []
    try:
        with open(os.path.join(os.path.splitext(path)[0], "subagents", f"agent-{agent}.jsonl"), encoding="utf-8") as f:
            for line in f:
                if '"Read"' not in line:
                    continue
                try:
                    entry = json.loads(line)
                except ValueError:
                    continue
                if entry.get("type") != "assistant":
                    continue
                for c in entry.get("message", {}).get("content", []) or []:
                    if isinstance(c, dict) and c.get("type") == "tool_use" and c.get("name") == "Read":
                        reads.append(("Read", str((c.get("input") or {}).get("file_path") or "")))
    except OSError:
        pass   # no transcript, no credit
    return reads


def tool_calls(path):
    """Every tool call AND tool result in the transcript, in order: (name, text) - a command or file path for a call;
    ("__output__", its text) for a result (migration 141: a write may announce itself only in its output). A helper that has
    FINISHED adds its Read calls at the place it was launched (row 1936: see the header)."""
    out = []
    done = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") == "attachment":
                att = entry.get("attachment")
                if isinstance(att, dict) and att.get("type") == "queued_command":
                    done.update(HELPER_DONE.findall(str(att.get("prompt") or "")))
                continue
            if entry.get("type") == "user" and isinstance(entry.get("message", {}).get("content"), list):
                for c in entry["message"]["content"]:
                    if isinstance(c, dict) and c.get("type") == "tool_result":
                        out.append(("__output__", result_text(c)))
                launched = entry.get("toolUseResult")
                if isinstance(launched, dict) and launched.get("agentId"):
                    out.append(("__helper__", str(launched["agentId"])))
                    if launched.get("status") == "completed":
                        done.add(str(launched["agentId"]))
                continue
            if entry.get("type") != "assistant":
                continue
            for c in entry.get("message", {}).get("content", []) or []:
                if not isinstance(c, dict) or c.get("type") != "tool_use":
                    continue
                inp = c.get("input") or {}
                text = str(inp.get("command") or inp.get("file_path") or inp.get("path") or "")
                out.append((c.get("name") or "", text))
    expanded = []
    for name, text in out:
        if name != "__helper__":
            expanded.append((name, text))
        elif text in done:
            expanded.extend(helper_reads(path, text))
    return expanded


def last_assistant_text(path):
    text = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") != "assistant":
                continue
            parts = [c.get("text", "") for c in entry.get("message", {}).get("content", []) or []
                     if isinstance(c, dict) and c.get("type") == "text"]
            if parts:
                text = "\n".join(parts)
    return text or ""


def unlooked(calls, rules):
    """[(rule, last change text, tables it wrote)] for every kind changed and not looked at since its last change.
    The tables are the db-write kind's (row 1203), used only to name them and their oversight.Surface in the refusal;
    [] for every other kind or a write that names no schema-qualified table."""
    missing = []
    for r in rules:
        ctools = set(r["ChangeTools"].split("|")); ltools = set(r["LookTools"].split("|"))
        cpat = re.compile(r["ChangePattern"], re.I); lpat = re.compile(r["LookPattern"], re.I)
        opat = re.compile(r["OutputPattern"]) if r.get("OutputPattern") else None
        db = r["Kind"] == "db-write"
        last_change = last_look = None; change_text = ""; tables = []
        for i, (name, text) in enumerate(calls):
            if name == "__output__":
                # a change announced in a tool's OUTPUT (141); outputs are never looks - a look is something done
                om = opat.search(text) if opat else None
                if om:
                    last_change, change_text = i, "output: " + om.group(0) + " " + text[om.end():om.end() + 60].strip()
                    q = re.search(QUALIFIED, om.group(0)) if db else None
                    tables = [table_key(q.group(0))] if q else []
                continue
            # a commit MESSAGE describes work, it does not do it: "git commit -m 'e2k_book.py: ...'" is not a drawing
            # run (the first dry run on the 10-27 transcript flagged exactly that)
            text = COMMIT_MSG.sub("-m ''", text)
            read = as_read(text) if name in ("Bash", "PowerShell") else text   # row 1203: for looks only
            cm = cpat.search(text) if name in ctools else None
            lm = None
            if name in ltools:
                # a look in the same command counts only when it comes after the change in the text
                lm = look_in(lpat, text, read, cm.end() if cm else 0)
                # the cut is looked for AFTER the write only: a `git log | head` before it does not touch its output
                # (review row 284)
                if db and cm and not lm and not TRUNCATED.search(text, cm.end()):
                    # row 1203: OUTPUT inserted.* / deleted.* returns every row the write touched as the statement's
                    # own result - the written rows read back, unless the output is cut (call 1542: | tail -3)
                    lm = OUTPUT_ROWS.search(text, cm.end())
            if cm:
                last_change, change_text = i, text
                tables = sorted({k for k, _ in written_tables(text, cpat) if k != "*"}) if db else []
            if lm:
                last_look = i
        if last_change is not None and (last_look is None or last_look < last_change):
            missing.append((r, change_text, tables))
    return missing


def surface_for(key, surfaces):
    """oversight.Surface rows whose Name names the table `key` (schema.table)."""
    return [s for s in surfaces or [] if names_table(s.get("Name") or "", key)]


def reason_for(missing, claim, surfaces=None):
    lines = [f"The text claims completion (\"{claim}\") but these changes have not been LOOKED at since their last "
             "instance (Ian: a COMPLETE LOOK before any claim; rules: KorStandards oversight.LookRule):"]
    for r, text, tables in missing:
        lines.append(f"- {r['Kind']}: last change `{text[:140]}` - complete look = {r['Complete']}")
        for key in tables:
            hits = surface_for(key, surfaces)
            if not hits:
                lines.append(f"  {key}: no oversight.Surface row names it, so its look is its rows read back: a SELECT "
                             f"of {key} after the write (or OUTPUT inserted.* in it), with no tail/head on the output")
            for s in hits:
                lines.append(f"  {key}: oversight.Surface {s.get('Code')} ({s.get('Name')}) - its consumers' check: "
                             f"{s.get('Check')}. Read the rows back (a SELECT of {key}, no tail/head), and run that check")
    if any("Read" in r["LookTools"].split("|") for r, _, _ in missing):
        lines.append("A picture too big for a main session (row 1936) is looked at by a helper: launch it after the change; "
                     "its Read of the picture counts here once the helper has finished.")
    lines.append("Do the look, then claim. Or reword so the text states only what was looked at.")
    return "\n".join(lines)


def claim_in(text):
    """The first claim word that is USED, not mentioned: a word wrapped in quotes ("passes") is skipped (row 1203)."""
    text = text or ""
    for m in CLAIM.finditer(text):
        before = text[m.start() - 1] if m.start() else ""
        after = text[m.end()] if m.end() < len(text) else ""
        if before and after and before in QUOTES and after in QUOTES:
            continue
        return m
    return None


def verdict(text, calls, rules, surfaces=None):
    """The refusal reason, or None when the text may go."""
    m = claim_in(text)
    if not m:
        return None
    missing = unlooked(calls, rules)
    return reason_for(missing, m.group(0), surfaces) if missing else None


def log_refusal(text, why, payload):
    """F11 (Framework row 1159): every refusal is a row - Rule = the unlooked kinds, Matched = the claim in its sentence.
    Logging can never change what this guard does."""
    try:
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        m = claim_in(text)
        kinds = ",".join(ln[2:].split(":")[0] for ln in why.splitlines() if ln.startswith("- "))
        guard_log.record("look", kinds, text[max(0, m.start() - 150):m.end() + 150] if m else why, None, payload=payload)
    except Exception:
        pass


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return
    path = payload.get("transcript_path")
    if not path or not os.path.exists(path):
        return
    rules = load_rules()
    if not rules:
        return
    try:
        calls = tool_calls(path)
    except OSError:
        return
    surfaces = load_surfaces()
    if payload.get("tool_name") == "SendMessage":
        msg = (payload.get("tool_input") or {}).get("message") or ""
        why = verdict(msg if isinstance(msg, str) else json.dumps(msg), calls, rules, surfaces)
        if why:
            log_refusal(msg if isinstance(msg, str) else json.dumps(msg), why, payload)
            json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                              "permissionDecisionReason": why}}, sys.stdout)
        return
    if payload.get("stop_hook_active"):
        return
    try:
        text = last_assistant_text(path)
        why = verdict(text, calls, rules, surfaces)
    except OSError:
        return
    if why:
        log_refusal(text, why, payload)
        json.dump({"decision": "block", "reason": why}, sys.stdout)


if __name__ == "__main__":
    main()
