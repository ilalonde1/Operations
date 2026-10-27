r"""suite_diff.py <dotnet-test-log> [known-failures.txt] : a Core suite run's failures against the known list - NEW (not on the list),
KNOWN (on it, failed again), FIXED (on it, passed or absent from this run's failures). Exit 1 when anything is NEW, 0 otherwise.

Why (2026-10-27): a midpoint report told operations-ee "3 known" failures when the list was 4 - counted from memory. The known list
is a file (Kor.Operations.EngineeringTools.Core.Tests/known-failures.txt) and every report is this script's output, not a recollection.

COVERS: the lines `dotnet test` prints as "  Failed <fully qualified name> [time]", and the run's own "Failed!/Passed! - Failed: N"
total (printed, and checked against the lines it found). DOES NOT: a run that crashed before printing a total (says so); a test
skipped (not a failure); which known failure failed for a NEW reason - a known test failing differently reads KNOWN. Read the
message of every KNOWN failure whose message you have not seen before.
"""
import os, re, sys

log = sys.argv[1]
known_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                                                  "Kor.Operations.EngineeringTools.Core.Tests", "known-failures.txt")
known = {}
for line in open(known_path, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        name, _, why = line.rstrip("\n").partition(" | ")
        known[name.strip()] = why.strip()
text = open(log, encoding="utf-8", errors="replace").read()
failed = sorted({m.group(1) for m in re.finditer(r"^\s+Failed (\S.*?) \[[^\]]*\]\s*$", text, re.M)})
total = re.search(r"(Failed|Passed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", text)
new = [f for f in failed if f not in known]
again = [f for f in failed if f in known]
fixed = [k for k in known if k not in failed]
print(f"run total: {total.group(0) if total else 'NO TOTAL PRINTED - the run did not finish'}")
if total and int(total.group(2)) != len(failed):
    print(f"WARNING: the total says {total.group(2)} failed and {len(failed)} distinct failure lines were found")
print(f"\nNEW ({len(new)}):" + "".join(f"\n  {f}" for f in new))
print(f"\nKNOWN ({len(again)} of {len(known)}):" + "".join(f"\n  {f}\n      {known[f]}" for f in again))
print(f"\nFIXED or not failing this run ({len(fixed)}):" + "".join(f"\n  {f}" for f in fixed))
sys.exit(1 if new else 0)
