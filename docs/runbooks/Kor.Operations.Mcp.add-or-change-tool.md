# Adding or Changing an MCP Tool

This runbook is short on purpose. After Batch 89 most of the wiring is
auto-discovered from `[McpServerToolType]` / `[McpServerTool(Name="get_x")]`
attributes, and a startup parity validator refuses to start the service if
the system prompt and tool inventory disagree.

## Mental model in one paragraph

Each canonical KPI is a `Kor.Operations.Business/<X>Service.cs` (owns the SQL,
shared with WPF), wrapped by a `Kor.Operations.Mcp/Tools/<X>Tool.cs` (shapes
JSON for Claude, attaches audit row). The system prompt in `AskService.cs` is
a raw string literal `"""..."""` with one line per KPI pointing at the
relevant tool. `AskService` itself contains no per-tool wiring - it consumes
`McpToolRegistry` which auto-discovers tools at startup.

## Adding a NEW KPI tool

1. Copy `Kor.Operations.Business/BacklogService.cs` as a template into a new
   `<X>Service.cs`. Update the class name, SQL, result records.
2. Copy `Kor.Operations.Mcp/Tools/BacklogTool.cs` as a template into a new
   `<X>Tool.cs`. Update class name, `[McpServerTool(Name="get_x")]`,
   `[Description(...)]`, payload projection, methodology string.
3. Register both in `Kor.Operations.Mcp/Program.cs`:
   - `builder.Services.AddSingleton<Kor.Operations.Financials.<X>Service>();`
   - `builder.Services.AddSingleton<Kor.Operations.Mcp.Tools.<X>Tool>();`
   - Add `sp.GetRequiredService<Kor.Operations.Mcp.Tools.<X>Tool>()` to the
     toolInstances array passed into `McpToolRegistry`.
4. Add ONE line to the system prompt's KPI METHODOLOGY block in
   `AskService.cs` pointing Claude at `` `get_x` ``. (Backticks around the tool
   name; the parity validator looks for that exact form.)
5. Bump `<Version>` in `Kor.Operations.Mcp.csproj`.
6. Add 1+ test case + a calibrator to `Kor.Operations.Mcp.Smoke`. Smoke must
   be green before commit.
7. `dotnet build` to verify. If the parity validator complains at startup,
   either the prompt line is missing or it doesn't backtick-wrap the tool
   name correctly.
8. Deploy: see `Kor.Operations.Mcp.deploy.md`.

The WPF Financials window picks up the new service automatically on next
build (instantiated in `ExecutiveSummaryDeltekLoader`'s constructor).

## Changing an EXISTING KPI's formula

Usually one file:
1. Edit the SQL in `Kor.Operations.Business/<X>Service.cs`.
2. If the returned fields didn't change shape, you're done. `dotnet build`,
   bump version, deploy.
3. If you changed which fields the service returns, also update:
   - The `<X>Result` record signature (positional params).
   - The tool's payload projection in `Kor.Operations.Mcp/Tools/<X>Tool.cs`.
   - The methodology string in the tool + the system-prompt line in
     `AskService.cs` if the description shifted.
4. The parity validator does not enforce the system-prompt's KPI line
   matching the tool's `[Description]` byte-for-byte - only that the tool
   name appears. So slight wording divergence between the two is allowed if
   it's deliberate.

## Renaming a tool

1. Change `[McpServerTool(Name=...)]` on the tool method.
2. Update the system-prompt line to use the new backticked name.
3. The parity validator will catch any place you missed at startup.

## The two recurring footguns

- **Period-anchored vs literal calendar windows.** KOR's Deltek posts ~3
  months late; "last 90 days" as a literal date filter collapses to ~$0.
  `Billed90` actually means "SUM across the latest 3 closed periods."
  Service methodology strings explain this where it matters
  (RecentBilledService, WipFinancialsService).
- **Raw string literal (Batch 89+).** Don't switch back to `@"..."`. The
  raw `"""..."""` form removes the doubled-quote escape and the
  drift-triggered Batch 83 1051-error incident.

## Deploy

See `Kor.Operations.Mcp.deploy.md`. Standard sequence: publish from
KOR-1001, stop service on KOR-APP01, snapshot for rollback, robocopy
`/MIR /XF appsettings.Production.json`, start, health check, smoke
through `/ask`.
