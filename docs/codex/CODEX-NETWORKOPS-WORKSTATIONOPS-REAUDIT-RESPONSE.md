# NetworkOps / WorkstationOps re-audit — 2026-10-03

Static review of the permitted files at HEAD; no builds, tests, network access or SQL. Findings below are source-derived, not experimentally reproduced. Paths abbreviate `Kor.Operations.NetworkOps.{Core,Service,Transport,Agent,Tests}/`. Prior audit reports are claims checked against the current source.

## Part 1 — fix verification

“Residual” means the claimed fix remains incomplete; it does not imply the change introduced the defect.

| Fix | Verdict and evidence |
|---|---|
| SMBIOS length guards | **Correct.** `Core/Smbios/SmbiosParser.cs:114,123`: every formatted-field read is length-guarded, including through `RawStructure.String` at 170; the outer header/walk reads are bounded too. No remaining unguarded field read found. |
| Map dictionary deduplication | **Correct for parsed input.** `Core/Network/NetworkMap.cs:38,53`; the parsers normalize MAC case before grouping. This fixes dictionary exceptions, not conflicting duplicate records throughout the rest of the builder. |
| Expired leases | **Correct only with a trustworthy controller clock.** `Core/Network/NetworkMap.cs:44`: APP01 is at 12:00, lease expires 12:30, controller reports 13:00: a current lease is dropped. Conversely, a retained old site timestamp preserves expired names. Pass APP01's build time explicitly. Cached leases renewed since collection can also temporarily lose names until refreshed. |
| VMware-only occupant | **Regression.** `Core/Network/NetworkMap.cs:215` on a core port containing only `00:50:56:00:00:01`, copies that VM into the synthetic occupant, then includes the same MAC in `behind`; `Everything()` emits it twice. Use a separate port label, not an endpoint identity. A non-VMware host retained in `here` prevents this branch; a host filtered out by stale placement does not (finding 4). |
| Box `ConnectedNow` fold | **Correct for the stated missing-port case.** `Core/Network/NetworkMap.cs:172,176`: live clients establish connectivity when no live port exists. An explicit `Up=false` remains authoritative; the fold does not resolve that disagreement. |
| Published snapshot | **Residual.** `Service/Network/NetworkMapService.cs:37–40`: all three public getters use `_published`, but each loads it separately. Read `Current=A`, publish B at 100, read `BuiltUtc/Notes=B`: still torn. Return one captured snapshot/response. No public map getter bypasses it in this file; external handlers are outside the allowed files. |
| Live state, freshness, lease isolation | **Correct within stated bounds.** `Service/Network/NetworkMapService.cs:83` captures the live triple once; 87 rejects it after 15 minutes since receipt. Malformed DHCP JSON preserves last-good leases at 127; field accesses in `ParseLeases` check kinds. This does not make live-API parsing equally isolated (finding 6). |
| Enrolment peek/claim and misses | **Claim correct; limiter regression.** `Service/Agents/AgentEnrolment.cs:61–63`: two valid redeems can both peek, but only one removes/returns the code. At 75, every new attacker-chosen device creates a queue that is never removed, even after expiry. Repeated anonymous requests with distinct names retain unbounded memory; the eight-slot gate limits concurrency, not cumulative names. Bound/expire this map. |
| Package cache | **Incomplete invalidation.** `Service/Agents/AgentEnrolment.cs:101–105`: change an older file while preserving file count and the directory's maximum timestamp; the old zip is returned. A service restart clears the static cache, so ordinary stop/copy/start deployment avoids this. Use a package version/content signature for updates without restart. |
| Before-key gate | **Correct locally.** `Service/Agents/AgentApi.cs:99–110` covers both exact routes, caps bodies and active requests, and releases every acquired permit in `finally`; timeout/cancellation before acquisition leaks none. Registration cannot be verified from the permitted files. |
| AD sanity floor | **Correct protective refusal, with an operational limitation.** `Service/Store/NetworkOpsStore.cs:60`: a legitimate reduction from 38 to 18 PCs is refused every run too. Provide an explicit, reviewed reconciliation path for large removals. |
| AD source qualification | **Regression: silent omission.** `Service/Store/NetworkOpsStore.cs:70–76`: AD contains X, but X already belongs to a Rack/Manual row; neither branch writes anything, the result is ignored and sync commits successfully. The AD machine is never represented as AD. Preserve the existing row but report the conflict. |
| Job-name gate | **Correct for nonrecursive dispatch.** `Service/Jobs/Scheduling.cs:56–62`: distinct names use distinct semaphores; exceptions, including failed terminal writes, release the gate. No lock cycle is evident here. Recursive dispatch awaiting the same name would deadlock; no such caller is established by this scope. |
| Mesh issuer requirement | **Correct agreement for canonical 64-hex pins.** `Transport/MeshTrust.cs:27–35`, `Service/Mesh/install-mesh.ps1:18–20`: exact pin OR valid TLS plus case-insensitive issuer substring. TLS-supplied DER is parseable; cryptographic parse failures are caught. C# additionally normalizes spaces/colons in pins; PowerShell does not. Neither rule is an issuer-key pin. |
| Cleanup hook/delete/drain | **Delete check correct; drain residual.** `Transport/OnTargetChannel.cs:130–138` checks deletion and closes the handle. At 101, drain can return while a launch is awaiting service creation at 117, before registration at 137. Continuous arrivals can also keep drain running indefinitely; it has no cancellation parameter. Stop admission and track launches before awaiting creation, then drain with a deadline. |
| SSH failure disposal | **Correct.** `Transport/EsxiShell.cs:31–44`: both disposal branches throw; only successful connection reaches the returned wrapper. No new use-after-dispose path. |

## Part 2 — ranked findings

**1. High — enrolment bootstrap executes from user-writable staging.**  
`Service/Agents/AgentEnrolment.cs:127–130`; `Agent/Enrol.cs:58,62–63,127`.

An unelevated process running as the same split-token administrator can write that user's predictable `%TEMP%\\kor-agent` directory. Plant an extra `sc.exe` there before the administrator runs the supplied enrolment command. `Expand-Archive -Force` overwrites archive members but leaves extra files. After valid redemption, `Sc` launches bare `sc.exe`; executable search includes the launching agent's application directory, so the planted executable runs elevated. The copy loop also copies arbitrary extra files into Program Files. Replacing the downloaded executable/configuration during staging is another route. This needs local staging access and an administrator performing enrolment, not an agent key or compromise of APP01.

**Smallest fix:** bootstrap into a newly created administrator-only directory under a trusted parent, reject existing/reparsed staging, and copy only expected package members. Resolve `sc.exe` and PowerShell by absolute system paths. TLS pinning does not protect downloaded bytes after they enter a writable directory.

**2. Material — credential revocation still has admission/completion gaps.**  
`Service/Agents/AgentHub.cs:120–138,156–158`; `Service/Agents/AgentApi.cs:83,203–204`.

For handoff: the old poll passes the locked check at 120, pauses, `Revoke` finishes, then the old poll claims and returns the job outside the lock. For results: authenticate an old-key result request, delay its body, rotate/revoke the key, then finish binding. `Complete` accepts the old hash because it checks only `HandedToKey`, not current authorization. Thus the prior revocation finding remains partly open; this is not a new cross-PC impersonation claim.

**Smallest fix:** make generation validation, claim and handoff bookkeeping one locked operation; validate current generation under the same connection lock in `Complete`. Work already handed over before revocation cannot be recalled by this alone.

**3. Material — live-only wireless clients disappear.**  
`Core/Network/NetworkMap.cs:54,236–245`.

Controller snapshot: one AP A, no clients. Live API: client M with `Wired=false, ApMac=A`. M never enters `liveAt`, and the wireless loop enumerates only historical `clients.Values`. The AP reports zero clients and `Everything()` omits M despite live evidence. Existing map tests use matching historical/live fixtures and do not cover this input.

**Smallest fix:** enumerate the union of historical and live MACs; prefer live attachment/type information and retain historical metadata only as fallback.

**4. Material — stale UniFi placement defeats fresh core evidence.**  
`Core/Network/NetworkMap.cs:155,166,207–216`.

Let switch S hang from core C. S's old port 5 has `LastMac=38:68:dd:55:de:b8` (host H); live API has no H. Fresh core port 3 learns H plus VM `00:50:56:8b:ed:b1`. The historical branch places H on S:5, then the core filter removes H from its actual port. The VMware-only branch now says “host not seen” even though core SNMP explicitly saw it. Without the VM, core port 3 becomes empty. The existing mixed-host test has no conflicting old port claim.

**Smallest fix:** reconcile fresh core attachment against historical claims before constructing endpoints, while distinguishing downstream MACs on known switch uplinks. Never infer “host not seen” from a list already filtered by historical placement.

**5. Material — stale IP can assign another machine's rack name.**  
`Core/Network/NetworkMap.cs:64,69`.

Historical port names MAC M with `LastIp=192.168.1.10`; current DHCP lease for M says `.50`; `KnownNames.ByIp[.10]="KOR-SERVER"`. With no fleet/device match, the old port IP wins and the rack-name branch overrides M's current DHCP hostname. The map labels M as KOR-SERVER. The expiry test does not vary the IP sources.

**Smallest fix:** prefer current live/lease addressing, and require a current MAC/IP association before using an IP-only rack identity.

**6. Material — valid JSON can still abort map refresh.**  
`Core/Network/NetworkMap.cs:421,431`; `Service/Network/NetworkMapService.cs:91–92`.

A live payload containing a device with `"state":1.5` reaches `GetInt64()`, which throws `FormatException`, outside the `JsonException` catch. Repeated payloads prevent publication. Similarly, an out-of-range Unix timestamp reaches unchecked `FromUnixTimeSeconds`. The map tests cover neither numeric case.

**Smallest fix:** validate numeric ranges/types with `TryGet*`, normalize schema failures to a caught parse error, and degrade the live source with a note.

**7. Low — text output silently omits unassociated wireless devices.**  
`Core/Network/NetworkMapText.cs:54–66`.

A historical wireless client whose AP MAC is absent from the device list enters `OtherWireless`. The summary counts it, but normal rendering never visits that collection; only search reveals it.

**Smallest fix:** render an `OtherWireless` group; add this missing-AP input to the text coverage.

No direct anonymous script submission, cross-PC key substitution, or arbitrary job replay was established in the reviewed steady-state agent path.

**Exactly one thing to fix before trusting this on the fleet: the writable enrolment bootstrap in finding 1.**

