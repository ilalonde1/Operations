#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Framing prose per sector, ported verbatim from tools/BdReportBuilders
/// (BD-UI-Plan parity target). Transformation rules applied during the port:
/// the "Compiled &lt;date&gt; from ..." sentence is dropped from intro notes
/// (the generator stamps a live generated-at line instead), leading "N. "
/// numbering is stripped from recommended actions (the generator numbers
/// them), and literal markdown ** emphasis is stripped (it rendered as
/// asterisks in the Word COM output — a PS artifact, not styling).
/// Point-in-time MPI ids/status in synthesis prose refresh when the sector
/// re-hones.
/// </summary>
public static class SectorReportProseCatalog
{
    public static SectorReportProse For(string sectorKey)
    {
        return Prose.TryGetValue(sectorKey, out var prose)
            ? prose
            : throw new ArgumentException(
                $"No report prose registered for sector key '{sectorKey}'.", nameof(sectorKey));
    }

    private static readonly IReadOnlyDictionary<string, SectorReportProse> Prose =
        new Dictionary<string, SectorReportProse>(StringComparer.OrdinalIgnoreCase)
        {
            ["hospitals"] = new(
                "Healthcare construction pipeline in BC and Alberta, from Sonnet first-pass + honing verification. This is the YURKOVICH CORRECTION PASS — the highest-rigor honing PROMPT in KOR's pipeline (20-30 tool calls per project). Procurement model identification (Alliance / P3 / Progressive DB captive / Progressive DB external / Traditional DBB / Pre-procurement) is non-negotiable in every brief.",
                new[]
                {
                    "Yurkovich correction worked. The honing PROMPT explicitly required procurement model identification via 2 independent sources before marking any project PURSUE. Alliance / P3 / Stantec-captive projects auto-marked DEAD with named incumbent. Progressive DB with external structural slot is where most PURSUE opportunities sit.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Procurement model breakdown",
                        new[]
                        {
                            "The Yurkovich correction worked as designed — Sonnet identified the procurement model on every project before scoring. Alliance projects (Richmond Hospital Yurkovich locked with Entuitive, Cowichan locked with Bush Bohlman) are correctly DEAD. Stantec-captive projects (Cariboo Memorial, several others) are correctly DEAD. The PURSUE list is concentrated in Progressive DB with external structural sub OR Traditional DBB pre-procurement.",
                        }),
                    new(
                        "NACIC + SACIC twin opportunity — Alberta compassionate intervention",
                        new[]
                        {
                            "Northern Alberta Compassionate Intervention Centre (NACIC, MPI 7036, $90M, Edmonton) and Southern Alberta Compassionate Intervention Centre (SACIC / Spyhill, MPI 6473, $159M, Calgary) are TWIN PROJECTS under the same DBFM procurement. RFP stage NOW. Contract award August 2026. Shortlisted consortia forming teams NOW. KOR's Alberta presence is the direct angle.",
                        }),
                    new(
                        "Plant and Animal Health Centre $400M — biggest URGENT",
                        new[]
                        {
                            "MPI 6585. RFP stage with shortlisted teams. Structural sub slot OPEN in each shortlisted team and filling NOW. Identify the shortlisted prime contractors and engage their pre-construction teams immediately.",
                        }),
                    new(
                        "Vancouver General Hospital Campus Redevelopment Building 1",
                        new[]
                        {
                            "MPI 6494. VCH Campus Building 1 — interim rezoning pending Summer 2026 City Council vote. Pre-procurement but high-priority. Sharon Petty (VCH Director Real Estate Operations, ex-CPO Richmond Hospital) is the relationship.",
                        }),
                    new(
                        "Vernon Jubilee Hospital Psychiatric — D-B RFP active",
                        new[]
                        {
                            "MPI 3133. IBC RFP stage. Award likely H1 2026. Structural sub within D-B team. If KOR can engage shortlisted teams (likely PCL, Graham, EllisDon, Bird), structural sub slot is accessible.",
                        }),
                },
                new[]
                {
                    "PURSUE_URGENT — IMMEDIATE outreach on NACIC + SACIC Alberta DBFM (combined $249M, twin procurement, shortlisted teams forming now). Reach out to known D-B contenders for Alberta health: PCL Health Group, EllisDon, Graham Healthcare.",
                    "Plant and Animal Health Centre $400M — identify shortlisted teams and engage structurally.",
                    "VGH Building 1 — Sharon Petty at VCH is the relationship. Sharon Petty LinkedIn: linkedin.com/in/sharon-petty-6491a911b/.",
                    "Vernon Jubilee Hospital Psychiatric — engage shortlisted D-B teams via Daniel Murphy (EllisDon VP Preconstruction dmurphy@ellisdon.com) or equivalent at Graham, PCL, Bird.",
                    "Audit DEAD list for Alliance / P3 dissolutions or future phase openings. Phase 2/3 of Richmond Hospital, Cowichan, etc. are MONITOR candidates as future Alliance partner re-procurement.",
                }
            ),
            ["schools"] = new(
                "K-12 school construction pipeline in BC and Alberta. Verdicts: PURSUE / MONITOR / DEAD / DISCOVER. Schools are KOR's bread-and-butter recurring market — landing one SD opens the whole district pipeline. The BMZ legacy (KOR was Bryson Markulin Zickmantel before 2021 rebrand) is explicitly leveraged where prior SD relationships exist.",
                new[]
                {
                    "Schools market is rolling procurement — SDs typically procure 1-5 projects per year via pre-qualified consultant lists. Landing one SD opens the whole pipeline.",
                },
                new SynthesisSection[]
                {
                    new(
                        "BC Seismic Mitigation Program — KOR direct specialty wave",
                        new[]
                        {
                            "The Province of BC SMP (Seismic Mitigation Program) drives a multi-decade rolling pipeline of school seismic upgrades — every BC school in the 1970s-1990s era is on the list. The PURSUE seismic projects (Sutherland, R.C. Palmer, Hugh McRoberts, Carson Graham, Churchill, Lynnmour, Matthew McNair) are KOR's direct competitive sweet spot. Land structural-engineer slots on these by positioning with the architect engaged on each project + the SD facilities team.",
                        }),
                    new(
                        "SD relationships compound — pick the high-velocity SDs",
                        new[]
                        {
                            "School District procurement cadence varies — some SDs procure 5+ projects per year (Surrey SD36, Burnaby SD41, Vancouver SD39, Richmond SD38, Sooke SD62), others 1-2 per year. Cross-reference the PURSUE + MONITOR + DISCOVER lists to identify SDs appearing 3+ times — those are KOR's highest-leverage relationship targets. Single SD pre-qualified list slot = 5+ years of recurring pursuits.",
                        }),
                    new(
                        "BMZ legacy — search KOR's prior SD relationships",
                        new[]
                        {
                            "KOR was Bryson Markulin Zickmantel (BMZ) before the 2021 rebrand. Many BC SD relationships pre-date the rename. Before contacting any SD, search KOR's portfolio for prior BMZ-era work in that SD. A 1990s-2010s BMZ project reference is the warm-introduction path that compounds — SD Facilities Directors typically remember prior consultants by name.",
                        }),
                    new(
                        "CSF (Conseil scolaire francophone) projects",
                        new[]
                        {
                            "École Beausoleil, École Élémentaire Beausoleil, École Beausoleil K-12 — multiple CSF MPIs appear PURSUE. CSF is a SINGLE province-wide francophone school district with consolidated capital procurement. One CSF relationship = access to their entire BC pipeline.",
                        }),
                },
                new[]
                {
                    "CALL Brian Jonker SD62 (Sooke) at 250-474-9800 re: North Langford Secondary $220M structural-engineer slot. Sonnet flagged this as the highest-priority single action.",
                    "Verify BidCentral for École Beausoleil $73M RFP + Matthew McNair Secondary SD38 RFP. Both have unawarded SE slots per honing pass.",
                    "Audit KOR portfolio for prior BMZ-era SD relationships — identify SDs where KOR has a warm-intro path that pre-dates the rebrand. Likely candidates: Vancouver SD39, Burnaby SD41, Coquitlam SD43, Surrey SD36, Richmond SD38.",
                    "Target BC seismic upgrade specialty — bid on the SMP pipeline. KOR's seismic structural expertise + BC market depth = direct competitive position. Architects to engage as warm-intro: PUBLIC Architecture, Acton Ostry, KMBR, MMA, Stantec.",
                    "Build CSF relationship — single province-wide francophone SD with multiple PURSUE projects. One relationship = recurring access.",
                }
            ),
            ["indigenous"] = new(
                "BC + Alberta First Nations / Métis / Indigenous construction pipeline. Verdicts: PURSUE / MONITOR / DEAD / DISCOVER. Indigenous procurement is relationship-driven, not open-bid — warm-introduction paths and 1-3 year relationship cadence acknowledged in every recommendation. Cultural-competency and protocol-respect baked into the honing PROMPT.",
                new[]
                {
                    "Indigenous BD reality: most projects are NOT competitive open-RFP. They are relationship-led procurement where Nation chief and council, development corporations, or non-profit operators select trusted partners. KOR's pursuit play is a 1-3 year relationship cadence. The PURSUE projects below have either explicit RFP windows OR specific named warm-introduction paths surfaced by the honing pass.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Lower Mainland Nation development corp dominance",
                        new[]
                        {
                            "Squamish Nation, Musqueam, Tsleil-Waututh control major Lower Mainland development pipelines (Senakw, Jericho Lands, Langara YMCA, Maplewood / statləw̓ District). These are billion-dollar multi-tower phased developments where structural-eng selection happens early in each phase. KOR's Lower Mainland concrete tower expertise is direct fit. Build relationships with the Development Corps directly: Senakw Development Corp (Squamish), MST Development Corporation (Musqueam-Squamish-Tsleil-Waututh).",
                        }),
                    new(
                        "Vancouver Island Nation housing wave",
                        new[]
                        {
                            "Tla'amin, Snuneymuxw, Tseshaht, Uchucklesaht, K'ómoks all have multi-unit affordable housing programs with BC Housing or CMHC co-funding. Smaller per-project value but RECURRING. Nation-level relationships compound across multiple project phases.",
                        }),
                    new(
                        "Indigenous-owned structural firm requirement caution",
                        new[]
                        {
                            "A growing number of Indigenous procurements require Indigenous-owned firms on the prime team. KOR is NOT Indigenous-owned. The play is: (1) sub-consultant to an Indigenous-led prime architect (Two Row Architect, Smoke Architecture, Brook McIlroy), (2) sub-consultant to a non-Indigenous prime with strong Indigenous track record (Stantec, DIALOG, IBI/Arcadis), or (3) joint-venture partnership with an Indigenous engineering firm for specific pursuits. Identify which projects allow which strategy.",
                        }),
                },
                new[]
                {
                    "Squamish Nation relationship play — Build 600 Affordable Homes Action Plan (Id 4873) + Sen̓áḵw Phases 3-4 (Id 6910 MONITOR). One named warm-intro target via Senakw Development Corp or via past architect.",
                    "Musqueam-Tsleil-Waututh development corp — Jericho Lands Phase 1 (Id 6911) + Maplewood Innovation District (Id 4882) + statləw̓ District (Id 6913). MST Development Corp is the gate. KOR should target this single development corp relationship for compounding returns across Phase 1-N.",
                    "VI housing wave — APD Lands Port Alberni (Id 4846), te'tuxwtun Camp Nanaimo (Id 4843), Redford Uchucklesaht (Id 5821). Smaller pursuits each but cumulative. Identify a Nation-relationship consultant or past-architect channel.",
                    "AB Alexander First Nation Mamawatoskewin (Id 6999) — single PURSUE in AB. Direct opportunity if warm-intro path identified.",
                }
            ),
            ["bc-housing"] = new(
                "BC Housing-funded projects: supportive housing, affordable housing, complex care, shelters, transitional housing. The honing PROMPT identified BC Housing partnership model (non-profit operator / municipality / for-profit developer / Indigenous operator / hybrid) for every project — that's where the structural-eng decision actually happens.",
                new[]
                {
                    "BC Housing rarely builds directly — they partner with non-profit operators (Lookout, Atira, RainCity, Coast Mental Health, PHS, AHMA), municipalities, or for-profit developers. The structural-engineer decision usually happens at the operator level, not BC Housing itself. Pursuit play is operator-relationship building.",
                },
                new SynthesisSection[]
                {
                    new(
                        "BC Housing operator landscape — relationship targets",
                        new[]
                        {
                            "Non-profit operators driving BC Housing pipeline: Lookout Housing & Health Society, Atira Women's Resource Society, RainCity Housing, Coast Mental Health, PHS Community Services Society, Pacific Coast Affordable Housing, BCNPHA members, AHMA (Aboriginal Housing Management Association), Lu'ma Native Housing, Vancouver Native Housing Society. Each operator has 5-15+ projects in 5-year pipeline. KOR's play is operator-level relationship rather than project-level chase.",
                        }),
                    new(
                        "Funding stream signals",
                        new[]
                        {
                            "BC Housing funding streams differ in cadence and partnership model:",
                            "BC Builds (new 2024 program) — fastest cadence, prefers established operators. Building BC umbrella. Supportive Housing Fund. Homes for BC. Community Housing Fund. Indigenous Housing Fund (AHMA-channel). Complex Care Housing initiative. CMHC RCFI co-funded (federal). Identifying funding stream per project tells KOR the operator + cadence.",
                        }),
                    new(
                        "Modular bundle risk",
                        new[]
                        {
                            "BC Housing has a Modular Housing Initiative that bundles structural-engineer with modular supplier (Horizon North, Britco, NRB). Projects flagged as modular procurement are typically DEAD for KOR direct entry — but the modular suppliers themselves are KOR relationship targets if KOR wants to pursue modular structural.",
                        }),
                    new(
                        "Mid-rise concrete = KOR sweet spot",
                        new[]
                        {
                            "BC Housing's preferred typology for supportive + transitional housing is mid-rise concrete. KOR's mid-rise concrete depth is the direct competitive fit. Stick-frame wood low-rises are less differentiated; mass timber hybrid is emerging (Equilibrium, Fast + Epp, ASPECT compete).",
                        }),
                },
                new[]
                {
                    "Audit MONITOR list for repeat operators — operators appearing 3+ times are KOR's highest-leverage relationships. Likely candidates: Lookout, Atira, RainCity, Coast Mental Health.",
                    "PURSUE projects — direct outreach to named operator + BC Housing project director per brief.",
                    "Build BCNPHA Annual Conference relationship attendance — operators network here, BC Housing executives attend.",
                    "Indigenous Housing Fund channel via AHMA — separate but adjacent to Indigenous Projects BD pipeline. Use the indigenous-projects honing learnings.",
                }
            ),
            ["defense"] = new(
                "BC + Alberta defense and military construction pipeline, with named contacts and KOR action items. Verdicts: PURSUE_URGENT / PURSUE / MONITOR / DEAD / DISCOVER. PROMPT-driven Yurkovich-class error catch confirmed the original DB filter pulled false positives (schools, civic arenas, RCMP, First Nations commercial misclassified as defense) — re-categorization candidates are flagged inside the MONITOR/DEAD briefs.",
                new[]
                {
                    "Defense procurement reality: nearly everything flows through DCC (Defence Construction Canada) procurement gates, with CFHA (Canadian Forces Housing Agency) running the residential programs. Security clearance (Reliability or SECRET-level FSC) gates many structural sub slots — every PURSUE brief should be checked for clearance requirements before outreach. KOR's entry is almost always as structural sub on a design-build prime's team (EllisDon, PCL, Graham, Bird), not direct to DND.",
                },
                new SynthesisSection[]
                {
                    new(
                        "DCC is the gate, primes are the door",
                        new[]
                        {
                            "Defense structural work is procured via DCC, but KOR's entry point is the design-build prime's sub-consultant list, not DCC directly. Identify the likely D-B contenders per pursuit (EllisDon, PCL, Graham, Bird) and pre-position before the RFP closes the team. Subscribe to MERX / buyandsell.gc.ca DCC notices for Pacific and Western regions.",
                        }),
                    new(
                        "EllisDon as strategic defense relationship",
                        new[]
                        {
                            "EllisDon recurs across the defense briefs as design prime (see PURSUE/MONITOR entries above) — the same firm KOR is building toward in BC healthcare. EllisDon is to defense what Graham Design Builders is to BC healthcare: a strategic relationship to build deliberately. Contacts via their Victoria, Vancouver, or Calgary offices (named contacts in the EllisDon deep-dive and IntelPersonAffiliation).",
                        }),
                    new(
                        "Security clearance capability",
                        new[]
                        {
                            "Several defense packages require Reliability or SECRET-level facility clearance for the structural sub. KOR should evaluate clearance capability once a concrete pursuit firms up — it is the recurring competitive gate across this sector.",
                        }),
                    new(
                        "Defense MPI ingestion hygiene",
                        new[]
                        {
                            "The honing pass functioned partly as a data-hygiene audit: geographic proximity to a CFB caused schools, arenas, RCMP detachments, and First Nations commercial projects to auto-classify as defense. Tighter ingestion filters needed; re-categorization candidates are flagged per-brief in the MONITOR and DEAD sections above.",
                        }),
                },
                Array.Empty<string>()
            ),
            ["recreational"] = new(
                "Recreational facility construction pipeline (aquatic centres, ice arenas, field houses, community centres, multipurpose recreation) across BC and Alberta. Long-span specialty (aquatic + arena + field house) is direct KOR structural sweet spot. HCMA Architecture + Design is the dominant BC rec architect — warm-intro channel.",
                new[]
                {
                    "Recreational facilities are KOR's structural sweet spot. Long-span roof structures (aquatic + arena + field house) + complex MEP coordination + corrosive environments = direct KOR specialty. HCMA Architecture + Design dominates BC rec specialist market — warm-intro path.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Long-span specialty = KOR direct fit",
                        new[]
                        {
                            "Aquatic centres (long-span roof + MEP + corrosive environment), ice arenas (long-span + low-temp condensation), and field houses (clear-span) are all direct KOR structural specialty. Any of the PURSUE/MONITOR projects in these typologies should rank top of pursuit list.",
                        }),
                    new(
                        "HCMA dominates BC rec — single relationship = compounding",
                        new[]
                        {
                            "HCMA Architecture + Design is BC's dominant recreational specialist architect. One sustained HCMA relationship = recurring KOR positioning across most BC rec pipeline. Newton Community Centre, Britannia, Cameron — HCMA is on most major BC rec projects.",
                        }),
                    new(
                        "Newton Community Centre quadruplets — m114 candidate",
                        new[]
                        {
                            "Newton Community Centre $310.6M (largest civic project in City of Surrey history) appears 4 times across MPIs 4221, 4589, 5288, 6782. Britannia Community Centre $300M appears 2 times (4196, 6483). Worth m114 consolidation similar to UHNBC + Vernon Jubilee. Sonnet may have flagged additional duplicates in the DUPLICATE bucket above.",
                        }),
                    new(
                        "Municipality relationships compound",
                        new[]
                        {
                            "Recreational procurement is municipality-driven. Operators with multiple in-pipeline rec projects (Surrey, Vancouver, Burnaby, Coquitlam, Calgary, Edmonton, Saskatoon) are KOR's highest-leverage relationship targets. Same pattern as BC Housing operators — operator-level relationship rather than project-level chase.",
                        }),
                    new(
                        "Referendum-funded BC projects need political timing",
                        new[]
                        {
                            "Many BC recreational projects are referendum-funded. Approval triggers RFP. Watch BC municipal election cycles (Oct 2025) for new project pipelines triggered by approved referenda.",
                        }),
                },
                new[]
                {
                    "HCMA Vancouver office — single warm-intro to HCMA Principal positions KOR for recurring BC rec pursuits. Identify Principal-in-Charge on Newton + Britannia and start there.",
                    "Newton Community Centre Surrey $310.6M — largest civic project in Surrey history. If KOR has Surrey municipal relationship, this is the showcase pursuit.",
                    "PURSUE list — review each for procurement model + incumbent before outreach.",
                    "m114 migration — consolidate Newton 4x + Britannia 2x duplicates before next drain cycle (same UHNBC + Vernon Jubilee pattern).",
                }
            ),
            ["residential"] = new(
                "Residential / condo tower construction pipeline in BC and Alberta — KOR's largest BD market. High-rise concrete + mid-rise concrete-wood hybrid = direct KOR core. Memory-confirmed KOR client list (Wesgroup, Bosa, Reliance, Westland, Belford, Anthem, Cressey, Peterson, Strand, Beedie, Capital Region Housing, Onni, Concord, Polygon) checked against every project.",
                new[]
                {
                    "Residential is RELATIONSHIP-driven — most BC residential is developer-led with in-house GC (Wesgroup, Bosa, Polygon, Onni) or developer + selected GC + pre-qualified subs. Architect drives structural-engineer selection. Memory-confirmed KOR client developers checked against every project for prior-relationship advantage.",
                    "NOTE: This report covers batch-001 (250 of 483 residential MPIs). Pt2 (233 items) honing not yet launched. Final residential coverage at ~100% pending pt2 completion.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Developer-relationship play dominates residential",
                        new[]
                        {
                            "Residential market is developer-led. Most BC residential structural is awarded via developer-PQ-list rather than open RFP. The compounding move is operator-level relationship rather than project-by-project chase. Memory-confirmed KOR clients (Wesgroup, Bosa, Reliance, Anthem, Cressey, Beedie, Onni, Polygon) each have multi-decade pipelines.",
                        }),
                    new(
                        "BMZ legacy = warm-intro lever",
                        new[]
                        {
                            "KOR was Bryson Markulin Zickmantel pre-2021. Many BC residential developers have BMZ-era references that pre-date the rebrand. Audit KOR portfolio for prior BMZ work on each developer in MONITOR list — that's the warm-intro path. Memory feedback explicitly noted BMZ legacy as KOR's 30+ year BC residential differentiator.",
                        }),
                    new(
                        "In-house GC developers = direct entry",
                        new[]
                        {
                            "Wesgroup, Bosa, Polygon, Onni all have in-house construction divisions. For these developers, structural selection is centralized — one VP Development/VP Construction relationship covers the entire pipeline. Identify the structural-engineer decision-maker at each.",
                        }),
                    new(
                        "Mass timber hybrid = emerging differentiator",
                        new[]
                        {
                            "TELUS Ocean (EllisDon GC, mass timber + concrete hybrid) is the Vancouver model. UBC Brock Commons set the residential mass timber precedent. KOR's ability to deliver mass timber + concrete hybrid is a competitive advantage on mid-rise (8-12 storey) residential.",
                        }),
                },
                new[]
                {
                    "Audit MONITOR list for repeat developers — developers appearing 3+ times = strategic-relationship targets. Memory-confirmed KOR clients appearing in MONITOR are warm-leads.",
                    "Launch residential-honing pt2 (233 items remaining) for complete coverage. After ingest, this report regenerates with full pipeline.",
                    "BMZ legacy audit — surface pre-2021 KOR portfolio references per developer to establish warm-intro lead-ins.",
                    "Wesgroup / Bosa / Onni in-house construction relationships — single VP Development relationship at each unlocks entire pipeline.",
                }
            ),
            ["commercial"] = new(
                "Commercial office tower pipeline in BC and Alberta. Mass timber emergence (TELUS Ocean model) flagged as KOR competitive zone. Vancouver downtown + Burnaby Metrotown + Surrey City Centre + Calgary CBD + Edmonton CBD coverage.",
                new[]
                {
                    "Commercial office market is heavily anchor-tenant-driven. Pre-lease secures viability, then RFP timing predictable. Most BC commercial office projects are developer-led with selected GC + pre-qualified subs.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Commercial office market reality — 88% DEAD",
                        new[]
                        {
                            "Of 126 commercial office projects honed, 111 (88%) are DEAD — structural engineer already assigned, project delivered, or owner pre-locked. The commercial office market in BC + AB is procurement-mature; pursuits are concentrated in a small set of active RFPs.",
                        }),
                    new(
                        "The 3 PURSUE — small but high-leverage",
                        new[]
                        {
                            "Waterfront Square Office Tower (BC), 11-15 East 4th Avenue Life Sciences (BC), Centre Block SFU/Provincial Office Tower Surrey. All require immediate engagement — DPB hearings complete or Development Permit in process. Structural selection window OPEN now.",
                        }),
                    new(
                        "TELUS Ocean was the model — find the next mass-timber office anchor",
                        new[]
                        {
                            "TELUS Ocean Vancouver (EllisDon GC, mass timber + concrete hybrid) set the model for the next wave of BC commercial office. The 2 DISCOVER projects are likely future mass-timber office candidates. Track anchor-tenant pre-lease announcements at Microsoft / Shopify / Amazon Canada to identify the next opportunity.",
                        }),
                    new(
                        "Strategic relationships > project pursuits",
                        new[]
                        {
                            "Given 88% DEAD, the commercial pursuit play is RELATIONSHIPS not projects. Build sustained relationships with the BC commercial office majors: Concert Properties, Westbank, Beedie Development, Anthem, Aquilini, Reliance. Most of these are already KorClients (per memory). The relationship investment compounds across multi-decade commercial pipelines.",
                        }),
                },
                new[]
                {
                    "Waterfront Square Office Tower — DPB hearing complete May 11, 2026. Verify approval status THIS WEEK. James Cheng Architects + Cadillac Fairview VP Development BC channel.",
                    "Oxford 11-15 East 4th Ave Life Sciences — Post-rezoning, Development Permit in process. Oxford Properties + Chernoff Thompson Architects channel.",
                    "Centre Block SFU Surrey — Note flagged as duplicate of 6890. Verify whether it is genuinely distinct or merge with 6890. Stantec Architecture + PCL Westcoast channel.",
                    "Audit MONITOR list for repeat developers — Concert / Westbank / Beedie / Anthem / Aquilini appearing multiple times = strategic-relationship targets.",
                }
            ),
            ["post-secondary"] = new(
                "Post-secondary capital pipeline (universities, colleges, polytechnics) in BC and Alberta. Mass timber academic specialist match (UBC Brock Commons / Earth Sciences model) flagged as KOR sweet spot. BMZ legacy reference baked into every brief.",
                new[]
                {
                    "Post-secondary is RELATIONSHIP-driven — universities + colleges procure on 5-year capital plans with pre-qualified consultant lists. Sonnet flagged 5 immediate actions including direct architect contact paths.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Post-secondary market is procurement-mature",
                        new[]
                        {
                            "Of 182 post-secondary projects honed, 169 are DEAD or DUPLICATE. The post-secondary market in BC + AB is procurement-mature; pursuits are concentrated in 10-13 active or upcoming RFPs.",
                        }),
                    new(
                        "Student housing is the dominant sub-segment",
                        new[]
                        {
                            "UBC Lower Mall, KPU Surrey, UVic 510-bed (delayed 2034 per m113), UBC Brock Commons follow-ons — student housing P3 / DBFM is the dominant procurement sub-pattern. Mass timber + concrete hybrid (Brock Commons model) = direct KOR specialty.",
                        }),
                    new(
                        "Research facility specialty is structural-eng intensive",
                        new[]
                        {
                            "U of A LIFT Centre, NorQuest CSC, UCalgary MDSH — research facilities have vibration-sensitive structural requirements. KOR competitive zone if mass timber academic precedent (UBC Earth Sciences) translates.",
                        }),
                    new(
                        "BMZ legacy + institution relationships compound",
                        new[]
                        {
                            "KOR was Bryson Markulin Zickmantel (BMZ) before 2021. Audit KOR portfolio for prior BMZ-era institution work — UBC, SFU, UVic, BCIT all likely have BMZ-era references that pre-date the rebrand. Surface those for warm-intro paths.",
                        }),
                },
                new[]
                {
                    "CALL Ryder Architecture re: UBC Lower Mall Student Housing 6880 — Sonnet flagged this as priority 1.",
                    "MONITOR BC Bid for KPU Surrey 6897 SE RFP — may be live now.",
                    "Build U of A relationship for LIFT Centre 6979 — SE RFP expected 2026.",
                    "NorQuest College CSC 7062 — open competition, no incumbent, KOR has fair chance.",
                    "UCalgary MDSH 3838 long-game — $450M project, relationship-build 18 months out.",
                }
            ),
        };
}
