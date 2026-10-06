# KOR NetworkOps -- one read-only health snapshot of an ESXi host, as JSON on stdout. Runs ON the host
# (python + pyVmomi ship with ESXi 7), logged in through hostd with a local ticket: no password exists
# anywhere. NetworkOps (Rack/EsxiRules) turns it into facts, metrics and findings. Changes NOTHING.
import json, ssl, time
from pyVmomi import vim, SoapStubAdapter

si = vim.ServiceInstance("ServiceInstance", SoapStubAdapter(host="localhost", port=443, path="/sdk", sslContext=ssl._create_unverified_context()))
c = si.RetrieveContent()
t = c.sessionManager.AcquireLocalTicket(userName="root")
with open(t.passwordFilePath) as f:
    c.sessionManager.Login(t.userName, f.read())
h = c.rootFolder.childEntity[0].hostFolder.childEntity[0].host[0]
cm = h.configManager


def service(key):
    s = [x for x in cm.serviceSystem.serviceInfo.service if x.key == key]
    return {"policy": s[0].policy, "running": s[0].running} if s else None


sensors = []
hw = h.runtime.healthSystemRuntime
if hw is not None:
    for s in (hw.systemHealthInfo.numericSensorInfo if hw.systemHealthInfo else []) or []:
        sensors.append({"name": s.name, "type": s.sensorType, "state": s.healthState.key if s.healthState else "unknown"})
    st = hw.hardwareStatusInfo
    if st is not None:
        for group in (st.memoryStatusInfo, st.cpuStatusInfo, st.storageStatusInfo):
            for s in group or []:
                sensors.append({"name": s.name, "type": "hardware", "state": s.status.key if s.status else "unknown"})

# Class 2 (d): each VM's virtual NIC adapter class (VirtualVmxnet3 is the one that performs; VirtualE1000/E1000e are legacy).
def vm_nics(v):
    try:
        devs = v.config.hardware.device if v.config and v.config.hardware else []
        return [type(d).__name__ for d in devs if isinstance(d, vim.vm.device.VirtualEthernetCard)]
    except Exception:
        return []


# Class 2 (f): repeated iSCSI connection drops. The host reads its own /var/log/vmkernel.log (the current, un-rotated one --
# inherently recent); a working path that flaps logs iscsivmk_StopConnection on every drop. -1 = the log could not be read.
iscsi_drops = -1
try:
    with open("/var/log/vmkernel.log", errors="ignore") as lf:
        iscsi_drops = sum(1 for line in lf if "iscsivmk_StopConnection" in line)
except (IOError, OSError):
    iscsi_drops = -1

out = {
    "name": h.name,
    "iscsiDrops": iscsi_drops,
    "version": c.about.fullName,
    "vendor": h.hardware.systemInfo.vendor,
    "model": h.hardware.systemInfo.model,
    "serial": next((i.identifierValue for i in (h.hardware.systemInfo.otherIdentifyingInfo or []) if i.identifierType.key == "SerialNumberTag"), None),
    "bootTime": h.runtime.bootTime.isoformat() if h.runtime.bootTime else None,
    "hostTimeUtc": cm.dateTimeSystem.QueryDateTime().isoformat(),
    "epoch": time.time(),
    "maintenanceMode": h.runtime.inMaintenanceMode,
    "overallStatus": str(h.overallStatus),
    "cpuMhzUsed": h.summary.quickStats.overallCpuUsage,
    "cpuMhzTotal": h.hardware.cpuInfo.hz / 1000000 * h.hardware.cpuInfo.numCpuCores,
    "memMbUsed": h.summary.quickStats.overallMemoryUsage,
    "memMbTotal": h.hardware.memorySize // (1024 * 1024),
    "ntp": service("ntpd"),
    "ptp": service("ptpd"),
    "ssh": service("TSM-SSH"),
    "ntpServers": list(cm.dateTimeSystem.dateTimeInfo.ntpConfig.server or []),
    "datastores": [{"name": d.summary.name, "type": d.summary.type, "accessible": d.summary.accessible,
                    "capacityGb": round(d.summary.capacity / 1e9, 1), "freeGb": round(d.summary.freeSpace / 1e9, 1)} for d in h.datastore],
    "vms": [{"name": v.name, "power": str(v.runtime.powerState), "tools": str(v.guest.toolsRunningStatus) if v.guest else None,
             "status": str(v.overallStatus)} for v in h.vm],
    "sensors": sensors,
}
c.sessionManager.Logout()
print(json.dumps(out))
