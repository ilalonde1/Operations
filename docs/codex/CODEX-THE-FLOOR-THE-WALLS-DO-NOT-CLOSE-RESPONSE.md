# Response — the floor the walls will not close

The sheet scope is a limitation, but changing the gate does not establish a safe floor boundary. I would not implement a new recovery rule from this evidence.

## 1. The sheet gate and the storey decision

The two sheet-level gates are `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:1705` and `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:1745`. However, the premise needs one correction: an earlier path already examines wall enclosure when slabs exist. If every slab is smaller than that enclosure, its centroid lies inside, and column coverage passes, it clears those slabs so the fallback can supply the wall boundary. That comparison still sees only one sheet's walls and columns (`StructuralPlanClassifier.cs:1674`).

The natural storey-level insertion point is `SettleFloorsAcrossSheets`, replacing its immediate skip when `plates.Count == 0` with a recovery attempt (`Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:3007`). It already groups sheets by storey (`DxfToEtabsService.cs:3005`); upstream, geometry has been transformed into the model frame (`DxfToEtabsService.cs:1721`) and copied separately for each storey (`DxfToEtabsService.cs:1924`).

Moving responsibility there requires pooled walls and columns, classification thresholds converted to model units, foundation eligibility, and knowledge of floors already in the destination model. Its current signature lacks the classification options and destination document (`DxfToEtabsService.cs:2994`); the composer explicitly counts existing floors (`Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:609`). Sheet fallback candidates would also need to be deferred so another sheet's legitimate floor can suppress them. The existing foundation exclusion must survive the move (`StructuralPlanClassifier.cs:1755`).

This would address missing plates only. A storey-level zero-plate test still misses storeys holding a small wrong plate. Replacing that plate requires separate evidence that it is wrong; changing the scope of `Count == 0` supplies none. Also, one sheet having a small slab does not imply that every other sheet has a slab: the present gates inspect each sheet independently.

## 2. What step 142 establishes

The reported result establishes that pooling was insufficient, not that the drawings lack a perimeter.

The detector does not require an ordered, connected vector chain. It paints rectangular wall footprints (`Kor.Operations.EngineeringTools.Core/Dxf/DxfFloodFillPlateDetector.cs:49`), dilates them across the permitted gaps, floods from outside (`DxfFloodFillPlateDetector.cs:96`), and extracts the largest remaining component (`DxfFloodFillPlateDetector.cs:126`). It requires an enclosure after that raster operation. Merely adding more disconnected panels cannot guarantee one.

Crucially, `null` is not a diagnosis of an open perimeter. Size and raster limits (`DxfFloodFillPlateDetector.cs:63`), boundary extraction and simplification (`DxfFloodFillPlateDetector.cs:126`), minimum plate area, and the final wall-area comparison (`DxfFloodFillPlateDetector.cs:154`) can all reject the result. That last comparison uses the summed area of all supplied walls, so overlapping or duplicated walls can increase the rejection threshold without adding equivalent painted area. This is a possible failure mechanism, not a demonstrated explanation of step 142.

To distinguish algorithm failure from missing drawing evidence, the failed cases need their exact rejection reason, transformed wall footprints overlaid on the source drawings, and an engineer-identified intended perimeter. The supplied observation that all 42 returned null cannot resolve that distinction. The permitted source files cannot establish whether the original drawings contain perimeter linework that extraction or classification discarded.

## 3. An outer edge without a closed ring

Wall-footprint boundaries are reachable without enclosure; a floor boundary is not uniquely determined by them.

The code already constructs each wall's footprint (`DxfFloodFillPlateDetector.cs:54`). Taking their geometric union and tracing its boundary would produce disconnected wall strips or, for a U-shaped arrangement, a U-shaped strip whose boundary returns along the inner faces. It would not produce the floor spanning the U. Connecting those fragments with a hull or extrapolated edges introduces unsupported boundary segments and can fill setbacks, courtyards, or space between buildings.

A vector boundary implementation could live beside `EnclosedByWallPanels`, with eligibility decided in the storey pass. It could avoid raster approximation where the wall footprints actually enclose a region. It cannot safely infer missing building faces merely by following outer wall edges: the ruling identifies which evidenced edge to use, but does not identify how absent edges should connect.

I would therefore retain the missing plate and use the brief's ModelQuestion J8 for ambiguous cases. I did not inspect the questionnaire implementation, which is outside the permitted files. Any future rule needs boundary evidence and measurement across all 48 comparable sets, with 30972-01 and 30993-01 unchanged. This agrees with the composer's explicit decision to report an absent floor rather than invent one (`E2kGeometryComposer.cs:1896`).

No production source files changed; no builds or tests run. This response file was saved at the user's subsequent request.
