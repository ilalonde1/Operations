# Codex response — step 83b review (2026-09-16 09:15), and what was done with it

**Codex's answer**: a 30 in wall drawn as an open U (0,0)→(0,500)→(30,500)→(30,0) with its centreline (15,0)→(15,500)
on the same wall-role layer loses its pairing: the centreline is parallel, on the partner's side, 15 ≥ WallFloor 6,
15 < 30 − 6, overlapping 500 — `AFaceLiesBetween` returns true; the classifier hands it in as a drawn face (lines
895–904); the existing 9 in test escapes only because its centreline is under WallFloor from either face. Proposed:
an intervening segment must have wall-boundary provenance; the (A, B) tuples lack it.

**Confirmed and adopted — step 95**: the provenance is the chain a face belongs to. The classifier reads no outline
from a chain under four points; a lone line — a centreline, a hatch stroke, a dimension on the wall layer — is a
two-point chain. `WallOutlineDecomposer.OutlineFaces(chains, loops)` selects the faces a pairing may see as a nearer
partner: the edges of chains of four points or more, and of loops; the classifier now hands the decomposer that
selection. Test `ALoneLineBetweenTwoFacesIsNotANearerPartner` at Codex's geometry: the U pairs at 30 in with the
selection, and is refused when the centreline is handed in directly (the reason the selection exists). Fast suite,
six-set gate and the DXF-route ratchets (`ModelCoverageTests`, `ModelIntegrityTests`): 1,483 green, 31168 and 31138
unchanged.
