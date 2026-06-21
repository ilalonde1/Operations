# Codex prompt — #5 Meeting synopsis + action-packages sheet

**Goal:** Produce the BD-meeting deliverable Ian owes the partners — a **synopsis + 3–4 action
packages** working sheet — as a branded doc.

**Source of truth:** `docs/BD-Meeting-Actionables-2026-06-19.md` (already verified against the DB;
CRM engagement #s are in it — use those, no DB query needed). Do not invent pursuits that aren't
in the source.

**Produce `docs/BD-Action-Sheet-Packages-2026-06-19.md`:**
1. A tight **1-paragraph synopsis** — what the meeting decided / the BD push.
2. **3–4 ACTION PACKAGES**, each a themed bundle with **2–3 assigned owners** and the concrete
   pursuits under it (org names + contacts + CRM engagement #s where they exist + the next step).
   Build them from the source doc:
   - **Reconnect existing clients** — warm Deltek clients to re-engage (Bosa Development #70,
     Pinnacle #11, RBI #4, Intergulf, Omicron, Axiom, Open Form #92…). Owners: Omar / Jim / John.
   - **Outside-the-box new sectors** (§6) — light industrial / small civil, TI & steel-stud
     add-on engineering, water features/pools, marine (BC Ferries / TransLink), modular/off-site
     (Defence Canada via Yellowridge). Owner: whoever the doc assigns + a named line owner.
   - **Regional pushes** — Island modular (Emerge Modular #95 / Evantra #97, Rory), Okanagan
     (Conor), California (John — Greystar + the 4 TBD), Alberta (Islam).
   - *(optional 4th)* **Lists & qualifications** — K-12 School Seismic (SMP), Fraser Health,
     BC Ferries, Ministry/design-build. Owners: JM / Ian / John.
   Each package: owner(s) → orgs/contacts (with eng #s) → next concrete step.
3. Produce the **markdown only** — do NOT run Format-BdDocx (Word COM needs a desktop session
   the Codex sandbox lacks). Claude renders the branded DOCX afterward.

**Style:** crisp, action-oriented — a working sheet the partners scan, not a report. Use the
owner names already in the source (Omar / John / Jim / Rory / Conor / Islam / Ian / JM / Andrea).

This is content + the Format-BdDocx script only — no app code to build or run.
