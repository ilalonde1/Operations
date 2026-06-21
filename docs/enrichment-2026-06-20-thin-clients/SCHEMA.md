# Thin-client deep-enrichment — output schema + guardrails (2026-06-20)

These are **real KOR clients** (Deltek-linked) that have **zero contacts and zero MPI roles** in the graph,
so their org-brief PDFs come out barren. Goal: research each one as deeply as a primary record.

## HARD RULES (no guessing)
- **Identity first.** Many BC firm names collide with US/other-province companies. KOR is a **Vancouver / Lower Mainland (BC)** structural firm; almost every client here is BC (a few SoCal). Confirm you are looking at the **right company** (BC location, real-estate/construction sector) before recording anything. If you cannot confirm identity, record `"identityConfirmed": false` and leave people empty.
- **Never fabricate an email.** Only set `email` if you actually found it (company site, RFP, news, public registry) — set `emailSource:"website"` (or `"news"`/`"asis"`). If you only pattern-guessed it (e.g. first.last@domain), set `emailSource:"PatternInferred"` and `emailConfidence` <= 40. No email found → `email:null`.
- **No invented people.** A person goes in only with a real name tied to this company.
- Record what you find; record nothing when you find nothing. Empty is a valid, honest result.

## Output (one JSON file, ASCII)
Write to the path your prompt names. Shape:
```json
{
  "orgs": [
    {
      "orgId": 20962,
      "displayName": "Mondiale Development",
      "identityConfirmed": true,
      "website": "https://...",            // fill ONLY if currently missing/confirmed
      "profileNote": "1-2 sentence factual profile: what they build, where, scale. <= 380 chars.",
      "bcRegistryLegalName": null,           // optional, if found
      "notableProjects": ["Name - City - status", "..."],   // optional context, factual
      "people": [
        { "name": "Jane Doe", "title": "President", "email": "jane@x.com",
          "emailSource": "website", "emailConfidence": 90,
          "note": "Founder; quoted in 2025 Storeys article re: Burnaby tower. <=380 chars." }
      ]
    }
  ]
}
```
- `emailSource` allowed: `website`, `news`, `asis`, `Hunter`, `PatternInferred`.
- Keep notes factual and sourced. ASCII only.
