# Org Identity Contract (canonical-org naming for all BD drains)

Every drain or research agent that emits an **organization** (as a record, or
as a named role on a project — proponent / architect / structuralEngineer /
generalContractor) MUST follow this. It exists because free-form org names mint
duplicate canonicals that later need manual dedup. The resolver
(`CanonicalOrgResolver`) was hardened to stop most of this, but it can only match
on what you emit — garbage in still fragments the graph.

## 1. ALWAYS emit `website` (load-bearing)
The registrable web domain is the org's identity key. As of migration 271 +
commit 47f34344, the resolver matches incoming orgs on **domain** (`formline.ca`)
*before* falling back to name — so name variants that share a domain
("Formline Architecture" vs "Formline Architecture + Urbanism") resolve to the
same canonical automatically. **No website ⇒ the domain key can't fire and the
org may be minted as a duplicate.**

- Emit the firm's real site, e.g. `https://formline.ca`. Any form is fine
  (scheme/`www`/path are stripped) — the host is what matters.
- If you genuinely can't find a site, set `website: null` **and** drop
  `confidence` ≤ 0.4 so it's flagged, never guessed.
- Do NOT emit shared-host/builder domains (squarespace.com, wixsite.com,
  wordpress.com, weebly.com, business.site, godaddysites.com) as the identity —
  they're denylisted from domain-match. Find the firm's own domain or leave null.

## 2. Name each org by its plainest canonical form
- Use the firm's common operating name. **Do not append** descriptors
  (`+ Urbanism`, `Architects`, `Engineers`, `Inc./Ltd./LLP/LP/Corp`),
  parentheticals (`(Read Jones Christoffersen)`), or project tags
  (`— SD39 Project`, `- Broadway Plan`).
- **Spell it out — no acronyms** when the full name is known
  (`mcfarlane biggar architects`, not `OMB`; `Read Jones Christoffersen`, not
  `RJC`). If you only have the acronym, put the full name in `notes` so it can be
  reconciled.
- Prefer the name as it appears on the firm's own website masthead over how a
  third party (news, a project page) refers to it.

## 3. One legal entity per field
Never emit `Firm A / Firm B` or `JV of X and Y` as one org — split into the
constituent firms in their typed role fields, unless it is itself a *named* JV
entity with its own website (e.g. `Sen̓áḵw Development Partnership`).

## Why
Domain + canonical name = the resolver attaches your record to the existing org
instead of minting a variant the dedup tool must later merge by hand. The
recurring "every drain needs a dedup cleanup" tax dies here — but only if every
org carries a real `website`.
