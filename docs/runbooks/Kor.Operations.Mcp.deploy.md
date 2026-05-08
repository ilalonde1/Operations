# Kor.Operations.Mcp Deploy Runbook

Mirrors the FileSync deploy pattern (`reference_filesync_deploy_runbook.md`).
Use **install.ps1** in the project root for the first-ever install on a host.
Use this runbook for every redeploy after that.

## Hosts

- **KOR-APP01**: production target. Lives at `D:\Services\Kor.Operations.Mcp\`.
- **KOR-1001**: staging area at `\\KOR-1001\publish\Kor.Operations.Mcp\`.

## One-time setup

1. Run `Sql\CreateAuditLog.sql` against the SQL Server instance the service will use. Idempotent and safe to re-run.
2. Populate `appsettings.Production.json` on KOR-APP01 with real values for `Mcp.Username`, `Mcp.Password`, `Mcp.SqlConnectionString`, and later the Anthropic API key and token-budget caps. **Never commit real secrets.**
3. Run `install.ps1` once from an admin PowerShell on KOR-APP01.
4. Wire up `mcp.korstructural.com` or the chosen hostname on the existing `*.korstructural.com` reverse proxy that fronts WatchlistSync.
5. Run `sc start Kor.Operations.Mcp` and verify with `curl https://mcp.korstructural.com/health` from a workstation.

## Ongoing redeploy

Run from KOR-1001 or wherever the build artifacts live:

```powershell
# 1. Build + publish.
dotnet publish "C:\VIsual Studio Projects\Operations\Kor.Operations.Mcp\Kor.Operations.Mcp.csproj" `
    -c Release -r win-x64 --self-contained false `
    -o "\\KOR-1001\publish\Kor.Operations.Mcp"

# 2. Stop the service on KOR-APP01.
sc.exe \\KOR-APP01 stop Kor.Operations.Mcp

# 3. Mirror the published bits into the install path. /MIR removes files
#    that no longer exist in the source, matching the FileSync deploy pattern.
robocopy "\\KOR-1001\publish\Kor.Operations.Mcp" "\\KOR-APP01\D$\Services\Kor.Operations.Mcp" /MIR

# 4. Start the service.
sc.exe \\KOR-APP01 start Kor.Operations.Mcp

# 5. Verify the new version is live.
curl https://mcp.korstructural.com/health
# Expect: { "status":"ok", "service":"Kor.Operations.Mcp", "version":"<new>", ... }
```

## Smoke test after redeploy

```powershell
# tools/list should include "ping" plus every tool added since.
$body = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
$auth = "Basic $([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('username:password')))"
Invoke-WebRequest https://mcp.korstructural.com/ -Method POST `
    -Headers @{
        Authorization = $auth
        'X-Kor-User-Upn' = $env:USERNAME + '@korstructural.com'
        'Accept' = 'application/json, text/event-stream'
    } `
    -ContentType 'application/json' -Body $body
```

Then confirm an audit row appeared:

```sql
SELECT TOP 5 * FROM Mcp.AuditLog ORDER BY OccurredAt DESC;
```

## Rollback

If a redeploy breaks things:

1. `sc.exe \\KOR-APP01 stop Kor.Operations.Mcp`
2. Restore the prior binaries from a tagged-by-version backup in a sibling folder, such as `D:\Services\Kor.Operations.Mcp.previous`, via `robocopy /MIR`.
3. `sc.exe \\KOR-APP01 start Kor.Operations.Mcp`
4. Verify `/health` returns the rolled-back version.

The Anthropic token cap, when wired in Phase 11d, keeps a runaway server from burning credits during a bad deploy. Rolling back the binary is still the right first move.
