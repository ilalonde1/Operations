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

stdin  : the Stop payload (transcript_path, stop_hook_active) or the PreToolUse payload (tool_name SendMessage)
stdout : nothing (allow); Stop -> {"decision": "block", ...}; PreToolUse -> permissionDecision "deny"
"""
import json
import os
import re
import sys
import time

# Repo rule 7: raw strings for every regex.
CLAIM = re.compile(
    r"\b(?:done|fixed|built|rebuilt|published|deployed|applied|verified|landed|shipped|works|passes|passed"
    r"|complete|completed|ready to (?:send|issue|go)|all good|confirmed working)\b", re.I)
COMMIT_MSG = re.compile(r"""-m\s+(?:"(?:[^"\\]|\\.)*"|'[^']*'|@'.*?'@)""", re.S)
CACHE_S = 600
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "KOR", "look_rules_cache.json")
DRAFTER_TOOLS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "KOR.Drafter", "tools"))


def load_rules():
    """[{Kind, ChangeTools, ChangePattern, LookTools, LookPattern, Complete}], or [] when neither DB nor cache."""
    try:
        st = os.stat(CACHE)
        if time.time() - st.st_mtime < CACHE_S:
            return json.load(open(CACHE, encoding="utf-8"))
    except (OSError, ValueError):
        pass
    try:
        sys.path.insert(0, DRAFTER_TOOLS)
        import standards_db  # noqa: E402
        rows = standards_db.rows("SELECT Kind, ChangeTools, ChangePattern, LookTools, LookPattern, OutputPattern, Complete "
                                 "FROM oversight.LookRule WHERE RetiredAtUtc IS NULL")
        if rows:
            os.makedirs(os.path.dirname(CACHE), exist_ok=True)
            json.dump(rows, open(CACHE, "w", encoding="utf-8"))
            return rows
    except BaseException:   # SystemExit from standards_db when the env var is missing, sqlcmd errors, timeouts
        pass
    try:
        return json.load(open(CACHE, encoding="utf-8"))
    except (OSError, ValueError):
        return []


def result_text(c):
    """A tool_result's text: a string, or a list of text parts."""
    cc = c.get("content")
    if isinstance(cc, str):
        return cc
    if isinstance(cc, list):
        return "\n".join(p.get("text", "") for p in cc if isinstance(p, dict) and p.get("type") == "text")
    return ""


def tool_calls(path):
    """Every tool call AND tool result in the transcript, in order: (name, text) - a command or file path for a call;
    ("__output__", its text) for a result (migration 141: a write may announce itself only in its output)."""
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if entry.get("type") == "user" and isinstance(entry.get("message", {}).get("content"), list):
                for c in entry["message"]["content"]:
                    if isinstance(c, dict) and c.get("type") == "tool_result":
                        out.append(("__output__", result_text(c)))
                continue
            if entry.get("type") != "assistant":
                continue
            for c in entry.get("message", {}).get("content", []) or []:
                if not isinstance(c, dict) or c.get("type") != "tool_use":
                    continue
                inp = c.get("input") or {}
                text = str(inp.get("command") or inp.get("file_path") or inp.get("path") or "")
                out.append((c.get("name") or "", text))
    return out


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
    """[(rule, last change text)] for every kind changed and not looked at since its last change."""
    missing = []
    for r in rules:
        ctools = set(r["ChangeTools"].split("|")); ltools = set(r["LookTools"].split("|"))
        cpat = re.compile(r["ChangePattern"], re.I); lpat = re.compile(r["LookPattern"], re.I)
        opat = re.compile(r["OutputPattern"]) if r.get("OutputPattern") else None
        last_change = last_look = None; change_text = ""
        for i, (name, text) in enumerate(calls):
            if name == "__output__":
                # a change announced in a tool's OUTPUT (141); outputs are never looks - a look is something done
                if opat and opat.search(text):
                    last_change, change_text = i, "output: " + opat.search(text).group(0) + " " + text[opat.search(text).end():opat.search(text).end() + 60].strip()
                continue
            # a commit MESSAGE describes work, it does not do it: "git commit -m 'e2k_book.py: ...'" is not a drawing
            # run (the first dry run on the 10-27 transcript flagged exactly that)
            text = COMMIT_MSG.sub("-m ''", text)
            cm = cpat.search(text) if name in ctools else None
            lm = None
            if name in ltools:
                # a look in the same command counts only when it comes after the change in the text
                lm = lpat.search(text, cm.end()) if cm else lpat.search(text)
            if cm:
                last_change, change_text = i, text
            if lm:
                last_look = i
        if last_change is not None and (last_look is None or last_look < last_change):
            missing.append((r, change_text))
    return missing


def reason_for(missing, claim):
    lines = [f"The text claims completion (\"{claim}\") but these changes have not been LOOKED at since their last "
             "instance (Ian: a COMPLETE LOOK before any claim; rules: KorStandards oversight.LookRule):"]
    for r, text in missing:
        lines.append(f"- {r['Kind']}: last change `{text[:140]}` - complete look = {r['Complete']}")
    lines.append("Do the look, then claim. Or reword so the text states only what was looked at.")
    return "\n".join(lines)


def verdict(text, calls, rules):
    """The refusal reason, or None when the text may go."""
    m = CLAIM.search(text or "")
    if not m:
        return None
    missing = unlooked(calls, rules)
    return reason_for(missing, m.group(0)) if missing else None


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
    if payload.get("tool_name") == "SendMessage":
        msg = (payload.get("tool_input") or {}).get("message") or ""
        why = verdict(msg if isinstance(msg, str) else json.dumps(msg), calls, rules)
        if why:
            json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                              "permissionDecisionReason": why}}, sys.stdout)
        return
    if payload.get("stop_hook_active"):
        return
    try:
        why = verdict(last_assistant_text(path), calls, rules)
    except OSError:
        return
    if why:
        json.dump({"decision": "block", "reason": why}, sys.stdout)


if __name__ == "__main__":
    main()
