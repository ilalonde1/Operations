<#
.SYNOPSIS
    Dead-man watchdog for Kor.Opportunities.Worker. Runs ON KOR-APP01 as a
    scheduled task, INDEPENDENT of every service it watches — closing the gap
    where the only thing that could email about a dead Worker was the Worker.

.DESCRIPTION
    Every run:
      1. Reads opportunities.ServiceHeartbeat (the Worker beats every 60 s).
      2. If the beat is older than -ThresholdMinutes (default 10):
         - if the Windows service is Stopped, attempts ONE restart;
         - emails the alert (Graph client-credential, same app registration the
           morning report uses), throttled to one alert per -AlertCooldownMinutes.
      3. If healthy after a stale period, sends a one-time recovery email.
    State (last alert time, stale flag) persists in ProgramData so restarts of
    the task don't re-spam. Logs to a daily file alongside the state.

    Secrets: NONE in this file. Reads machine env vars already on KOR-APP01
    (KOR_OPPORTUNITIES_OPPORTUNITIESDB + KOR_OPPORTUNITIES_MORNINGREPORT*).

.PARAMETER Test
    Sends a TEST alert immediately regardless of health, to prove the pipe.

.NOTES
    PS 5.1-compatible (server default). Installed as scheduled task
    'KOR Opportunities Heartbeat Watchdog' (SYSTEM, every 10 minutes).
    Doctrine D10 companion: failures must be DELIVERED, and the deliverer
    must not be the patient.
#>
[CmdletBinding()]
param(
    [int]$ThresholdMinutes = 10,
    [int]$AlertCooldownMinutes = 60,
    [string]$ServiceName = 'Kor.Opportunities.Worker',
    [string]$SenderUpn = 'ilalonde@korstructural.com',
    [string]$Recipient = 'ilalonde@korstructural.com',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$stateDir  = 'C:\ProgramData\KorOperations\Watchdog'
$stateFile = Join-Path $stateDir 'state.json'
$logFile   = Join-Path $stateDir ("watchdog-{0:yyyyMMdd}.log" -f (Get-Date))
if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Path $stateDir -Force | Out-Null }

function Write-Log([string]$msg) {
    $line = "{0:yyyy-MM-dd HH:mm:ss zzz} {1}" -f (Get-Date), $msg
    Add-Content -Path $logFile -Value $line
    Write-Host $line
}

function Get-State {
    if (Test-Path $stateFile) {
        try { return Get-Content $stateFile -Raw | ConvertFrom-Json } catch { }
    }
    return [pscustomobject]@{ LastAlertUtc = [datetime]::MinValue.ToString('o'); WasStale = $false }
}

function Set-State($state) { $state | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8 }

function Send-GraphMail([string]$subject, [string]$bodyText) {
    $tenant = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_MORNINGREPORTTENANTID', 'Machine')
    $client = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_MORNINGREPORTCLIENTID', 'Machine')
    $secret = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_MORNINGREPORTCLIENTSECRET', 'Machine')
    if (-not ($tenant -and $client -and $secret)) { throw 'Graph env vars missing on this machine.' }

    $tok = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" -Body @{
        client_id = $client; client_secret = $secret
        scope = 'https://graph.microsoft.com/.default'; grant_type = 'client_credentials'
    }

    $mail = @{
        message = @{
            subject = $subject
            body = @{ contentType = 'Text'; content = $bodyText }
            toRecipients = @(@{ emailAddress = @{ address = $Recipient } })
        }
        saveToSentItems = $false
    } | ConvertTo-Json -Depth 6

    Invoke-RestMethod -Method Post -Uri "https://graph.microsoft.com/v1.0/users/$SenderUpn/sendMail" `
        -Headers @{ Authorization = "Bearer $($tok.access_token)" } -ContentType 'application/json' -Body $mail | Out-Null
}

# ---- read the heartbeat -----------------------------------------------------
$connStr = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'Machine')
if (-not $connStr) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB not set on this machine.' }

$beatUtc = $null; $version = '(unknown)'
$con = New-Object System.Data.SqlClient.SqlConnection $connStr
try {
    $con.Open()
    $cmd = $con.CreateCommand()
    $cmd.CommandText = "SELECT TOP 1 LastBeatUtc, Version FROM opportunities.ServiceHeartbeat WHERE ServiceName = @s ORDER BY LastBeatUtc DESC;"
    [void]$cmd.Parameters.AddWithValue('@s', $ServiceName)
    $r = $cmd.ExecuteReader()
    if ($r.Read()) { $beatUtc = ([datetimeoffset]$r[0]).UtcDateTime; $version = [string]$r[1] }
    $r.Close()
}
finally { $con.Dispose() }

$nowUtc = (Get-Date).ToUniversalTime()
$ageMin = if ($beatUtc) { [math]::Round(($nowUtc - $beatUtc).TotalMinutes, 1) } else { 99999 }
$stale  = ($ageMin -gt $ThresholdMinutes)
$state  = Get-State

Write-Log ("beat age {0} min (threshold {1}); version {2}; stale={3}" -f $ageMin, $ThresholdMinutes, $version, $stale)

if ($Test) {
    Send-GraphMail "[TEST] KOR Worker watchdog is armed" `
        ("This is a TEST alert from the heartbeat watchdog on {0}.`nCurrent state: beat age {1} min, version {2}, service watch = {3}.`nA real alert fires when the beat exceeds {4} minutes." -f $env:COMPUTERNAME, $ageMin, $version, $ServiceName, $ThresholdMinutes)
    Write-Log 'TEST alert sent.'
    return
}

if ($stale) {
    # Attempt recovery if the service is plainly stopped.
    $restartNote = ''
    try {
        $svc = Get-Service -Name $ServiceName -ErrorAction Stop
        if ($svc.Status -eq 'Stopped') {
            try { Start-Service -Name $ServiceName; Start-Sleep -Seconds 5
                  $restartNote = "Service was STOPPED - watchdog attempted a restart; status now: $((Get-Service $ServiceName).Status)." }
            catch { $restartNote = "Service was STOPPED - restart attempt FAILED: $($_.Exception.Message)" }
        }
        else { $restartNote = "Service status is '$($svc.Status)' but the heartbeat is stale - likely hung; manual attention needed." }
    }
    catch { $restartNote = "Could not query service: $($_.Exception.Message)" }
    Write-Log $restartNote

    $lastAlert = [datetime]::Parse($state.LastAlertUtc).ToUniversalTime()
    if (($nowUtc - $lastAlert).TotalMinutes -ge $AlertCooldownMinutes) {
        Send-GraphMail "[ALERT] KOR Opportunities Worker heartbeat is STALE" `
            ("The Worker's DB heartbeat on {0} is {1} minutes old (threshold {2}).`nLast known version: {3}.`n{4}`n`nNext alert suppressed for {5} minutes. Log: {6}" -f $env:COMPUTERNAME, $ageMin, $ThresholdMinutes, $version, $restartNote, $AlertCooldownMinutes, $logFile)
        $state.LastAlertUtc = $nowUtc.ToString('o')
        Write-Log 'ALERT email sent.'
    }
    else { Write-Log 'Stale, but inside alert cooldown - no email.' }
    $state.WasStale = $true
    Set-State $state
}
else {
    if ($state.WasStale) {
        Send-GraphMail "[RECOVERED] KOR Opportunities Worker heartbeat is healthy" `
            ("The Worker heartbeat on {0} recovered: beat age {1} min, version {2}." -f $env:COMPUTERNAME, $ageMin, $version)
        Write-Log 'RECOVERY email sent.'
        $state.WasStale = $false
        Set-State $state
    }
}
