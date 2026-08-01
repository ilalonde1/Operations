**To:** aaronp@t-net.ca (T-Net)
**From:** Ian Lalonde, KOR Structural
**Subject:** Backup infrastructure review — service restored, guidance requested (report attached)

---

Hi Aaron,

Over the past week we worked through a backup outage on our Veeam environment and I've put together a full write-up (attached) so we're working from the same picture. Service is fully restored and verified — this isn't a fire, it's a request for your guidance on getting the design onto a durable footing.

**The short version:**

- Our vCenter TLS certificate expired around July 7, which silently stopped every backup job until we caught it on July 15. No alert reached us during that window — that's one of the things I'd like to fix with you.
- Recovering the file server (KOR-FS01, 21 TB) ran into a repository capacity/job-design issue. We resolved it by standing up a new forward-incremental job to the Synology, which completed cleanly (5.79 TB, ~14 hours, no errors).
- All three tiers — local, off-site copy to your DC, and the file server — are backing up again, and email alerts are now confirmed working.
- We also ran restore tests (the first documented ones here): local and off-site file-level restores both pass. File-level restore of the *new* deduplicated FS01 backup fails, and we've confirmed why — it needs a Server 2025 mount server, which ties into a Veeam version upgrade.

**Where I need your input** (detailed in the report):

1. **Licensing first, as it gates the rest:** we're on 12.0.0.1420 and need to upgrade. Does our rental licence entitle us to v13 (current 13.0.2.29), or should we target the latest 12.3.x? Is a direct upgrade from 12.0 supported, and can you supply the ISO or would you prefer to run the upgrade?
2. Recommended job design and repository sizing going forward (our E: repository is effectively full).
3. Adding KOR-APP01 as the mount server to restore file-level recovery for the file server.
4. Re-establishing off-site coverage for KOR-FS01 (≈5.8 TB would need seeding to the DC).
5. Monitoring/alerting expectations, and certificate-renewal ownership ahead of the next expiry (2028).

The report has the full timeline, verified numbers, the restore-test results, and a proposed sequence of next steps. Happy to jump on a call to walk through any of it, and I can share the underlying logs and screenshots if useful.

Could you let me know on the licensing/upgrade path first? That's the one blocking everything else.

Thanks,
Ian

---
*Attachment: KOR-Backup-Review-TNet-2026-07-20-web.pdf*
