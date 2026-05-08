# Kor.Operations.Mcp Deploy Runbook

LAN-only Windows service. Mirrors the FileSync deploy pattern.
Use **install.ps1** in the project root for the first-ever install on a host.
Use this runbook for every redeploy after that.

## Hosts

- **KOR-APP01**: production target. Lives at `C:\Program Files\KorOperations\Mcp\`
  (alongside `KorOperations\FileSync\` and `KorOperations\Opportunities\`).
- **KOR-1001**: dev box / publish staging. Build artifacts go to
  `C:\Publish\Kor.Operations.Mcp\` locally, then robocopy over UNC to KOR-APP01.

## Service URL

`http://kor-app01:5500/` — LAN-only, plain HTTP, Basic auth. No reverse proxy
and no TLS by design (see `feedback_lan_only_services.md`). All callers are
WPF clients on the KOR LAN.

## One-time setup (per host)

1. **SQL Server prep.** Connect to `KOR-APP01\SQLEXPRESS` in SSMS as a sysadmin
   and run:
   ```sql
   IF DB_ID('KorMcp') IS NULL CREATE DATABASE KorMcp;
   GO
   USE KorMcp;
   GO
   ```
   Then run `Kor.Operations.Mcp\Sql\CreateAuditLog.sql` (idempotent — creates
   `Mcp` schema + `Mcp.AuditLog` table + indexes).

2. **SQL login + linked-server mapping.** The MCP service connects with the
   login encoded in `Mcp:SqlConnectionString` in `appsettings.Production.json`.
   That login needs:
   - `db_datareader` + `db_datawriter` on `KorMcp` (for `Mcp.AuditLog` writes).
   - A `master..sp_addlinkedsrvlogin` mapping for `DELTEK_VP` so the AI's
     queries against `[DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.<Table>`
     work. Without this, queries fail with login errors at runtime.
   - `transmittals_app` is verified working through the linked server (used
     during the gateway smokes); reusing it is the path of least resistance
     unless audit separation is desired.

3. **`appsettings.Production.json` on the target host.** Populate with the
   following keys; leave the in-repo template empty (commit-safe). Real
   secrets are NEVER committed.
   ```json
   {
     "Mcp": {
       "Username": "<basic-auth user the WPF clients send>",
       "Password": "<basic-auth password>",
       "SqlConnectionString": "Server=KOR-APP01\\SQLEXPRESS;Database=KorMcp;User Id=<login>;Password=<pwd>;Encrypt=True;TrustServerCertificate=True;",
       "AnthropicApiKey": "sk-ant-...",
       "AnthropicModel": "claude-sonnet-4-6",
       "SqlQueryTimeoutSeconds": 30,
       "SqlQueryRowCap": 1000
     }
   }
   ```

4. **Run `install.ps1`** once from an admin PowerShell on KOR-APP01. Creates
   the service running as `KOR\app-admin` (per `reference_kor_service_account.md`).

5. **Open the firewall** for TCP 5500 inbound on KOR-APP01 (Domain profile only —
   it's LAN-only, so don't expose Public or Private profiles).

6. **`sc start Kor.Operations.Mcp`** and verify with `curl http://kor-app01:5500/health`
   from any LAN workstation. Expect:
   ```json
   {"status":"ok","service":"Kor.Operations.Mcp","version":"...","timestamp":"..."}
   ```

## Ongoing redeploy

Run from KOR-1001 in an admin PowerShell:

```powershell
# 1. Build + publish to local staging.
$src = 'C:\Publish\Kor.Operations.Mcp'
dotnet publish "C:\VIsual Studio Projects\Operations\Kor.Operations.Mcp\Kor.Operations.Mcp.csproj" `
    -c Release -r win-x64 --self-contained false -o $src

# 2. Stop the service on KOR-APP01.
sc.exe \\KOR-APP01 stop Kor.Operations.Mcp

# 3. Snapshot the current install for rollback (dated _prev_<yyyymmdd_hhmmss>),
#    matching the FileSync_prev_* convention already on the host.
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
robocopy "\\KOR-APP01\C$\Program Files\KorOperations\Mcp" `
         "\\KOR-APP01\C$\Program Files\KorOperations\Mcp_prev_$stamp" /MIR /XF appsettings.Production.json | Out-Null

# 4. Mirror the new bits over. /XF protects the production secrets file —
#    without this, the empty in-repo template would overwrite real secrets.
robocopy $src "\\KOR-APP01\C$\Program Files\KorOperations\Mcp" /MIR /XF appsettings.Production.json

# 5. Start the service.
sc.exe \\KOR-APP01 start Kor.Operations.Mcp

# 6. Verify the new version is live.
curl http://kor-app01:5500/health
# Expect: { "status":"ok", "service":"Kor.Operations.Mcp", "version":"<new>", ... }
```

## Smoke test after redeploy

The user-facing endpoint is `/ask`. Confirms the gateway, the linked server,
and the audit log all work end-to-end:

```powershell
$auth = "Basic $([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('USER:PASSWORD')))"
$body = '{"question":"How many active projects do we have?"}'
Invoke-RestMethod -Uri http://kor-app01:5500/ask -Method Post `
    -Headers @{
        Authorization = $auth
        'X-Kor-User-Upn' = $env:USERNAME + '@korstructural.com'
    } `
    -ContentType 'application/json' -Body $body
```

Expect a natural-language answer plus token-usage fields. Confirm an audit row
also landed:

```sql
SELECT TOP 5 * FROM KorMcp.Mcp.AuditLog ORDER BY OccurredAt DESC;
```

## Rollback

```powershell
# 1. Stop the service.
sc.exe \\KOR-APP01 stop Kor.Operations.Mcp

# 2. Restore the most recent _prev_ snapshot. /XF preserves the live secrets file.
$prev = Get-ChildItem '\\KOR-APP01\C$\Program Files\KorOperations\' -Directory `
    | Where-Object Name -Like 'Mcp_prev_*' | Sort-Object Name -Descending | Select-Object -First 1
robocopy $prev.FullName "\\KOR-APP01\C$\Program Files\KorOperations\Mcp" /MIR /XF appsettings.Production.json

# 3. Start.
sc.exe \\KOR-APP01 start Kor.Operations.Mcp

# 4. Verify.
curl http://kor-app01:5500/health
```

The per-question input-token budget (300k, in `AskService.cs`) keeps a runaway
server from burning Anthropic credits during a bad deploy. Rolling back the
binary is still the right first move.
