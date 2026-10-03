# FIX remove-stale-datastore -- runs ON an ESXi host (python + pyVmomi ship with ESXi 7), logged in through hostd with a
# local ticket, exactly as Rack/esxi-health.py: no password exists anywhere. Removes ONE datastore, and only when every one
# of these holds, read from the host itself just before:
#   it is a Veeam instant-recovery leftover (named VeeamBackup_*), it is NFS, the host reports it INACCESSIBLE, and no VM
#   is registered on it.
# Anything else is refused and nothing changes. After removing, the host's datastore list is read again to confirm it is gone.
# Ian, 2026-10-02: "Can we actually remove the stale data store?" -- it came back four times that week (Veeam leaves one
# behind after an instant recovery), each time cleared by hand with esxcli.
# argv: <datastore name> [check]   -- "check" stops after the checks: nothing is removed.
import json, ssl, sys
from pyVmomi import vim, SoapStubAdapter


def out(result, **extra):
    print(json.dumps([dict(Result=result, **extra)]))
    sys.exit(0)


name = sys.argv[1]
dry = len(sys.argv) > 2 and sys.argv[2] == "check"

si = vim.ServiceInstance("ServiceInstance", SoapStubAdapter(host="localhost", port=443, path="/sdk", sslContext=ssl._create_unverified_context()))
c = si.RetrieveContent()
t = c.sessionManager.AcquireLocalTicket(userName="root")
with open(t.passwordFilePath) as f:
    c.sessionManager.Login(t.userName, f.read())
h = c.rootFolder.childEntity[0].hostFolder.childEntity[0].host[0]

found = [d for d in h.datastore if d.summary.name == name]
if not found:
    out("Nothing to do: no datastore named %s on this host (already removed?)." % name, Removed=False)
d = found[0]
s = d.summary
vms = [v.name for v in (d.vm or [])]
facts = dict(Type=s.type, Accessible=bool(s.accessible), Vms=vms)
if not name.startswith("VeeamBackup_"):
    out("Refused: %s is not a Veeam instant-recovery datastore (VeeamBackup_*); this fix removes only those." % name, Removed=False, **facts)
if s.type not in ("NFS", "NFS41"):
    out("Refused: %s is %s, not NFS." % (name, s.type), Removed=False, **facts)
if s.accessible:
    out("Refused: %s is ACCESSIBLE -- it is in use, not a leftover." % name, Removed=False, **facts)
if vms:
    out("Refused: %d VM(s) are registered on %s: %s." % (len(vms), name, ", ".join(vms)), Removed=False, **facts)
if dry:
    out("Ready: %s is an inaccessible Veeam NFS leftover with no VMs; it can be removed. Nothing was changed." % name, Removed=False, **facts)

h.configManager.datastoreSystem.RemoveDatastore(d)
gone = not [x for x in h.datastore if x.summary.name == name]
out(("Removed %s; the host no longer lists it." % name) if gone else ("RemoveDatastore returned but %s is still listed." % name), Removed=gone, **facts)
