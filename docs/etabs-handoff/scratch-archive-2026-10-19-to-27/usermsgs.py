"""Print the user's own messages from a Claude Code transcript. usage: usermsgs.py <file.jsonl>"""
import json, sys
n = 0
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        m = json.loads(line)
    except Exception:
        continue
    if m.get("type") != "user":
        continue
    c = m.get("message", {}).get("content")
    if isinstance(c, list):
        c = " ".join(x.get("text", "") for x in c if isinstance(x, dict) and x.get("type") == "text")
    if not isinstance(c, str) or c.startswith("<") or len(c.strip()) < 3:
        continue
    n += 1
    print("-", m.get("timestamp", "")[11:16], c.strip().replace("\n", " ")[:240])
print("user messages:", n)
