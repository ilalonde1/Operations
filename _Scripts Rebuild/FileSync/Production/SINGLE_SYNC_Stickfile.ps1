param (
    [string]$GivenPath
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ==============================
# CONFIG
# ==============================
$TenantID     = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID     = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID       = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID      = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile      = "C:\_APPS\FileSync\Production\Logs\Single_Stickfile_Sync_Log.txt"

# ==============================
# LOGGING (UTF-8 with BOM)
# ==============================
$script:LogLock = New-Object object

function Initialize-Log {
    $dir = Split-Path $LogFile -Parent
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $LogFile)) {
        $utf8bom = New-Object System.Text.UTF8Encoding($true)
        $sw = New-Object System.IO.StreamWriter($LogFile, $false, $utf8bom)
        $sw.WriteLine("{0} - Log created" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
        $sw.Close()
    }
}

function Log-Message {
    param([string]$Message)
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "$ts - $Message"
    $utf8bom = New-Object System.Text.UTF8Encoding($true)
    try {
        [System.Threading.Monitor]::Enter($script:LogLock)
        $sw = New-Object System.IO.StreamWriter($LogFile, $true, $utf8bom)
        $sw.WriteLine($line)
        $sw.Close()
    } finally {
        [System.Threading.Monitor]::Exit($script:LogLock)
    }
}

Initialize-Log

# Helpful: surface Graph error body text when 4xx/5xx happens
function Get-HttpErrorText {
    param($ErrorRecord)
    try {
        $resp = $ErrorRecord.Exception.Response
        if ($resp -and $resp.GetResponseStream) {
            $sr = New-Object IO.StreamReader($resp.GetResponseStream())
            return $sr.ReadToEnd()
        }
    } catch {}
    return "$ErrorRecord"
}

# ==============================
# AUTH
# ==============================
function Get-AccessToken {
    if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) { return $global:AccessToken }
    $Body = @{
        client_id     = $ClientID
        scope         = "https://graph.microsoft.com/.default"
        client_secret = $ClientSecret
        grant_type    = "client_credentials"
    }
    try {
        $r = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
        $global:AccessToken = $r.access_token
        $global:TokenExpiration = (Get-Date).AddSeconds($r.expires_in - 60)
        return $global:AccessToken
    } catch {
        Log-Message "ERROR: Token fetch failed - $(Get-HttpErrorText $_)"
        exit 1
    }
}
function Get-AuthHeader { @{ "Authorization" = "Bearer $(Get-AccessToken)" } }

# Correct header merge (avoid hashtable '+')
function New-AuthHeaders {
    param([string]$ContentType)
    $h = Get-AuthHeader
    if ($ContentType) { $h["Content-Type"] = $ContentType }
    return $h
}

# ==============================
# FOLDERS
# ==============================
function Test-SharePointFolder {
    param([string]$FolderPath)
    $effective = if ([string]::IsNullOrWhiteSpace($FolderPath)) { "/" } else { $FolderPath }
    $uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($effective))"
    try {
        Invoke-RestMethod -Uri $uri -Headers (Get-AuthHeader) -Method Get -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Ensure-SharePointFolder {
    param([string]$FolderPath)
    if (Test-SharePointFolder -FolderPath $FolderPath) { Log-Message "Skipping folder creation (already exists): $FolderPath"; return }
    Log-Message "Creating missing folder: $FolderPath"

    $parts = $FolderPath -split "/"
    if ($parts.Count -lt 2) { $parent=""; $name=$parts[0] } else { $parent = $parts[0..($parts.Count-2)] -join "/"; $name=$parts[-1] }

    $body = @{ name=$name; folder=@{}; "@microsoft.graph.conflictBehavior"="fail" } | ConvertTo-Json
    $effectiveParent = if ([string]::IsNullOrWhiteSpace($parent)) { "/" } else { $parent }
    $uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($effectiveParent)):/children"

    try {
        Invoke-RestMethod -Uri $uri -Headers (New-AuthHeaders -ContentType "application/json") -Method Post -Body $body -ErrorAction Stop | Out-Null
        Log-Message "Created folder: $FolderPath"
        Start-Sleep -Seconds 2
    } catch {
        $msg = Get-HttpErrorText $_
        if ($msg -match "nameAlreadyExists") {
            Log-Message "Skipping folder creation (already exists): $FolderPath"
        } elseif ($msg -match "resourceModified") {
            Log-Message "WARNING: resourceModified; rechecking $FolderPath"
            Start-Sleep -Seconds 2
            if (-not (Test-SharePointFolder -FolderPath $FolderPath)) { Log-Message "ERROR: Folder not confirmed: $FolderPath"; exit 1 }
        } else {
            Log-Message "ERROR: Create folder failed ($FolderPath) - $msg"; exit 1
        }
    }
}

# ==============================
# LISTING
# ==============================
function Get-SharePointFiles {
    param([string]$FolderPath)
    $uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($FolderPath)):/children?`$select=id,name,size,file,folder,fileSystemInfo&`$top=200"
    try {
        (Invoke-RestMethod -Uri $uri -Headers (Get-AuthHeader) -Method Get -ErrorAction Stop).value
    } catch {
        Log-Message "ERROR: List SP files failed - $(Get-HttpErrorText $_)"
        @()
    }
}

# ==============================
# UPLOAD DECISION + RETRY
# ==============================
function Should-Upload {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo] $LocalFile,
        $SpItem
    )
    if (-not $SpItem) { return $true }
    if ($LocalFile.Length -ne [int64]$SpItem.size) { return $true }

    # Only upload if local is meaningfully newer than SP (handle rounding/lag)
    $skewSeconds = 15
    try {
        $spUtc = [datetimeoffset]::Parse($SpItem.fileSystemInfo.lastModifiedDateTime).UtcDateTime
    } catch {
        # If SP timestamp isn't usable, assume it's fine (avoid re-upload storm)
        return $false
    }
    $localUtc = $LocalFile.LastWriteTimeUtc
    return ($localUtc -gt $spUtc.AddSeconds($skewSeconds))
}

function Invoke-UploadWithRetry {
    param(
        [string]$SPFilePath, [string]$LocalPath, [int]$MaxTries = 4, [int]$DelayMs = 750
    )
    $headers = New-AuthHeaders -ContentType "application/octet-stream"
    $uri = "https://graph.microsoft.com/v1.0/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) ):/content"
    for ($i=1; $i -le $MaxTries; $i++) {
        try {
            return Invoke-RestMethod -Uri $uri -Headers $headers -Method Put -InFile $LocalPath -ErrorAction Stop
        } catch {
            $msg = Get-HttpErrorText $_
            if ($msg -match 'being used by another process' -or $msg -match 'The process cannot access the file') {
                if ($i -lt $MaxTries) { Start-Sleep -Milliseconds $DelayMs; continue }
            }
            throw ($msg)
        }
    }
}

function Upload-SharePointFiles {
    param ($FilesToUpload, [string]$FolderPath)

    $spItems = @(Get-SharePointFiles -FolderPath $FolderPath)
    $byName  = @{}
    foreach ($it in $spItems) { $byName[$it.name.ToLower()] = $it }

    foreach ($f in $FilesToUpload) {
        $spPath = "$FolderPath/$($f.Name)"
        $sp = $byName[$f.Name.ToLower()]

        if (Should-Upload -LocalFile $f -SpItem $sp) {
            if ($sp) {
                if ($f.Length -ne [int64]$sp.size) { Log-Message "Uploading (size mismatch) L=$($f.Length) SP=$($sp.size): $($f.Name)" }
                else { Log-Message "Uploading (newer timestamp, same size): $($f.Name)" }
            } else {
                Log-Message "Uploading (new file): $($f.Name)"
            }
            try {
                $resp = Invoke-UploadWithRetry -SPFilePath $spPath -LocalPath $f.FullName
                Log-Message "Uploaded: $($f.Name)"
                if ($resp.webUrl) { Log-Message "SP URL: $($resp.webUrl)" }

                # Update in-memory index so we don’t re-evaluate again in the same run
                $nowUtc = (Get-Date).ToUniversalTime().ToString("o")
                $byName[$f.Name.ToLower()] = @{
                    name = $f.Name
                    size = [int64]$f.Length
                    file = @{ mimeType = "application/pdf" }
                    fileSystemInfo = @{ lastModifiedDateTime = $nowUtc }
                }
            } catch {
                Log-Message "ERROR: Upload failed $($f.Name) - $_"
            }
        } else {
            Log-Message "Skipping upload (same size & not newer): $($f.Name)"
        }
    }
}

# ==============================
# DELETE (robust)
# ==============================
function Invoke-DeleteWithRetry {
    param([string]$ItemId, [string]$SPPath, [int]$MaxTries = 3)
    for ($i=1; $i -le $MaxTries; $i++) {
        try {
            Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/drives/$DriveID/items/$ItemId" -Headers (Get-AuthHeader) -Method Delete -ErrorAction Stop
            return $true
        } catch {
            $msg = Get-HttpErrorText $_
            if ($msg -match "accessDenied") { Log-Message "ERROR: Retention/permissions prevented delete: $SPPath [$ItemId]"; return $false }
            if ($i -lt $MaxTries) { Start-Sleep -Seconds 1; continue }
            try {
                $uri = "https://graph.microsoft.com/v1.0/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPPath) )"
                Invoke-RestMethod -Uri $uri -Headers (Get-AuthHeader) -Method Delete -ErrorAction Stop
                return $true
            } catch {
                Log-Message "ERROR: Delete failed for $SPPath [$ItemId] - $(Get-HttpErrorText $_)"
                return $false
            }
        }
    }
}

function Delete-SharePointFiles {
    param ($FilesToDelete, [string]$FolderPath)
    foreach ($item in $FilesToDelete) {
        $spPath = "$FolderPath/$($item.name)"
        Log-Message "Deleting: $($item.name)"
        [void](Invoke-DeleteWithRetry -ItemId $item.id -SPPath $spPath)
    }
}

# ==============================
# MAIN
# ==============================
Log-Message "DEBUG: Given Path - '$GivenPath'"

if (-not $GivenPath) { Log-Message "ERROR: GivenPath is null/empty"; exit 1 }

if (-not (Test-Path -LiteralPath $GivenPath)) {
    Log-Message "WARNING: Test-Path failed on: $GivenPath"
    try { Get-ChildItem -LiteralPath $GivenPath -ErrorAction Stop | Out-Null; Log-Message "OVERRIDE: GCI succeeded, proceeding" }
    catch { Log-Message "ERROR: GCI failed, aborting"; exit 1 }
}

# Project folder = parent of "05 Stickfile"
$ProjectDir  = Split-Path -Path $GivenPath -Parent
$ProjectName = Split-Path -Path $ProjectDir -Leaf

# DEST: SharePoint project ROOT (change next line to "$SPRoot/Reports" if you prefer uploads into Reports)
$SPRoot   = $ProjectName
$SPFolder = $SPRoot

Log-Message "Extracted Project Name: $ProjectName"
Log-Message "Constructed SharePoint Path (root): '$SPFolder'"

# Ensure folders (root + Reports)
Ensure-SharePointFolder -FolderPath $SPRoot
Ensure-SharePointFolder -FolderPath "$SPRoot/Reports"
Log-Message "Ensured project root and 'Reports' under $SPRoot"

# Local PDFs
$LocalFiles = @(Get-ChildItem -LiteralPath $GivenPath -File -Filter "*.pdf")
Upload-SharePointFiles -FilesToUpload $LocalFiles -FolderPath $SPFolder

# Remote PDFs
$SPFiles = @(Get-SharePointFiles -FolderPath $SPFolder)
$remotePdfFiles = $SPFiles | Where-Object { $_.file -and ([IO.Path]::GetExtension($_.name)).ToLower() -eq ".pdf" }

# Compare & delete
$localNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($f in $LocalFiles) { [void]$localNames.Add($f.Name) }

$FilesToDelete = @()
foreach ($it in $remotePdfFiles) {
    if (-not $localNames.Contains($it.name)) { $FilesToDelete += $it }
}

Log-Message ("Local PDFs: {0}, SP PDFs: {1}, ToDelete: {2}" -f $LocalFiles.Count, $remotePdfFiles.Count, $FilesToDelete.Count)
if ($FilesToDelete.Count -gt 0) {
    Log-Message ("Will delete: " + ($FilesToDelete.name -join ", "))
    Delete-SharePointFiles -FilesToDelete $FilesToDelete -FolderPath $SPFolder
}

Log-Message "Sync complete."
