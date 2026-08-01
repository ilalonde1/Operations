# BD Module — Demo Playbook (one-pager)

**For: Ian · 2026-05-30 · Branch `develop` · Open `Kor.Operations.App` → BD Workspace**

---

## ⚙️ Pre-flight (30 sec before the demo)

1. Launch `Kor.Operations.App` (Debug build is on `develop`)
2. Home tile → **Business Development**
3. Confirm the nav strip shows: **Opportunities · Awards · Competition · Region Brief · BD Tracking · Pursuits · MPI · Relationships**

---

## 🎯 The 5-screen flight plan

### Screen 1 — BD Tracking (the new screen, the unlock)

> **Talking point:** "This is our 2026 spreadsheet — now live in the system, with every touchpoint cross-linked to projects and orgs."

| Action | What to point at |
|---|---|
| Click **Alberta** tab | 32 engagements pop, $660K+ submitted YTD, partners Islam + Omar + Islam visible in initiator filter |
| Filter initiator → **Jim** | His 4 USA touchpoints (Bosa, DBRDS, JWDA, CDA Architects) — $2.0M+ submitted across them |
| Select **Bosa Development** row | Drill panel shows 4 activities + Louis Nasr + Paul Lamme as contacts |
| Click the **Company** name (Bosa Development) | Pops the Org Dossier — every project they've appeared on across MPI + every other BD touch |

### Screen 2 — Region Brief (the geo lens)

> **Talking point:** "Want the Vancouver story or the Alberta story? One click."

- **Vancouver / Lower Mainland:** 205 MPI projects, ~$16B in pipeline, top architects HCMA + GBL + Chris Dikeakos
- **Alberta:** Calgary 75 / Edmonton 40 active projects, $6B+ combined
- **Headline picks for demo:**
  - **St. Paul's Hospital Full Replacement** (Vancouver, $2.17B)
  - **Richmond Hospital Yurkovich Family Pavilion** (Richmond, $1.96B)
  - **Galleria Performing Arts Centre** (Edmonton, $850M)
  - **UofC Multidisciplinary Science Hub** (Calgary, $450M)

### Screen 3 — Org Dossier (the company lens)

> **Talking point:** "Pick any firm — we know what they're working on and who we know there."

Demo-quality firms to click:

| Firm | Why it's a good demo click |
|---|---|
| **Stantec Inc.** | 23 MPI projects across BC + AB — biggest prime in our universe |
| **HCMA architecture + design** | 15 projects, all BC — partnership material |
| **Perkins+Will Architects** | 11 projects, US + BC overlap — Jim's USA market |
| **Chris Dikeakos Architects** | 18 BC projects — Richard Bernstein is in our BD contacts |
| **Bosa Development** | Active BD touch + named contacts + lower-mainland tower pipeline |
| **BC Housing** | 51 MPI projects as proponent — the single biggest public buyer |
| **Vancouver School Board (SD39)** | 46 projects — recurring K-12 capital flow |
| **Alberta Health Services** | 16 projects — institutional anchor for AB strategy |

### Screen 4 — Competition (the win/loss lens)

> **Talking point:** "When we bid against firm X, here's the actual record."

- Right-click any opportunity in the grid → **Competition Info** opens the inline view (now hardened in Round 37; was an unguarded async-void)
- Click any **winning vendor** name → Competitor Profile pops with HQ / size / ownership / **KOR overlap score (0–10)** *(newly wired in Round 37d — Round 6f set it but the binding was missed)*

### Screen 5 — MPI (the project lens)

> **Talking point:** "2,302 live projects, filtered by what KOR can chase."

| Filter / pick | Storyline |
|---|---|
| Sort by Cost desc, Province=BC | First 5 rows = St. Paul's, Richmond Hospital ×3, Northeast False Creek — every prime / owner clickable |
| Sort by Cost desc, Province=AB | Galleria, Cement Plant Expansion, Vista Mine, UofC, Foothills Fieldhouse |
| Pick **Garibaldi at Squamish** ($3.5B) | Hits the "we cover destination resort work" angle |

---

## 🎤 If asked the hard questions

| "How fresh is this?" | Worker on KOR-APP01 ingests every source on a cron — Bonfire / Bids&Tenders / SAM.gov / CanadaBuys / GraphEmail emails / APC / etc. all live |
| "How accurate is the org list?" | 56,154 canonical orgs after de-duping; aliases tracked; honing-pass runs weekly |
| "What about wins/losses?" | 136K historical awards already scored + assigned to vendors with KOR overlap scoring |
| "Can I get a one-pager I can email?" | Generate-Brief feature (PDF default, DOCX on right-click) wired to brand-matched template — already designed, build queued |
| "Who built this?" | Stack: .NET 8 WPF + SQL Server (KOR-APP01\SQLEXPRESS · KorOpportunitiesDb) + Worker service + MCP service for AI Q&A |

---

## 🟢 Confidence-rated demo picks (use these for sure)

Best for showing depth in **one click** — every item below has data on every panel:

1. **Bosa Development** (Lower Mainland) — 2 contacts, 4 activities, multiple Lower Mainland tower projects
2. **Kimley-Horn** (USA / Jim's lane) — Geoff Rubin as contact, US$197K submitted
3. **Chris Dikeakos Architects** (BC, 18 projects, Richard Bernstein contact)
4. **Stantec Inc.** (deepest BC + AB footprint, 23 projects)
5. **HCMA + DIALOG + Perkins+Will** (any of these — full architect dossier flow)
6. **St. Paul's Hospital Full Replacement** (biggest BC project, Providence Health owner)
7. **Galleria Performing Arts Centre** (biggest AB project, Edmonton)
8. **City of San Diego** (Ali Fattah contact, public-sector US footprint)
