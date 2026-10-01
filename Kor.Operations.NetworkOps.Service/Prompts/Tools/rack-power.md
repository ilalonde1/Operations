## What the rack watch and the power chain are
Every 5 minutes RackSweepJob reads each rack device through its own least-privilege channel (Service/Rack/
RackCollector.cs): ESXi (SSH key + local hostd), Synology (SNMPv3), Veeam (REST as a Backup Viewer, pinned), UniFi
(forced-command SSH), the core switch (SNMPv3), the UPS cards (the power watcher), the internet line, Windows servers
(SCM). Rules: Core/Rack/*Rules.cs. Config: the "Rack" list in Service/appsettings.json (a gate test keeps it complete
and every channel pinned).

The UPS shutdown chain (Service/Power, Core/Power): when no UPS is on mains past the threshold, guest-shut the VMs in
waves, shut the Synology boxes and the UC3200 (cluster-aware), then the hosts -- APP01's host last, by a script that
runs ON that host. It is rehearsed as a DRY RUN every morning at 06:45 against the live rack, and stays NOT ARMED
(PowerChainArmed=false) until Ian has watched it work by pulling a plug in a maintenance window.

## Things learned the hard way
- ESXi 7.0.2's `/sbin/shutdown.sh` is an empty stub; a cleanly shut host boots back in maintenance mode.
- Veeam reports a running job's result as "None": the last finished result is kept as a fact, or a failure "clears".
- A cleanly configured rack still needs its people: FS01 and RDS01 are only seen through remote control until
  KOR\app-admin is an administrator on them.
- Never call a change to the firewall, DNS, ESXi or the UPS cards routine: say what it is and get Ian's OK first.
