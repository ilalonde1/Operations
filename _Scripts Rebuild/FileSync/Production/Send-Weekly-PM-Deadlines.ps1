param([switch]$WhatIf)

# ============ CONFIG ============
$TenantID     = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID     = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"

$FromAddress  = "ilalonde@korstructural.com"   # MUST exist now; switch to weeklydeadlines@... later
$GlobalCc     = "ilalonde@korstructural.com"
$LogFile      = "C:\_APPS\FileSync\Production\Logs\Weekly_Deadlines_Send_Log.txt"

# Excel
$ExcelPath    = "C:\Users\app-admin\KOR - Structured Engineering\Kor Hub - Deltek Connection\Project Deadlines.xlsx"
$SheetName    = "Projects_Deadlines"
$PmCol        = "PM"
$DateCol      = "CustDateExpected"
$ProjCol      = "WBS1"
$CustIssuesCol  = "CustIssue"
$CustRemarksCol = "CustRemarks"

# PM emails (exact names as they appear in the sheet)
$PmEmail = @{
  "Andrea Neuviale"       = "andrean@korstructural.com"
  "Conor Murtagh"         = "cmurtagh@korstructural.com"
  "Griffin Dow"           = "gdow@korstructural.com"
  "James DesRoches"       = "jdesroches@korstructural.com"
  "Jason Stuart"          = "jstuart@korstructural.com"
  "Jeremy Atkinson"       = "jatkinson@korstructural.com"
  "John Bryson"           = "jbryson@korstructural.com"
  "John Markulin"         = "jmarkulin@korstructural.com"
  "Katherine Reid"        = "kreid@korstructural.com"
  "Kevin Wurmlinger"      = "kevinw@korstructural.com"
  "Omar Alcazar Pastrana" = "omara@korstructural.com"
  "Rory Beirne"           = "rbeirne@korstructural.com"
}
$IgnorePMs = @("Adrian Crowder","John Zickmantel")

# ============ UTIL ============
function Log-Message {
  param([string]$Message)
  $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
  "$ts - $Message" | Out-File -Append -FilePath $LogFile -Encoding UTF8
  Write-Host "$ts - $Message"
}

Add-Type -AssemblyName System.Web
function HtmlEnc($s){ if($null -eq $s){""} else { ([System.Web.HttpUtility]::HtmlEncode([string]$s)) -replace "\r?\n","<br>" } }

function Get-AccessToken {
  if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) { return $global:AccessToken }
  $Body = @{
    client_id     = $ClientID
    scope         = "https://graph.microsoft.com/.default"
    client_secret = $ClientSecret
    grant_type    = "client_credentials"
  }
  $resp = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method POST -ContentType "application/x-www-form-urlencoded" -Body $Body
  $global:AccessToken     = $resp.access_token
  $global:TokenExpiration = (Get-Date).AddSeconds($resp.expires_in - 60)
  $global:AccessToken
}

function Send-GraphMailHtml {
  param(
    [string]$FromSmtp,
    [string]$ToSmtp,
    [string]$Cc,
    [string]$Subject,
    [string]$HtmlBody
  )
  $headers = @{ "Authorization" = "Bearer $(Get-AccessToken)"; "Content-Type" = "application/json" }
  $toRecipients = @(@{ emailAddress = @{ address = $ToSmtp.Trim() } })
  $ccRecipients = @()
  if ($Cc) { ($Cc -split '[;,]') | % { $a=$_.Trim(); if($a){ $ccRecipients += @{ emailAddress=@{ address=$a } } } } }
  $payload = @{
    message = @{
      subject      = ([regex]::Replace($Subject, "[\r\n]+", " "))
      body         = @{ contentType="HTML"; content=$HtmlBody }
      toRecipients = $toRecipients
      ccRecipients = $ccRecipients
    }
    saveToSentItems = "true"
  } | ConvertTo-Json -Depth 10

  $uri = "https://graph.microsoft.com/v1.0/users/$([uri]::EscapeDataString($FromSmtp))/sendMail"
  if ($WhatIf){ Log-Message "WHATIF send From=$FromSmtp To=$ToSmtp CC=$Cc Subj='$Subject'"; return }
  Invoke-RestMethod -Uri $uri -Headers $headers -Method POST -Body $payload
  Log-Message "Sent From=$FromSmtp To=$ToSmtp CC=$Cc Subj='$Subject'"
}

# Ensure we always give Excel a local, unlocked xlsx (bypass SharePoint URL/placeholder/locks)
function Get-LocalReadableXlsx {
  param([string]$Path)
  try { Unblock-File -LiteralPath $Path -ErrorAction SilentlyContinue } catch {}
  $tmp = Join-Path $env:TEMP ("WeeklyDeadlines_" + [IO.Path]::GetFileName($Path))
  try {
    Copy-Item -LiteralPath $Path -Destination $tmp -Force
  } catch {
    Log-Message "ERROR: Failed to copy workbook to temp. $($_.Exception.Message)"
    return $null
  }
  return $tmp
}

function Read-ExcelRows {
  param([string]$Path,[string]$Sheet)
  if (-not $Path -or -not (Test-Path $Path)) { Log-Message "ERROR: Excel file not found: $Path"; return @() }

  $xl = $null; $wb = $null
  try {
    $xl = New-Object -ComObject Excel.Application
    $xl.Visible = $false

    # normal open; fallback to repair mode if needed (CorruptLoad=1)
    try {
      $wb = $xl.Workbooks.Open($Path, 0, $true)
    } catch {
      Log-Message "INFO: Normal open failed. Retrying with repair mode."
      $wb = $xl.Workbooks.Open($Path, 0, $true, $null,$null,$null,$null,$null,$null,$null,$null,$null,$null,$null,1)
    }

    $ws = $wb.Worksheets | ? { $_.Name -eq $Sheet } | Select-Object -First 1
    if (-not $ws) { Log-Message "ERROR: Sheet not found: $Sheet"; return @() }

    $used = $ws.UsedRange
    $v = $used.Value2
    if (-not $v) { Log-Message "WARN: UsedRange empty."; return @() }

    $headers = for ($c=1; $c -le $v.GetLength(1); $c++) { [string]$v[1,$c] }
    $rows = @()
    for ($r=2; $r -le $v.GetLength(0); $r++){
      $h=@{}
      for ($c=1; $c -le $headers.Count; $c++){
        $name = ($headers[$c-1] -as [string]).Trim()
        if (![string]::IsNullOrWhiteSpace($name)) { $h[$name] = $v[$r,$c] }
      }
      if ($h.Count -gt 0) { $rows += [pscustomobject]$h }
    }
    return $rows
  } catch {
    Log-Message "ERROR: Excel open/read failed. $($_.Exception.Message)"
    return @()
  } finally {
    if ($wb){ try { $wb.Close($true) | Out-Null } catch {} }
    if ($xl){ try { $xl.Quit() | Out-Null } catch {} }
    if ($xl){ try { [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null } catch {} }
  }
}

function Convert-ExcelDate {
  param($val)
  if ($val -is [double]) { [datetime]::FromOADate([double]$val) }
  else { try { [datetime]$val } catch { $null } }
}

function Get-WeekWindow {
  $today = Get-Date
  $daysToMonday = ([DayOfWeek]::Monday - $today.DayOfWeek); if ($daysToMonday -lt 0){ $daysToMonday += 7 }
  $monday = (Get-Date -Year $today.Year -Month $today.Month -Day $today.Day).AddDays($daysToMonday).Date
  [pscustomobject]@{ Start=$monday; End=$monday.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59) }
}

# ============ MAIN ============
function Send-WeeklyDeadlines {
  Log-Message "========== Weekly PM Deadlines run started =========="
  if ($WhatIf){ Log-Message "WHATIF mode - no emails will be sent." }

  # hydrate and avoid locks
  $localPath = Get-LocalReadableXlsx -Path $ExcelPath
  if (-not $localPath) { Log-Message "Aborting: could not prepare local workbook copy."; return }
  Log-Message "Reading Excel (local temp): $localPath  Sheet: $SheetName"

  $rows = Read-ExcelRows -Path $localPath -Sheet $SheetName
  if (-not $rows -or $rows.Count -eq 0) { Log-Message "Aborting: no rows read from Excel."; return }

  # Normalize to the fields we need
  $norm = foreach($row in $rows){
    $pm = $row.$PmCol; $d = Convert-ExcelDate $row.$DateCol
    if($pm -and $d){
      [pscustomobject]@{
        PM          = [string]$pm
        Date        = $d
        Proj        = ( $row.PSObject.Properties.Name -contains $ProjCol        ) ? ([string]$row.$ProjCol)        : ""
        CustIssue   = ( $row.PSObject.Properties.Name -contains $CustIssuesCol  ) ? ([string]$row.$CustIssuesCol)  : ""
        CustRemarks = ( $row.PSObject.Properties.Name -contains $CustRemarksCol ) ? ([string]$row.$CustRemarksCol) : ""
      }
    }
  }

  # Normalize PM names and recheck date type
  $norm = $norm | ForEach-Object {
    $_.PM = ($_.PM -replace '\s+', ' ').Trim()
    if ($_.Date -is [double]) { $_.Date = [datetime]::FromOADate($_.Date) }
    $_
  }

  $win = Get-WeekWindow
  Log-Message "Week window: $($win.Start.ToShortDateString()) -> $($win.End.ToShortDateString())"

  $maxSeen = ($norm | Select-Object -Expand Date | Measure-Object -Maximum).Maximum
  if ($maxSeen) { Log-Message "Freshest CustDateExpected read: $($maxSeen.ToShortDateString())" }

  $week = $norm | ? { $_.Date -ge $win.Start -and $_.Date -le $win.End }
  if (-not $week){ Log-Message "No deadlines for this week ($($win.Start.ToShortDateString()) - $($win.End.ToShortDateString()))."; return }

  $week = $week | ? { $IgnorePMs -notcontains $_.PM }

  # Log counts per PM
  $week | Group-Object PM | ForEach-Object {
    Log-Message ("PM bucket: {0} -> {1} item(s)" -f ($_.Name), $_.Count)
  }

  $groups = $week | Sort-Object PM, Date | Group-Object PM

  foreach($g in $groups){
    $pmName = ($g.Name -replace '\s+', ' ').Trim()
    $to = $PmEmail[$pmName]
    if(-not $to){ Log-Message "No email for PM '$pmName' - skipping."; continue }

    $rowsHtml = foreach($it in ($g.Group | Sort-Object Date, Proj)){
      $dateTxt = $it.Date.ToString('ddd, MMM d, yyyy')
      "<tr><td>$dateTxt</td><td>$(HtmlEnc $it.Proj)</td><td>$(HtmlEnc $it.CustIssue)</td><td>$(HtmlEnc $it.CustRemarks)</td></tr>"
    }

    $subject = "Weekly Project Deadlines for $pmName (Week of $($win.Start.ToString('MMM d')) - $($win.End.ToString('MMM d, yyyy')))"
    $html = @"
<!DOCTYPE html>
<html>
  <body style="font-family:Arial,Helvetica,sans-serif; font-size:13px; color:#222;">
    <p>Hi $(HtmlEnc $pmName),</p>
    <p>Here are your deadlines for the week of $($win.Start.ToString('ddd MMM d')) through $($win.End.ToString('ddd MMM d, yyyy')).</p>
    <table border="1" cellpadding="6" cellspacing="0" style="border-collapse:collapse; font-family:Arial,Helvetica,sans-serif; font-size:12px;">
      <thead>
        <tr style="background:#f2f2f2;">
          <th align="left">Date</th><th align="left">Project</th><th align="left">CustIssue</th><th align="left">CustRemarks</th>
        </tr>
      </thead>
      <tbody>
        $($rowsHtml -join "`n")
      </tbody>
    </table>
    <p style="margin-top:16px;">If anything is missing or needs adjustment, please email Ian - ilalonde@korstructural.com</p>
    <p>Thanks,<br>KOR Automated Deadlines</p>
  </body>
</html>
"@
    Send-GraphMailHtml -FromSmtp $FromAddress -ToSmtp $to -Cc $GlobalCc -Subject $subject -HtmlBody $html
  }

  Log-Message "========== Weekly PM Deadlines run complete =========="
}

Send-WeeklyDeadlines
