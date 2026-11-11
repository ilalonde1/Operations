r"""PreToolUse guard for Write, Edit, MultiEdit, Bash and PowerShell: nobody but Ian takes the sample-first stop out.

Why this exists, 11 Nov 2026 (board row 2014, record #132764). Ian, 10 Nov (#132080): one offline pass, one sample, one
full run. At 11:00 on 11 Nov a full 31218 run started at 288fb00d with no --sample at that sha; the precheck PASSED while
naming '(d) sheets drawn before this run' UNPROVEN (#132758), and three post-Revit plot checks failed. KOR.Drafter's
virtual_drafter.py now refuses such a run (the SAMPLE-FIRST STOP block, commit 153dbed9). Ian, to OverLORD, 11 Nov: "And
what's to prevent you from 'skipping' whatever the fuck you want next time?" A stop a session can edit away is prose.
So this guard denies the ways a session would take it out, and OverLORD framework-7f ordered it beside the stop.

What it denies (PROTECTED below):
  * a Write, Edit or MultiEdit that changes text inside the SAMPLE-FIRST STOP markers of KOR.Drafter tools/virtual_drafter.py
    or the SAMPLE-FIRST STOP TEST markers of tools/virtual_drafter_build_gate_test.py, adds or removes a marker, or drops a
    block from a Write; any change to this file or its test;
  * a change to a .claude/settings*.json that removes this guard's line (an Edit whose old text names it and new text does
    not; a Write that drops it);
  * a Write or Edit of a Claude transcript (.claude/projects/**/*.jsonl) - the stop's lift is read from those;
  * a Bash or PowerShell segment that writes into a protected file (a redirect, tee, sed -i / perl -i, Set-Content,
    Add-Content, Out-File, a cp / mv / Copy-Item / Move-Item onto it, git checkout / restore / reset / stash naming it), that
    writes into a transcript, that edits a settings file naming this guard, that SETS a bypass variable (a name with
    SKIP/BYPASS/NO + SAMPLE, or SAMPLE + SKIP/BYPASS/FIRST/STOP/OFF: export, NAME=v cmd, $env:, set, setx,
    SetEnvironmentVariable), or that carries the phrase IAN-APPROVED-SKIP-SAMPLE in a writing segment (echo/printf/Write-
    Output/Set-Content/redirect/tee/export/env assignment) - the phrase is Ian's to type, never a session's to write.
The only lift: one of the last LOOKBACK prompts IAN TYPED in this session holds IAN-APPROVED-SKIP-SAMPLE. "Typed" is read
off the session's own transcript (payload transcript_path): a user entry with entrypoint "cli" and promptSource "typed" or
"queued", not isMeta, not a tool result, not a sidechain - the same test virtual_drafter.ian_typed_texts applies. A lane
brief (entrypoint sdk-cli, promptSource sdk) and a peer's message (isMeta, promptSource system) never lift it. Unlike
claude_api_guard.py, the prompt recorder's file is NOT read: it holds lane briefs as user messages (DrafterPythonGuard.cs
names that gap).

Reads (grep, cat, Get-Content ...) pass. Running virtual_drafter.py passes (a redirect of its output to a log is not a write
into it). Edits elsewhere in virtual_drafter.py pass: other lanes work in that file.

DOES NOT CATCH: a Revit run started outside virtual_drafter.py run (bridge_play, drafted_set.py, revise, place, a person in
Revit) - the stop's own header says so; a program the session runs that writes the file (python -c / a script with open(),
a generator, git apply / am / merge / pull / rebase, a patch tool); a path held in a variable or built at run time; a block
edited in another checkout and committed there; a whole-file revert by git checkout of a commit without naming the path
(git checkout <sha> alone); a settings.json the session never loads (a repo where this line was not added); a lift typed by
Ian for some other reason (the token is the whole test). Same-class fault it would NOT catch: a session that copies the
stop's checks into a new wrapper which it then calls instead of virtual_drafter.py run. It holds an honest session against
momentum; it is not a sandbox.

FAILS: open on a payload it cannot parse (a guard fault must never stop all work); CLOSED on the lift - a transcript it
cannot read means no lift.

stdin  : the PreToolUse payload          stdout : nothing (allow), or a permissionDecision of "deny"
Test   : python -I tools/claude_run_sample_guard_test.py
"""
import json
import os
import re
import sys

LOOKBACK = 3
APPROVAL = "IAN-APPROVED-SKIP-SAMPLE"
GUARD_NAME = "claude_run_sample_guard"
# (path suffix, lower case, forward slashes) -> (begin marker, end marker); a block runs from a begin line to the next end
PROTECTED = {
    "kor.drafter/tools/virtual_drafter.py": (">>> SAMPLE-FIRST STOP", "<<< SAMPLE-FIRST STOP <<<"),
    "kor.drafter/tools/virtual_drafter_build_gate_test.py": (">>> SAMPLE-FIRST STOP TEST", "<<< SAMPLE-FIRST STOP TEST <<<"),
}
WHOLE = ("operations/tools/claude_run_sample_guard.py", "operations/tools/claude_run_sample_guard_test.py")
NAMES = ("virtual_drafter.py", "virtual_drafter_build_gate_test.py", "claude_run_sample_guard.py", "claude_run_sample_guard_test.py")

# Repo rule 7: every pattern is a raw string.
READ_SEGMENT = re.compile(
    r"^\s*(?:sudo\s+)?(?:cat|bat|less|more|head|tail|grep|rg|wc|file|stat|diff|ls|find|git\s+(?:log|show|diff|status|blame|grep)|"
    r"Get-\w+|Select-String|Select-Object|Where-Object|Measure-Object|Test-Path|type)\b", re.I)
WRITES = re.compile(r"(?<![<>=!-])>{1,2}(?!&|=)|\btee\b|\bTee-Object\b|\bSet-Content\b|\bAdd-Content\b|\bOut-File\b|\bsed\b[^|;&]*\s-i|"
                    r"\bperl\b[^|;&]*\s-[a-z]*i|\becho\b|\bprintf\b|\bWrite-Output\b|\bexport\b|\$env:|\bsetx?\s|"
                    r"SetEnvironmentVariable|^\s*[A-Za-z_][A-Za-z0-9_]*=", re.I)
BYPASS_NAME = r"[A-Za-z0-9_]*(?:(?:SKIP|BYPASS|NO)[_-]?SAMPLE|SAMPLE[_-]?(?:SKIP|BYPASS|FIRST|STOP|OFF))[A-Za-z0-9_]*"
BYPASS_SET = re.compile(
    r"(?:\bexport\s+|\$env:|\bset\s+|\bsetx\s+|SetEnvironmentVariable\(\s*['\"]|(?:^|[;&|]\s*|\s))" + BYPASS_NAME + r"\s*(?:=|['\"]\s*,)|\bsetx\s+" + BYPASS_NAME + r"\b", re.I)
REDIRECT_TARGET = re.compile(r"(?<![<>=!-])>{1,2}\s*(['\"]?)([^\s'\";|&]+)\1")
COPY_VERB = re.compile(r"^\s*(?:cp|mv|copy|move|xcopy|Copy-Item|Move-Item|cpi|mi)\b", re.I)
GIT_REVERT = re.compile(r"^\s*git\s+(?:-C\s+\S+\s+|-C\s+\"[^\"]+\"\s+)?(?:checkout|restore|reset|stash)\b", re.I)


def norm(path):
    return (path or "").replace("\\", "/").lower()


def protected_key(path):
    p = norm(path)
    return next((k for k in PROTECTED if p.endswith(k)), None)


def blocks(text, key):
    begin, end = PROTECTED[key]
    t = (text or "").replace("\r\n", "\n"); out = []; i = 0
    while True:
        a = t.find(begin, i)
        if a < 0: return out
        b = t.find(end, a + len(begin))
        b = len(t) if b < 0 else b + len(end)
        out.append((a, b)); i = b


def read(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return f.read().replace("\r\n", "\n")
    except OSError:
        return None


def ian_typed(transcript):
    """Every prompt Ian TYPED in this transcript, oldest first (same test as virtual_drafter.ian_typed_texts)."""
    out = []
    try:
        f = open(transcript, encoding="utf-8", errors="replace")
    except (OSError, TypeError):
        return out
    with f:
        for ln in f:
            if '"user"' not in ln: continue
            try: o = json.loads(ln)
            except ValueError: continue
            if (o.get("type") != "user" or o.get("isMeta") or o.get("toolUseResult") is not None or o.get("isSidechain")
                    or o.get("entrypoint") != "cli" or o.get("promptSource") not in ("typed", "queued")):
                continue
            c = (o.get("message") or {}).get("content")
            if isinstance(c, list):
                if any(isinstance(x, dict) and x.get("type") == "tool_result" for x in c): continue
                c = "\n".join(x.get("text", "") for x in c if isinstance(x, dict) and x.get("type") == "text")
            if isinstance(c, str): out.append(c)
    return out


def approved(transcript):
    return any(APPROVAL in p for p in ian_typed(transcript)[-LOOKBACK:])


def edit_touches(current, key, old, new, replace_all=False):
    """Does replacing `old` by `new` in `current` change a protected block, or add/remove a marker?"""
    begin, end = PROTECTED[key]
    old = (old or "").replace("\r\n", "\n"); new = (new or "").replace("\r\n", "\n")
    if any(m in old or m in new for m in (begin, end)): return True
    spans = blocks(current, key)
    if not spans or not old: return False
    starts = []; i = current.find(old)
    while i >= 0:
        starts.append(i)
        if not replace_all: break
        i = current.find(old, i + 1)
    return any(s < b and s + len(old) > a for s in starts for a, b in spans)


def file_reason(tool, inp):
    """Why this Write/Edit/MultiEdit is refused, or None."""
    path = inp.get("file_path") or inp.get("notebook_path") or ""
    p = norm(path)
    if any(p.endswith(w) for w in WHOLE):
        return "%s would change the sample-first guard itself (%s)" % (tool, p)
    if "/.claude/projects/" in p and p.endswith(".jsonl"):
        return "%s would write a Claude transcript (%s): the stop's lift is read from those" % (tool, p)
    if re.search(r"/\.claude/settings(?:\.local)?\.json$", p):
        cur = read(path) or ""
        if tool == "Write" and GUARD_NAME in cur and GUARD_NAME not in (inp.get("content") or ""):
            return "Write would drop %s from %s" % (GUARD_NAME, p)
        edits = inp.get("edits") if tool == "MultiEdit" else [inp]
        for e in edits or []:
            if GUARD_NAME in (e.get("old_string") or "") and GUARD_NAME not in (e.get("new_string") or ""):
                return "%s would remove %s from %s" % (tool, GUARD_NAME, p)
        return None
    key = protected_key(path)
    if not key: return None
    cur = read(path)
    if cur is None: return None
    if tool == "Write":
        new = (inp.get("content") or "").replace("\r\n", "\n")
        if [cur[a:b] for a, b in blocks(cur, key)] != [new[a:b] for a, b in blocks(new, key)]:
            return "Write would change the SAMPLE-FIRST STOP block of %s" % p
        return None
    edits = inp.get("edits") if tool == "MultiEdit" else [inp]
    for e in edits or []:
        if edit_touches(cur, key, e.get("old_string"), e.get("new_string"), bool(e.get("replace_all"))):
            return "%s would change the SAMPLE-FIRST STOP block of %s (between its markers)" % (tool, p)
    return None


def segments(cmd):
    return [s for s in re.split(r"&&|\|\||[;|\n]", cmd) if s.strip()]


def names_protected(text):
    t = norm(text)
    return next((n for n in NAMES if n in t), None)


def command_reason(cmd):
    """Why this Bash/PowerShell command is refused, or None."""
    for seg in segments(cmd):
        if READ_SEGMENT.match(seg) and not REDIRECT_TARGET.search(seg): continue     # `cat x > file` is a write
        if BYPASS_SET.search(seg):
            return "it sets a sample-first bypass variable (%s)" % BYPASS_SET.search(seg).group(0).strip()
        if APPROVAL in seg and WRITES.search(seg):
            return "it writes Ian's phrase %s from a session" % APPROVAL
        for _, target in REDIRECT_TARGET.findall(seg):
            if names_protected(target): return "it redirects output into %s" % target
            if "/.claude/projects/" in norm(target) and norm(target).endswith(".jsonl"): return "it writes a Claude transcript (%s)" % target
        n = names_protected(seg)
        if n:
            if re.search(r"\btee\b|\bTee-Object\b|\bSet-Content\b|\bAdd-Content\b|\bOut-File\b|\bsed\b[^|;&]*\s-i|\bperl\b[^|;&]*\s-[a-z]*i", seg, re.I):
                return "it writes into %s" % n
            if COPY_VERB.match(seg) and names_protected(seg.split()[-1].strip("'\"")):
                return "it copies over %s" % n
            if GIT_REVERT.match(seg):
                return "it reverts %s with git" % n
        if re.search(r"settings(?:\.local)?\.json", seg, re.I) and GUARD_NAME in seg and WRITES.search(seg + " "):
            return "it edits a settings file that names %s" % GUARD_NAME
    return None


def decide(payload):
    """(reason or None) for a PreToolUse payload, before the lift."""
    tool = payload.get("tool_name") or ""
    inp = payload.get("tool_input") or {}
    if tool in ("Write", "Edit", "MultiEdit", "NotebookEdit"):
        return file_reason(tool, inp)
    if tool in ("Bash", "PowerShell"):
        return command_reason(inp.get("command") or "")
    return None


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    if not isinstance(payload, dict): return
    try:
        why = decide(payload)
    except Exception:
        return                                   # a guard fault never stops work
    if not why or approved(payload.get("transcript_path")):
        return
    try:   # F11 (Framework row 1159): every refusal is a row; logging can never change what this guard does
        sys.path.append("C:/VIsual Studio Projects/Framework/tools"); import guard_log  # noqa: E702
        guard_log.record("run-sample", why[:200], (payload.get("tool_input") or {}).get("command") or why, None, payload=payload)
    except Exception:
        pass
    reason = ("SAMPLE-FIRST STOP is Ian's (board row 2014, record #132764; Ian 10 Nov #132080: one offline pass, one sample, "
              "one full run). Refused: %s. Only a prompt Ian types himself containing %s lifts this; a lane, a conductor "
              "or OverLORD cannot." % (why, APPROVAL))
    json.dump({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                      "permissionDecisionReason": reason}}, sys.stdout)


if __name__ == "__main__":
    main()
