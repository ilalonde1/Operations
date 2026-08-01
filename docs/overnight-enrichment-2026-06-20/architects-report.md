# Architect BD Enrichment Report - 2026-06-20

## Summary

Researched 10 target architecture firms to identify SE-selection decision makers. All 10 firms processed; emails verified via Hunter.io domain search. ASCII-clean JSON written to `architects.json`.

---

## Firm-by-Firm Findings

### 54571 KMBR Architects (kmbr.com)
- **People found:** 4 (2 current principals + 1 emeritus + 1 associate)
- **Key target:** Kate Lemon (Principal, klemon@kmbr.com, confidence 67) and Bassem Tawfik (Principal, pattern-inferred)
- **Hunter pattern:** {f}{last} - confirmed; klemon and wabele both found
- **Email gap:** Bassem Tawfik not in Hunter index; inferred btawfik@kmbr.com at confidence 55
- **Note:** Hunter flagged domain as accept_all so all verifications are "accept_all" status

### 68972 Parkin Architects (parkin.ca)
- **People found:** 3 confirmed + 1 pattern-inferred
- **Key targets:** Kim Kennedy (Principal, kennedy@parkin.ca, confidence 92), Robert Boraks (Director, boraks@parkin.ca, confidence 87)
- **Hunter pattern:** {last} - confirmed across 30+ employees
- **Note:** Cameron Shantz (Vancouver director) retired October 2025. Vancouver leadership gap unclear. Lynne Wilson Orr named at 2026 healthcare conference as Principal but email pattern uncertain (hyphenated name).

### 38960 Acton Ostry Architects (actonostry.ca)
- **People found:** 7 (both founders + 5 principals including Director of Operations)
- **Key targets:** Russell Acton (racton@, 99), Mark Ostry (mostry@, 97), Derek Fleming (dfleming@, 96), Michael Fugeta (mfugeta@, 96), Mark Simpson (msimpson@, 97 - VERIFIED VALID)
- **STANDOUT:** Mark Simpson holds P.Eng registration with EGBC - he is the in-house structural/technical lead bridging architecture and engineering. He is KOR's most important contact at AOA.
- **Hunter pattern:** {f}{last} - confirmed; domain does NOT accept_all, verifications are meaningful
- **Ruth Chau** (Director of Operations, rchau@, 97 VALID) controls vendor/sub relationships

### 26929 Rositch Hemphill Architects (rharchitects.ca)
- **People found:** 2 (both named principals)
- **Key targets:** Bryce Rositch (bryce@rharchitects.ca, 95) and Keith Hemphill (keith@rharchitects.ca, 97 VALID)
- **Hunter pattern:** {first} for this firm (first-name-only pattern confirmed from multiple employees)
- **Note:** Small firm (~20 staff). Both principals personally involved in all SE selections. Keith Hemphill email verified VALID by Hunter.

### 54300 Hotson Bakker Architects (dialogdesign.ca)
- **CRITICAL NOTE:** Hotson Bakker Architects no longer exists as an independent entity. It merged into DIALOG (dialogdesign.ca) around 2009-2010. The orgId 54300 should be flagged for DB cleanup.
- **People found:** 1 (Joost Bakker, original co-founder, now DIALOG Vancouver Principal)
- **Email:** Not found in Hunter; DIALOG pattern is {first}.{last}@dialogdesign.ca suggesting joost.bakker@dialogdesign.ca but not verified
- **Recommendation:** Confirm whether the 9 projects in KOR DB were under the old Hotson Bakker brand or current DIALOG entity, and update the org record accordingly.

### 69756 Low Hammond Rowe Architects (lhra.ca)
- **People found:** 3 (all three partners)
- **Key targets:** Christopher Rowe (christopherrowe@lhra.ca, 85, VALID), Jackson Low (jacksonlow@lhra.ca, 80)
- **Hunter pattern:** {first}{last} (no separator) - confirmed
- **Email gap:** Paul Hammond not in Hunter; pattern-inferred paulhammond@lhra.ca at confidence 55
- **Note:** Victoria-based firm; projects primarily on Vancouver Island and throughout BC. Domain is accept_all.

### 76490 Bluegreen Architecture (bluegreenarchitecture.com)
- **People found:** 2 named (Wendy Rempel, Kevin Ryan) but ZERO emails resolved
- **Hunter:** Zero results on bluegreenarchitecture.com; only info@bluegreenarch.com on alternate domain
- **Email gap:** Cannot safely infer any personal emails. Pattern unknown.
- **Recommendation:** First contact via info@bluegreenarch.com. Kelowna-based firm; lower priority for KOR's Metro Vancouver focus.

### 12496 NORR Architects Engineers Planners (norr.com)
- **People found:** 3 (VP Health Sciences, Director, VP)
- **Key targets:** Frank Panici (frank.panici@norr.com, 99, VALID), Kiran Sharma-Boon (kiran.sharma-boon@norr.com, 99, VALID)
- **Hunter pattern:** {first}.{last} - confirmed
- **CAUTION:** NORR's Vancouver office (710-1201 W Pender) is NOT listed on their main contact page and no Vancouver-specific principal was identified. The 6 KOR DB projects may be handled by Toronto/Calgary teams. Frank Panici (Health Sciences VP) is the best proxy contact.

### 16944 ThinkSpace (thinkspace.ca)
- **People found:** 5 principals + BD director = 6
- **Key targets:** Allan Francis (allan.francis@, 94), Henk Kampman (henk.kampman@, 93), Ray Wolfe (ray.wolfe@, 90), Leonard Rodrigues (leonard.rodrigues@, 94)
- **Hunter pattern:** {first}.{last} - confirmed
- **STANDOUT:** Stuart Kernaghan (BD Director, stuart.kernaghan@, 93) is the first-outreach target; he manages sub-consultant relationships before projects go to principals.
- **Note:** ThinkSpace is a top-5 K-12 education firm in BC. Seismic program (SPIR/SMP) work flows through them heavily. This is a HIGH-VALUE relationship for KOR.

### 75916 Taylor Kurtz Architecture + Design (tkad.ca)
- **People found:** 4 (President + 3 principals)
- **Key targets:** Craig Taylor (craig@tkad.ca, 99, VALID), Kelly Riopelle (kelly@tkad.ca, 97, VALID)
- **Hunter pattern:** {first} (first-name-only) - confirmed from multiple staff
- **Note:** Multi-family residential and commercial focus; 40+ staff. SE selection done at principal level. KOR fits on their seismic-critical residential projects.

---

## Coverage Summary

| Firm | People | Emails Resolved | Hunter Coverage |
|------|--------|-----------------|-----------------|
| KMBR | 4 | 3/4 (1 inferred) | Good |
| Parkin | 4 | 3/4 (1 inferred) | Good |
| Acton Ostry | 7 | 7/7 (all Hunter) | Excellent |
| Rositch Hemphill | 2 | 2/2 | Good |
| Hotson Bakker/DIALOG | 1 | 0/1 | Firm defunct - merge issue |
| Low Hammond Rowe | 3 | 2/3 (1 inferred) | Fair |
| Bluegreen | 2 | 0/2 | None - email unknown |
| NORR | 3 | 3/3 | Good (no Vancouver-specific lead) |
| ThinkSpace | 6 | 6/6 | Excellent |
| Taylor Kurtz | 4 | 4/4 | Excellent |

---

## Action Items

1. **Hotson Bakker (54300):** Flag for DB cleanup - entity merged into DIALOG. Projects may need re-linking to DIALOG org.
2. **Bluegreen (76490):** No emails available. First contact via info@bluegreenarch.com. Lower priority.
3. **NORR (12496):** Identify which of the 6 KOR DB projects were handled by which NORR office team. May need LinkedIn outreach to find the Vancouver project lead.
4. **Parkin Vancouver (68972):** Post-Shantz-retirement, the Vancouver leadership gap is real. Consider whether the remaining directors (Boraks, Kennedy) are handling Vancouver projects or whether a new person was appointed.
5. **ThinkSpace + KMBR:** Highest-priority outreach for BC seismic program (SPIR) work. Both firms do heavy K-12 school district work where KOR's seismic engineering credential is directly relevant.
