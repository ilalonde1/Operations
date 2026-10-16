"""Run SQL against KorOpportunitiesDb using the app's own connection string.

Windows auth on this account cannot open the database; the service login in
KOR_OPPORTUNITIES_OPPORTUNITIESDB can. Parses that env var rather than holding
a second copy of the credentials anywhere.

Usage:  python oq.py -i <file.sql>
        python oq.py -q "SELECT 1"
"""
import os
import subprocess
import sys

RAW = os.environ.get("KOR_OPPORTUNITIES_OPPORTUNITIESDB", "")
if not RAW:
    print("KOR_OPPORTUNITIES_OPPORTUNITIESDB is not set", file=sys.stderr)
    sys.exit(2)

parts = {}
pending_pwd = None
for seg in RAW.split(";"):
    if "=" not in seg:
        continue
    k, v = seg.split("=", 1)
    key = k.strip().lower()
    if key == "":
        # The password key arrives with an empty name in this variable.
        pending_pwd = v
    else:
        parts[key] = v

server = parts.get("server") or parts.get("data source")
database = parts.get("database") or parts.get("initial catalog")
user = parts.get("uid") or parts.get("user id")
pwd = parts.get("pwd") or parts.get("password") or pending_pwd

if not (server and database and user and pwd):
    print("connection string is missing server/database/uid/pwd", file=sys.stderr)
    sys.exit(2)

cmd = ["sqlcmd", "-S", server, "-d", database, "-U", user, "-P", pwd,
       "-b", "-y", "0", "-Y", "0",
       "-l", "15", "-t", "120"]   # login 15 s, query 120 s: a VPN stall is an error, never a hang
cmd += sys.argv[1:]
r = subprocess.run(cmd)
sys.exit(r.returncode)
