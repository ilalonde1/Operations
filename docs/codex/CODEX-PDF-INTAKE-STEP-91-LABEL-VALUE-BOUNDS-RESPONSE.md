# Codex response — step 91 review (2026-09-16 08:40), and what was done with it

**Codex's answer** (verbatim in substance): yes — on a two-column block with SHEET TITLE (MinX 900–1000, Cy 200) and
CHECKED BY (1040–1100, Cy 200) on one line, FOUNDATION PLAN (900–1020, Cy 180) under the left column and SCALE
(1060–1100, Cy 140), CHECKED BY is right-aligned (line 159), left = −∞ (160), right = 1300, floor = 140 from
SCALE; SCALE is on the floor and does not narrow `right`, SHEET TITLE is to the left; both title tokens pass the
value filter and CHECKED BY = "FOUNDATION PLAN". No left-column exclusion stops it.

**Confirmed** by `ARightAlignedLabelsColumnStartsWhereTheLabelBeforeItEnds` at Codex's tokens (page width 1100 so
the tokens lie in the right fifth): red before the rule (CHECKED BY = "PLAN" — the reader's own region cut the
rest), green after. **Step 93** (`TitleBlockFields.Read`): a right-aligned label's column starts where the label
before it on its line ends; from the strip's left only when it stands alone on its line (30985's [ T I T L E ]).
30985 and 50026 still read their titles exactly. 1,458 green, six byte-identical.
