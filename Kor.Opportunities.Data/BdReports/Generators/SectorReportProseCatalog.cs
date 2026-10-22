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
                "BC and Alberta healthcare construction projects KOR could work on. Each project's procurement model (Alliance / P3 / Progressive DB captive / Progressive DB external / Traditional DBB / Pre-procurement) is identified, because it decides whether the structural seat is still open.",
                new[]
                {
                    "Alliance, P3 and Stantec-captive projects already have their structural engineer locked in; they are rated DEAD with the incumbent named. Most PURSUE opportunities are progressive design-build projects that bring in an outside structural engineer.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Procurement model breakdown",
                        new[]
                        {
                            "Alliance projects (Richmond Hospital Yurkovich Pavilion, locked with Entuitive; Cowichan, locked with Bush Bohlman) are DEAD. Stantec-captive projects (Cariboo Memorial and several others) are DEAD. The PURSUE list is concentrated in Progressive DB with an external structural sub, or Traditional DBB before procurement.",
                        }),
                    new(
                        "NACIC + SACIC twin opportunity — Alberta compassionate intervention",
                        new[]
                        {
                            "Northern Alberta Compassionate Intervention Centre (NACIC, Id 7036, $90M, Edmonton) and Southern Alberta Compassionate Intervention Centre (SACIC / Spyhill, Id 6473, $159M, Calgary) are TWIN PROJECTS under the same DBFM procurement. RFP stage NOW. Contract award August 2026. Shortlisted consortia forming teams NOW. KOR's Alberta presence is the direct angle.",
                        }),
                    new(
                        "Plant and Animal Health Centre $400M — biggest URGENT",
                        new[]
                        {
                            "Id 6585. RFP stage with shortlisted teams. Structural sub slot OPEN in each shortlisted team and filling NOW. Identify the shortlisted prime contractors and engage their pre-construction teams immediately.",
                        }),
                    new(
                        "Vancouver General Hospital Campus Redevelopment Building 1",
                        new[]
                        {
                            "Id 6494. VCH Campus Building 1 — interim rezoning pending Summer 2026 City Council vote. Pre-procurement but high-priority. Sharon Petty (VCH Director Real Estate Operations, ex-CPO Richmond Hospital) is the relationship.",
                        }),
                    new(
                        "Vernon Jubilee Hospital Psychiatric — D-B RFP active",
                        new[]
                        {
                            "Id 3133. IBC RFP stage. Award likely H1 2026. Structural sub within D-B team. If KOR can engage shortlisted teams (likely PCL, Graham, EllisDon, Bird), structural sub slot is accessible.",
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
                "K-12 school construction pipeline in BC and Alberta. Ratings: PURSUE / MONITOR / DEAD / DISCOVER. Schools are KOR's bread-and-butter recurring market — landing one SD opens the whole district pipeline. The BMZ legacy (KOR was Bryson Markulin Zickmantel before 2021 rebrand) is explicitly leveraged where prior SD relationships exist.",
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
                            "École Beausoleil, École Élémentaire Beausoleil, École Beausoleil K-12 — multiple CSF projects are rated PURSUE. CSF is a SINGLE province-wide francophone school district with consolidated capital procurement. One CSF relationship = access to their entire BC pipeline.",
                        }),
                },
                new[]
                {
                    "CALL Brian Jonker SD62 (Sooke) at 250-474-9800 re: North Langford Secondary $220M structural-engineer slot. This is the highest-priority single action.",
                    "Verify BidCentral for École Beausoleil $73M RFP + Matthew McNair Secondary SD38 RFP. Both had unawarded structural slots when last researched.",
                    "Audit KOR portfolio for prior BMZ-era SD relationships — identify SDs where KOR has a warm-intro path that pre-dates the rebrand. Likely candidates: Vancouver SD39, Burnaby SD41, Coquitlam SD43, Surrey SD36, Richmond SD38.",
                    "Target BC seismic upgrade specialty — bid on the SMP pipeline. KOR's seismic structural expertise + BC market depth = direct competitive position. Architects to engage as warm-intro: PUBLIC Architecture, Acton Ostry, KMBR, MMA, Stantec.",
                    "Build CSF relationship — single province-wide francophone SD with multiple PURSUE projects. One relationship = recurring access.",
                }
            ),
            ["indigenous"] = new(
                "BC + Alberta First Nations / Métis / Indigenous construction pipeline. Ratings: PURSUE / MONITOR / DEAD / DISCOVER. Indigenous procurement is relationship-driven, not open-bid — warm-introduction paths and 1-3 year relationship cadence acknowledged in every recommendation. Every recommendation assumes cultural competency and respect for Nation protocol.",
                new[]
                {
                    "Indigenous BD reality: most projects are NOT competitive open-RFP. They are relationship-led procurement where Nation chief and council, development corporations, or non-profit operators select trusted partners. KOR's pursuit play is a 1-3 year relationship cadence. The PURSUE projects below have either explicit RFP windows OR specific named warm-introduction paths.",
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
                            "A growing number of Indigenous procurements require Indigenous-owned firms on the prime team. KOR is NOT Indigenous-owned. The play is: (1) sub-consultant to an Indigenous-led prime architect (Two Row Architect, Smoke Architecture, Brook McIlroy), (2) sub-consultant to a non-Indigenous prime with strong Indigenous track record (Stantec, DIALOG, IBI/Arcadis), or (3) joint-venture / teaming with an Indigenous-owned engineering firm for specific pursuits — KOR now has a named partner for option (3): see Gwaii Engineering below.",
                        }),
                    new(
                        "Named Indigenous-owned engineering partner — Gwaii Engineering (Victoria)",
                        new[]
                        {
                            "Gwaii Engineering (gwaiieng.com) is an Indigenous-owned (Haida) CCAB-certified civil & environmental engineering firm, Victoria BC, founded 2017 by Corey Brown (Sta'staas Eagle Clan, Old Masset). ~20+ staff. It is the incumbent civil engineer for 25+ coastal-BC / Vancouver Island First Nations (Malahat, Cowichan, Tsawout, Tsawwassen, Songhees, Ditidaht, Ucluelet, Tla'amin, K'ómoks, Haida Nation) plus BC Housing — the same owners this pipeline tracks.",
                            "WHY IT SOLVES THE CAUTION: civil (Gwaii) and structural (KOR) are complementary, non-competing disciplines. A KOR-structural + Gwaii-civil team satisfies the Indigenous-owned-firm / participation requirement (CCAB) AND brings Gwaii's standing with the Nation/owner — turning the 'KOR is not Indigenous-owned' blocker into a strength. No prior KOR–Gwaii contractual history (net-new white space).",
                            "Sibling firm: Islander Engineering (Victoria, civil/land-dev) shares founder Corey Brown + staff — covers the conventional-market Island lane. Single relationship (Corey Brown, cbrown@gwaiieng.com; Brandon Ducharme, Capital Advisory, bducharme@gwaiieng.com) opens both. Full dossiers on both firms (June 2026) are available from BD.",
                        }),
                },
                new[]
                {
                    "Squamish Nation relationship play — Build 600 Affordable Homes Action Plan (Id 4873) + Sen̓áḵw Phases 3-4 (Id 6910). One named warm-intro target via Senakw Development Corp or via past architect.",
                    "Musqueam-Tsleil-Waututh development corp — Jericho Lands Phase 1 (Id 6911) + Maplewood Innovation District (Id 4882) + statləw̓ District (Id 6913). MST Development Corp is the gate. KOR should target this single development corp relationship for compounding returns across Phase 1-N.",
                    "VI housing wave — APD Lands Port Alberni (Id 4846), te'tuxwtun Camp Nanaimo (Id 4843), Redford Uchucklesaht (Id 5821). Smaller pursuits each but cumulative. Identify a Nation-relationship consultant or past-architect channel.",
                    "AB Alexander First Nation Mamawatoskewin (Id 6999) — single PURSUE in AB. Direct opportunity if warm-intro path identified.",
                }
            ),
            ["bc-housing"] = new(
                "BC Housing-funded projects: supportive housing, affordable housing, complex care, shelters, transitional housing. Each project's BC Housing partnership model (non-profit operator / municipality / for-profit developer / Indigenous operator / hybrid) is identified — that's where the structural-eng decision actually happens.",
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
                    "Indigenous Housing Fund channel via AHMA — separate but adjacent to Indigenous Projects BD pipeline. See the Indigenous Projects report for the approach.",
                }
            ),
            ["defense"] = new(
                "BC + Alberta defense and military construction pipeline, with named contacts and KOR action items. Ratings: PURSUE_URGENT / PURSUE / MONITOR / DEAD / DISCOVER. Some projects listed near military bases are not defense work (schools, civic arenas, RCMP, First Nations commercial); their notes in the MONITOR and DEAD tables say so.",
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
                            "EllisDon recurs across the defense briefs as design prime (see PURSUE/MONITOR entries above) — the same firm KOR is building toward in BC healthcare. EllisDon is to defense what Graham Design Builders is to BC healthcare: a strategic relationship to build deliberately. Contacts via their Victoria, Vancouver, or Calgary offices (named contacts in the Strategic Relationships report).",
                        }),
                    new(
                        "Security clearance capability",
                        new[]
                        {
                            "Several defense packages require Reliability or SECRET-level facility clearance for the structural sub. KOR should evaluate clearance capability once a concrete pursuit firms up — it is the recurring competitive gate across this sector.",
                        }),
                    new(
                        "Live edge — Vancouver Island CFHA housing cluster",
                        new[]
                        {
                            "The current PURSUE_URGENT set is concentrated on Vancouver Island CFHA (Canadian Forces Housing Agency) residential plus a hangar, all DCC-procured: 19 Wing Comox CFHA Housing Phase 2, 19 Wing Comox CMMA Hangar, and CFB Esquimalt CFHA 120 Units (CFB Edmonton CFHA Phase 2 and an Esquimalt DoD project follow as PURSUE). Housing-led, repeatable work in KOR's coastal-BC backyard. NOTE on past performance: KOR's Esquimalt-area structural history is Esquimalt-MUNICIPALITY work, not the naval base — KOR has no confirmed direct DND/CFB project record, so entry is via teaming, not incumbency.",
                        }),
                    new(
                        "Team partners KOR can leverage — Yellowridge, DHK, and an Indigenous civil partner",
                        new[]
                        {
                            "KOR has active project relationships with two viable defense-team partners: Yellowridge Construction (design-build GC, 60+ years, builds DND plus Indigenous / First Nations housing & health facilities) and DHK Architects — both real KOR working relationships (e.g. Yellowridge — 611 Brunswick St, Prince George; DHK — All Clan House, Stellako BC). For DND packages with Indigenous-participation requirements or on/near Nation land, pair with Gwaii Engineering (Indigenous-owned, CCAB-certified civil) and its sibling Islander Engineering: KOR structural + Gwaii civil earns Indigenous-participation scoring. NOTE: KOR has no prior jobs with Islander/Gwaii — forward-looking teaming, not past performance.",
                        }),
                },
                new[]
                {
                    "Pre-position on the 3 Vancouver Island CFHA urgents (19 Wing Comox housing + CMMA hangar, CFB Esquimalt 120 units) via the likely design-build primes BEFORE the teams close — subscribe to DCC / MERX Pacific-region notices.",
                    "Build the Yellowridge Construction + DHK Architects relationships deliberately — both are existing KOR working relationships and credible defense-team routes.",
                    "For Indigenous-participation defense packages, line up Gwaii Engineering (Indigenous-owned civil) as the participation + civil partner.",
                    "Confirm KOR's facility-security-clearance capability (Reliability / SECRET) — the recurring gate on structural-sub slots.",
                }
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
                }
            ),
            ["residential"] = new(
                "Residential / condo tower construction pipeline in BC and Alberta — KOR's largest BD market. High-rise concrete + mid-rise concrete-wood hybrid = direct KOR core. Every project is checked against KOR's existing developer clients (Wesgroup, Bosa, Reliance, Westland, Belford, Anthem, Cressey, Peterson, Strand, Beedie, Capital Region Housing, Onni, Concord, Polygon).",
                new[]
                {
                    "Residential is RELATIONSHIP-driven — most BC residential is developer-led with in-house GC (Wesgroup, Bosa, Polygon, Onni) or developer + selected GC + pre-qualified subs. Architect drives structural-engineer selection. Where a project's developer is an existing KOR client, that prior relationship is the advantage.",
                    "NOTE: residential projects are still being reviewed — the counts above cover those reviewed so far and will grow.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Developer-relationship play dominates residential",
                        new[]
                        {
                            "Residential market is developer-led. Most BC residential structural is awarded via developer-PQ-list rather than open RFP. The compounding move is operator-level relationship rather than project-by-project chase. Existing KOR clients (Wesgroup, Bosa, Reliance, Anthem, Cressey, Beedie, Onni, Polygon) each have multi-decade pipelines.",
                        }),
                    new(
                        "BMZ legacy = warm-intro lever",
                        new[]
                        {
                            "KOR was Bryson Markulin Zickmantel pre-2021. Many BC residential developers have BMZ-era references that pre-date the rebrand. Audit KOR portfolio for prior BMZ work on each developer in MONITOR list — that's the warm-intro path. The BMZ legacy is KOR's 30+ year BC residential differentiator.",
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
                    "Audit MONITOR list for repeat developers — developers appearing 3+ times = strategic-relationship targets. Existing KOR clients appearing in MONITOR are warm leads.",
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
                        "Commercial office market reality — procurement-mature, mostly locked",
                        new[]
                        {
                            "Most commercial office projects reviewed are DEAD — structural engineer already assigned, project delivered, or owner pre-locked (see the current counts above). The commercial office market in BC + AB is procurement-mature; pursuits concentrate in a small set of active RFPs.",
                        }),
                    new(
                        "The PURSUE set — small but high-leverage",
                        new[]
                        {
                            "Waterfront Square Office Tower (BC), 11-15 East 4th Avenue Life Sciences (BC), Centre Block SFU/Provincial Office Tower Surrey. All require immediate engagement — DPB hearings complete or Development Permit in process. Structural selection window OPEN now.",
                        }),
                    new(
                        "TELUS Ocean was the model — find the next mass-timber office anchor",
                        new[]
                        {
                            "TELUS Ocean Vancouver (EllisDon GC, mass timber + concrete hybrid) set the model for the next wave of BC commercial office. The DISCOVER projects are likely future mass-timber office candidates. Track anchor-tenant pre-lease announcements at Microsoft / Shopify / Amazon Canada to identify the next opportunity.",
                        }),
                    new(
                        "Strategic relationships > project pursuits",
                        new[]
                        {
                            "Because the market is largely locked, the commercial pursuit play is RELATIONSHIPS not projects. Build sustained relationships with the BC commercial office majors: Concert Properties, Westbank, Beedie Development, Anthem, Aquilini, Reliance. Most of these are already KOR clients. The relationship investment compounds across multi-decade commercial pipelines.",
                        }),
                },
                new[]
                {
                    "Waterfront Square Office Tower — DPB hearing complete May 11, 2026. Verify approval status THIS WEEK. James Cheng Architects + Cadillac Fairview VP Development BC channel.",
                    "Oxford 11-15 East 4th Ave Life Sciences — Post-rezoning, Development Permit in process. Oxford Properties + Chernoff Thompson Architects channel.",
                    "Centre Block SFU Surrey — may be the same project as Id 6890; confirm before outreach. Stantec Architecture + PCL Westcoast channel.",
                    "Audit MONITOR list for repeat developers — Concert / Westbank / Beedie / Anthem / Aquilini appearing multiple times = strategic-relationship targets.",
                }
            ),
            ["post-secondary"] = new(
                "Post-secondary capital pipeline (universities, colleges, polytechnics) in BC and Alberta. Mass timber academic specialist match (UBC Brock Commons / Earth Sciences model) flagged as KOR sweet spot. BMZ-era references are noted where they exist.",
                new[]
                {
                    "Post-secondary is RELATIONSHIP-driven — universities + colleges procure on 5-year capital plans with pre-qualified consultant lists. Five immediate actions are listed below, including direct architect contact paths.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Post-secondary market is procurement-mature",
                        new[]
                        {
                            "Of 182 post-secondary projects reviewed, 169 were already locked, delivered or listed twice. The post-secondary market in BC + AB is procurement-mature; pursuits are concentrated in 10-13 active or upcoming RFPs.",
                        }),
                    new(
                        "Student housing is the dominant sub-segment",
                        new[]
                        {
                            "UBC Lower Mall, KPU Surrey, UVic 510-bed (delayed to 2034), UBC Brock Commons follow-ons — student housing P3 / DBFM is the dominant procurement sub-pattern. Mass timber + concrete hybrid (Brock Commons model) = direct KOR specialty.",
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
                    "CALL Ryder Architecture re: UBC Lower Mall Student Housing (Id 6880) — priority 1.",
                    "MONITOR BC Bid for KPU Surrey 6897 SE RFP — may be live now.",
                    "Build U of A relationship for LIFT Centre 6979 — SE RFP expected 2026.",
                    "NorQuest College CSC 7062 — open competition, no incumbent, KOR has fair chance.",
                    "UCalgary MDSH 3838 long-game — $450M project, relationship-build 18 months out.",
                }
            ),
            ["okanagan"] = new(
                "Okanagan Region construction pipeline — Kelowna, West Kelowna, Vernon, Penticton, Lake Country, Summerland, Peachland, Coldstream, Armstrong, Enderby, Oliver, Osoyoos. A region view: the sector reports group projects by what is being built; this one groups by where. Buyer contacts are mapped for the region's municipal and institutional owners — City of Kelowna, City of Vernon, City of Penticton, District of Peachland, District of Summerland, City of West Kelowna, and Interior Health.",
                new[]
                {
                    "The Okanagan is KOR's natural interior growth corridor: close enough to serve from Vancouver, public-owner-driven (municipalities, school districts, Interior Health), and procurement-open — unlike the Lower Mainland's developer-PQ-list market, most Okanagan structural work is won through open municipal and institutional RFPs where KOR's credentials compete directly. It is a young pipeline: no projects rated DEAD, with the active list concentrated in PURSUE and MONITOR.",
                    "Recurring buyers dominate the region: City of Kelowna (civic cluster — Mission Activity Centre, Okanagan Centre for the Performing Arts, Community Theatre upgrades), SD23 Central Okanagan and SD83 North Okanagan-Shuswap (school programs), Interior Health (Vernon Jubilee and Cottonwoods), and the utilities (FortisBC Kelowna LNG capacity, BC Hydro Westbank substation). The compounding play is buyer-level relationships, not project-level chase — the same pattern as BC Housing operators and rec municipalities.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Vernon Jubilee Hospital — the region's URGENT",
                        new[]
                        {
                            "New Inpatient Psychiatric Unit (Id 7031, Interior Health / North Okanagan Regional Hospital District) is the region's PURSUE_URGENT and pairs with the hospitals report's Vernon Jubilee D-B RFP entry (Id 3133). Interior Health is the single most leveraged relationship in the region — it also owns Cottonwoods Care Centre Redevelopment in Kelowna (Id 4486). One Interior Health capital-projects relationship covers the regional healthcare pipeline.",
                        }),
                    new(
                        "Burtch Road Middle School $101M — the region's largest education pursuit",
                        new[]
                        {
                            "SD23's Burtch Road Middle School ($101M) is the region's largest single education opportunity, and SD23 is the region's highest-velocity school district. The project is listed under three Ids (2195, 4537, 5113).",
                        }),
                    new(
                        "Municipal civic + recreation wave = KOR structural sweet spot",
                        new[]
                        {
                            "Penticton Twin Pad Arena ($70M, City of Penticton) is a long-span recreational structure — direct KOR specialty per the recreation report. Kelowna's civic cluster (performing arts centre, community theatre, Mission Activity Centre) and Peachland Firehall ($16-18M) round out a municipal wave where facilities and procurement contacts are mapped at every buyer. Small-to-mid municipal owners here procure openly — a credentials-forward introduction lands differently than in the Lower Mainland.",
                        }),
                    new(
                        "Indigenous and institutional threads",
                        new[]
                        {
                            "Penticton Indian Band's Outma Sqilx'w School Addition (Id 4860) and BC Housing's Skaha Assembly Redevelopment (Id 4467) connect this region to the Indigenous and BC Housing playbooks. UBCO Student Housing Expansion (Id 4513) and Okanagan College Aviation Campus (Id 5668) do the same for post-secondary. Okanagan Indian Band (OKIB) contacts are mapped. Live Edge Okanagan Builders is already a KOR client — the region is not a cold start.",
                        }),
                },
                new[]
                {
                    "Interior Health capital projects relationship — covers Vernon Jubilee Psychiatric (URGENT, Id 7031) + Cottonwoods Kelowna (Id 4486) in one channel. Cross-reference the hospitals report's Vernon Jubilee D-B entry before outreach.",
                    "SD23 Central Okanagan — Burtch Road Middle School $101M is the region's biggest education pursuit.",
                    "City of Penticton — Twin Pad Arena $70M long-span structure; lead with KOR's recreational portfolio (aquatic/arena precedent projects).",
                    "Review the mapped buyer contacts (Kelowna, Vernon, Penticton, Peachland, Summerland, West Kelowna) and pick the top three municipal introductions for Q3 2026.",
                }
            ),
            ["us-west"] = new(
                "US West Coast construction pipeline — California, Washington, Oregon. A region view covering KOR's US projects; the BC + AB sector reports leave them out. KOR's US practice serves Los Angeles and San Diego today; Washington and Oregon are stated growth markets. US dollar costs are converted to CAD, with the rate and date shown in the cost text.",
                new[]
                {
                    "California is the depth market: the largest US state slice with the bulk of the live pursuits, anchored by KOR's existing LA + San Diego presence. Washington is thin but carries the single biggest US opportunity (Harborview, Seattle). Oregon is a pure watch-list — the Portland and Salem projects are all rated MONITOR, early-stage projects worth a quarterly review rather than outreach today.",
                    "About a quarter of US projects have no sector assigned yet, so any sector breakdown understates the true mix.",
                },
                new SynthesisSection[]
                {
                    new(
                        "Harborview Medical Center — the biggest US URGENT",
                        new[]
                        {
                            "Harborview Medical Center New Inpatient Tower + campus rebuild (Id 3943, Seattle, USD $1.74B) is the largest live pursuit in the US slice and the single most expensive URGENT in the whole inventory. It is KOR's natural Washington beachhead: a healthcare campus rebuild squarely in KOR's hospital depth, in a stated growth market.",
                        }),
                    new(
                        "University of California student-housing wave",
                        new[]
                        {
                            "Three UC system projects sit PURSUE_URGENT: UC Santa Barbara East Campus Student Housing (Id 4166, USD $566M), UCLA 901 Levering Student Housing (Id 4151, USD $351M), and UC San Diego Pepper Canyon East District (Id 4154). The UC capital program is the US equivalent of the BC post-secondary play — one institutional relationship compounds across campuses, and UCLA / UCSD are inside KOR's existing LA + San Diego service area.",
                        }),
                    new(
                        "California hospital wave",
                        new[]
                        {
                            "Beyond the URGENTs (Hillcrest Medical Center Replacement in San Diego, Id 3940; the new hospital wing at Beverly & San Vicente in Los Angeles, Id 3938), the PURSUE list is hospital-heavy: Santa Clara Medical Center (USD $2.8B), La Jolla Medical Center Tower 2 (USD $1.0B), Emeryville Medical Center (USD $1.0B). California hospital owners are procuring at scale, and the San Diego / LA entries overlap KOR's existing footprint.",
                        }),
                    new(
                        "San Diego Civic Center — the megaproject",
                        new[]
                        {
                            "San Diego Civic Center Redevelopment (Id 4278, USD $3.3B, PURSUE) is the largest single project in the US slice — a downtown civic core redevelopment in the city KOR already serves. Multi-phase, multi-tower; structural selection will happen per-phase, so the relationship clock starts now.",
                        }),
                },
                new[]
                {
                    "Harborview Seattle (Id 3943) — identify the design-build teams forming on the inpatient tower and campus rebuild packages; this is the Washington market entry.",
                    "UC system relationship — UCLA 901 Levering + UCSD Pepper Canyon are inside the existing service area; one UC capital-programs relationship covers the URGENT trio.",
                    "San Diego pair — Hillcrest Medical Center Replacement (URGENT) + Civic Center Redevelopment ($3.3B): both in KOR's home US market, both early enough to position.",
                    "Keep Oregon (Portland / Salem, all MONITOR) on a quarterly review.",
                }
            ),
        };
}
