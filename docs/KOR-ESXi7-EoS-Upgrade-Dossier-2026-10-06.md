# KOR ESXi 7.0 End-of-Support — Upgrade Dossier

**Date:** 2026-10-06
**Author:** Ian Lalonde + Claude (NetworkOps session)
**Scope:** The two rack hypervisors, `.10` (production) and `.16` (standby), both on VMware ESXi 7.0.2.
**Trigger:** NetworkOps `version.end-of-support` findings 549 (.10) and 550 (.16).

---

## 1. Recommendation (the short version)

ESXi 7.0 reached **end of general support on 2 October 2025** — both hosts now take **no security patches** from the vendor. This is a security-clock problem, not a "it still runs fine" problem, so it has to be closed, but there is no emergency: the hosts are healthy and isolated behind the firewall on the management network.

The decision is **licensing, not technical**. The SR650s *can* run ESXi 8.0. The question is whether to keep paying Broadcom's new subscription for a two-host shop, or move to a hypervisor with no per-core licence.

**Recommendation:** pilot **Proxmox VE on `.16` (the standby) first**, keep `.10` on ESXi 7 until the pilot proves out, then migrate `.10`. Rationale below — in one line: for two hosts, Broadcom's 16-core-per-socket floor makes VMware the most expensive option by a wide margin, and we already carry the two faults that most scare people off a migration (SAN pathing and backup) as **continuously-monitored** NetworkOps checks, so a migration is observable rather than a leap.

If there is an appetite to stay on VMware (familiarity, vCenter features, a reseller relationship), **Option A (ESXi 8 + VVF)** is viable and lower-effort — it is a cost call, not a risk call.

The **iSCSI "pin"** (§7) and the **exact-hardware confirmations** (§8) are independent of the hypervisor choice and can be done now.

---

## 2. Current state (facts, from NetworkOps telemetry)

| | `.10` production | `.16` standby |
|---|---|---|
| NetworkOps device | `ESXi host .10 (production)` | `ESXi host .16 (standby)` |
| ESXi build | VMware ESXi **7.0.2 build-17867351** | same |
| Hardware | Lenovo ThinkSystem **SR650** (7X06) | Lenovo ThinkSystem **SR650** (7X06), S/N J10001WG |
| ESXi hostname | — | `VMHost02.engineering.local` |
| VMs | — | 3 of 5 running; CPU 3%, RAM 44% |
| SAN access | iSCSI to the SAN on the **192.168.200.x** storage network (MTU 9000) | same; active path now back on 192.168.200.x (10G) after the 6 Oct recable |

- **ESXi 7.0.2 build-17867351** is the original 7.0 Update 2 GA from 2021 — well behind even the final 7.0 U3 patch level, let alone supported.
- Backups: Veeam on BK01 writes these hosts' VMs; Veeam keeps a vPower NFS mount (`VeeamBackup_*`) on each host, which is normal and re-adds itself (see the NetworkOps note on .16, run 10).

## 3. What "end of support" actually costs us

- **No security patches.** Any ESXi CVE published after 2025-10-02 is unpatched on these hosts forever. ESXi has had serious remote CVEs (the 2021 iSCSI/OpenSLP worming, the 2023 ESXiArgs ransomware). Mitigation today is network isolation (management network, firewalled) — real, but it is compensating control, not a fix.
- **No Broadcom support.** A production incident on 7.0 gets no vendor help.
- **It drifts further every month.** 7.0.2 is already two years of patches behind; the gap only grows.

NetworkOps will keep both `version.end-of-support` findings open until the build changes, so this stays visible rather than forgotten.

## 4. Option A — ESXi 8.0 (stay on VMware)

**Technical:** viable. The SR650 (7X05/7X06) with Skylake-SP Xeons is on **Lenovo's published ESXi 8.0 recipe**. Use Lenovo's **custom ESXi 8.0 image** and bring firmware to the recipe's baseline. Confirm the exact Xeon SKU against the 8.0 HCL first (§8) — Skylake-SP is supported on 8.0; this is a confirmation, not a known blocker.

**Licensing (the catch):** Broadcom sells vSphere **subscription-only** now — no more perpetual licences, and the old free ESXi is not a basis for production. Metric is **per core, minimum 16 cores per socket**. For our pair that is roughly:

> 2 hosts × 2 sockets × max(actual cores, 16) = **≈ 64 core-subscriptions** at the floor (more if the CPUs are >16 cores each). Confirm sockets/cores in §8.

The right SKU for us is **VMware vSphere Foundation (VVF)** — standalone vSphere + vCenter, no need for the full VCF/vSAN stack. Budget it as an **annual recurring** line, not a one-time cost. Get a written quote from the reseller for 64 cores of VVF/yr; that number is the whole decision.

**Pros:** lowest migration effort (in-place 7→8 upgrade path exists; vCenter, Veeam, our runbooks all unchanged). **Cons:** a new recurring cost that did not exist under the perpetual licence; locks us further into Broadcom's pricing trajectory.

## 5. Option B — Proxmox VE (recommended to pilot)

**What:** Debian + KVM/QEMU, open-source, enterprise support optional (~€/host/yr for the stable repo, far below VMware). Mature, widely used at exactly our scale.

**Pros:** no per-core licence; built-in ZFS, snapshots, replication and clustering; a 2-node cluster (+ a QDevice for quorum, which BK01 or APP01 can host) is a normal Proxmox topology; good iSCSI and NFS support; live import tooling for VMware VMs has improved markedly.
**Cons:** new platform to learn and document; Veeam support for Proxmox exists but is newer than its VMware support — **validate a Proxmox backup+restore with Veeam before committing** (this is the one hard gate); no vCenter-equivalent single-pane if we are used to that.

**Why pilot on `.16`:** it is the standby, carries non-critical VMs, and if the pilot stalls we lose nothing. We convert `.16` to Proxmox, move a test VM, prove Veeam backup+restore and SAN access, run it for a week, then decide on `.10`.

## 6. Option C — Hyper-V

**What:** we already run **Windows Server 2025**; Hyper-V is a role, and Datacenter edition licensing (if we hold it) covers unlimited Windows guests.

**Pros:** no new hypervisor licence if Datacenter is already owned; familiar Windows tooling; **Veeam's Hyper-V support is first-class and long-standing** (unlike Proxmox); Failover Clustering is well understood.
**Cons:** check our Server 2025 licence edition/entitlement first; Linux guests are supported but Hyper-V is less common for mixed Linux fleets; re-platforming VMs from VMware to Hyper-V is a conversion, same order of effort as Proxmox.

**Hyper-V vs Proxmox:** if the VMs are mostly Windows and we already own Datacenter, **Hyper-V may beat Proxmox on both cost and Veeam-maturity** — worth pricing both in the pilot.

## 7. The iSCSI "pin" — do this now, independent of the upgrade

**Goal:** bind the iSCSI initiator so its traffic can only ever egress the 10G storage NIC, never the 1G management uplink. The 6 Oct recable put the active path back on `192.168.200.x`, but nothing *stops* it drifting again; port binding is what stops it.

**Discover first (read-only; run on the host over SSH):**
```
esxcli iscsi adapter list                         # find the software iSCSI HBA, e.g. vmhba64/vmhba65
esxcli iscsi networkportal list -A <vmhba>        # which vmk ports are bound to it today
esxcli network ip interface list                  # vmk -> portgroup -> which vSwitch/uplink
esxcli network vswitch standard list              # confirm the storage vSwitch + its 10G vmnic
vmkping -I <storage-vmk> -d -s 8972 <SAN-IP>      # prove jumbo (MTU 9000) end-to-end on the storage vmk
```

**Pin (maintenance window — binding changes can reset active iSCSI sessions; `.16` standby is the safe one to do first):**
1. On the storage portgroup, override NIC teaming so the storage vmk has **exactly one active uplink** = the 10G storage vmnic (no standby on the 1G side).
2. Bind **only** the storage vmk to the iSCSI HBA; remove any management-net vmk that is bound:
   ```
   esxcli iscsi networkportal remove -A <vmhba> -n <wrong-vmk>
   esxcli iscsi networkportal add    -A <vmhba> -n <storage-vmk>
   ```
3. Rescan and confirm the active path's local/remote are both `192.168.200.x`:
   ```
   esxcli storage adapter rescan --all
   esxcli storage core path list      # verify runtime/active path is on the storage subnet
   ```

> I can generate the **exact** commands (real `vmhba`/`vmk`/`vmnic` names) from a read-only SSH read of each host on request — I did not run SSH changes against the live hosts in this pass, by policy (no autonomous iSCSI surgery on production).

**Order:** pin `.16` in a window, watch for a day, then pin `.10`.

## 8. Confirmations needed before committing (one short pass)

1. **Exact Xeon SKU + sockets + cores per host** (`esxcli hardware cpu list`, or the host summary). Drives both the ESXi-8 HCL check and the VVF core-count/cost.
2. **Current Broadcom entitlement** — do we hold any active vSphere subscription today, or did we fall off a perpetual licence at EoS? Determines whether Option A is "renew" or "buy new".
3. **SAN model + its HCL** against whichever target (ESXi 8 / Proxmox / Hyper-V). The SAN's iSCSI is standard, but confirm firmware + any vendor plugin.
4. **Server 2025 edition** (Standard vs Datacenter) — gates Option C.
5. **Veeam support matrix** for the chosen target — the hard gate for Options B and C; a proven backup+restore on the pilot host is the go/no-go.

## 9. What NetworkOps already watches here (so the migration is observable)

- **`esxi.iscsi-wrong-path`** — fires if any *active* SAN path's local/remote IP leaves `192.168.200.x` (the drift the pin prevents). Continuous; no action needed to keep it on.
- **`esxi.iscsi-flapping`** — fires at ≥8 `iscsivmk_StopConnection` events in the current `vmkernel.log` (the standby path that dropped during the 6 Oct recable; cleared 01:15 on 7 Oct once the recable settled).
- **`version.end-of-support` / version baseline** — tracks the ESXi build against its support/EoS date; the two open findings close automatically when the build moves to a supported one. If we move to Proxmox/Hyper-V, add those products to the version baseline so the same clock watches them.

## 10. Suggested sequence

1. Run the §8 confirmations (hardware + licensing + Veeam matrix). ~1 short session.
2. Do the §7 iSCSI pin on `.16`, then `.10`, in windows. Independent of the hypervisor choice; closes the drift risk now.
3. Price **VVF (64 cores)** vs **Proxmox support** vs **Hyper-V (existing Datacenter?)**. The VVF quote is the pivot.
4. Pilot the chosen non-VMware target (if cost says move) on `.16`: convert, migrate a test VM, **prove Veeam backup+restore and SAN access**, run a week.
5. Migrate `.10`. Keep both `version.end-of-support` findings as the done-gate — they close when the builds are supported.

---

### Sources (licensing/support facts, retrieved 2026-10-06)
- [End of General Support for vSphere 7.0 — Broadcom KB 415405](https://knowledge.broadcom.com/external/article/415405/end-of-general-support-for-vsphere.html) (EoGS 2 Oct 2025)
- [VMware licensing after Broadcom — VVF/VCF per-core, 16-core/socket floor](https://wiki.licenseware.io/wiki/vmware-vsphere-and-vcf/)
- [Broadcom 16-cores-per-processor floor](https://wiki.licenseware.io/catalog/rules/broadcom-16-cores-per-processor-floor/)
- [Lenovo ThinkSystem SR650 Skylake — ESXi 8.0 recipe](https://vmware.lenovo.com/content/recipe/2023_04/SR650-Skylake-ESXi8.0.html)
