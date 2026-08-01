# KOR — Backup Restore Test Runbook

**Purpose:** prove the backups are actually restorable. Nobody has ever tested a restore here (finding A11).
**Where:** Veeam console on KOR-BK01. **Time:** ~30 min for all three tests.
**Risk:** none — every restore goes to a NEW location. We never overwrite an original.

---

## GOLDEN RULE
In the Backup Browser always use **"Copy To…"** (restores to a location you choose).
**NEVER use "Restore"** — that overwrites the original file on the live server.

---

## TEST 1 — FS01 from the LOCAL pre-dedup chain (E:)  ← most important
Proves the ~23 TB E: chain is restorable, which unblocks the decision about whether it can ever be retired.

1. Veeam console → bottom-left **Home** → left tree **Backups → Disk**.
2. Expand **Kor-VMs-New** → click **Kor-FS01**.
3. In the right pane you'll see restore points. **Right-click one dated 2026-07-07** (or any date Jul 1–7 — these are pre-dedup).
4. Choose **Restore guest files → Microsoft Windows…**
5. Wizard: confirm the restore point → **Next** → type a reason ("restore test") → **Next** → **Finish**.
6. The **Backup Browser** window opens showing FS01's file tree (E:\ drive contents).
7. Navigate to any small, recognisable file — e.g. `E:\Management\...` or `E:\Partners\...` — pick something a few hundred KB.
8. **Right-click the file → Copy To…**
9. Destination: choose **KOR-BK01** local path `C:\RestoreTest` (create the folder if prompted). **Do NOT pick the original location.**
10. Wait for it to complete → close the Backup Browser.
11. **Verify:** open `C:\RestoreTest` on BK01, open the file. Does it open correctly with the right content?

**RESULT TO RECORD:** ✅ / ❌ and any error text.

---

## TEST 2 — FS01 from the OFF-SITE Cloud Connect copy
Proves the T-Net DC copy (your last-resort backstop) actually works.

1. **Home → Backups → Cloud** → expand **Kor-Replication** → click **Kor-FS01**.
2. Right-click a restore point (newest will be **2026-07-07**).
3. **Restore guest files → Microsoft Windows…** → same wizard → Finish.
4. Backup Browser opens (may take longer — data comes over the WAN).
5. Pick a small file → **Copy To…** → `C:\RestoreTest` → wait → verify it opens.

**Note:** expect this to be slower than Test 1. If it's very slow, a small file still proves the path works.

**RESULT TO RECORD:** ✅ / ❌ and any error text.

---

## TEST 3 — FS01 from the NEW Synology backup (post-dedup)  ← expected to FAIL
This is the deduplicated backup. We expect failure because the mount server (KOR-BK01) is Server 2019 and cannot mount a Server 2025 deduplicated volume. **Running it documents the gap for T-Net.**

1. **Home → Backups → Disk** → expand **Kor-FS01** (the new job) → click **Kor-FS01**.
2. Right-click the restore point dated **2026-07-20**.
3. **Restore guest files → Microsoft Windows…** → Finish.
4. Either the Backup Browser opens (great — better than expected), or it errors / shows the volume as unreadable.

**RESULT TO RECORD:** ✅ or ❌ **plus the exact error message** — that error is the evidence justifying the Veeam 12.3 upgrade + KOR-APP01 mount server.

---

## OPTIONAL TEST 4 — a small VM (2 min)
Proves the small-VM chain that just resumed.
- **Home → Backups → Disk → Kor-VMs-New → Kor-DC01** (or Kor-APP01) → newest restore point → Restore guest files → copy any small file to `C:\RestoreTest` → verify.

---

## WHAT THE RESULTS MEAN

| Test 1 (E: local) | Test 2 (Cloud) | Meaning |
|---|---|---|
| ✅ | ✅ | Best case. Two proven copies. The 23 TB E: retirement decision can be made safely with T-Net. |
| ✅ | ❌ | Local history is good but the off-site backstop is unproven — **do not retire the E: chain**; escalate the cloud copy to T-Net urgently. |
| ❌ | ✅ | The E: chain is suspect (likely damaged by the failed July-15 session) — validate it, and the cloud copy becomes the history of record. |
| ❌ | ❌ | Serious: no proven FS01 history before July 20. Escalate to T-Net immediately; the only proven copy would be the new Synology full. |

Test 3 failing is **expected** and is not a problem — it is the documented justification for punch-list items 9 and 10 (Veeam 12.3 upgrade → KOR-APP01 as mount server).

---

## AFTERWARDS
- Delete `C:\RestoreTest` when done (housekeeping — it's on the tight C: drive).
- Report all results; they go into the T-Net dossier (`docs/KOR-Backup-Infrastructure-Findings-2026-07-16-web.pdf`, findings A11 and item 6).
- Only after Tests 1 and 2 are recorded should the ~23 TB E: chain decision be discussed.
