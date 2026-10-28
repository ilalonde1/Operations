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

# the password goes to sqlcmd through SQLCMDPASSWORD, never -P: a command line is readable by any process lister
# (NetworkOps' wedge check showed it, 2026-10-28 01:57)
cmd = ["sqlcmd", "-S", server, "-d", database, "-U", user,
       "-b", "-W", "-s", "|", "-w", "400",
       "-l", "15", "-t", "120"]   # login 15 s, query 120 s: a VPN stall is an error, never a hang
# sqlcmd -q runs the query and then STAYS OPEN reading stdin; -Q runs it and exits. Every caller here means "run and
# exit", so -q is passed as -Q and stdin is closed: four background oq.py calls (10-25, 10-26 x2, 10-28 00:34) sat idle
# for hours holding KOR-APP01:1433 because their stdin never closed - the silent-hang class, found by NetworkOps' lister
cmd += ["-Q" if x == "-q" else x for x in sys.argv[1:]]
r = subprocess.run(cmd, env=dict(os.environ, SQLCMDPASSWORD=pwd), stdin=subprocess.DEVNULL)
sys.exit(r.returncode)
