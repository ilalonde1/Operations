# Email Signature Slim-Down — 2026-07

John's request (2025-11-22): drop the long disclaimer text from everyone's
signature and replace it with a single **Email Disclaimer** link to a page on
korstructural.com, Fast+Epp style. EGBC Permit 1000378 stays in the signature
(one short line; Professional Governance Act identification — flagged to John
rather than silently dropped).

Everyone at KOR uses **classic desktop Outlook with local signatures**, so the
whole pipeline is local-file based: a logon script collects current sigs to
the LAN share for analysis, a generator builds the new ones from a reviewed
roster, and a second logon script installs them. No OWA involved; nobody
touches their own Outlook.

## What's in this folder

| File | Purpose |
|---|---|
| `email-disclaimer-page.md` | Copy for the new website page — **John reviews before publish** |
| `signature-template.html` | Tokenized Outlook-safe signature (one template covers engineers and admin — blank fields drop their lines) |
| `signature-john-markulin.html` | Filled example to show John |
| `Collect-RemoteSignatures.ps1` | Proactive sweep: harvests `\\PC\c$\...\Signatures` for the users in `ADUsers.csv` across all reachable domain PCs (ADSI, no RSAT needed) |
| `Deploy-RemoteSignatures.ps1` | **The deploy mechanism**: overwrites each user's personal sig files in place over `c$` (dry-run by default, `-Commit` to apply). No GPO/registry needed — Outlook's default points at a filename, and we keep the filenames |
| `Collect-LocalSignatures.ps1` / `Set-LocalOutlookSignature.ps1` | OPTIONAL logon-script variants — only needed if we ever want new hires / rebuilt PCs handled automatically; direct push replaced them |
| `Build-RosterDraft.ps1` | Run once: parses the collected sigs (name, credentials, role, title, D/M/ext, email) → `roster.draft.csv` |
| `Generate-Signatures.ps1` | roster.csv + template → `generated\<alias>.htm`; `-Publish` stages them to the share |
| `Set-LocalOutlookSignature.ps1` | GPO logon script: installs the user's generated sig, sets it default for New/Reply, disables roaming signatures. Exits silently until a generated file exists for the user |

## Rollout — in this order

1. **John sign-off** on two things: the disclaimer page copy and the example
   signature (including keeping the EGBC permit line).
2. ~~Publish the disclaimer page~~ **DONE 2026-07-09** — live and verified at
   `https://www.korstructural.com/email-disclaimer/` (all 6 sections render,
   permit number present). Logo resolved: `korlogo.png` in this folder
   (= website `KOR-logo-slogan.png`); deployed signatures embed a local copy
   so recipients never see a blocked-image placeholder.
3. ~~Collect signatures~~ **DONE 2026-07-09** — `Collect-RemoteSignatures.ps1`
   swept 30 reachable PCs and harvested 23/24 users to
   `\\KOR-FS01\BD Brain\email-signatures\collected`. Only `ishabana` missing.
4. ~~Roster~~ **DONE 2026-07-09** — `roster.csv` built from the harvested sigs,
   hand-corrected; the four gaps (ishabana, kevinw, simons, markb) filled from
   details Ian supplied. Complete for all 24 users.
5. ~~Generate~~ **DONE 2026-07-09** — all 24 generated to `generated\`, blank
   fields verified to drop cleanly. The old "Click here to send me files" link
   survives as **Send Me Files** in the bottom row for everyone, personalized
   to `https://tracking.korstructural.com/filedrop?to=<email>` (endpoint
   verified live, HTTP 200).
6. **Ship (after John signs off):** `Deploy-RemoteSignatures.ps1 -Commit` —
   direct push over `c$`, effective immediately (running Outlook reads sig
   files at insert time). Dry-run validated 2026-07-09: 23/24 targeted;
   protected from overwrite: vacation/holiday variants (kevinw), "Thx MM"
   (mmousa), "Reviews" shared-mailbox sig (ilalonde), "Okanagan" role variant
   (cmurtagh — still carries old boilerplate; make it a proper variant later
   if Conor wants). Rerun for `ishabana` when his PC is on. `.rtf` sig
   variants are left stale (only Rich-Text-format mail would show them).

## Draft note to John

> John — re: slimming the signatures. Two attachments: (1) the proposed
> disclaimer page that would live at korstructural.com/email-disclaimer — this
> carries everything we're removing from the signatures (sealed-drawings
> caveat, intended-recipient text, no-reliance, etc.), and (2) your signature
> as it would look — everything gone below the phone line except a small
> "Email Disclaimer" link and the EGBC permit number. I'd keep the permit
> number visible since PGA expects the permit identified on professional
> communications and it's one short line — but it can move to the page if you
> want it maximally clean. If the wording and look are good I can roll it out
> to everyone automatically; nobody has to touch their own Outlook.

## Notes / caveats

- **jbryson is OPTED OUT (2026-07-10, Ian's instruction)**: reverted to his
  original signature (desktop file restored from `collected\`, cloud sig
  restored with the hosted logo-with-tagline since the legacy store strips
  inline images) and removed from `roster.csv`, so deploy/cloud runs skip him.
  He's OWA-only. To bring him back in: re-add his roster row and regenerate.

- Colours are sampled from the brand logo PNG: orange `#F15D3A`, dark text
  `#373435`; body slate `#5A6771` matched to the current signature.
- `Generate-Signatures.ps1 -Publish` ships `korlogo.png` to the share; the
  deploy script installs it to `Signatures\Kor Structural_files\` where the
  sig references it, so classic Outlook embeds it in every outgoing message.
- Deploy script re-asserts the default signature at every logon, so a user who
  fiddles with theirs gets reset next morning — by design.
- OWA / new Outlook signatures are deliberately untouched (nobody uses them).
  If someone later lives in OWA, `Set-MailboxMessageConfiguration -SignatureHtml`
  can push the same generated HTML there.
- Commercial alternative (zero maintenance, ~$2–3/user/mo): Exclaimer or
  CodeTwo server-side injection. Script route chosen — one-time build, free.
