# Writing BD knowledge claims

A claim is one sentence in plain words: standalone, actionable or testable by a
Director of BD at a sixty-person structural engineering consultancy. Write only
what one cited passage supports. Do not add advice merely because it sounds useful.
the passage is the citation; a claim the passage does not support is a defect.

| Kind | Meaning | Illustrative example (not extracted from a book) |
|---|---|---|
| Rule | An instruction or condition to follow | Confirm the buyer's decision criteria before preparing a proposal. |
| Fact | A testable assertion | A proposal consumes staff time before the work is awarded. |
| Definition | The meaning of a term | A referral is an introduction made by someone who knows both parties. |
| Heuristic | A useful guide with exceptions | A short qualification conversation usually saves proposal effort. |

Confidence measures how directly the passage states the claim, not whether the
claim is universally true: a quotation is 1.0; a faithful paraphrase is high; an
inference is lower. Use a number from 0 to 1. Use your own concise wording where
possible. Front matter, praise, contents pages, exercises and anecdotes without
an assertion yield no claim: leave their `claims` array empty. Do not turn every
passage into a claim or count the same assertion twice in one passage.

Give each claim **one to three distinct concepts**. Reuse `knownConcepts` before
inventing a slug. A slug is normalized to lower-case ASCII letters, digits and
hyphens, 3–80 characters (`Qualify Before Investing!` becomes
`qualify-before-investing`). Give it a short name, 1–150 characters. The first
accepted name stored for a slug is retained; importing a new name never renames it.

Export one book with `export 18 --to <folder-outside-repo>`. Fill the same document
and save it as `<sourceId>-<title>.claims.json` beside the books, outside git:

```json
{
  "sourceId": 18, "title": "Example book", "author": "Example author",
  "exportedAtUtc": "2026-10-14T00:00:00Z", "writtenBy": "Claude session / writer name",
  "knownConcepts": [{ "slug": "qualify-before-investing", "name": "Qualify before investing" }],
  "passages": [{ "id": 8402, "ordinal": 1, "ref": "Example reference", "claims": [
    { "kind": "Rule", "statement": "Confirm the buyer's need before preparing a proposal.",
      "confidence": 0.9,
      "concepts": [{ "slug": "qualify-before-investing", "name": "Qualify before investing" }] }
  ] }]
}
```

The example is synthetic. Keep the exported source ID and passage IDs/ordinals;
they must match the store. `text` may be omitted on import. `writtenBy` is required
(1–100 characters) and becomes the claim writer and passage-stamp attribution;
the CLI's `--by` (default Windows username) identifies the importer. Claims require
1–1,000 characters with no line breaks; punctuation/abbreviations are not counted.
Mechanical validation cannot establish support, relevance or sentence meaning.

Run `claims import <file.json|folder>`; folders process `*.claims.json` in name
order and report every failure/rejection. Listed passages are stamped even when
empty or when all their claims are rejected. Invalid passage identities/ordinals,
duplicate passages or missing claims arrays refuse the entire file. Omitted
passages are not stamped. A previously stamped listed passage refuses import
unless `--force`. Force retires all active, unreviewed model-linked claims for that
source, including those on omitted passages: use a complete book file for replacement.
Reviewed, already retired, human-linked and unlinked claims remain intact. Fixing
rejected claims on a stamped passage therefore also needs force. Nothing is deleted.

Extraction happens in Claude's own session between export and import. Neither
verb calls a model. Ian reviews the cited claims in the Director's Knowledge tab:
Keep records review; Retire requires a 1–200 character reason and records review.
