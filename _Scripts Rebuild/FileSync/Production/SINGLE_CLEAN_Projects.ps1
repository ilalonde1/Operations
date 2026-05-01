param(
    [string]$GivenPath,        # e.g. \\KOR-FS01\Projects\Projects\03 Residential\31003-01 (Sendero ... )
    [string]$ProjectNumber     # e.g. 31003-01   (used if GivenPath not supplied)
)

# ==============================
# CONFIG
# ==============================
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$TenantID     = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID     = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"

$SiteID  = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

$LogFile       = "C:\_APPS\FileSync\Production\Logs\Single_Project_Clean_Log.txt"
$HoldingFolder = "_ToBeDeleted"   # destination at the drive root
$TopN          = 200              # items enumerated at drive root

# ==============================
# LOGGING (resilient)
# ==============================
function Log {
    param([string]$msg)
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    for ($i=0; $i -lt 5; $i++) {
        try {
            Add-Content -Path $LogFile -Encoding utf8 -Value "$ts - $msg"
            break
        } catch {
            Start-Sleep -Milliseconds (50 * ($i+1))
        }
    }
}

# ==============================
# AUTH
# ==============================
$script:AccessToken = $null
$script:TokenExpiry = Get-Date

function Get-AccessToken {
    if ($script:AccessToken -and (Get-Date) -lt $script:TokenExpiry) { return $script:AccessToken }

    $body = @{
        client_id     = $ClientID
        scope         = "https://graph.microsoft.com/.default"
        client_secret = $ClientSecret
        grant_type    = "client_credentials"
    }
    try {
        $resp = Invoke-RestMethod -Method POST -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body $body -ErrorAction Stop
        $script:AccessToken = $resp.access_token
        $script:TokenExpiry = (Get-Date).AddSeconds([int]$resp.expires_in - 60)
        return $script:AccessToken
    } catch {
        Log "FATAL auth: $($_.Exception.Message)"
        throw
    }
}

function GH { @{ Authorization = "Bearer $(Get-AccessToken)" ; "Content-Type" = "application/json" } }

# ==============================
# HELPERS: derive target display-name
# ==============================
function Get-TargetNameFromGivenPath {
    param([string]$Path)
    # If user passed a child folder (e.g., ..\05 Stickfile), go up to project folder
    $leaf = Split-Path -Path $Path -Leaf
    if ($leaf -match '^(05 Stickfile|04 Construction Admin|Newforma|Photos|SSI|RFI)') {
        $Path = Split-Path -Path $Path -Parent
    }
    # Expect "NNNNN-NN (Project Name ...)"
    return (Split-Path -Path $Path -Leaf)
}

function Get-TargetNameFromProjectNumber {
    param([string]$PN)
    return $PN  # we'll match at SP root by "PN*"
}

# ==============================
# GRAPH: find & create
# ==============================
function Find-ProjectFolder {
    param([string]$NameOrPN)

    $uri = "https://graph.microsoft.com/v1.0/drives/$DriveID/root/children?`$select=id,name,folder&`$top=$TopN"
    try {
        $list = Invoke-RestMethod -Method GET -Uri $uri -Headers (GH) -ErrorAction Stop
    } catch {
        Log "FATAL list root: $($_.Exception.Message)"
        throw
    }

    if ($NameOrPN -match '^\d{5}-\d{2}\s*\(') {
        # Looks like full name: prefer exact
        $hit = $list.value | Where-Object { $_.folder -and ($_.name -ieq $NameOrPN) } | Select-Object -First 1
        if ($hit) { return $hit }
        # Fallback to prefix by PN
        $pn = ($NameOrPN -split '\s+')[0]
        return ($list.value | Where-Object { $_.folder -and ($_.name -like "$pn*") } | Select-Object -First 1)
    } else {
        # Only a PN
        $pn = $NameOrPN
        return ($list.value | Where-Object { $_.folder -and ($_.name -like "$pn*") } | Select-Object -First 1)
    }
}

function Get-OrCreate-HoldingFolder {
    param([string]$Name = $HoldingFolder)

    $uri = "https://graph.microsoft.com/v1.0/drives/$DriveID/root/children?`$select=id,name,folder&`$top=$TopN"
    $list = Invoke-RestMethod -Method GET -Uri $uri -Headers (GH)
    $dest = $list.value | Where-Object { $_.folder -and ($_.name -ieq $Name) } | Select-Object -First 1
    if ($dest) { return $dest.id }

    $createUri = "https://graph.microsoft.com/v1.0/drives/$DriveID/root/children"
    $body = @{ name = $Name; folder = @{}; "@microsoft.graph.conflictBehavior" = "rename" } | ConvertTo-Json
    $created = Invoke-RestMethod -Method POST -Uri $createUri -Headers (GH) -Body $body
    return $created.id
}

# ==============================
# MOVE
# ==============================
function Move-ItemToFolder {
    param(
        [string]$ItemId,
        [string]$DestinationFolderId,
        [string]$DisplayName
    )

    $uri  = "https://graph.microsoft.com/v1.0/drives/$DriveID/items/$ItemId"
    $body = @{ parentReference = @{ id = $DestinationFolderId } } | ConvertTo-Json
    try {
        Invoke-RestMethod -Method PATCH -Uri $uri -Headers (GH) -Body $body -ErrorAction Stop | Out-Null
        Log "MOVED id=$ItemId ($DisplayName) → '$HoldingFolder'"
        return $true
    } catch {
        # On name conflict some tenants return 409. Try again with timestamped name.
        $code = $null; try { $code = $_.Exception.Response.StatusCode.value__ } catch {}
        $err  = $_.Exception.Message
        $bodyTxt = $_.ErrorDetails.Message
        Log "WARN move id=$ItemId ($DisplayName) failed (code=$code): $err | Body: $bodyTxt"

        $ts = (Get-Date -Format "yyyyMMdd_HHmmss")
        $renamed = "$DisplayName (archived $ts)"
        $body2 = @{ parentReference = @{ id = $DestinationFolderId }; name = $renamed } | ConvertTo-Json
        try {
            Invoke-RestMethod -Method PATCH -Uri $uri -Headers (GH) -Body $body2 -ErrorAction Stop | Out-Null
            Log "MOVED (renamed) id=$ItemId → '$HoldingFolder' as '$renamed'"
            return $true
        } catch {
            $err2 = $_.Exception.Message
            $body2Txt = $_.ErrorDetails.Message
            Log "ERROR move (retry) id=$ItemId ($DisplayName): $err2 | Body: $body2Txt"
            return $false
        }
    }
}

# ==============================
# MAIN
# ==============================
# Determine target name
$targetName = $null
if ($GivenPath) {
    $targetName = Get-TargetNameFromGivenPath -Path $GivenPath
    Log "CLEAN by GivenPath → targetName: $targetName"
} elseif ($ProjectNumber) {
    $targetName = Get-TargetNameFromProjectNumber -PN $ProjectNumber
    Log "CLEAN by ProjectNumber → PN: $targetName"
} else {
    Log "ERROR no GivenPath or ProjectNumber specified"
    exit 1
}

# Per-project mutex
$mutexName = "Global\CLEAN_" + ($targetName -replace '[^\w\-]+','_')
$mtx = New-Object System.Threading.Mutex($false, $mutexName)
$hasHandle = $false
try {
    $hasHandle = $mtx.WaitOne([TimeSpan]::FromSeconds(1))
} catch {}
if (-not $hasHandle) {
    Log "LOCK present for $targetName → exiting duplicate invocation"
    exit 0
}

try {
    # Locate the project folder at SharePoint root
    $item = $null
    if ($GivenPath -and ($targetName -match '^\d{5}-\d{2}\s*\(')) {
        $item = Find-ProjectFolder -NameOrPN $targetName
    }
    if (-not $item) {
        $pn = if ($GivenPath) { ($targetName -split '\s+')[0] } else { $targetName }
        $item = Find-ProjectFolder -NameOrPN $pn
    }

    if (-not $item) {
        Log "INFO project '$targetName' not found on SharePoint (nothing to move)"
        exit 0
    }

    # Ensure holding folder exists
    $destId = Get-OrCreate-HoldingFolder

    # Move
    [void](Move-ItemToFolder -ItemId $item.id -DestinationFolderId $destId -DisplayName $item.name)
}
catch {
    Log "FATAL: $($_.Exception.Message)"
}
finally {
    if ($hasHandle) { $mtx.ReleaseMutex() | Out-Null }
    $mtx.Dispose()
}
