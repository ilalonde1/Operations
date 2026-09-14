# Six-set gate read cache

Implemented in the working tree. No build, test run, drawing read, database access or network access was performed, as the brief requires. Runtime acceptance and the under-two-minute target remain for Claude to verify.

## Changes

- `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetsBuildAsBankedTests.cs`: `EveryBankedSetBuildsByteIdenticalToItsBaseline` computes one reader fingerprint, then a PDF/scale fingerprint per mirrored set. A matching cache calls `PdfOnlyBuild.Recompose` with the saved page count and reconstructed `SheetsResult`. A miss calls the original `PdfOnlyBuild.Build(pdf, work, set.Scale, options, rulesConnection: conn, stem: set.Job)` with exactly the same arguments, then saves the cache. The baseline comparison still runs on either path. Per-set output identifies read/recompose, the miss reason or the original read timestamp. The summary states the cache's coverage and exclusions.
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetReadCache.cs`: repository-root discovery, source/PDF fingerprints, manifest validation, sheet-ledger projection, cache writing and reconstruction. `CorpusAnalyzer.Rows` is private, so the helper reproduces its sheet-row projection, including placement/storey/flag fields. It uses the existing public `WriteSheetCsv` and `DxfFilesOf`, and internal `ReadSheetRows`; no production visibility changes were needed.
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetReadCacheTests.cs`: three focused tests covering included/excluded source edits, additions/removals/renames, scalar and word-list options, PDF content changes despite unchanged size/time, scale mismatch, repository discovery, typed sheet-row reconstruction, retained read timestamps, and absent/corrupt cache artifacts. These tests use temporary synthetic files and do not parse drawings or call the composer. They were written but not run.

`PdfOnlyBuild.cs`, `CorpusAnalyzer.cs`, baselines and the existing step-63 edits were not modified by this task.

## Exact manifest contents

Each set stores `read-cache.json` beside `sheets.csv` and `dxf/` under `TestResults/six-sets/<job>/` in the test output directory. It is indented JSON with these fields and no others:

| JSON field | Value |
|---|---|
| `Version` | Integer `1`; changing the cache format requires a version bump. |
| `Inputs.PdfSha256` | Uppercase hexadecimal SHA-256 of the mirrored PDF's bytes. |
| `Inputs.Scale` | The set's integer scale passed to the full build. |
| `Inputs.ReaderSha256` | Uppercase hexadecimal digest of the reader source and options described below. |
| `ReadAtUtc` | UTC time the full build's cache is saved, serialized as an ISO date/time. Recompose does not replace it. |
| `Pages` | `BuildOutcome.Pages`, so a hit needs no separate PDF page-count read by the test. |
| `Written`, `Empty`, `NotPlan`, `Failed` | Original `SheetsResult` counts. |
| `SheetsSha256` | Uppercase hexadecimal SHA-256 of the written `sheets.csv`, detecting later truncation or modification. |

The source digest includes precisely the requested source selection: recursive `.cs` files in `PdfToSafe`; recursive `.cs` files in `Intake` excluding `CorpusAnalyzer.cs`, `CorpusDiff.cs`, `SheetDiff.cs`, `SetCheck.cs`, `StoreysFromPlans.cs`; and the eleven specified DXF files. `Intake/PdfOnlyBuild.cs` is explicitly required and deduplicated from the recursive selection. Missing explicitly required files fail fingerprint computation rather than hash an incomplete set.

The digest's exact UTF-8 input is, for each distinct repository-relative path in ordinal order, with `/` separators:

```text
<relative path>\n<uppercase SHA-256 of that file's bytes>\n
```

After those entries it appends literal `options\n` followed by `JsonSerializer.Serialize(options)` with default serializer settings and no final newline. SHA-256 is applied to that complete byte sequence. Paths participate so additions, removals and renames invalidate as well as byte edits. Whitespace changes in included sources invalidate.

JSON is used instead of record `ToString()` because `PdfIntakeOptions` contains `IReadOnlyList<string>` members. A record's default string representation does not serialize their word contents; JSON does. This includes the options actually returned by `PdfIntakeOptions.For(conn)`, without storing or hashing a connection string.

The writer deletes an earlier manifest, writes `sheets.csv`, then writes the manifest last. It immediately reads/deserializes that manifest and compares record equality with the intended value, throwing if the round trip disagrees. This runtime check is implemented; no actual cache manifest was produced during this task.

## Reuse and limits

A hit requires matching version and inputs, a valid positive page count, the sheet CSV with its original hash, and a `dxf/` directory whose nonempty DXF filename set equals the files named by the sheet rows. Missing/extra views also force a full read. Malformed JSON/CSV and ordinary cache I/O failures become misses with an explanatory reason. The existing full build clears its own work directory and writes the DXFs; that behaviour is unchanged.

Reconstruction follows the analyzer: sheet reading fields and view names survive; raw/annotation-path and footing counts, assemblies, transient diagnostics and in-memory views are not reconstructed. `Recompose` receives disk views through its existing implementation and reruns the ladder/composer. Cached composer-side sheet metadata is not used to decide the new composition. The saved CSV remains the original full build's ledger; a cache hit does not relabel it as a fresh read.

As requested, composer-only files (`E2kGeometryComposer.cs`, `DxfToEtabsService.cs`, `WallOutlineDecomposer.cs`, `WallNetwork.cs`, `ModelDiff.cs`, `ModelYardstick.cs`) and the Tests project do not invalidate. Neither do the five excluded Intake files. The cache does not fingerprint DXF contents, arbitrary files outside the specified source list, rule rows absent from `PdfIntakeOptions`, or mutable static state. A composer depending on a reader-side static set during reading could therefore hide a fault on reuse; `PlanSheetNaming.Vocabulary` and its reinitialisation in `WriteLevels` are explicitly called out in the gate summary. Hashing the options is not a substitute for that initialisation.

## Verification performed

Read back the changed/new C# files and checked the gate diff to confirm the full-build call and baseline comparison remain intact. Scoped whitespace checks were performed. No build or test results, baseline agreement, or speed measurements are claimed. Claude's specified two gate runs and composer-versus-reader whitespace edits are still the runtime acceptance; the new `SixSetReadCacheTests` also need to be run.
