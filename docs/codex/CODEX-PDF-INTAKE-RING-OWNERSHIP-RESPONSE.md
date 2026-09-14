# Ring ownership: topology prototype, not integrated

Implemented against the working tree at `0f600803`. Only the new class, its new fixture class and this note were written for this task. No build, test, drawing read, database access or network access was performed. The implementation has been read back; compilation, fixture results and real-drawing acceptance remain unverified.

**Ownership rule:** In the snapped, planarized graph, each directed half-edge bounds the face on its left, so an undirected edge shared by two bounded faces belongs to both, while edges with no bounded side remain open geometry.

This confirms bounded faces as the ownership primitive. It refutes two stronger claims: that bounded faces alone replace structural outlines, and that existing consumers universally treat their input as a set. An outer slab containing a wall band is not itself an atomic face. Producing all its cells does not automatically produce that slab.

## 1. Nodes and an embedding independent of arrival

`PlanarRings` takes the existing constructor parameters and defaults: join `0.05`, bridge `6`, extension `48`. `Build` returns `Result(Loops, OpenChains)`. It is not referenced by existing production code.

The arrangement first deduplicates exact undirected spans, finds proper crossings, splits at endpoint contacts and collinear overlap endpoints, then forms the transitive closure of sample pairs within the join tolerance. All distance acceptance uses `LoopGeometry.Within/Beyond`, including their rounding to six decimal places. Unique sample coordinates each contribute once to a cluster centroid; repeated identical segments do not reweight a corner. A spatial bucket only finds potential neighbours: every qualifying neighbour participates, with no nearest-node or first-match choice.

The partition is therefore determined by the proximity relation, rather than an insertion traversal. Its centroid translates with the samples. It is not an absolute-coordinate priority that chooses a winning segment when the page moves. Decimal accumulation prevents the sum itself from depending on the order of these finite samples. Floating-point intersection construction and distance tests still have numerical limits; this is not an exact-arithmetic proof for every representable drawing.

The deliberate cost of transitive closure is visible in a fixture: endpoints at `0`, `0.04`, `0.08` form one cluster at join `0.05`, although its diameter is `0.08`. A diameter cap prevents that collapse but does not define a unique partition: the middle endpoint could join either outer endpoint. Greedy capped union would restore arrival dependence. This prototype accepts transitive collapse; it does not silently choose a capped partition. A different policy would need an explicit ambiguity rule, such as rejecting the whole offending component.

Node IDs only identify incidences. Splits are ordered by parameter **along their own segment**, as required to construct adjacent atomic edges; they are not globally sorted by X/Y. Equal parameters for distinct snapped nodes are rejected. Angular ordering defines each vertex's cyclic adjacency. Containment uses area, with equal enclosing choices rejected. The only label ordering is ordinal layer text, solely to give mixed-layer input a stable output label; it is not a structural-role decision. Callers must retain the current family/role separation.

Snapping can create a crossing that was absent before snapping. The prototype checks for unresolved contacts/crossings and throws instead of walking a nonplanar embedding. Coincident outgoing rays are also rejected. Coordinates must be finite and within `+/-1e9` drawing units to bound decimal geometric calculations. This is an additional explicit input restriction, not a claim that every input accepted by the old builder is accepted here.

## 2. Faces, exterior boundaries and open geometry

Each non-cut edge gets two half-edges. At the destination of an incoming half-edge, the clockwise predecessor of its reverse continues the face on the left. Seeds affect only list order and the starting vertex of a ring. The successor map is walked in full; repeated articulation vertices split a boundary walk into simple cycles.

Positive cycles are outer boundaries. Simply dropping every negative cycle would lose holes: the clockwise outside boundary of a disconnected inner component bounds a hole in an enclosing face. `Regions` assigns that boundary to the unique smallest enclosing positive face of another component. Negative boundaries without an enclosing face are exterior and omitted. `Result.Faces` exposes `Face(Outer, Holes)`. The compatibility property `Loops` exposes each face's outer boundary; it cannot encode holes. A consumer that uses only `Loops` must not assume its areas are filled surface areas.

Iterative degree-one pruning is insufficient: a bridge between two closed squares has no degree-one endpoint. Iterative Tarjan bridge detection removes **all graph cut edges** from face enumeration. They become maximal open paths, stopping at branches or attachment to cyclic geometry. There is no straightest-continuation choice at an open branch. The fixture includes both a dead end and a link between cycles.

The old `BridgeChains` cannot become order independent merely by moving it earlier: it still searches ordered chain pairs and accepts the first merge. The prototype replaces it with one simultaneous geometric proposal batch before face enumeration:

- Consider degree-one tips in the arranged graph. Prefer a forward ray intersection within the extension bound; otherwise consider a direct link within the bridge bound.
- Reject proposals conflicting with existing edges. Rank the remaining proposals by total added length rounded to six decimals. Accept only a mutually unique minimum for both tips. Equal best choices remain open.
- If accepted proposals conflict with one another, reject both; no proposal wins by being processed first. Rebuild the arrangement with the remaining synthetic spans, then enumerate faces.

This does not reproduce the old repeated chain-merging heuristic. It does not extend a branch attachment or invent a tip on a closed ring. These conservative differences can leave more open geometry and need explanation in the gate. A returned boundary using a synthetic span has `ClosedExactly == false`; tolerance joins without inserted spans remain exact in the same practical sense as ordinary joined linework.

Simplification first evaluates each cyclic vertex simultaneously in a local frame using `Beyond`, then calls the existing `LoopGeometry.Simplify` for exact cleanup. Calling its sequential near-point pass directly with the join tolerance would let the initial ring vertex affect the result. Simplification retains original snapped coordinates rather than reconstructing them from a page-dependent origin.

## 3. Recovering structural surfaces after the straightest walk

Face adjacency replaces `PickContinuation` for topology. **An explicit choice of filled cells replaces it for a structural surface.** `Result.RecoverSurfaces(isWallBand, isSlabCell)` keeps the wall selection and slab selection independent. For the slab, it removes each edge whose two incident cells are selected, walks the surviving directed boundary, and returns outer boundaries with holes. Cells can serve both a wall interpretation and a slab interpretation.

The proposed rule “union only the remaining non-band cells across non-band edges” cannot recover the requested crossing-band fixture. In the `30 x 20` rectangle, the band between `x=14` and `x=16` separates the left and right cells. Excluding it yields two slabs of area `280`, with a slot between them. Keeping its ribbon separately **and filling all three cells for the slab** yields one slab of area `600` and a wall ribbon of area `40`. Both interpretations are asserted, so the test cannot pass merely by discarding the band.

`IsRectangularBand` implements a narrow geometric predicate: a hole-free rectangle, opposite sides agreeing within join tolerance, right angles within the same length tolerance, and its short dimension between the caller's thickness floor and cap. The default minimum aspect is `2`. Thickness has no hard-coded structural default and remains in drawing units. This is not a general wall classifier: compound ribbons, annular walls, intersections that split a ribbon, and short wall/column ambiguity still require the existing role and thickness evidence. The callback API permits that evidence; the class does not invent it.

A nested rectangle is likewise not sufficient evidence of an opening. The topology includes both the annular face and the inner face. Selecting just the annulus preserves a hole; selecting both fills it. Blindly filling all bounded cells on a page would erase openings and could unite neighbouring structural surfaces. Callers must select cells per intended surface, preserving family boundaries, opening evidence and the existing restrictions on slab recovery.

### Consumer findings in the requested excerpts

| Consumer | Observed handling of order | Required interpretation/change at integration |
|---|---|---|
| Classifier wall/column outline reader, lines 540–548 | Iterates wall then column and appends the loops returned by `CloseByFamilyThenPooled`. No local first-ring selection, but preserves their order in a list. | Expose role-appropriate outlines. Walls need ribbons; a small column rectangle divided by a diagonal is two faces and needs a justified column union before rectangle recognition. |
| `MemberPoints`, lines 559–564 | Projects the ordered `Classify` result: column centres then wall endpoints. No local competition, but no order normalization either. | Cannot establish downstream model independence from this projection alone. |
| Main classifier, lines 612–720 | Accumulates lists; distinct roles follow source encounter order. Wall/column and slab builders use different bridge bounds. Family closure precedes pooling the remaining open geometry. Slab rescue and borrowed closure depend on the resulting open chains. | Preserve family-first closure and existing evidence restrictions. Carry faces and hole relationships through slab selection; replacing only the constructor leaves the recovery problem unsolved. The requested excerpt does not establish downstream set semantics. |
| Partition reader, lines 1940–1944 | Appends all returned loops, preserving order; no first-winner choice in this local reader. | Partitions are drawn footprints. Internal linework may split a footprint into cells; do not silently interpret every cell as a separate partition. |
| `PairConcentricWallRings`, lines 1964–2012 | Sorts by area rounded to three decimals, retaining input order for ties; nested scans consume the first acceptable pair and then `break`. **An order-sensitive consumer remains.** | Use explicit outer/hole adjacency for annular walls, and adapt it to the decomposer's ribbon representation. Resolve competing structural matches as a set or report ambiguity; do not rely on the enumeration's list order. |
| PDF slab-edge reader, `Core/PdfToSafe/GeometryFilterService.cs`, lines 1430–1500 | Copies loops to a list, filters open chains, rebuilds/bridges them and appends loops, then sorts qualifying floors by descending area. Equal areas retain input order. No final floor-selection behaviour is established by the excerpt. | Recover a selected slab's outer boundary and holes before area/structure qualification. The documented corner-block/perimeter sharing needs cell union: atomic faces alone still leave the large outline split. Preserve chain-length and slab-evidence safeguards. |

`WallOutlineDecomposer` needs a wall ribbon, not every room cell. The existing concentric pairing already constructs a keyhole ribbon from two outlines. `Face.Holes` makes their adjacency explicit but this prototype deliberately does not modify that adapter. Slab readers need surfaces with holes; column readers need justified small outlines. Consequently, the matching `Build` shape is useful for an experimental option, but is not sufficient for a safe semantic drop-in replacement across these consumers.

## 4. Failed walks and consumed edges

For a valid planar embedding, every active half-edge has one successor and belongs to exactly one closed orbit. Visiting one side cannot spend the other side, so there is no failed greedy walk that silently steals a later ring's edges. Cut edges are accounted for separately as open paths; exterior boundaries are deliberately omitted from the returned bounded faces.

“Nothing fails” is a mathematical property of that valid embedding, not a promise about finite-precision input handling. This implementation throws on an open successor, a repeated visit outside the current orbit, a zero-area/degenerate cycle, or leftover path edges. It returns no partial success after such an error. Those diagnostics need investigation if real drawings trigger them; removing the checks would hide the ownership fault again.

## Expected bank changes and remaining acceptance

These are hypotheses from the code and synthetic geometry, not measured changes to the bank:

- A square with a diagonal becomes two triangles; two adjacent squares retain both rings and their shared edge. This is correct topology. Calling the triangles two columns, or calling every large face a thick wall, would be incorrect structural interpretation.
- A slab crossed by linework becomes several atomic faces. Feeding those directly to an area filter can lose the slab. Explicit filled-cell union should recover its perimeter, including the PDF corner-block case, while retaining useful smaller outlines separately. Whether that recovers any particular banked floor has not been measured here.
- A junction can expose wall cells the greedy walk previously consumed, or divide one previous wall outline into multiple cells. Extra/missing walls are acceptable only when the resulting ribbons and members explain the drawing. A face-count increase is not itself evidence of improved walls.
- Concentric boundaries now carry a hole relationship. Counting their outer areas as two filled plates is wrong; feeding only one outer loop to a wall decomposer can lose the retaining wall.
- Centroid joins can move corners, and transitive clusters can collapse narrow features. Isolated exact rings without internal linework should retain their geometry. Ambiguous bridges can remain open where the old first-match search closed something; each resulting loss needs its geometry explained.

The new fixture class contains eleven synthetic cases, including all requested shapes, overlap and crossing splits, touching cycles, nested holes, transitive collapse, a unique extension and ambiguous gap proposals. Every fixture is reversed, endpoint-flipped and shuffled with seeds `7`, `11`, `19`, then translated by `(5000,3000)` and `(5000.37,3000.61)`. One-to-one matching compares rings, holes and chains without relying on output order, winding or ring start, using a stricter `1e-6` point tolerance. Recovery comparisons include band selection, filled unions, band exclusion and holes. Its summary explicitly excludes real drawings, consumer semantics and performance, and names an untested near-degenerate angle-order fault.

The existing reversal differential compares the resulting model through `ModelDiff` and its zero-shift check. It is not a proof over arbitrary permutations, every intermediate ring, or every member property. The new local fixtures complement it; they do not replace either model differential or the six-set gate.

No acceptance commands were run, as instructed. Pairwise segment intersection and snapped-embedding validation are quadratic, and gap proposals examine pairs of tips against existing edges; full-sheet performance is unverified. Before production use, Claude still needs compilation with warnings as errors, the new fixtures, role-aware integration behind the option, reversal and page-shift differentials, and the six-set gate with each actual geometry difference explained. No existing test, baseline, `PlanLoopBuilder`, classifier or PDF reader was edited by this task. Other concurrent workspace changes were left alone.
