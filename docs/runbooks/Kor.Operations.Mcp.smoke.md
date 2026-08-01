# Kor.Operations.Mcp Smoke Harness

Purpose: `Kor.Operations.Mcp.Smoke` is the pre-commit quality gate for `/ask`.
It compares the live MCP answer against canonical `Kor.Operations.Business`
services on three axes: right tool, right scope, right number.

Run:

```powershell
dotnet run --project Kor.Operations.Mcp.Smoke -c Release
```

Config loads from `./appsettings.smoke.json` if present, otherwise from:
`\\KOR-APP01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json`.
Do not commit `appsettings.smoke.json`.

Exit codes:

- `0`: green, all smoke cases passed.
- `1`: red, at least one smoke case failed.
- Anything else: infrastructure/config failure before the harness completed.

Mandate: any change touching `Kor.Operations.Mcp/` or
`Kor.Operations.Business/` must run smoke green before commit. This includes
Codex prompt batches.

Failure-axis names:

- `[TOOL-CHOICE]`: Claude called the wrong tool or no canonical tool.
- `[SCOPE-MISMATCH]`: tool was right, input scope was wrong.
- `[NUMBER-MISMATCH]`: answer did not contain the calibrated Business-service value.
- `[TOO-SLOW]`: answer exceeded the case duration budget.
- `[INFRA]`: config, HTTP, audit, or Deltek access failed.
