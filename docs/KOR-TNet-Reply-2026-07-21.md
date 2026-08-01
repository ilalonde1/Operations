**To:** aaronp@t-net.ca (T-Net)
**From:** Ian Lalonde, KOR Structural
**Subject:** RE: Backup infrastructure review — responses and a couple of things to align on

---

Hi Aaron,

Thanks for the quick turnaround, and for reviewing the monitoring process internally — appreciated. Responses to each point below, in the same order.

**1. Upgrade to 12.3**
Agreed — 12.3 it is, to stay aligned with the replication server. Happy for you to run it, or if you'd rather, send me the ISO and I'll do it in a scheduled window with you on standby. Either way, before we start I'll take a Veeam configuration backup and a VM snapshot of BK01 as rollback. Two quick confirmations: is a direct 12.0 → 12.3 upgrade supported (or is an interim build needed), and do you have a preferred evening this week? Jobs need to be idle, so I'd suggest kicking off after ~7:30 PM once the nightly runs finish.

**2. Job design and repository sizing**
One clarification first so we're looking at the same thing: I didn't move FS01 into the ManualNew storage. During the recovery I built a **new dedicated job (`Kor-FS01`), forward incremental, targeting the Synology (Synology105)** — separate from ManualNew. That's what's protecting the file server today.

On reverse vs forward incremental — I want to flag this respectfully, because our recovery gave us a direct side-by-side on the same VM:

- The old **reverse-incremental** chain for FS01 had grown to occupy **~23 TB of the 25 TB E: repository**, and its last run couldn't complete — the rollback file ballooned past the remaining free space. It was also running at ~29 MB/s.
- The new **forward-incremental** full of the same VM landed at **5.79 TB and completed in ~14 hours at ~126 MB/s, zero errors.**

So for this particular VM, forward incremental was both dramatically faster and far smaller on disk. I take your point that reverse incremental keeps a single merged full — but the issue we hit is that reverse incremental needs the repository to hold that full *plus* rollback/merge growth, and E: at 25 TB simply isn't sized for a 21 TB source with headroom. Deleting old recovery points to make it fit works at the margin, but it trades away restore history rather than solving the sizing. Could we align on one of two paths: (a) keep FS01 on forward incremental where it's working well, or (b) if you'd prefer reverse incremental for the merge behaviour, spec a repository sized to hold a full + working space for a VM this size? Genuinely open — I just want to avoid re-creating the condition that filled E:.

**3. Mount server / file-level restore**
Understood — APP01 isn't a managed server because it's in the cluster, and mounts run from the backup server. That's helpful. The specific problem we hit: file-level restore from FS01's **new (post-deduplication) backup** fails on 12.0 with *"restoring files from deduplicated volumes requires a mount server … with the data deduplication feature enabled"* — and installing that feature on BK01 (Server 2019) + rebooting didn't clear it, because FS01 is Server 2025. **Does the 12.3 upgrade resolve file-level restore of a Server 2025 deduplicated volume from the backup server?** We'll re-test the moment the upgrade is done; I just want to confirm that's the expected outcome, and if not, agree how we get deduped file-level restore working.

**4. Off-site seeding for FS01** — this is the one I most want to close
I may be misunderstanding, so let me lay out what I know: our off-site copy has historically been seeded by **physically transporting hardware to your facility** — you've picked up equipment from us and brought it to where the replication resides. Veeam Cloud Connect does support seeding by importing a shipped copy at the provider end, which lines up with that. So a physical seed does appear possible — it's how we got current before.

FS01's new chain is ~5.8 TB and won't realistically traverse the WAN. Can we repeat that same physical seed process for it? If a physical seed genuinely isn't available this time, then I need to flag that **FS01 currently has no current off-site protection** — its cloud restore points are frozen at July 7 — and I'd like to agree a concrete plan and timeline to close that, because it's our only remaining gap.

**5. Monitoring, alerting and certificate ownership**
Understood, and thanks for tightening the internal check. One addition I'd like in place as a backstop: please also configure Veeam to **email job-failure/warning alerts directly to me (ilalonde@korstructural.com)** — I've enabled this on our side too, so detection never depends on a single manual check again. And can we formally note **ownership of the vCenter certificate renewal** with a reminder ahead of the next expiry (2028-07-14)?

Happy to jump on a call to work through 2 and 4 — those are the two with real design decisions in them. And I can share the underlying session logs, restore-point listings and the restore-test output any time it's useful.

Thanks,
Ian
