"""Read-only: run a SELECT ... FOR JSON PATH against KorOpportunitiesDb (and DELTEK_VP via the linked server)
with the app's own connection string, and write the JSON array to a file. Parses the env var like oq.py;
never prints the connection string.  Usage: python oqjson.py <in.sql> <out.json>"""
import os, subprocess, sys, json
RAW = os.environ.get("KOR_OPPORTUNITIES_OPPORTUNITIESDB", "")
parts, pending = {}, None
for seg in RAW.split(";"):
    if "=" not in seg: continue
    k, v = seg.split("=", 1); k = k.strip().lower()
    if k == "": pending = v
    else: parts[k] = v
server = parts.get("server") or parts.get("data source")
db = parts.get("database") or parts.get("initial catalog")
user = parts.get("uid") or parts.get("user id")
pwd = parts.get("pwd") or parts.get("password") or pending
sql = open(sys.argv[1], encoding="utf-8").read()
cmd = ["sqlcmd", "-S", server, "-d", db, "-U", user, "-P", pwd, "-b", "-y", "0", "-l", "15", "-t", "300", "-f", "65001", "-Q", "SET NOCOUNT ON; " + sql]
r = subprocess.run(cmd, capture_output=True)
if r.returncode != 0:
    print("FAIL", r.returncode, r.stdout.decode("utf-8", "replace")[:2000], r.stderr.decode("utf-8", "replace")[:2000]); sys.exit(1)
lines = r.stdout.decode("utf-8", "replace").splitlines()
start = next((i for i, l in enumerate(lines) if l.lstrip().startswith("[")), len(lines))
txt = "".join(lines[start:]).strip()
try:
    data = json.loads(txt) if txt else []
except json.JSONDecodeError as e:
    print("JSON error at", e.pos, repr(txt[max(0, e.pos - 300):e.pos + 300])); sys.exit(1)
json.dump(data, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False)
print(len(data), "rows ->", sys.argv[2])
