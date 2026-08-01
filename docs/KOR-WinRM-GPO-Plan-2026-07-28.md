# KOR — WinRM enablement via GPO (scoped)

**Date:** 2026-07-28
**Status:** PLAN — for approval, not applied
**Author:** Ian Lalonde

---

## Why

Workstation administration currently works around three blocked channels:

| Channel | State | Consequence |
|---|---|---|
| WinRM 5985/5986 | blocked | no `Invoke-Command`, no remote PowerShell |
| RPC dynamic ports | blocked | no `Get-WinEvent -ComputerName`, no `Get-CimInstance -ComputerName`, no `schtasks /s` |
| RemoteRegistry | Disabled by default | `reg.exe \\host` fails until the service is started |

What works today is `c$`/`ADMIN$` over 445 plus `sc.exe` over the svcctl named pipe. That combination is enough — the whole KOR-206-N diagnosis ran on it — but it costs a 20 MB `.evtx` copy per machine per query, and every registry read requires starting and then re-disabling a service.

With WinRM, `Get-KorWorkstationHealth` across 27 machines becomes seconds instead of minutes, and event queries become server-side filtered rather than copy-then-parse.

## Risk position

KOR has **two confirmed mailbox compromises** (Mousa Oct-2025, Rory Jan-2026, both session-token theft with MFA bypassed) and an ongoing password-spray. Opening a management port on a flat /24 with no VLAN segmentation is not a free action. Hence: scoped, not blanket.

The `Use-KorRemoteRegistry` pattern in `tools/WorkstationOps` — start, use, always restore to Disabled in a `finally` — stays the model. WinRM should be equally constrained.

---

## Plan

### 1. GPO: `KOR-WinRM-Admin-Scoped`

Link to the workstations OU only. **Not** to Domain Controllers, and **not** to servers, which should be handled separately and deliberately.

| Setting | Value |
|---|---|
| `Computer Config → Policies → Admin Templates → Windows Components → WinRM Service → Allow remote server management through WinRM` | **Enabled**, IPv4 filter = admin host address only |
| `Windows Settings → Security Settings → System Services → Windows Remote Management (WS-Management)` | **Automatic** |
| `Computer Config → Preferences → Control Panel Settings → Services` | ensure `WinRM` started |

HTTP (5985) with Kerberos is acceptable **only** because the IPv4 filter and firewall rule below restrict the source. Domain Kerberos already encrypts the payload. If the scope is ever widened, move to 5986/HTTPS with a proper certificate first.

### 2. Firewall rule — the actual control

```
Rule:       Allow WinRM-HTTP-In from admin host
Direction:  Inbound
Port:       TCP 5985
Profile:    Domain only
Scope:      Remote address = 10.0.254.2/32   (KOR-1001 over OpenVPN — see §3)
Action:     Allow
```

Every other source stays blocked. This rule, not the GPO, is what keeps the surface small.

Because the admin host is remote, the IPv4 filter on the GPO setting in §1 must carry the same `10.0.254.2` value. A filter left at `192.168.1.*` would reject Ian's own traffic and the whole thing would appear broken.

### 3. Admin host reaches the LAN over VPN — resolved 2026-07-28

The network documentation is correct; KOR-1001 was simply remote. Confirmed topology:

| | |
|---|---|
| VPN client | OpenVPN Connect, TAP-Windows Adapter V9 |
| Tunnel address | **10.0.254.2/24**, `PrefixOrigin: Manual` |
| Tunnel gateway | 10.0.254.1 |
| Route | `192.168.1.0/24 → 10.0.254.1` via the TAP adapter |
| Default route | stays on Wi-Fi (`10.0.0.1`) — **split tunnel**, only KOR LAN traverses the VPN |

So workstations see admin traffic sourced from **10.0.254.2**, not a `192.168.1.x` address. The §2 firewall scope must be the VPN-side address.

> **Scope to `10.0.254.2/32` — not to `10.0.254.0/24`.**
> Scoping to the whole tunnel subnet would let **any VPN user** reach WinRM on **every workstation**. Against a background of two session-token compromises, that converts one stolen VPN credential into remote PowerShell across the fleet. The /32 keeps it to one host.

**Confirm before applying:** `PrefixOrigin: Manual` indicates the address is assigned rather than pool-allocated, but verify in pfSense under **OpenVPN → Client Specific Overrides** that `10.0.254.2` is pinned to Ian's certificate. If it is not pinned, whichever client connects first takes `.2` and the rule silently protects the wrong host. Pin it first, then scope.

Secondary benefit worth noting: tonight's diagnostics pulled a 20 MB `.evtx` per machine across the VPN. A 27-machine sweep is ~540 MB over an OpenVPN tunnel on a 721 Mbps Wi-Fi link. WinRM filters server-side and returns only matching events, which matters considerably more when administering remotely than it would on the LAN.

### 4. Rollout

1. Resolve §3, assign the reserved static.
2. Create the GPO, link to a **single pilot machine** via security filtering — suggest `KOR-320` (clean, no ACE conflict, low crash count, so any new noise is attributable).
3. Verify: `Test-KorWorkstationChannel -ComputerName KOR-320` should report `WinRM = True`.
4. Verify the negative case from a non-admin host — it must fail. **This is the test that matters**; an unverified scope is an assumed scope.
5. Widen security filtering to the workstations OU.
6. Re-run `Test-KorWorkstationChannel` fleet-wide to confirm uniform state.

### 5. Rollback

Unlink the GPO and run `gpupdate /force`, or `sc.exe \\host config winrm start= disabled` per machine. `tools/WorkstationOps` continues to function without WinRM either way — nothing in it depends on this GPO landing.

---

## Not in scope

- Servers and Domain Controllers — separate decision, higher blast radius
- WinRM over HTTPS/5986 — revisit if the scope is ever widened beyond one admin host
- JEA (Just Enough Administration) endpoints — the correct long-term posture, worth considering once WinRM is proven
