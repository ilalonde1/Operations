# Codex response — step 89 review (2026-09-16 08:50), and what was done with it

**Codex's answer**: "10.5" used as a decimal dimension is accepted (the point splits it into two numeric parts);
placed on a GRID layer exactly at the end of a vertical grid-layer line it becomes a false axis named 10.5. Proposed:
refuse a dotted name whose two parts are both digits — while noting it "also rejects legitimate numeric dotted grid
names, if those occur; text alone cannot distinguish them from decimal dimensions."

**Decision: NOT adopted — the limit is stated instead.** They do occur, measured (`corpus-query grid-names`): 31040
names sub-grids 2.1, 2.2, 2.3, 2.4 (beside 2.A, 2.B, 2.C), 31152 1.1, 1.2, 2.2, 2.3, 30997 3.0, 4.0, 5.0, 31009 15.2,
6.2. The proposed condition would refuse every one of them to guard against a text neither route produces: on the
PDF route the GRID layer's TEXT is written by `DxfExporter` from the bubble reader's named axes alone (one name at
each end of each axis line — a dimension never reaches it); on the DXF route the GRID layer's text is Revit's grid
names. The assertion `IsGridName("10.5") == IsGridName("2.1") == true` now stands in the test with this reason, so
the next reader finds the decision, not a gap. Section 105.
