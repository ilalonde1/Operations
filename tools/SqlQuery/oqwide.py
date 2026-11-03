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
# (as oq.py since 2026-10-28; oqwide.py still passed -P until 3 Nov 2026, Framework lead's ask)
cmd = ["sqlcmd", "-S", server, "-d", database, "-U", user,
       "-b", "-y", "0", "-Y", "0",
       "-l", "15", "-t", "120"]   # login 15 s, query 120 s: a VPN stall is an error, never a hang
args = sys.argv[1:]
dry = "--dry-run-argv" in args
args = [a for a in args if a != "--dry-run-argv"]
# -q leaves sqlcmd open on stdin (oq.py's silent-hang class): run as -Q, stdin closed
cmd += ["-Q" if x == "-q" else x for x in args]
if dry:
    # proof that the child's argv holds no password: argv as it would run, plus the NAME of the variable that carries it
    print("argv:", cmd)
    print("password passed in child env var: SQLCMDPASSWORD")
    print("password in argv:", any(x == pwd for x in cmd) or any(pwd in x for x in cmd))
    sys.exit(0)
r = subprocess.run(cmd, env=dict(os.environ, SQLCMDPASSWORD=pwd), stdin=subprocess.DEVNULL)
sys.exit(r.returncode)
