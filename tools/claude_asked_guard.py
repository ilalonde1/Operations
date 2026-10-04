"""PreToolUse guard for Bash and PowerShell: refuse an action class the user did not ask for.

The fault this exists for, from 2026-09-09, twice in one day: the ask was "check whether
three PCs are using the app and get them ready", the prior art (the publish script, the
install scripts) was found in the first two calls, and what followed anyway was a git
worktree, a Release build, transient services on a colleague's machine, a CIM sweep and an
event-log pull. Every one of those was a command in a class nobody had asked for. Saying
"I will stop" did not stop it; CLAUDE.md rule 12 records that only a gate ever has.

How it decides: claude_prompt_recorder.py keeps the last user messages of this session.
A command in one of the CLASSES below is allowed only if one of that class's verbs appears
in the last LOOKBACK messages. Otherwise it is denied with a one-line reason, before the
action -- the same moment claude_bash_guard.py fires.

What it gates: only actions on ANOTHER machine -- remote services, remote process execution,
copying onto or deleting from a c$/admin$ share, and pulling event logs or dumps off one.

What it does not gate: anything local (dotnet build/test/publish, deploy scripts, git commit,
push, worktree), and reads. Grep, cat, ls, Get-Content, a CIM query, a c$ listing all pass.
The gate stops acting beyond the ask, not investigating beyond it. A verb match is crude:
"don't fix it yet" contains "fix". And a class not listed here is not gated -- e.g. a deploy
script that itself copies to c$ passes, because only the script name is on the command line.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import json
import os
import re
import sys
import tempfile

LOOKBACK = 3   # user messages; "go" after "unzip the new build" still carries the ask

# Repo rule 7 in force here too: every pattern is a raw string.
# ONLY actions on ANOTHER machine. Local build, test, publish, worktree and commit/push were
# gated here at first and were dropped on 2026-10-03: every one is cheap to undo on this PC,
# and gating them blocked ordinary asks ("clean up my git" says neither commit nor push).
# What the 2026-09-09 incident actually cost was done to colleagues' machines.
CLASSES = [
    # Its own class, because "get it ready" legitimately authorises staging an app onto c$
    # and must NOT also authorise hoovering diagnostics off a colleague's machine. The
    # 2026-09-09 event-log pull off Jim's PC was rejected by the user by hand.
    ("pull event logs or crash dumps off a remote machine",
     re.compile(r"(?:\\\\|//)[\w.-]+[\\/](?:c|admin)\$[^\n]*(?:\.evtx|winevt|CrashDumps|Minidump)"
                r"|(?:\.evtx|winevt|CrashDumps)[^\n]*(?:\\\\|//)[\w.-]+[\\/](?:c|admin)\$", re.I),
     ["log", "event", "crash", "dump", "diagnose", "diagnostic", "investigate", "why"]),

    ("create, start, stop or delete a service on a remote host",
     re.compile(r"\bsc(?:\.exe)?\s+(?:\\\\|//)[\w.-]+\s+(?:create|start|stop|delete|config|failure)\b", re.I),
     ["service", "start", "stop", "restart", "install", "deploy", "remediate", "fix", "kill"]),

    ("execute a process on a remote host",
     re.compile(r"\bInvoke-CimMethod\b|\bInvoke-Command\b|\bEnter-PSSession\b|\bInvoke-WmiMethod\b|\bpsexec\b", re.I),
     ["run", "execute", "remote", "restart", "install", "deploy", "remediate", "fix", "kill", "purge"]),

    ("copy, extract or move files onto a remote admin share",
     re.compile(r"\b(?:robocopy|Copy-Item|xcopy|Expand-Archive|Move-Item|cp)\b[^\n]*(?:\\\\|//)[\w.-]+[\\/](?:c|admin)\$", re.I),
     ["copy", "deploy", "install", "push", "stage", "unzip", "extract", "replace", "ready", "pull", "log"]),

    ("delete files on a remote admin share",
     re.compile(r"\b(?:Remove-Item|rm|del|rmdir)\b[^\n]*(?:\\\\|//)[\w.-]+[\\/](?:c|admin)\$", re.I),
     ["remove", "delete", "wipe", "clean", "uninstall", "deploy", "ready"]),
]


# Reading a script is how prior art gets found -- CLAUDE.md rule 1 requires it. A segment
# that only reads must never be gated, or the guard punishes the right behaviour.
# It bit immediately: a Get-CimInstance listing of running processes was denied because the
# filter string named MSBuild.exe. Inspecting is not acting -- every read-only verb belongs here.
READ_SEGMENT = re.compile(
    r"^\s*(?:sudo\s+)?(?:cat|bat|less|more|head|tail|grep|rg|sed|awk|wc|file|stat|diff|ls|find|"
    r"tasklist|python|py|"
    r"Get-\w+|Select-String|Select-Object|Where-Object|Measure-Object|Test-Path|Test-NetConnection|"
    r"Resolve-DnsName|type)\b", re.I)


def segments(cmd):
    """Split on shell separators so a read of a script is not read as a run of it."""
    return [s for s in re.split(r"&&|\|\||[;|\n]", cmd) if s.strip()]


def acting_segments(cmd):
    return [s for s in segments(cmd) if not READ_SEGMENT.match(s)]


def recent_prompts(session):
    path = os.path.join(tempfile.gettempdir(), "claude-asked", (session or "unknown") + ".jsonl")
    if not os.path.exists(path):
        return None
    prompts = []
    with open(path, encoding="utf-8") as f:
        for ln in f.read().splitlines():
            try:
                prompts.append(json.loads(ln).get("prompt", ""))
            except Exception:
                pass
    return prompts[-LOOKBACK:]


def deny(reason):
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, sys.stdout)
    sys.exit(0)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    cmd = (payload.get("tool_input") or {}).get("command") or ""
    if not cmd:
        return

    acting = "\n".join(acting_segments(cmd))
    if not acting.strip():
        return
    hits = [(name, verbs) for name, pattern, verbs in CLASSES if pattern.search(acting)]
    if not hits:
        return

    prompts = recent_prompts(payload.get("session_id"))
    if prompts is None:
        deny("NOT ASKED (no record of any user message this session). This command would "
             "%s. Ask the user first, then act." % hits[0][0])

    text = "\n".join(prompts).lower()
    for name, verbs in hits:
        if not any(re.search(r"\b" + re.escape(v) + r"\b", text) for v in verbs):
            deny("NOT ASKED. This command would %s, and none of the last %d user messages "
                 "says %s. Ask the user first, then act." % (
                     name, len(prompts), ", ".join(verbs)))


if __name__ == "__main__":
    main()
