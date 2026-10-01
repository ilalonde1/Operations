| Finding | Verdict |
|---|---|
| 1. Writable descendants | **Closed** for the original script-replacement attack under the default Program Files installation; separate migration defect below. |
| 2. Cancelled queued work | **Closed**: withdrawal now runs in `AgentHub.RunAsync`'s `finally`. |
| 3. Orphaned PowerShell | **Partly closed**: launch precedes containment (`Agent/AgentLoop.cs:218`); see Material. |
| 4. Concurrent installation | **Closed** within the stated single service process: install/remove share a per-PC lock. |
| 5. Old poll confirms installation | **Closed** for the reported false-positive: confirmation requires the new credential hash. |
| 6. Revoked polls | **Partly closed**: stale authentication can attach to the replacement connection token (`Service/Agents/AgentApi.cs:90`, `Service/Agents/AgentHub.cs:95`). |
| 7. Partial installation/removal | **Partly closed**: deletion errors are checked, but stale polls can restore confirmation (`Service/Store/NetworkOpsStore.Agents.cs:60`); failures before credential save retain old confirmation. |
| 8. Idle helper | **Closed**: no inherited handles or pipe read; process wait is bounded. |
| 9. Unauthenticated work | **Partly closed**: body binding is gated, but SQL authentication shares capacity with held polls (`Service/Agents/AgentApi.cs:57`, `:127`). |
| 10. Concurrent jobs | **Closed** for ordinary dispatch: server and endpoint enforce limits; process-containment exception remains under 3. |
| Smart-quote work directory | **Closed**: restricted path syntax, quote rejection and per-poll paths remove the injection/race. |
| 11. Rollout selection/actions | **Partly closed**: snapshot eligibility is not revalidated; cancellation still bypasses completion (`Service/Agents/AgentRollout.cs:26`, `:45`). |
| 12. Grace/versions | **Partly closed**: malformed shipped versions suppress comparison (`Core/Health/AgentRules.cs:46`). Grace exists in the rule; caller wiring is outside the permitted files. |

Static review at HEAD only; no execution. Project prefixes abbreviate `Kor.Operations.NetworkOps.*`.

## Critical

**New: migration deletion can follow an ancestor junction.** `Transport/RemoteAgentInstall.cs:66`, `:116`, `:180`. Before installation, a user able to pre-create the legacy ProgramData hierarchy creates `C:\ProgramData\KorOperations` as a junction to `C:\Program Files\KorOperations`. Installation copies the new package and key, then deletes legacy `...\KorOperations\Agent`. That final component is an ordinary directory, so its reparse-point check passes: deletion instead removes the newly created Program Files installation using administrator rights. Other targets with an `Agent` child are also exposed. Reinstallation repeats the deletion. Checking only the final component does not establish a trusted path. **Smallest fix:** leave legacy data untouched when any ancestor is untrusted/reparsed; for automatic cleanup, validate the entire path and prevent replacement races using trusted directory handles.

## Material

**Revocation remains vulnerable across authentication/body binding.** `Service/Agents/AgentApi.cs:60`, `:90`, `:107`; `Service/Agents/AgentHub.cs:71`, `:95`, `:104`. An old-key request authenticates, delays its body, then resumes after Save/Remove and Revoke. `Seen` accepts its stale hash and `NextJobAsync` captures the *new*, uncancelled token. It can receive a later job. Even a poll already in `Seen` can cross revocation before entering `NextJobAsync`; cancellation after dequeue is not checked before handoff. **Fix:** bind authentication, poll admission, claim and result completion to an authoritative credential generation, checked under the same synchronization as revocation. Rechecking SQL alone creates another check/use race.

**New containment implementation has a launch window.** `Agent/AgentLoop.cs:217`; `Agent/ProcessTree.cs:36`. Start PowerShell, then deschedule the agent before `Add`: PowerShell can spawn descendants before assignment, and those existing descendants are not retroactively contained. If assignment fails, disposing the empty job and `Process` leaves the running process alive. A fast completed script can instead be reported as an error when assignment fails after exit. **Fix:** create suspended, assign successfully, then resume; terminate on assignment failure. APP01 restart also still loses execution tracking without stopping endpoint jobs.

**Confirmation remains writable by old requests.** `Service/Store/NetworkOpsStore.Agents.cs:47`, `:60`; `Service/Agents/AgentApi.cs:103`. Save clears contact; a previously authenticated poll then touches the row by DeviceId alone. New-agent startup fails, but rollout sees current version plus fresh contact and skips it. Separately, a same-version repair failing during copy never clears its previous confirmation. **Fix:** persist pending installation before remote mutation; qualify contact/confirmation updates by credential generation and installation attempt.

**Before rollout, isolate authentication capacity.** `Service/Agents/AgentApi.cs:57`, `:65`, `:127`. No semaphore leak was found: all acquired slots release. However, plausible bogus credentials still cause unrestricted-rate SQL lookups. Sufficient concurrent slow lookups fill the shared 256 slots, rejecting healthy polls/results. **Fix:** a smaller authentication-only concurrency budget, per-source rate limits and reserved authenticated capacity. A credential cache is optional; invalidate it synchronously on rotation/removal.

## Minor

- `Service/Agents/AgentRollout.cs:27`: taking the batch before skipping offline PCs lets the same offline first N starve reachable later candidates. Count attempted online PCs instead. Recheck removal/retirement under the installer lock and finalize cancelled actions.
- `Core/Health/AgentRules.cs:46`: an invalid shipped version returns false for every comparison. Validate the package version and surface “unknown”; do not silently declare it current.
- `Agent/ProcessTree.cs:29`: constructor failure leaks the native job handle. Close on failure or use a SafeHandle.
- `Agent/DataFolder.cs:41`: startup deletes administrator-added backups/diagnostics too. Restrict deletion to owned scratch entries or reject unexpected content. Per-PC semaphore retention is negligible for 38 names; holding slots during PickupWait preserves the cap, although three long repairs can delay probes.

**Exactly one ship-blocker: migration cleanup can delete through an attacker-prepared ancestor junction.**

