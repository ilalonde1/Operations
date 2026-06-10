#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// The ten strategic compounding-relationship targets, ported verbatim from
/// tools/BdReportBuilders/build-strategic-relationships.ps1 (2026-06-09
/// sweep). Config prose — refresh when targets re-hone (Phase E refreshes).
/// </summary>
public static class StrategicTargetCatalog
{
    public static IReadOnlyList<StrategicTargetDefinition> All { get; } = new[]
    {
        new StrategicTargetDefinition(
            "Graham Design Builders LP (BC Healthcare Cornerstone)",
            "Design-Build GC + sometimes Prime",
            "$3.4B+ active BC healthcare pipeline. Embedded on Richmond Hospital Yurkovich Pavilion (Phase 2, $1.96B). Honing pass surfaced as strategic canonical layered with KOR (m108).",
            new[]
            {
                "Alex Trifunov — Pre-Construction Manager, Graham Vancouver office. Primary entry point.",
                "Graham Healthcare division — pursue cold via LinkedIn + direct office outreach.",
                "(Additional contacts may be in IntelPersonAffiliation table — query Org Dossier 8361 in app.)",
            },
            "Graham",
            "Healthcare specialty depth — KOR's BC institutional structural experience compatible with Graham's healthcare delivery model. Position as preferred structural sub on BC Cancer Kamloops + BC Children's Centre for Health Complexity + future RIH Phase 3.",
            new[]
            {
                "Month 1: Email Alex Trifunov + cc Graham BD lead. Reference KOR BC institutional portfolio.",
                "Month 2-3: In-person meeting at Graham Vancouver office.",
                "Month 4-6: Position on pre-construction team for upcoming BC healthcare pursuit.",
                "Month 6-12: Sub-list positioning for D-B response on flagship BC healthcare project.",
            }),

        new StrategicTargetDefinition(
            "EllisDon Corporation (Defense + Healthcare + Commercial Cross-Cutting)",
            "Major GC / Design-Build Prime",
            "Embedded on Esquimalt JNCM $165M + CFB Cold Lake FFCP + TELUS Ocean Vancouver + multiple BC healthcare pursuits. Strategic canonical (22257). 7 named BC contacts already captured in IntelPersonAffiliation.",
            new[]
            {
                "Daniel Murphy — VP Preconstruction Services (dmurphy@ellisdon.com). Decision-maker for structural sub on BC pursuits.",
                "Keeli Husband — Director Business Development (khusband@ellisdon.com). Front-door for pursuit conversations.",
                "Craig Enns — SVP and Area Manager British Columbia (cenns@ellisdon.com). Senior sponsorship.",
                "Candace MacDonald — Director Special Projects BC (cmacdonald@ellisdon.com). UHNBC Acute Care Tower channel.",
                "David McFarlane — COO and EVP Construction Western Canada (dmcfarlane@ellisdon.com). Western Canada gate including Alberta.",
            },
            "EllisDon",
            "EllisDon holds the design prime on Esquimalt JNCM; structural sub-consultant slot was not publicly named (Gap 3 in EllisDon brief). KOR's BC institutional + Esquimalt proximity is direct pitch. Cross-sector positioning: defense + healthcare + commercial all flow through same BC office relationships.",
            new[]
            {
                "Month 1: Email Keeli Husband + Daniel Murphy. Reference Esquimalt JNCM structural slot.",
                "Month 2: In-person at EllisDon Vancouver or Victoria office.",
                "Month 3-6: Pre-qualified status on EllisDon BC structural sub list.",
                "Month 6-12: Targeted pursuit on TELUS Ocean follow-on, UHNBC Phase 3, or Alberta health pipeline (NACIC/SACIC).",
            }),

        new StrategicTargetDefinition(
            "MST Development Corp (Musqueam-Squamish-Tsleil-Waututh) — Indigenous Lower Mainland Anchor",
            "Indigenous Joint Venture Developer",
            "Single highest-leverage Indigenous BD relationship. Controls Jericho Lands (MPI 6911), Maplewood Innovation District (MPI 4882), Tsleil-Waututh North Shore Innovation District (MPI 6913). Billion-dollar multi-tower phased developments.",
            new[]
            {
                "MST Development Corp — Vancouver office. Identify Principal-in-Charge via LinkedIn + Vancouver BoT.",
                "Coordinate intro via Squamish Nation Council relationships.",
                "AHMA (Aboriginal Housing Management Association) — common ground for Indigenous Housing Fund context.",
            },
            "MST",
            "Indigenous pursuit play is 1-3 year relationship-building, NOT cold outreach. MST controls multiple billion-dollar phased Lower Mainland developments. One MST relationship = compounding across Phase 1-N.",
            new[]
            {
                "Month 1-3: Warm-intro via shared past project (Indigenous Housing Fund channel via AHMA).",
                "Month 4-6: In-person meeting + presentation of KOR Indigenous portfolio (if any).",
                "Month 7-12: Position as preferred structural for Jericho Phase 1 or Maplewood Phase 1.",
                "Year 2-3: Compounding pursuit across Phase 2-N as developments mature.",
            }),

        new StrategicTargetDefinition(
            "HCMA Architecture + Design (BC Recreational Dominant Architect)",
            "Architect (Prime Consultant on BC Rec)",
            "29 affiliations in IntelPersonAffiliation — second-most referenced architect in KOR's pipeline. Dominant BC rec specialist. On Newton Community Centre, Britannia, Cameron, most major BC rec projects.",
            new[]
            {
                "HCMA Vancouver office — Principal-in-Charge on Newton or Britannia. Identify via firm website / LinkedIn.",
                "HCMA BD lead — pursue cold via LinkedIn + email.",
            },
            "HCMA",
            "Single Principal-in-Charge relationship at HCMA Vancouver = recurring KOR positioning across BC recreational. Long-span specialty (aquatic centres, ice arenas, field houses) is direct KOR competitive fit + HCMA's primary typology.",
            new[]
            {
                "Month 1: Cold email to HCMA Principal + BD lead. Reference KOR long-span credentials.",
                "Month 2-3: In-person at HCMA Vancouver studio.",
                "Month 4-6: Pre-construction collaboration on next BC rec pursuit.",
                "Month 6-12: Sub-list standing on HCMA-led BC rec RFPs (Newton, Britannia, future projects).",
            }),

        new StrategicTargetDefinition(
            "TELUS Living (Manasweeta Bhatia Program) — National Real Estate Transformation",
            "Major Tenant / Developer (Real Estate Repurposing)",
            "40 sites under evaluation, 20 in planning/construction. Repurposing 2,300+ institutional properties. TELUS Ocean Vancouver was the model. Single Vancouver HQ relationship unlocks national pipeline.",
            new[]
            {
                "Manasweeta Bhatia — TELUS Real Estate program lead. Vancouver HQ.",
                "TELUS Vancouver HQ Real Estate Operations — coordinate intro via TELUS Living's D-B prime contractors (EllisDon overlap).",
            },
            "TELUS Living",
            "Mass timber + concrete hybrid (TELUS Ocean model) = KOR's emerging differentiator. TELUS Living national scale = multi-billion compounding pipeline if KOR positions as preferred structural for the transformation program.",
            new[]
            {
                "Month 1-2: Cold outreach to Manasweeta Bhatia via LinkedIn referencing TELUS Ocean structural quality.",
                "Month 3-4: In-person meeting + KOR's mass-timber+concrete hybrid case study presentation.",
                "Month 5-8: Pre-construction collaboration on first TELUS Living project where KOR positions as structural.",
                "Month 9-12: National pipeline positioning (Calgary, Edmonton, Toronto TELUS sites).",
            }),

        new StrategicTargetDefinition(
            "Sharon Petty — VCH Director Real Estate Operations (VGH + Future BC Healthcare)",
            "Owner-side Decision Maker",
            "VGH Building 1 Redevelopment (MPI 6494) + future VCH pipeline. Ex-CPO at Richmond Hospital (long history with BC healthcare procurement).",
            new[]
            {
                "Sharon Petty — Director, Real Estate Operations, Vancouver Coastal Health.",
                "LinkedIn: linkedin.com/in/sharon-petty-6491a911b/",
                "Channel: VCH Real Estate + Procurement office.",
            },
            "Sharon Petty / VCH",
            "Owner-side relationship = direct entry into VCH procurement decisions before RFP issues. Sharon's ex-Richmond background + VGH current role = compounding across BC healthcare planning + Capital pipeline.",
            new[]
            {
                "Month 1: LinkedIn connection + intro re VGH Building 1 redevelopment.",
                "Month 2-3: In-person meeting at VCH offices.",
                "Month 4-8: Position on VGH Building 1 pre-procurement consultant shortlist.",
                "Month 9-12: Sub-list positioning on next VCH Capital pursuit.",
            }),

        new StrategicTargetDefinition(
            "Jessie Gresley-Jones — VSB ($850M Seismic Schools Program)",
            "Owner-side Decision Maker (Procurement)",
            "VPO pre-qualification = single action that unlocks 30 VSB seismic schools ($850M program). Currently honed PURSUE for Olympic Village Elementary (MPI 4436, PURSUE_URGENT).",
            new[]
            {
                "Jessie Gresley-Jones — VSB Vancouver School Board, jgresley-jones@vsb.bc.ca",
                "Channel: VSB Facilities + Procurement office.",
            },
            "Jessie Gresley-Jones / VSB",
            "30 seismic schools in pipeline. One VPO pre-qualification = recurring KOR positioning across $850M program. Schools specialty for KOR builds on existing BC institutional portfolio.",
            new[]
            {
                "Month 1: Email Jessie re VPO pre-qual process for VSB seismic program.",
                "Month 2: Submit VPO documentation.",
                "Month 3-4: In-person VSB Facilities meeting.",
                "Month 5-12: Position on 3+ VSB seismic school structural RFPs.",
            }),

        new StrategicTargetDefinition(
            "Brian Jonker — SD62 Sooke (North Langford Secondary $220M + SD62 Capital)",
            "Owner-side Decision Maker",
            "SD62 capital plan in active design procurement. North Langford Secondary $220M = immediate action item.",
            new[]
            {
                "Brian Jonker — SD62 contact, 250-474-9800",
                "Channel: SD62 Facilities + Capital Projects office.",
            },
            "Brian Jonker / SD62",
            "Vancouver Island position = KOR Nanaimo office competitive advantage. SD62 multi-year capital = compounding schools pursuit on Vancouver Island.",
            new[]
            {
                "Month 1: Call Brian directly re North Langford Secondary design RFP timing.",
                "Month 2: Travel to SD62 office for in-person meeting.",
                "Month 3-6: Submit on North Langford Secondary structural RFP.",
                "Month 6-12: Position on remaining SD62 capital pipeline.",
            }),

        new StrategicTargetDefinition(
            "KOR Memory Residential Clients (Wesgroup / Bosa / Reliance / Anthem / Cressey / Onni / Polygon / Concord)",
            "Existing Developer Clients (BMZ Legacy)",
            "Memory-confirmed KOR clients with 30+ years of BC residential relationships (BMZ pre-2021 legacy). Each represents multi-decade compounding pipeline.",
            new[]
            {
                "Wesgroup — VP Development + in-house construction lead. Vancouver HQ.",
                "Bosa Properties — VP Development. Vancouver HQ.",
                "Reliance Properties — VP Development. Vancouver HQ.",
                "Anthem Properties — already on Anthem Park MPI 2548 (CDA + KOR aligned).",
                "Cressey Development, Onni Group (in-house construction), Polygon Homes, Concord Pacific — each warrants dedicated relationship audit.",
                "BMZ-era references should be audited per developer before outreach.",
            },
            "Wesgroup|Bosa|Reliance|Anthem|Cressey|Onni|Polygon|Concord",
            "Existing relationship leverage. BMZ legacy gives KOR 30+ year history. New developments via these clients = highest-probability close given existing relationship + sub-list standing.",
            new[]
            {
                "Month 1: Audit KOR portfolio for past projects with each developer (BMZ legacy included).",
                "Month 2: Email VP Development at each with relationship-refresh narrative + current capability.",
                "Month 3-6: Position as preferred structural on each developer's next 2-3 launches.",
                "Month 7-12: Sustained presence at industry events (UDI Awards, NAIOP BC).",
            }),

        new StrategicTargetDefinition(
            "Ryder Architecture (UBC Lower Mall Student Housing $144M)",
            "Architect (Prime Consultant)",
            "Sonnet honing flagged as immediate-action target for UBC Lower Mall Student Housing (MPI 6880). Mass timber academic specialty + UBC institutional portfolio.",
            new[]
            {
                "Ryder Architecture Vancouver office — Principal-in-Charge on UBC Lower Mall.",
                "Coordinate intro via UBC Project Services Office.",
            },
            "Ryder Architecture",
            "Mass timber + concrete hybrid student housing (Brock Commons model) = KOR direct specialty. UBC Lower Mall Phase 1 of multi-phase student housing initiative — sub-list positioning = recurring pursuit.",
            new[]
            {
                "Month 1: Cold email Ryder Vancouver Principal. Reference UBC Brock Commons mass timber precedent.",
                "Month 2-3: In-person meeting.",
                "Month 4-6: Position on UBC Lower Mall Phase 1 structural sub list.",
                "Month 7-12: Pursue Phase 2+ when announced.",
            }),
    };
}
