# PDF page walk cache

Implemented in the repository working tree inspected at `bbfe27e4`. The prescribed source reads were performed in order, followed by caller searches and the local source needed to trace those callers. No build, test, drawing read, database access or network access was performed. Code was read back and reviewed; compilation, real-PDF equivalence and timing remain for Claude's acceptance runs.

## Changes

| File | Members and behaviour |
|---|---|
| `Kor.Operations.EngineeringTools.Core/VectorPageReader.cs` | Added `RawSubpath`, `RawPage`, `Walk(Page, curveSegments)` and `Derive(...)`. Existing `ReadPage` signatures remain and delegate to `Derive(Walk(...))`. |
| `Kor.Operations.EngineeringTools.Core/PdfToSafe/PageReadCache.cs` | New `GetOrWalk(pdfSha256, page, curveSegments, walk)`, binary gzip storage, key/version/footer validation, atomic publication, and field-by-field read-back verification. The optional constructor root isolates synthetic disk tests; production defaults to `DrawingMirror.Root`. |
| `Kor.Operations.EngineeringTools.Core/PdfToSafe/PdfPlanReader.cs` | Added `ParsePage(RawPage, scale)` and the overload returning `PageContent` and kept ordinals. The existing `Page` overload walks once, then delegates. Scale-dependent tolerances and the mm projection are unchanged. |
| `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` | `Read` hashes once for its document. Added a hash-carrying `ReadSheet` overload; the original signature remains a hashless, non-recording entry point. `ReadPage` gets one raw page and derives the full population and classifier read from it. |
| `Kor.Operations.EngineeringTools.Core/Intake/PdfOnlyBuild.cs` | `WriteSheets` computes one PDF content hash beside the document open and passes it to every `ReadSheet`. |
| `Kor.Operations.EngineeringTools.Core/Dxf/DrawingMirror.cs` | Added `FileSha256`, the shared content-hash helper used by both document entry points. Existing mirror selection/copy behaviour is unchanged. |
| `Kor.Operations.EngineeringTools.Core.Tests/PdfToSafe/APageIsWalkedOnceTests.cs` | Added hand-built derivation, projection, exact disk round-trip, cache-hit, corrupt/missing-record, wrong-key, empty-page and failed-publication fixtures. |
| `docs/codex/CODEX-PDF-INTAKE-PAGE-READ-CACHE-RESPONSE.md` | This response. |

The hash travels as one extra argument instead of changing the separately defined `DocumentFacts` type. This keeps edits within the named files and preserves the existing public `ReadSheet` signature. A caller with a `Page` or document but no content hash still walks, without recording. Neither the page cache nor a page loop hashes a PDF.

**Hashing finding:** `DrawingMirror.SingleFile` does not already compute a PDF SHA-256. Its `Key` computes a shortened SHA-1 of a lowercased folder-path string, while freshness checks use file size/time. That key cannot identify PDF bytes. `FileSha256` streams the selected file once with `SHA256.HashData`; it does not call `SingleFile` again or reread the file per page. The existing six-set read-cache fingerprint performs its own PDF hash outside these entry points; this task does not alter that test infrastructure to pass its hash through.

## Exact replay

`Walk` preserves word extraction, glyph deduplication, colour quantization, the existing Bézier arithmetic and sample order, and annotation extraction. Every flattened content point is retained with `minPointDistance: null`. A subpath with fewer than two raw points is omitted, but its ordinal still advances. Stored subpath ordinals are the existing global reading-order ordinals across all paths, including gaps; `PathOrdinal` separately identifies the enclosing PDF path.

`Derive` copies the raw point stream through the existing `AddPoint` function. A candidate at distance **less than or equal to** the tolerance from the last **kept** point is dropped. Filtering never uses the immediately preceding discarded point. Subpaths with fewer than two surviving points are omitted before either closure or kept-ordinal append.

Closure preserves the source's exact branches:

- A `Close` command sets closed without removing a point.
- Otherwise, only a path with at least three surviving points can acquire inferred closure.
- With `closeDistance`, Euclidean distance must be strictly smaller; the last point is removed and the path is closed. There is no second minimum-point-count check after that removal, matching the old code.
- Without `closeDistance`, **both individual coordinate differences** must be strictly smaller than `0.5`; the last point stays. This is not a Euclidean half-point rule.

The existing `ToGeomPath` computes the content path's bounds after these operations; clipping and path ordinal are copied. The caller's kept-ordinal list is appended to, never cleared. Annotation `GeomPath`s are appended unchanged only when requested, without thinning, re-closing, recalculating their stored bounds, or adding content ordinals. The stored raw points are not mutated by derivation.

No dependency was found that requires approximating a thinning, closure or projection rule. Rules needing PdfPig objects remain in `Walk`: word/glyph extraction, Bézier flattening, path colour extraction and annotation geometry. The record contains their current outputs, not raw glyphs, graphics commands or annotation dictionaries. Changing those rules, or a PdfPig upgrade that changes them, requires bumping `PageReadCache.Version`. Changing downstream reading rules, scale or closure tolerances does not invalidate the walk record.

`Walk` always captures annotation paths so a later derivation can opt in without a PDF. Consequently a cold `ReadPage(..., includeAnnotations: false)` now performs annotation extraction and then omits its result. The annotation implementation is unchanged; exceptional failure timing or its diagnostic traces are not claimed identical for that formerly unvisited work. Intake already requested annotation geometry in both reads.

## Binary layout, version 1

Location: `DrawingMirror.Root/pages/<uppercase SHA-256>/<page>-<curveSegments>.bin`. Page is one-based; numeric filenames use invariant culture. The entire file is one gzip member written with `CompressionLevel.Fastest`. **The version byte is the first decompressed byte**, inside the gzip envelope.

Integers below are signed little-endian `Int32`, doubles are little-endian IEEE-754 `Double` with their original bits, and booleans are `BinaryWriter`'s single byte `0`/`1`. Strings use `BinaryWriter.Write(string)`: UTF-8 bytes preceded by a 7-bit encoded byte length. Collections retain original order. There is no JSON, rounding, float32 conversion or recomputation of annotation bounds.

| Order | Fields |
|---|---|
| Header | `Byte Version = 1`; `String PdfSha256`; `Int32 CurveSegments`; `Int32 PageNumber`; `Double WidthPts`; `Double HeightPts`. |
| Words | `Int32 Count`; for each: `String Text`, then six doubles `Cx, Cy, MinX, MinY, MaxX, MaxY`. |
| Content subpaths | `Int32 Count`; for each: `Int32 PathOrdinal`, `Int32 SubpathOrdinal`; points as defined below; three booleans `HasCloseCommand, IsFilled, IsStroked`; `Double LineWidth`; three bytes `R, G, B`; `Boolean IsClipping`. |
| Annotation paths | `Int32 Count`; for each: points; three booleans `IsClosed, IsFilled, IsStroked`; four doubles `MinX, MinY, MaxX, MaxY`; three bytes `R, G, B`; `Boolean IsAnnotation`; `Double LineWidth`; `Boolean IsClipping`; `Int32 PathOrdinal`. |
| Points, wherever used above | `Int32 Count`, followed by `Count` pairs of `Double X`, `Double Y`. |
| End | Exact end of decompressed payload, followed by the normal gzip envelope's CRC-32 and uncompressed-size footer. No extra application trailer. |

The reader validates the version and all three key components, rejects negative/impossible collection counts and raw content subpaths with fewer than two points, and requires exact payload consumption. It explicitly compares the gzip footer's CRC-32 and uncompressed size against the decompressed payload, in addition to using `GZipStream`; a missing footer must not become a successful read just because decompression yielded all the fields.

Any exception while reading an existing record is traced as a miss, followed by a walk and rewrite. Walk or write/verification failures propagate. A unique same-directory `.tmp` file is closed and flushed, read back through the same strict reader, and compared **field by field against the source raw object**, including every double via `BitConverter.DoubleToInt64Bits`. Only then does `File.Move(..., overwrite: true)` publish it. Normal failure cleanup removes the temporary file; a process kill may leave an ignored `.tmp`, never a partially published `.bin`. No rounded or tolerance equality can authorize publication.

Bounded process-wide lock stripes prevent simultaneous callers of the same path from duplicating a cold walk in one process. Different processes may both walk a cold key, but each publishes a complete verified record. “Once” applies to an intact record of the same version/key, not after corruption, deletion, version changes or cross-process cold races. A cache read temporarily holds the decompressed binary payload as well as the decoded record; memory and elapsed-time effects require the prescribed corpus measurement.

## Unchanged callers and remaining PDF work

The source search was `rg -n -F 'VectorPageReader.ReadPage(' Kor.Operations.EngineeringTools.Core`. Before this edit it found **13 matching lines: 12 executable calls and one XML `cref`**. Two executable calls are the intake and parser sites changed here. There are **10 unchanged executable calls in the seven named files**, rather than thirteen other executable callers. A repository-wide search also found CLI, App and existing-test callers; those were not edited.

| Unchanged Core caller, original line(s) | Relationship to `PdfOnlyBuild.WriteSheets` / `DrawingIntake` |
|---|---|
| `SlabTakeoffEngine.cs`: 413, 466, 522 | Separate takeoff/scale/vector reads, not the intake's sheet-read call chain. |
| `PdfVersusDxf.cs`: 97, 137 | Separate comparison reads, not the intake's sheet-read call chain. |
| `Intake/SetSchedules.cs`: 32 | **Runs within `WriteSheets`, before its sheet loop.** `SetSchedules.Of` opens the PDF and reads every page for assemblies and column rows. This is the immediate remaining prepass that could use a record next. |
| `Intake/SetStoreys.cs`: 43 | Called by `PdfOnlyBuild.WriteLevels` during composition, after sheet reading. It is part of the wider build, not a nested read inside `DrawingIntake.ReadPage`; it is another useful next cache consumer. |
| `Intake/AssemblySchedule.cs`: 98 | The standalone `ReadSet` entry point. Current `WriteSheets` uses `SetSchedules.Of`, which calls `AssemblySchedule.Read(content, ...)` on its existing content, not `ReadSet` and its extra walk. |
| `DrawingDigest.cs`: 49 | Separate digest reader, not the intake's sheet-read call chain. |
| `Dxf/StickFileSlabThicknessReader.cs`: 57 | Optional stick-file read in the DXF/composition route when a stick-file path is supplied; not inside `WriteSheets` or `DrawingIntake.ReadPage`. `PdfOnlyBuild` does not set a stick-file PDF in its composer request. |

Those unchanged calls use the default curve setting, `0`; the intake uses `PdfToSafeConstants.BezierSegments`. A future migration must respect that key distinction. It cannot silently substitute a differently tessellated record and claim identical paths, even where the present caller mainly wants words.

Both vector-derived `PageContent` reads now work from `RawPage` without opening a PDF. **The complete intake still opens the document and obtains a `Page`, even on a warm cache.** Its other existing outputs inspect annotation notes/links, glyph rendering/orientation/fonts/colour spaces, clipping operators, page rotation and images. Those facts are outside the requested raw record. They remain on the original PDF path, with no invented replacements. The set-wide schedule/storey passes also remain. This task therefore removes repeated vector/word extraction at the two targeted sites; it does not claim that every operation in a warm corpus build avoids PdfPig.

## Verification left to Claude

The new fixture summary specifies its scope and a same-class gap: it starts with flattened points, so it cannot detect a changed Bézier formula or sample order in the actual walk. Exact round-trip fixtures include adjacent representable doubles, signed zero, Unicode text, two differently attributed subpaths and an annotation with explicitly stored bounds. Corruption fixtures cover wrong version, missing files, body/footer truncation and a valid record copied under a different hash/page/curve key. They also verify that an invalid write is not published and that a valid empty page is a hit.

Only source read-back and scoped diff/whitespace checks were performed here. The fast suite, cold six-set gate, warm page-cache gate after invalidating the separate read-cache manifest, and timed cold/warm `30993-01` run remain unrun, as the brief requires. No byte-identity result or speedup is claimed. Existing tests, baselines, `CorpusAnalyzer.cs` and `GeometryFilterService.cs` were not edited by this task. Unrelated concurrent workspace changes were left alone.
