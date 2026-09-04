"""Run a query against KorOpportunitiesDb using the worker's own connection string.

Usage:  python q.py <file.sql>   |   echo "SELECT 1" | python q.py -
Prints pipe-separated rows. Credentials come from the KOR_OPPORTUNITIES_OPPORTUNITIESDB
environment variable and are never echoed.
"""
import os
import subprocess
import sys
import tempfile

cs = os.environ.get("KOR_OPPORTUNITIES_OPPORTUNITIESDB", "")
parts = {}
for kv in cs.split(";"):
    if "=" in kv:
        k, v = kv.split("=", 1)
        parts[k.strip().lower()] = v.strip()

sql = sys.stdin.read() if (len(sys.argv) < 2 or sys.argv[1] == "-") else open(sys.argv[1], encoding="utf-8").read()

# sqlcmd -i needs a Windows path and a BOM (utf8BOM always, per the repo notes).
with tempfile.NamedTemporaryFile("w", suffix=".sql", delete=False, encoding="utf-8-sig") as f:
    f.write(sql)
    path = f.name

cmd = [
    "sqlcmd",
    "-S", parts["server"],
    "-d", parts["database"],
    "-U", parts["uid"],
    "-P", parts["pwd"],
    "-C",
    "-W",
    "-s", "|",
    "-i", path,
]
r = subprocess.run(cmd, capture_output=True, text=True)
sys.stdout.write(r.stdout)
sys.stderr.write(r.stderr)
os.unlink(path)
sys.exit(r.returncode)
