# Architecture Map Audit Response

## Findings

CRITICAL - The committed model is already stale, and the freshness test would not fail.

`Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:44` reads projects from disk, `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:45` reads projects from the model, and `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:47` through `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:63` compare only project names and project references. The implementation explicitly does not compare file counts or type counts, matching the comment at `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:21` through `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs:25`.

The current model lists `Kor.Operations.Architecture.Tests` with one file and 148 lines at `docs/architecture/architecture.json:873` through `docs/architecture/architecture.json:885`, but the tree now has `Kor.Operations.Architecture.Tests/VisioRendererTests.cs:20` declaring `VisioRendererTests`. That is a new mapped type and a renderer test file, but it does not alter any `.csproj` reference. Consequence: the committed reference artifact can be stale while the freshness gate remains green.

CRITICAL - The instrument still measures itself through its test project.

The source filter excludes `Kor.Operations.Architecture/` at `Kor.Operations.Architecture/Program.cs:399` through `Kor.Operations.Architecture/Program.cs:403`, and the project filter does the same at `Kor.Operations.Architecture/Program.cs:421` through `Kor.Operations.Architecture/Program.cs:424`. Neither excludes `Kor.Operations.Architecture.Tests/`. The self-measurement test repeats the same narrower guard at `Kor.Operations.Architecture.Tests/ExtractorTests.cs:70` through `Kor.Operations.Architecture.Tests/ExtractorTests.cs:74`.

The committed model includes `Kor.Operations.Architecture.Tests` as a project at `docs/architecture/architecture.json:873` through `docs/architecture/architecture.json:879`, includes its test type at `docs/architecture/architecture.json:9636` through `docs/architecture/architecture.json:9641`, and records `Kor.Operations.Architecture.Tests/ExtractorTests.cs` as Deltek evidence at `docs/architecture/architecture.json:54222`. The graph then draws `Kor.Operations.Architecture.Tests -> ext:Deltek Vision (ODBC)` at `docs/architecture/architecture.json:58108` through `docs/architecture/architecture.json:58110`. Trigger: any architecture test text containing a broad external marker such as `Deltek`; `ExternalSystems.Detect` searches raw text at `Kor.Operations.Architecture/Program.cs:782` through `Kor.Operations.Architecture/Program.cs:786`. Consequence: the map says the mapper's own tests talk to a production database.

MATERIAL - `architecture.json` is not locale-deterministic.

`GraphBuilder.Relationships` formats node details with the current culture at `Kor.Operations.Architecture/Graphs.cs:48` through `Kor.Operations.Architecture/Graphs.cs:51`; those `Detail` strings are serialized into the model at `Kor.Operations.Architecture/Graphs.cs:140` through `Kor.Operations.Architecture/Graphs.cs:154`. The JSON writer normalizes encoding and line endings at `Kor.Operations.Architecture/Program.cs:49` through `Kor.Operations.Architecture/Program.cs:51`, but it cannot normalize already-cultured strings. Trigger: running under a culture whose `N0` separator is not comma, such as `fr-CA`. Consequence: the same source produces different committed JSON bytes, violating the first invariant.

MATERIAL - File ownership does not match what projects compile.

The extractor walks physical `*.cs` files at `Kor.Operations.Architecture/Program.cs:396` and assigns each file to the nearest project directory at `Kor.Operations.Architecture/Program.cs:242` through `Kor.Operations.Architecture/Program.cs:247`. It does not read `Compile Include`, `Compile Remove`, or linked source items when deciding which assembly owns a type. This repo has linked compile items: `Kor.EmailSearch.Core/Kor.EmailSearch.Core.csproj:23` through `Kor.EmailSearch.Core/Kor.EmailSearch.Core.csproj:24`, `Kor.Operations.Business/Kor.Operations.Business.csproj:29` through `Kor.Operations.Business/Kor.Operations.Business.csproj:32`, and `Kor.Operations.App/Kor.Operations.App.csproj:286` through `Kor.Operations.App/Kor.Operations.App.csproj:289` all compile `Kor.Operations.Data/SqlTimeouts.cs`.

Trigger: any source file linked into another project or removed from default compile. Consequence: type ownership, project line counts, duplicate detection, and mention edges describe physical folders rather than compiled assemblies. That is a different map than the one users will infer from project boxes.

MATERIAL - CLI verb count misses live verbs that use normal equality.

`CliVerbs` only recognizes `args[0].Equals("...")` invocations at `Kor.Operations.Architecture/Program.cs:466` through `Kor.Operations.Architecture/Program.cs:478`. It does not recognize `args[0] == "..."`. Current live tools use that shape: `tools/BdSynthesisSmoke/Program.cs:18`, `tools/BdSynthesisSmoke/Program.cs:23`, and `tools/BdSynthesisSmoke/Program.cs:30` define `sector`, `emit`, and `ensure`; `tools/BdSectorSmoke/Program.cs:17`, `tools/BdSectorSmoke/Program.cs:35`, and `tools/BdSectorSmoke/Program.cs:58` define `docx`, `all`, and `pursuit`; `tools/MerxProbe/Program.cs:7` defines `pages`.

Trigger: command dispatch written with `==`, `switch`, `System.CommandLine`, or any parser other than the single recognized pattern. Consequence: the reported CLI verb count is lower than reality, and the CLI verbs page omits commands a reviewer may need to know exist.

MATERIAL - Format ownership is inflated by tests and helper types.

Every type declaration gets format detection at `Kor.Operations.Architecture/Program.cs:278` through `Kor.Operations.Architecture/Program.cs:305`. `FileFormats.For` treats a token in the type name as format ownership at `Kor.Operations.Architecture/Program.cs:722` through `Kor.Operations.Architecture/Program.cs:727` and `Kor.Operations.Architecture/Program.cs:744` through `Kor.Operations.Architecture/Program.cs:746`. It also treats any matching `using` directive in the file as applying to every type in that file at `Kor.Operations.Architecture/Program.cs:748` through `Kor.Operations.Architecture/Program.cs:750`. The renderer then presents those as "Which project handles which file format" at `Kor.Operations.Architecture/VisioRenderer.cs:475` through `Kor.Operations.Architecture/VisioRenderer.cs:493`.

The committed model shows test classes as `.rvt`, `.xlsx`, and `.csv` handlers at `docs/architecture/architecture.json:52947` through `docs/architecture/architecture.json:52962` and `docs/architecture/architecture.json:52965` through `docs/architecture/architecture.json:52974`. Trigger: tests named after a format-bearing production type, or files containing helper classes next to a `ClosedXML`/`PdfPig` using. Consequence: the matrix can tell a reviewer that a project handles a format when it only tests or sits beside code that handles it.

MATERIAL - Cycle detection silently ignores long cycles.

`Cycles` walks project references recursively at `Kor.Operations.Architecture/Program.cs:625` through `Kor.Operations.Architecture/Program.cs:638`, but it stops when `path.Count > 12` at `Kor.Operations.Architecture/Program.cs:635`. Trigger: a dependency loop of 13 or more projects. Consequence: the model can report zero cycles even when a cycle exists. The test at `Kor.Operations.Architecture.Tests/ExtractorTests.cs:145` through `Kor.Operations.Architecture.Tests/ExtractorTests.cs:149` only asserts today's extracted model is empty; it does not pin the algorithm against a long-cycle fixture.

MATERIAL - The duplicate similarity score is nondeterministic for partial types and duplicate declarations.

Source enumeration order comes from `Directory.EnumerateFiles` at `Kor.Operations.Architecture/Program.cs:396`. A type id is only project plus namespace plus simple name at `Kor.Operations.Architecture/Program.cs:280` through `Kor.Operations.Architecture/Program.cs:283`. Each declaration then overwrites `declarations[id]` at `Kor.Operations.Architecture/Program.cs:294` through `Kor.Operations.Architecture/Program.cs:295`. This repo has many partial types, for example `Kor.Operations.App/Brochures/BrochureBuilderViewModel.cs:24`, `Kor.Operations.App/Brochures/BrochureBuilderViewModel.Blocks.cs:11`, and `Kor.Operations.App/Brochures/BrochureBuilderViewModel.ClientList.cs:11`.

Trigger: a partial type split across multiple files, or any accidental duplicate declaration with the same namespace/name in one project. Consequence: whichever file is enumerated last supplies the declaration used by `Duplicates`, so the reported similarity and line count can churn between machines even when all sorted output code remains unchanged.

MINOR - Script reference counts are substring mentions, not calls.

`ScriptInventory` groups scripts by bare file name at `Kor.Operations.Architecture/Scripts.cs:74` through `Kor.Operations.Architecture/Scripts.cs:77`, reads candidate caller text at `Kor.Operations.Architecture/Scripts.cs:83` through `Kor.Operations.Architecture/Scripts.cs:88`, and counts any case-insensitive substring hit at `Kor.Operations.Architecture/Scripts.cs:90` through `Kor.Operations.Architecture/Scripts.cs:95`. Candidate callers include Markdown and JSON at `Kor.Operations.Architecture/Scripts.cs:127` through `Kor.Operations.Architecture/Scripts.cs:132`.

Trigger: a changelog, audit response, JSON string, or comment naming a script without invoking it. Consequence: "referenced by nothing" is conservative, but `ReferencedBy > 0` is not evidence that a script is wired into the system. The limitation is materially broader than the page subtitle "Scripts nothing references" at `Kor.Operations.Architecture/VisioRenderer.cs:560` through `Kor.Operations.Architecture/VisioRenderer.cs:565`.

MINOR - Text decoding is implicit.

The extractor reads source with `File.ReadAllText(file)` at `Kor.Operations.Architecture/Program.cs:249` through `Kor.Operations.Architecture/Program.cs:251`; the script inventory does the same for callers at `Kor.Operations.Architecture/Scripts.cs:83` through `Kor.Operations.Architecture/Scripts.cs:88` and counts lines with `File.ReadLines(full)` at `Kor.Operations.Architecture/Scripts.cs:107` through `Kor.Operations.Architecture/Scripts.cs:114`. Trigger: a non-UTF-8 source, script, or SQL file with bytes outside UTF-8. Consequence: markers, line counts, and syntax text can be decoded differently from the compiler or the user's editor, and the report does not state that assumption.

MINOR - Renderer automation state is not restored when Visio is kept open.

The renderer disables `ScreenUpdating`, `EventsEnabled`, `DeferRecalc`, and document undo at `Kor.Operations.Architecture/VisioRenderer.cs:99` through `Kor.Operations.Architecture/VisioRenderer.cs:110`. It restores only `DeferRecalc` at `Kor.Operations.Architecture/VisioRenderer.cs:128` through `Kor.Operations.Architecture/VisioRenderer.cs:131`. The `finally` block either quits Visio or releases only the application RCW at `Kor.Operations.Architecture/VisioRenderer.cs:147` through `Kor.Operations.Architecture/VisioRenderer.cs:153`.

Trigger: `--keep-open`, or an exception after the automation switches are changed. Consequence: the visible Visio instance can be left with events and screen updating disabled; on exceptions, the unsaved document and page/shape RCWs are abandoned to COM cleanup behavior the code does not control.

## Ship-Blocker

The single ship-blocker is the stale-map gate. `ArchitectureMapIsCurrentTests` only compares projects and project references, while the map's own committed artifact is already missing a current source file/type. Until the gate can fail for a change that actually moves the committed drawing, the map cannot be trusted as a reference.

## Missing

XAML is the next blind spot. The extractor maps C# only via `Directory.EnumerateFiles(root, "*.cs", ...)` at `Kor.Operations.Architecture/Program.cs:396`; XAML appears only as a possible script caller at `Kor.Operations.Architecture/Scripts.cs:131`. The WPF app's view tree, resource dictionaries, bindings, commands, and navigation relationships are therefore mostly invisible. That is the next "this repo is not just C#" category after scripts.

## What Looks Right

The durable extraction/rendering split is real: `Program.Main` writes `architecture.json` before rendering at `Kor.Operations.Architecture/Program.cs:46` through `Kor.Operations.Architecture/Program.cs:51`, and rendering failures return nonzero after the model is already on disk at `Kor.Operations.Architecture/Program.cs:69` through `Kor.Operations.Architecture/Program.cs:79`. That preserves the artifact that matters most when Visio is unavailable or COM fails.
