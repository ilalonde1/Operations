**To:** aaronp@t-net.ca (T-Net)
**From:** Ian Lalonde, KOR Structural
**Subject:** Request — enable Veeam WAN Acceleration for our Cloud Connect backup copy (+ seed options)

---

Hi Aaron,

We've stood up a new off-site **Backup Copy** of our file server (**KOR-FS01, ~5.8 TB**) to your Cloud Connect repository (`Kor-Replication` / `remote.t-net.ca`). It's seeding now, but throughput is sitting at **~8 MB/s (~64 Mbps)** against our **182 Mbps** office upload — consistent with WAN latency to Toronto. We'd like to enable **Veeam WAN Acceleration** to improve the seed and all future copies.

WAN Acceleration needs a Veeam accelerator component on **both** ends. We'll deploy the **source-side** accelerator on our end; the **target-side** accelerator runs on your Cloud Connect infrastructure and can only be configured by you.

**What we're asking you to do / confirm:**
1. Do you offer Veeam **WAN Acceleration** on your Cloud Connect service? Is there an additional cost?
2. If yes, please **configure and assign a target WAN accelerator to our tenant** so it becomes selectable in our backup copy job.
3. **Confirm when it's assigned** — it should then appear automatically in our console via the Cloud Connect connection, and we'll point the copy job at it (source accelerator here + your target accelerator).

**Information we need back from you:**
- Confirmation the **target accelerator is assigned** to our tenant (and how it will appear in our console).
- Any **cost** for the WAN Acceleration option.
- Is there a **bandwidth cap / rate-limit** on our Cloud Connect tenant? Our seed is running at ~8 MB/s — we'd like to know whether that's purely the WAN latency or a throttle on your side.
- Your recommended **cache sizing** for our source-side accelerator, if you have a guideline (we'll place its cache on a volume with room, not the system drive).
- Confirmation of **storage headroom** on our tenant — we're adding FS01 (~5.8 TB); the repo currently shows ~11.5 TB free. (Note: once the new copy is verified we'll remove the stale July-2026 FS01 data already in the repo, which will net us space back.)

We're seeding over the wire in the meantime (off-hours, throttled) and will let it run — no seed-drive needed. WAN Acceleration is what we're after to speed the ongoing copies and, if it helps, the tail of this seed.

Thanks,
Ian

---

### For KOR (internal note — what we do once T-Net enables their side)
1. Deploy a **source WAN Accelerator**: Backup Infrastructure → WAN Accelerators → Add Server → choose an on-prem host (KOR-BK01 or another) → set the **cache folder on a volume with space** (E:/F:, not C:) → size per T-Net's guidance.
2. Edit the `Kor-FS01 Offsite Copy` job → **Data Transfer** page → select **"Through built-in WAN accelerators"** → source = our accelerator, target = T-Net's accelerator (appears once they assign it).
3. Re-run; throughput should improve materially on the high-latency link.
