# KOR-302N — Executive Briefing

**Prepared for:** KOR leadership / HR / legal
**Prepared by:** Ian Lalonde, Operations & IT
**Date:** 2026-07-10
**Classification:** Confidential — Security Incident
**Full detail:** see the *KOR-302N Security Incident Dossier* (same date). Evidence preserved and hashed at `C:\Escrow\FORENSICS-KOR-302N-2026-07-10\`.

---

## The situation in one paragraph

Our departing Revit/BIM lead is the sole author of roughly 200 custom Revit plugins the drafting team uses every day. In preparing for his exit, we examined his company workstation and found that **KOR's source code for those plugins is no longer on any company system** — his working folders were emptied on July 8, minutes around the time a personal USB drive was connected and his source projects were open on it. We also found a pattern of **personal, unauthorized infrastructure and data handling**: a custom tool he built to export his KOR mailbox to a file, an unauthorized personal VPN linking his machine to an off-domain computer, a second remote-access program, and — as of the night of July 9 — **both of his web browsers' history erased**. The plugins still run, so there is no immediate disruption to drafting, but we currently cannot rebuild or maintain them, and the machine has had an outside connection into our environment.

This briefing states what we know as fact and what remains unproven. It does **not** conclude anything about his motive or intent — that is for HR and legal to pursue. The findings are, however, more than enough to act on now.

---

## What we found (plainly)

**1. The source code left the building.**
His source-code folders on the workstation (`###_Business_LWJ`, `2026_InBox`) were emptied on the morning of July 8. The Recycle Bin was empty, which means the files were *moved*, not deleted. At the same time, Windows records show a **personal Kingston USB drive** was plugged in and Windows Explorer was open to those same source projects **on that USB**. What remains on company systems is only a handful of old, incomplete leftovers. The good news: the finished plugins are still deployed and we have safe copies of them, so we can rebuild the source ourselves if needed (see "Where this leaves us").

**2. He exported his KOR mailbox.**
He wrote his own small program to export his `mli@korstructural.com` mailbox and produced a **357 MB file (a "PST") of his email** dated June 29. We have preserved both the tool and the file.

**3. His machine was on an unauthorized private VPN.**
The workstation runs **Tailscale**, a product that creates a private encrypted network reaching across the internet, **bypassing our firewall and normal controls**. It was tied to a **personal Google account (`elton.rheek@gmail.com`), not a KOR account**, and it linked his workstation to a second "test" computer that had been removed from our domain (and so had vanished from our management tools). It was active from late May through July 3.

**4. A second remote-access tool.**
**TeamViewer** is also installed — an independent way to reach the machine remotely.

**5. He erased his tracks.**
Both his **Chrome and Edge browsing histories were cleared** — the databases are intact but empty of any web addresses, while his saved passwords remain. Edge was wiped as recently as the night of July 9. This removes the record of what websites and uploads he used. His command history also shows him **stripping the digital signature off a commercial plugin file** and running scripts to reorganize his mailbox.

**6. Personal cloud and heavy external-drive use.**
A **personal OneDrive** account is configured on the machine alongside the company one, and Windows shows frequent use of large personal external drives (1 TB and 2 TB) over the past two months.

---

## What is certain vs. what is not

- **Certain:** the dates and device details above; the emptied source folders; the mailbox export tool and file; the VPN, the account behind it, and its link to the off-domain PC; TeamViewer; the cleared browser histories. These come from Windows logs, file records, and preserved copies — all collected read-only, without altering his machine.
- **Not certain:** exactly which files he copied to the USB (provable only from the USB itself), his motive, and the real identity behind the `elton.rheek@gmail.com` account. Our research on that email found **no public footprint** — it appears to be a low-profile or pseudonymous account, and it does not match his name. Identifying the person behind it would require legal requests to the VPN and email providers.

A note on interpretation: the evidence shows **removal of company property, data handling outside policy, and unauthorized remote access**. That is the case, and it is serious on its own. We should keep the file strictly to those facts — conclusions based on anything else (including his nationality) are unsupported and would only weaken KOR's position legally.

---

## The risks

- **Loss of company intellectual property** — our custom Revit tooling source is off our systems.
- **Data exfiltration indicators** — source moved to personal media; mailbox exported; history erased; compression/archiving tools present.
- **A standing outside connection** into our environment for about six weeks (last active July 3), via a network we do not control.

---

## What to do now (recommended)

The order matters — preserve before disabling anything.

1. **Preserve his mailbox** (litigation hold) and its audit records **before** touching the account.
2. **Cut the outside access** — block the VPN (Tailscale) and TeamViewer at our firewall.
3. **Disable his account** and reset the passwords/keys he had access to.
4. **Secure the physical evidence** — the Kingston USB and other drives, the off-domain test PC, and a full image of the workstation (which also captures the few items we couldn't copy while he was logged in).
5. **Engage HR and legal now.** Given likely IP theft and unauthorized access, counsel should decide on outside forensics and any referral to law enforcement. HR and legal own any conversation with him; IT's role is containment, preservation, and recovery.

We have already **preserved 785 MB of evidence with integrity hashes** and a chain-of-custody record, and all examination so far was read-only. **No containment steps have been taken yet** — those are leadership's call.

---

## Where this leaves us (recovery)

There is **no immediate impact to drafting** — the plugins are self-contained and keep working. Because we hold safe copies of every deployed plugin, we can **rebuild a source-code baseline that KOR fully owns and controls**, independent of his cooperation, and put it under proper company version control so this can't recur.

Separately, we recommend a set of **going-forward safeguards** (centralized logging that can't be locally erased, endpoint monitoring, USB and cloud-upload controls, mailbox-export restrictions, and mandatory company code storage). These are detailed in the full dossier.

---

*This is a summary. The complete evidence-referenced findings, timeline, device identifiers, and step-by-step containment runbook are in the KOR-302N Security Incident Dossier and its companion documents.*
