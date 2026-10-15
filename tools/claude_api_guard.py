"""PreToolUse guard for Write, Edit, Bash and PowerShell: no Anthropic API in the BD work.

Why this exists, 2026-10-15. Ian, on the 9d first draft, which put claim extraction on the
MCP server through the API: "Why would we EVER use the API for this?" I rewrote 9d so the
extraction ran in my session. Then I wrote the 9e brief with the Director's weekly brief
generated at RUNTIME on the server through `AskService` -- reasoning that `/ask` and the COO
card already did -- and put that as a decision line in the brief instead of asking him. It
shipped, I fired it five times, and told him to buy credits. "WHY THE FUCK ARE YOU USING
THE API? WE TALKED ABOUT THIS." Then: "You KEEP going back to it." Then: "This is nearly
constant fighting against you to get things done... I implore you to just please try to be
better."

The failure is a decision he had already made being re-decided inside a document. Trying
harder is prose, and CLAUDE.md rule 12 records that prose loses to momentum and only a gate
has ever held. So this is the gate.

What it checks:
  * A Write or Edit whose new text carries an API marker (AskService/AskRequest/AskAsync,
    api.anthropic.com, AnthropicApiKey/AnthropicModel, x-api-key, anthropic-version, a Claude
    model id) INTO a Codex brief (docs/codex/) or into the BD Director / Knowledge / CLI code
    paths listed in SCOPED_PATHS is denied.
  * A Bash or PowerShell acting segment that calls the live gateway's spending endpoints
    (:5500/ask, any .../run-now) or api.anthropic.com is denied. Reads (grep, cat, Get-*)
    pass, so looking for AskService in the code is never blocked.
  * The only thing that lifts a denial is the literal token IAN-APPROVED-API-USE in one of the
    last LOOKBACK user messages, which only Ian writes. Nothing in the tool call itself can
    grant it.

What it does not check: the existing `/ask` product and COO card (Kor.Operations.Mcp/Ai,
Brief, Program.cs) -- those are Ian's call and are out of scope, not precedent; a command
that runs a script which itself calls the API (only the script name is on the command line);
a brief that describes the API in words none of the markers match; a new wrapper or package
name not in MARKERS. Same-class fault it would NOT catch: a design that moves the model call
into the App or the Worker under a name this list does not know. The remedy for that is the
rule in prose plus the next marker, added the day it is found.

stdin  : the PreToolUse payload
stdout : nothing (allow), or a permissionDecision of "deny"
"""
import json
import os
import re
import sys
import tempfile

LOOKBACK = 3
APPROVAL = "IAN-APPROVED-API-USE"

# Repo rule 7: every pattern is a raw string.
MARKERS = re.compile(
    r"\bAskService\b|\bAskRequest\b|\bAskAsync\b|api\.anthropic\.com|\bAnthropicApiKey\b|"
    r"\bAnthropicModel\b|x-api-key|anthropic-version|\bclaude-(?:sonnet|opus|haiku|fable)-\d",
    re.I)

# Where a marker is a design decision Ian has already made: briefs, and the BD Director /
# Knowledge line of work. Forward slashes; the path is normalised before matching.
SCOPED_PATHS = (
    "docs/codex/",
    "kor.operations.mcp/director/",
    "kor.operations.mcp/alerts/",
    "kor.opportunities.knowledge/",
    "kor.opportunities.knowledge.tests/",
    "kor.opportunities.data/knowledge/",
    "kor.operations.app/businessdevelopment/director/",
    "kor.operations.app/services/directorbriefclient.cs",
    "tools/bdknowledgeingest/",
)

SPENDING_CALL = re.compile(
    r":5500/ask\b|/ask\?|\brun-now\b|api\.anthropic\.com", re.I)

# Same read-only verbs as claude_asked_guard.py: inspecting is not acting.
READ_SEGMENT = re.compile(
    r"^\s*(?:sudo\s+)?(?:cat|bat|less|more|head|tail|grep|rg|sed|awk|wc|file|stat|diff|ls|find|"
    r"tasklist|python|py|"
    r"Get-\w+|Select-String|Select-Object|Where-Object|Measure-Object|Test-Path|Test-NetConnection|"
    r"Resolve-DnsName|type)\b", re.I)


def segments(cmd):
    return [s for s in re.split(r"&&|\|\||[;|\n]", cmd) if s.strip()]


def acting_segments(cmd):
    return [s for s in segments(cmd) if not READ_SEGMENT.match(s)]


def recent_prompts(session):
    path = os.path.join(tempfile.gettempdir(), "claude-asked", (session or "unknown") + ".jsonl")
    if not os.path.exists(path):
        return []
    prompts = []
    with open(path, encoding="utf-8") as f:
        for ln in f.read().splitlines():
            try:
                prompts.append(json.loads(ln).get("prompt", ""))
            except Exception:
                pass
    return prompts[-LOOKBACK:]


def approved(session):
    return any(APPROVAL in p for p in recent_prompts(session))


def deny(what):
    reason = ("NO API. Ian, 9d: \"Why would we EVER use the API for this?\"; 10-15: \"WHY THE FUCK ARE "
              "YOU USING THE API? WE TALKED ABOUT THIS.\" This %s. The method is export, write it in "
              "this session, import. Only a user message containing %s lifts this." % (what, APPROVAL))
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, sys.stdout)
    sys.exit(0)


def norm(path):
    return (path or "").replace("\\", "/").lower()


def in_scope(path):
    p = norm(path)
    return any(s in p for s in SCOPED_PATHS)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    tool = payload.get("tool_name") or ""
    inp = payload.get("tool_input") or {}
    session = payload.get("session_id")

    if tool in ("Write", "Edit"):
        text = inp.get("content") if tool == "Write" else inp.get("new_string")
        path = inp.get("file_path") or ""
        if text and in_scope(path) and MARKERS.search(text):
            if approved(session):
                return
            m = MARKERS.search(text).group(0)
            deny("%s would put an Anthropic API call (%s) into %s" % (tool, m, norm(path)))
        return

    if tool in ("Bash", "PowerShell"):
        cmd = inp.get("command") or ""
        acting = "\n".join(acting_segments(cmd))
        if acting.strip() and SPENDING_CALL.search(acting):
            if approved(session):
                return
            deny("command would call the live gateway's spending endpoint or the Anthropic API (%s)"
                 % SPENDING_CALL.search(acting).group(0))
        return


if __name__ == "__main__":
    main()
