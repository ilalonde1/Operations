#Requires -Version 7.0
<#
.SYNOPSIS
    Launch Claude Code with the NetworkOps MCP tools available (fleet / findings / device, over the live service on APP01).

.DESCRIPTION
    The NetworkOps MCP endpoint (https://KOR-APP01.int.korstructural.com:8445/mcp) is behind the same Entra auth as the rest
    of the API, and the service uses a self-signed certificate (pinned by the app/CLI). A Claude Code session, which uses
    Node's HTTPS, needs two things, which this sets for the launched session only -- the DB credential never leaves APP01:

      NETWORKOPS_TOKEN      an access token for NetworkOps, as the signed-in person (via `netops token`; silent once the
                            app/CLI has signed in, otherwise a browser opens for MFA). The repo .mcp.json sends it as the
                            Bearer. It lasts about an hour -- re-run this to refresh.
      NODE_EXTRA_CA_CERTS   the service's certificate (tools/networkops-app01-cert.pem), so Node trusts the endpoint
                            without touching the machine's trust store.

    Then it launches Claude Code (any extra arguments are passed through). The .mcp.json at the repo root does the rest.
#>
$ErrorActionPreference = 'Stop'

$netops = Get-Command netops -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $netops) { $netops = Join-Path $PSScriptRoot '..\Kor.Operations.NetworkOps.Cli\bin\Debug\net8.0-windows\netops.exe' }
if (-not (Test-Path $netops)) { throw "netops.exe not found (build Kor.Operations.NetworkOps.Cli, or put netops on PATH)." }

Write-Host "Getting a NetworkOps token (a browser may open for sign-in the first time)..." -ForegroundColor Cyan
$token = & $netops token
if (-not $token -or -not $token.StartsWith('eyJ')) { throw "netops token did not return a token." }
$env:NETWORKOPS_TOKEN = $token
$env:NODE_EXTRA_CA_CERTS = (Resolve-Path (Join-Path $PSScriptRoot 'networkops-app01-cert.pem')).Path

Write-Host "NetworkOps MCP ready (token set ~1h; cert trusted for this session). Launching Claude Code..." -ForegroundColor Green
claude @args
