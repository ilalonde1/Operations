param(
    # Month to process in yyyy-MM format. Default = previous calendar month.
    [string]$Month = $(Get-Date (Get-Date).AddMonths(-1) -Format 'yyyy-MM'),

    # Source drop where reports are downloaded (ROOT ONLY, no recursion)
    [string]$SourceRoot = '\\KOR-FS01\Library\ADMIN\Concrete Test Reports',

    # Excel mapping file (sheet 'CTR')
    [string]$MappingXlsx = '\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Notes\Concrete Test Report Data.xlsx',

    # Where to write summaries and logs
    [string]$OutputRoot = '\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Processed',

    # If set, do not overwrite identical file names. Instead append (1), (2), ...
    [switch]$RenameOnConflict,

    # Optional: disable emailing for this run
    [switch]$NoEmail,

    # Dry run (no file moves, no actual email sends)
    [switch]$WhatIf
)

# ================== CONSTANTS (hard coded) ==================
# Sender mailbox
$FromAddress = 'app-admin@korstructural.com'

# Microsoft Graph app (client credentials) - hard coded
$TenantID     = 'd9be1f7f-aacf-461a-8d1b-5528b86d540f'
$ClientID     = '5b20a407-0b59-4c75-b2e5-d2cf970c5dbd'
$ClientSecret = 'lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw'

# Global CC (hard coded)
$GlobalCc = 'app-admin@korstructural.com'

# Hard-coded PM email map (PowerShell hashtable = case-insensitive, supports .ContainsKey)
$PmEmailMap = @{
  'Andrea Neuviale'       = 'andrean@korstructural.com'
  'Conor Murtagh'         = 'cmurtagh@korstructural.com'
  'Griffin Dow'           = 'gdow@korstructural.com'
  'James DesRoches'       = 'jdesroches@korstructural.com'
  'Jason Stuart'          = 'jstuart@korstructural.com'
  'Jeremy Atkinson'       = 'jatkinson@korstructural.com'
  'John Bryson'           = 'jbryson@korstructural.com'
  'John Markulin'         = 'jmarkulin@korstructural.com'
  'Katherine Reid'        = 'kreid@korstructural.com'
  'Kevin Wurmlinger'      = 'kevinw@korstructural.com'
  'Omar Alcazar Pastrana' = 'omara@korstructural.com'
  'Rory Beirne'           = 'rbeirne@korstructural.com'
}

# Optional nickname/alias normalization (before lookup)
$PmAliases = @{
  'Jim DesRoches' = 'James DesRoches'
}

# ================== CONFIG ==================
# File types to move
$Extensions = @('.pdf', '.PDF')

# Logging and month window
$MonthStart = [datetime]::ParseExact($Month + '-01','yyyy-MM-dd',$null)
$MonthEnd   = $MonthStart.AddMonths(1)
$MonthTag   = $MonthStart.ToString('yyyy-MM')
$RunRoot    = Join-Path $OutputRoot $MonthTag
$LogPath    = Join-Path $RunRoot   "File_CTR_$($MonthTag).log"
$MasterCsv  = Join-Path $RunRoot   "Summary_Master_$($MonthTag).csv"
$ByPmDir    = Join-Path $RunRoot   'ByPM'

New-Item -ItemType Directory -Force -Path $RunRoot,$ByPmDir | Out-Null

function Write-Log {
    param([string]$msg)
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    $line | Tee-Object -FilePath $LogPath -Append
}

Write-Log "== START run for $MonthTag =="

# ================== HELPERS ==================
function SafeJoin([string]$folder, [string]$leaf) {
    $folder = $folder.TrimEnd('\')
    return "$folder\$leaf"
}

function Ensure-Folder([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        if ($WhatIf) {
            Write-Log "WhatIf: would create folder: $path"
        } else {
            New-Item -ItemType Directory -Force -Path $path | Out-Null
        }
    }
}

function Move-WithPolicy {
    param(
        [string]$src,
        [string]$dst,
        [switch]$RenameOnConflict
    )
    if ($WhatIf) {
        if (Test-Path -LiteralPath $dst -PathType Leaf) {
            if ($RenameOnConflict) {
                Write-Log "WhatIf: would rename and move: '$src' -> '$dst' (unique)"
            } else {
                Write-Log "WhatIf: would overwrite: '$src' -> '$dst'"
            }
        } else {
            Write-Log "WhatIf: would move: '$src' -> '$dst'"
        }
        return
    }

    $target = $dst
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        if (-not $RenameOnConflict) {
            Copy-Item -LiteralPath $src -Destination $target -Force
            Remove-Item -LiteralPath $src -Force
            return
        }
        $dir  = Split-Path $target -Parent
        $name = [System.IO.Path]::GetFileNameWithoutExtension($target)
        $ext  = [System.IO.Path]::GetExtension($target)
        $n = 1
        do {
            $candidate = SafeJoin $dir ("{0} ({1}){2}" -f $name,$n,$ext)
            $n++
        } while (Test-Path -LiteralPath $candidate)
        $target = $candidate
    }
    Move-Item -LiteralPath $src -Destination $target
}

function Remove-Diacritics([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $s }
    $formD = $s.Normalize([Text.NormalizationForm]::FormD)
    $filtered = New-Object System.Text.StringBuilder
    foreach ($ch in $formD.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$filtered.Append($ch)
        }
    }
    return $filtered.ToString().Normalize([Text.NormalizationForm]::FormC)
}

function Normalize-PmName([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $raw }
    $t = Remove-Diacritics($raw.Trim())
    # If it looks like "Last, First Middle", flip to "First Middle Last"
    if ($t -match '^[^,]+,\s*.+$') {
        $parts = $t -split ',', 2
        $last = $parts[0].Trim()
        $first = $parts[1].Trim()
        $t = "$first $last"
    }
    # Compress spaces
    $t = ($t -split '\s+' -ne '') -join ' '
    return $t
}

function HtmlEncode([string]$s) {
    if ($null -eq $s) { return '' }
    # Lightweight encoder to avoid System.Web dependency
    $s = $s -replace '&','&amp;'
    $s = $s -replace '<','&lt;'
    $s = $s -replace '>','&gt;'
    $s = $s -replace '"','&quot;'
    $s = $s -replace '''','&#39;'
    return $s
}

# ================== LOAD MAPPING (COM) ==================
function Load-MappingRows {
    param([string]$xlsxPath, [string]$sheetName = 'CTR')

    # Expects these exact headers in row 1:
    # Project No.
    # Project Manager
    # Project Name
    # Concrete Testing Engineer
    # Concrete Testing Project Number
    # Concrete Testing Project Name
    # CTR Folder on Projects Drive

    $excel = $null
    $wb    = $null
    $ws    = $null
    $rows  = @()
    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
        $wb = $excel.Workbooks.Open($xlsxPath)
        $ws = $wb.Worksheets.Item($sheetName)

        $used = $ws.UsedRange
        $rMax = $used.Rows.Count
        $cMax = $used.Columns.Count

        $headers = @{}
        for ($c=1; $c -le $cMax; $c++) {
            $h = [string]$ws.Cells.Item(1,$c).Text
            if (-not [string]::IsNullOrWhiteSpace($h)) {
                $headers[$h.Trim()] = $c
            }
        }

        $req = @(
            'Project No.',
            'Project Manager',
            'Project Name',
            'Concrete Testing Engineer',
            'Concrete Testing Project Number',
            'Concrete Testing Project Name',
            'CTR Folder on Projects Drive'
        )
        foreach ($k in $req) {
            if (-not $headers.ContainsKey($k)) { throw "Missing expected column header: '$k'" }
        }

        for ($r=2; $r -le $rMax; $r++) {
            $projNo   = [string]$ws.Cells.Item($r, $headers['Project No.']).Text
            $pmRaw    = [string]$ws.Cells.Item($r, $headers['Project Manager']).Text
            $projName = [string]$ws.Cells.Item($r, $headers['Project Name']).Text
            $agency   = [string]$ws.Cells.Item($r, $headers['Concrete Testing Engineer']).Text
            $agencyNo = [string]$ws.Cells.Item($r, $headers['Concrete Testing Project Number']).Text
            $agencyPn = [string]$ws.Cells.Item($r, $headers['Concrete Testing Project Name']).Text
            $ctrPath  = [string]$ws.Cells.Item($r, $headers['CTR Folder on Projects Drive']).Text

            if ([string]::IsNullOrWhiteSpace($projNo) -and [string]::IsNullOrWhiteSpace($ctrPath)) { continue }

            $rows += [pscustomobject]@{
                ProjectNo   = $projNo.Trim()
                ProjectMgr  = (Normalize-PmName $pmRaw)
                ProjectName = $projName.Trim()
                Agency      = $agency.Trim()
                AgencyNo    = $agencyNo.Trim()
                AgencyName  = $agencyPn.Trim()
                TargetRoot  = $ctrPath.Trim()   # trust XLSX path as-is
            }
        }
    }
    finally {
        if ($wb)    { $wb.Close($false); [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb)    | Out-Null }
        if ($excel) { $excel.Quit();      [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null }
        [gc]::Collect(); [gc]::WaitForPendingFinalizers()
    }
    return $rows
}

Write-Log "Loading mapping from: $MappingXlsx"
$mapping = Load-MappingRows -xlsxPath $MappingXlsx
$mapping = $mapping | Where-Object { -not [string]::IsNullOrWhiteSpace($_.TargetRoot) }
Write-Log ("Loaded {0} mapping rows with target folders." -f ($mapping.Count))

# Build lookups for matching by agency project number / name
$byAgencyNo = @{}
foreach ($m in $mapping) {
    if (-not [string]::IsNullOrWhiteSpace($m.AgencyNo)) {
        $key = $m.AgencyNo.ToLower()
        if (-not $byAgencyNo.ContainsKey($key)) { $byAgencyNo[$key] = @() }
        $byAgencyNo[$key] += $m
    }
}
function Tokenize([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return @() }
    return ($s -split '[^A-Za-z0-9]+' | Where-Object { $_.Length -ge 3 }) | ForEach-Object { $_.ToLower() }
}
$byAgencyTokens = @{}
foreach ($m in $mapping) {
    foreach ($t in (Tokenize $m.AgencyName)) {
        if (-not $byAgencyTokens.ContainsKey($t)) { $byAgencyTokens[$t] = @() }
        $byAgencyTokens[$t] += $m
    }
}

# ================== COLLECT SOURCE FILES (ROOT ONLY) ==================
if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
    throw "SourceRoot not found: $SourceRoot"
}
Write-Log "Scanning SOURCE ROOT ONLY (no recursion): $SourceRoot for $MonthTag"

$allFiles = Get-ChildItem -LiteralPath $SourceRoot -File -ErrorAction SilentlyContinue `
    | Where-Object {
        $Extensions -contains $_.Extension -and
        $_.LastWriteTime -ge $MonthStart -and
        $_.LastWriteTime -lt $MonthEnd
    }

Write-Log ("Found {0} candidate file(s) for month {1} in root only." -f $allFiles.Count, $MonthTag)

# ================== MATCHING + MOVING ==================
$summary = New-Object System.Collections.Generic.List[object]
$pmProjects = @{}    # PM -> HashSet of ProjectNo
$projectCounts = @{} # ProjectNo -> count

function Add-PmProject([string]$pm,[string]$proj) {
    if (-not $pmProjects.ContainsKey($pm)) {
        $pmProjects[$pm] = New-Object 'System.Collections.Generic.HashSet[string]'
    }
    [void]$pmProjects[$pm].Add($proj)
}

foreach ($f in $allFiles) {
    $nameLower = $f.Name.ToLower()

    # 1) AgencyNo containment
    $candidates = @()
    foreach ($k in $byAgencyNo.Keys) {
        if ($nameLower -like "*$k*") { $candidates += $byAgencyNo[$k] }
    }
    $candidates = $candidates | Select-Object -Unique

    # 2) Fallback: agency name tokens
    if (-not $candidates -or $candidates.Count -eq 0) {
        $hits = @()
        foreach ($t in ($nameLower -split '[^a-z0-9]+')) {
            if ($byAgencyTokens.ContainsKey($t)) { $hits += $byAgencyTokens[$t] }
        }
        $candidates = ($hits | Select-Object -Unique)
    }

    if (-not $candidates -or $candidates.Count -eq 0) {
        Write-Log "UNMATCHED: $($f.FullName)"
        $summary.Add([pscustomobject]@{
            Month        = $MonthTag
            Status       = 'Unmatched'
            SourceFile   = $f.FullName
            ProjectNo    = ''
            ProjectName  = ''
            ProjectMgr   = ''
            TargetFolder = ''
            Action       = ''
        })
        continue
    }

    # Prefer candidates whose Agency string is also present in the filename
    $disambig = $candidates
    $better = $disambig | Where-Object {
        $ag = $_.Agency
        -not [string]::IsNullOrWhiteSpace($ag) -and ($nameLower -like "*$($ag.ToLower())*")
    }
    if ($better.Count -gt 0) { $disambig = $better }

    if ($disambig.Count -gt 1) {
        $projList = ($disambig | ForEach-Object { $_.ProjectNo }) -join ', '
        Write-Log "AMBIGUOUS (skip): $($f.Name) -> candidates: $projList"
        $summary.Add([pscustomobject]@{
            Month        = $MonthTag
            Status       = 'Ambiguous'
            SourceFile   = $f.FullName
            ProjectNo    = $projList
            ProjectName  = ''
            ProjectMgr   = ''
            TargetFolder = ''
            Action       = 'Manual review'
        })
        continue
    }

    $m = $disambig[0]
    if ([string]::IsNullOrWhiteSpace($m.TargetRoot)) {
        Write-Log "NO TARGET FOLDER in mapping for project $($m.ProjectNo). Skip $($f.Name)"
        $summary.Add([pscustomobject]@{
            Month        = $MonthTag
            Status       = 'NoTarget'
            SourceFile   = $f.FullName
            ProjectNo    = $m.ProjectNo
            ProjectName  = $m.ProjectName
            ProjectMgr   = $m.ProjectMgr
            TargetFolder = ''
            Action       = 'Mapping fix needed'
        })
        continue
    }

    Ensure-Folder $m.TargetRoot
    $destPath = SafeJoin $m.TargetRoot $f.Name

    # Determine action label once
    $action = if ($RenameOnConflict) { 'Move (rename if needed)' } else { 'Move/Overwrite' }

    # Move (respects -WhatIf internally)
    Move-WithPolicy -src $f.FullName -dst $destPath -RenameOnConflict:$RenameOnConflict

    # Log accurately depending on -WhatIf
    if ($WhatIf) {
        Write-Log ("WOULD MOVE: {0} -> {1}" -f $f.FullName, $destPath)
    } else {
        Write-Log ("MOVED: {0} -> {1}" -f $f.FullName, $destPath)
    }

    # Add to summary
    $summary.Add([pscustomobject]@{
        Month        = $MonthTag
        Status       = if ($WhatIf) { 'WouldMove' } else { 'Moved' }
        SourceFile   = $f.FullName
        ProjectNo    = $m.ProjectNo
        ProjectName  = $m.ProjectName
        ProjectMgr   = $m.ProjectMgr
        TargetFolder = $m.TargetRoot
        Action       = $action
    })

    # PM/project rollups
    Add-PmProject $m.ProjectMgr $m.ProjectNo
    if (-not $projectCounts.ContainsKey($m.ProjectNo)) { $projectCounts[$m.ProjectNo] = 0 }
    $projectCounts[$m.ProjectNo]++
}

# ================== SUMMARIES ==================
$summary | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $MasterCsv
Write-Log "Wrote master summary: $MasterCsv"

# Collate by unique project for the month (Moved or WouldMove)
$byProj = @{}
foreach ($row in $summary | Where-Object { $_.Status -match 'Moved$' -or $_.Status -eq 'WouldMove' }) {
    $byProj[$row.ProjectNo] = $row
}

# Per-PM CSVs
foreach ($pm in $pmProjects.Keys) {
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($projNo in $pmProjects[$pm]) {
        if (-not $byProj.ContainsKey($projNo)) { continue }
        $r = $byProj[$projNo]
        $count = $projectCounts[$projNo]
        $rows.Add([pscustomobject]@{
            Month        = $MonthTag
            ProjectNo    = $r.ProjectNo
            ProjectName  = $r.ProjectName
            ReportsMoved = $count
            TargetFolder = $r.TargetFolder
        })
    }
    if ($rows.Count -gt 0) {
        $out = $rows | Sort-Object ProjectNo
        $safePm = ($pm -replace '[^\w.-]', '_')
        $pmCsv  = Join-Path $ByPmDir ("{0}_{1}.csv" -f $safePm, $MonthTag)
        $out | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $pmCsv
        Write-Log "PM summary: $pm -> $pmCsv"
    }
}

# Catch-all "Unassigned" CSV (projects that received reports but PM is blank or not in $PmEmailMap)
$unassignedRows = New-Object System.Collections.Generic.List[object]
foreach ($projNo in $byProj.Keys) {
    $r = $byProj[$projNo]
    $pmKey = Normalize-PmName $r.ProjectMgr
    if ($PmAliases.ContainsKey($pmKey)) { $pmKey = $PmAliases[$pmKey] }

    $needsCatchAll = [string]::IsNullOrWhiteSpace($pmKey) -or (-not $PmEmailMap.ContainsKey($pmKey))
    if ($needsCatchAll) {
        $count = $projectCounts[$projNo]
        $unassignedRows.Add([pscustomobject]@{
            Month        = $MonthTag
            ProjectNo    = $r.ProjectNo
            ProjectName  = $r.ProjectName
            ReportsMoved = $count
            TargetFolder = $r.TargetFolder
            ProjectMgr   = $r.ProjectMgr  # whatever was in mapping (may be blank or non-mapped)
        })
    }
}
if ($unassignedRows.Count -gt 0) {
    $unCsv = Join-Path $ByPmDir ("Unassigned_{0}.csv" -f $MonthTag)
    ($unassignedRows | Sort-Object ProjectNo) | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $unCsv
    Write-Log "Unassigned summary -> $unCsv"
}

# ================== EMAIL (always unless -NoEmail) ==================
if (-not $NoEmail) {
    Write-Log "Email step enabled."

    function Get-GraphToken([string]$tenant,[string]$client,[string]$secret) {
        $body = @{
            client_id     = $client
            client_secret = $secret
            scope         = 'https://graph.microsoft.com/.default'
            grant_type    = 'client_credentials'
        }
        $uri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token"
        return Invoke-RestMethod -Method Post -Uri $uri -Body $body
    }

    function Send-GraphMail {
        param(
            [string]$AccessToken,
            [string]$FromAddress,
            [string]$To,
            [string]$Subject,
            [string]$HtmlBody,
            [string[]]$AttachmentPaths = @(),
            [string]$Cc = $null
        )
        $attachments = @()
        foreach ($p in $AttachmentPaths) {
            if (Test-Path -LiteralPath $p -PathType Leaf) {
                $bytes = [System.IO.File]::ReadAllBytes($p)
                $b64   = [System.Convert]::ToBase64String($bytes)
                $attachments += @{
                    "@odata.type" = "#microsoft.graph.fileAttachment"
                    "name"        = [System.IO.Path]::GetFileName($p)
                    "contentType" = "text/csv"
                    "contentBytes"= $b64
                }
            }
        }

        $payload = @{
            message = @{
                subject = $Subject
                body = @{
                    contentType = "HTML"
                    content     = $HtmlBody
                }
                toRecipients = @(@{emailAddress = @{address = $To}})
                attachments = $attachments
            }
            saveToSentItems = $true
        }

        if ($Cc) {
            $payload.message['ccRecipients'] = @(@{emailAddress = @{address = $Cc}})
        }

        $headers = @{
            Authorization = "Bearer $AccessToken"
            'Content-Type' = 'application/json'
        }

        $url = "https://graph.microsoft.com/v1.0/users/$FromAddress/sendMail"
        Invoke-RestMethod -Method Post -Uri $url -Headers $headers -Body ($payload | ConvertTo-Json -Depth 6)
    }

    $token = $null
    try {
        $token = Get-GraphToken -tenant $TenantID -client $ClientID -secret $ClientSecret
        Write-Log "Obtained Graph access token."
    } catch {
        Write-Log "Failed to obtain Graph token: $($_.Exception.Message)"
    }

    if ($token) {
        foreach ($pm in $pmProjects.Keys) {
            $safePm = ($pm -replace '[^\w.-]', '_')
            $pmCsvPath = Join-Path $ByPmDir ("{0}_{1}.csv" -f $safePm, $MonthTag)

            if (-not (Test-Path -LiteralPath $pmCsvPath -PathType Leaf)) {
                Write-Log "Skip email for $pm (no CSV found)."
                continue
            }

            # Resolve PM email using normalized PM name against hard-coded map
            $pmKey = Normalize-PmName $pm
            if ($PmAliases.ContainsKey($pmKey)) { $pmKey = $PmAliases[$pmKey] }

            $to = $null
            if ($PmEmailMap.ContainsKey($pmKey)) {
                $to = $PmEmailMap[$pmKey]
            } else {
                Write-Log "No email mapping for PM '$pmKey'. Skipping."
                continue
            }

            # Build HTML body
            $rows = Import-Csv -Path $pmCsvPath
            if ($rows.Count -eq 0) {
                Write-Log "Skip email for $pm (CSV is empty)."
                continue
            }

            $tbl = "<table border='1' cellspacing='0' cellpadding='4' style='border-collapse:collapse;font-family:Segoe UI,Arial;font-size:12px;'>"
            $tbl += "<tr><th>ProjectNo</th><th>ProjectName</th><th>ReportsMoved</th><th>Folder</th></tr>"
            foreach ($r in $rows) {
                $folder = HtmlEncode $r.TargetFolder
                $projNameHtml = HtmlEncode $r.ProjectName
                $tbl += "<tr><td>$($r.ProjectNo)</td><td>$projNameHtml</td><td style='text-align:center;'>$($r.ReportsMoved)</td><td><a href=""$folder"">$folder</a></td></tr>"
            }
            $tbl += "</table>"

            $subj = "Concrete Test Reports - $MonthTag - $pmKey"
            $body = @"
<p>Hello $pmKey,</p>
<p>Here are the projects that received concrete test reports in <b>$MonthTag</b>. Your CSV is attached; a quick view is below.</p>
$tbl
<p>- Auto-filed via CTR script</p>
"@

            if ($WhatIf) {
                Write-Log "WOULD EMAIL: To=$to  Subject='$subj'  Attach='$pmCsvPath'  Cc='$GlobalCc'"
            } else {
                try {
                    Send-GraphMail -AccessToken $token.access_token -FromAddress $FromAddress -To $to -Subject $subj -HtmlBody $body -AttachmentPaths @($pmCsvPath) -Cc $GlobalCc
                    Write-Log "Emailed PM: $pmKey <$to>  ($pmCsvPath)"
                } catch {
                    Write-Log "FAILED to email PM '$pmKey' <$to>: $($_.Exception.Message)"
                }
            }
        }
    }
} else {
    Write-Log "Email step disabled for this run (-NoEmail)."
}

Write-Log "== END run for $MonthTag =="
