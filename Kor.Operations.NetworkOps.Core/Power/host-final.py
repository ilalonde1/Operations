# KOR NetworkOps -- the last step of the UPS shutdown chain, run ON an ESXi host.
#
# NetworkOps writes this to /tmp over SSH and starts it detached, so it runs to the end after
# NetworkOps itself (a VM on this host) is gone. Everything it does goes through hostd with a
# local ticket (no password): a clean guest shutdown, then a forced ShutdownHost -- NOT busybox
# poweroff, and NOT /sbin/shutdown.sh, which on ESXi 7.0.2 is an empty stub.
#
# argv[1] is a JSON config file, deleted the moment it is read: it carries the Synology passwords,
# and /tmp on ESXi is RAM.
#   dryRun          true: change nothing; prove every step CAN be taken (VM found, each Synology
#                   answers a login, the host API answers). The firewall is still opened and
#                   closed, because that is part of what is being proven.
#   vm              the VM to shut down first (the controller), or null
#   timeoutSeconds  wait for a guest shutdown before a hard power-off
#   storage         [{name, address, account, password}] shut down in this order (the SAN last)
#   powerOffHost    true: power this host off at the end
#
# Each stage is independent: a failure is logged and the chain carries on, because stopping
# half-way leaves the rack running on a battery that is about to go flat.
import json, os, ssl, subprocess, sys, time, urllib.parse, urllib.request
from pyVmomi import vim, SoapStubAdapter

LOG = "/tmp/kor-chain.log"


def log(msg):
    # /tmp for NetworkOps to read back, syslog for the host's own log. ESXi has no `logger` binary,
    # and nothing here may stop the chain: a log line that cannot be written is dropped, not raised.
    line = time.strftime("%Y-%m-%d %H:%M:%S") + " " + msg
    try:
        with open(LOG, "a") as f:
            f.write(line + "\n")
    except Exception:
        pass
    try:
        import syslog
        syslog.openlog("kor-chain")
        syslog.syslog(msg)
    except Exception:
        pass


def detach():
    # Double fork: the SSH session that started this can close (and NetworkOps can die with its VM)
    # without taking the chain with it. ESXi has no nohup/setsid commands, only these syscalls.
    if os.fork() > 0:
        os._exit(0)
    os.setsid()
    if os.fork() > 0:
        os._exit(0)
    devnull = os.open(os.devnull, os.O_RDWR)
    for fd in (0, 1, 2):
        os.dup2(devnull, fd)


def connect():
    ctx = ssl._create_unverified_context()
    si = vim.ServiceInstance("ServiceInstance", SoapStubAdapter(host="localhost", port=443, path="/sdk", sslContext=ctx))
    content = si.RetrieveContent()
    ticket = content.sessionManager.AcquireLocalTicket(userName="root")
    with open(ticket.passwordFilePath) as f:
        pwd = f.read()
    content.sessionManager.Login(ticket.userName, pwd)
    return content, content.rootFolder.childEntity[0].hostFolder.childEntity[0].host[0]


def wait_task(task, seconds):
    deadline = time.time() + seconds
    while task.info.state not in ("success", "error") and time.time() < deadline:
        time.sleep(2)
    return task.info.state


def shut_vm(vm, timeout, dry):
    state = vm.runtime.powerState
    if state != "poweredOn":
        log("%s is %s: nothing to do" % (vm.name, state))
        return
    if dry:
        log("DRY RUN: would shut down %s (VMware Tools %s)" % (vm.name, vm.guest.toolsRunningStatus))
        return
    log("shutting down %s" % vm.name)
    started = time.time()
    try:
        vm.ShutdownGuest()
    except Exception as e:
        log("guest shutdown of %s refused (%s): powering it off" % (vm.name, e))
        log("%s power-off: %s" % (vm.name, wait_task(vm.PowerOffVM_Task(), 120)))
        return
    while time.time() - started < timeout:
        if vm.runtime.powerState == "poweredOff":
            log("%s is off after %d s" % (vm.name, time.time() - started))
            return
        time.sleep(5)
    log("%s still on after %d s: powering it off" % (vm.name, timeout))
    log("%s power-off: %s" % (vm.name, wait_task(vm.PowerOffVM_Task(), 120)))


def firewall_enabled():
    return "Enabled: true" in subprocess.check_output(["esxcli", "network", "firewall", "get"]).decode()


def set_firewall(on):
    subprocess.check_call(["esxcli", "network", "firewall", "set", "--enabled", "true" if on else "false"])


def dsm(target, dry):
    # The calls are the ones DSM's own power menu makes (read from its sds.js on these boxes,
    # 2026-09-29). A dual-controller UC3200 must be shut down as ONE cluster -- relay_node
    # "node0,node1" with suspend_taking_over -- or the surviving controller takes over the other's
    # storage instead of stopping. Relay calls need the cookie session and its SynoToken, not _sid.
    base = "https://%s:5001/webapi/" % target["address"]
    ctx = ssl._create_unverified_context()
    headers = {}

    def call(path, params):
        req = urllib.request.Request(base + path, data=urllib.parse.urlencode(params).encode(), headers=headers)
        with urllib.request.urlopen(req, context=ctx, timeout=60) as r:
            return json.loads(r.read().decode())

    info = call("query.cgi", {"api": "SYNO.API.Info", "version": "1", "method": "query",
                              "query": "SYNO.API.Auth,SYNO.Core.System,SYNO.Core.System.Poweroff"})
    auth, system = info["data"]["SYNO.API.Auth"], info["data"]["SYNO.Core.System"]
    login = call(auth["path"], {"api": "SYNO.API.Auth", "version": str(min(6, auth["maxVersion"])), "method": "login",
                                "account": target["account"], "passwd": target["password"], "session": "NetworkOps",
                                "format": "cookie", "enable_syno_token": "yes"})
    if not login.get("success"):
        raise Exception("login refused, error %s" % login.get("error", {}).get("code"))
    headers["Cookie"] = "id=" + login["data"]["sid"]
    headers["X-SYNO-TOKEN"] = login["data"].get("synotoken", "")
    cluster = bool(target.get("dualController"))
    relay = {"relay_options": json.dumps({"skip_role_check": True})}

    if dry:
        if cluster:
            check = info["data"]["SYNO.Core.System.Poweroff"]
            for node in ("node0", "node1"):
                r = call(check["path"], dict(relay, api="SYNO.Core.System.Poweroff", version="1", method="check",
                                             cache_check_shutdown="false", relay_node=node))
                log("DRY RUN: %s %s power-off check: %s" % (target["name"], node, "clear" if r.get("success") else "BLOCKED: %s" % r))
        else:
            r = call(system["path"], {"api": "SYNO.Core.System", "version": "1", "method": "info"})
            log("DRY RUN: %s answered (%s): would shut it down" % (target["name"], r.get("data", {}).get("model", "model unknown")))
        call(auth["path"], {"api": "SYNO.API.Auth", "version": "1", "method": "logout", "session": "NetworkOps"})
        return

    # force: an emergency does not wait for DSM's running tasks, exactly as the UI does once confirmed.
    params = {"api": "SYNO.Core.System", "version": "1", "method": "shutdown",
              "force": "true", "local": "true", "cache_check_shutdown": "true"}
    if cluster:
        params.update(relay, relay_node="node0,node1", suspend_taking_over="true")
    else:
        params["firmware_upgrade"] = "false"
    r = call(system["path"], params)
    log("%s: shutdown %s" % (target["name"], "accepted" if r.get("success") else "REFUSED: %s" % r))


def main():
    with open(sys.argv[1]) as f:
        cfg = json.load(f)
    os.remove(sys.argv[1])
    dry = bool(cfg.get("dryRun", True))
    log("chain final step starting%s" % (" (DRY RUN: nothing will be changed)" if dry else ""))

    content = host = None
    try:
        content, host = connect()
        log("host API answered: %s, maintenance mode %s" % (host.name, host.runtime.inMaintenanceMode))
    except Exception as e:
        log("FAILED to reach the host API: %s" % e)

    if host is not None:
        try:
            timeout = int(cfg.get("timeoutSeconds", 300))
            if cfg.get("vm"):
                match = [v for v in host.vm if v.name == cfg["vm"]]
                if match:
                    shut_vm(match[0], timeout, dry)
                else:
                    log("VM %s is not on this host" % cfg["vm"])
            # Belt and braces: anything else still running here goes down cleanly too.
            for v in host.vm:
                if v.runtime.powerState == "poweredOn" and v.name != cfg.get("vm") and not v.name.startswith("vCLS-"):
                    log("%s is still running on this host" % v.name)
                    shut_vm(v, timeout, dry)
        except Exception as e:
            log("FAILED shutting down VMs: %s" % e)

    storage = cfg.get("storage") or []
    if storage:
        was_on = True
        try:
            was_on = firewall_enabled()
            # ESXi's outbound rules allow 80/443 only and DSM's API is on 5001: open the firewall for
            # the few seconds these calls take, then close it again. It is restored before the host
            # powers off, so a host that comes back up comes back with its firewall on.
            if was_on:
                set_firewall(False)
            for t in storage:
                try:
                    dsm(t, dry)
                except Exception as e:
                    log("FAILED on %s: %s" % (t.get("name"), e))
        finally:
            if was_on:
                set_firewall(True)
                log("firewall restored: enabled=%s" % firewall_enabled())

    if cfg.get("powerOffHost"):
        if host is None:
            log("cannot power off the host: its API did not answer")
        elif dry:
            log("DRY RUN: would power off host %s" % host.name)
        else:
            log("powering off host %s" % host.name)
            host.ShutdownHost_Task(force=True)
    log("KOR-CHAIN-DONE")


if __name__ == "__main__":
    detach()
    try:
        main()
    except Exception as e:
        log("FAILED: %s" % e)
        log("KOR-CHAIN-DONE")
