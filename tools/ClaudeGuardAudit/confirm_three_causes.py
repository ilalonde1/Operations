r"""Confirm, against the live regexes in claude_instrument_guard.py, the three causes Virtual Drafter
reported for the PDF Ingestor's wrong refusals. Imports the guard by path so there is no second copy
of the patterns to drift."""
import importlib.util
import os
import sys

GUARD = r"C:\VIsual Studio Projects\Operations\tools\claude_instrument_guard.py"
spec = importlib.util.spec_from_file_location("guard", GUARD)
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)

print("=== (a) does net_probe's real output satisfy SELF_TEST? ===")
outs = [
    "31087 axes in plan: 80,103 and 80,124 listed",
    "nothing listed outside the plan",
    "scanned 62 files\nresult: 0 found",
]
for o in outs:
    print("  SELF_TEST %-5s  NIL %-5s  %r" % (
        bool(g.SELF_TEST.search(o)), bool(g.NIL_CLAIM.search(o)), o[:46]))

print()
print("=== (b) does the basename appear in an indirect invocation? ===")
base = "net_probe.ps1"
for cmd in ["& $p -Plan 31087", "pwsh -File 'C:/scratch/net_probe.ps1' -Plan 31087"]:
    print("  %-52s basename-in-cmd: %s" % (cmd, base in cmd))

print()
print("=== (c) which nils does one message carry, all pinned on one script? ===")
msg = ("Probe result: no axes were found outside the plan; tasklist shows 0 of 6 PIDs left "
       "and the network-check returned none found.")
print("  matches:", [m.group(0) for m in g.NIL_CLAIM.finditer(msg)])
