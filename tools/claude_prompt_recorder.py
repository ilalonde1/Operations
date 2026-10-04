"""UserPromptSubmit hook: record what the user actually asked, so a later hook can check it.

Companion to claude_asked_guard.py. Appends each user message to a per-session file; the
guard reads the last few back and refuses build/publish/deploy/remote-exec commands whose
verb never appeared in them. This file is the memory the guard checks against -- it holds
nothing else and emits nothing.

stdin  : the UserPromptSubmit payload ({"session_id", "prompt", ...})
stdout : nothing
"""
import json
import os
import sys
import tempfile

KEEP_LINES = 20
MAX_CHARS = 4000


def record_dir():
    d = os.path.join(tempfile.gettempdir(), "claude-asked")
    os.makedirs(d, exist_ok=True)
    return d


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return
    session = payload.get("session_id") or "unknown"
    prompt = (payload.get("prompt") or "").strip()
    if not prompt:
        return
    path = os.path.join(record_dir(), session + ".jsonl")
    lines = []
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            lines = [ln for ln in f.read().splitlines() if ln.strip()]
    lines.append(json.dumps({"prompt": prompt[:MAX_CHARS]}, ensure_ascii=False))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines[-KEEP_LINES:]) + "\n")


if __name__ == "__main__":
    main()
