param (
    [string]$GivenPath
)

# ==============================
# CONFIGURATION VARIABLES
# ==============================
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile = "C:\_APPS\FileSync\Production\Logs\Single_RFI_Sync_Log.txt"

# ==============================
# FUNCTION: Logging
# ==============================
function Log-Message {
    param ($Message)
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$Timestamp - $Message" | Out-File -Append -FilePath $LogFile
}

# ==============================
# FUNCTION: Get Access Token for SharePoint
# ==============================
function Get-AccessToken {
    if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) {
        return $global:AccessToken
    }
    $Body = @{
        client_id     = $ClientID
        scope         = "https://graph.microsoft.com/.default"
        client_secret = $ClientSecret
        grant_type    = "client_credentials"
    }
    try {
        $TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
        $global:AccessToken = $TokenResponse.access_token
        $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
        return $global:AccessToken
    } catch {
        Log-Message "ERROR: Failed to retrieve access token - $_"
        exit 1
    }
}

# ==============================
# FUNCTION: Check if SharePoint Folder Exists
# ==============================
function Test-SharePointFolder {
    param ($FolderPath)
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($FolderPath) )"
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }
    try {
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

# ==============================
# FUNCTION: Ensure SharePoint Folder Exists (Fully Restored!)
# ==============================
function Ensure-SharePointFolder {
    param ($FolderPath)

    if (Test-SharePointFolder -FolderPath $FolderPath) {
        Log-Message "Skipping folder creation (already exists): $FolderPath"
        return
    }

    Log-Message "Creating missing folder: $FolderPath"

    $PathParts = $FolderPath -split "/"
    if ($PathParts.Count -lt 2) {
        $ParentPath = ""
        $FolderName = $PathParts[0]
    } else {
        $ParentPath = $PathParts[0..($PathParts.Count - 2)] -join "/"
        $FolderName = $PathParts[-1]
    }

    $Body = @{
        name = $FolderName
        folder = @{}
        "@microsoft.graph.conflictBehavior" = "fail"
    } | ConvertTo-Json -Depth 2

    $EffectiveParentPath = if ([string]::IsNullOrWhiteSpace($ParentPath)) { "/" } else { $ParentPath }
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($EffectiveParentPath)):/children"

    try {
        Invoke-RestMethod -Uri $Uri -Headers @{ 
            "Authorization" = "Bearer $(Get-AccessToken)"; 
            "Content-Type" = "application/json" 
        } -Method Post -Body $Body -ErrorAction Stop

        Log-Message "Created folder: $FolderPath"
    } catch {
        Log-Message "ERROR: Failed to create folder: $FolderPath - $_"
        exit 1
    }
}


# ==============================
# FUNCTION: Get SharePoint Files
# ==============================
function Get-SharePointFiles {
    param ($FolderPath)
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($FolderPath) ):/children?`$select=id,name,size,file,folder"
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }
    try {
        $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
        return $Response.value
    } catch {
        Log-Message "ERROR: Unable to retrieve files from SharePoint - $_"
        return @()
    }
}

# ==============================
# FUNCTION: Upload Only New or Modified Files
# ==============================
function Upload-SharePointFiles {
    param ($FilesToUpload, $FolderPath)

    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)"; "Content-Type" = "application/octet-stream" }

    # Get existing files ONCE
    $ExistingFiles = Get-SharePointFiles -FolderPath $FolderPath

    foreach ($File in $FilesToUpload) {
        $SPFilePath = "$FolderPath/$($File.Name)"
        $SPMatch = $ExistingFiles | Where-Object { $_.name -eq $File.Name }

        $NeedsUpload = $true

        if ($SPMatch) {
            $spSize = $SPMatch.size

            if ($File.Length -eq $spSize) {
                Log-Message "Skipping upload (same size): $($File.Name)"
                $NeedsUpload = $false
            } else {
                Log-Message "Uploading due to size mismatch - Local: $($File.Length), SharePoint: $spSize"
            }
        }

        if ($NeedsUpload) {
            Log-Message "Uploading file: $($File.Name)"
            try {
                Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) ):/content" `
                    -Headers $Headers -Method Put -InFile $File.FullName -ErrorAction Stop
                Log-Message "Uploaded: $($File.Name)"
            } catch {
                Log-Message "ERROR: Failed to upload $($File.Name) - $_"
            }
        }
    }
}


# ==============================
# FUNCTION: Delete Extra Files
# ==============================
function Delete-SharePointFiles {
    param ($FilesToDelete, $FolderPath)
    foreach ($File in $FilesToDelete) {
        $SPFilePath = "$FolderPath/$($File.name)"
        $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) )"

        try {
            Invoke-RestMethod -Uri $Uri -Headers @{ "Authorization" = "Bearer $(Get-AccessToken)" } -Method Delete
            Log-Message "Deleted: $SPFilePath"
        } catch {
            Log-Message "ERROR: Failed to delete $SPFilePath - $_"
        }
    }
}

# ==============================
# MAIN EXECUTION LOGIC
# ==============================
# Validate Input Path
if (-not $GivenPath -or -not (Test-Path $GivenPath)) {
    Log-Message "ERROR: Invalid folder path: $GivenPath"
    exit 1
}

# Extract Project Name and Set SharePoint Folder
$ProjectDir  = Split-Path (Split-Path (Split-Path $GivenPath -Parent) -Parent) -Parent
$ProjectName = Split-Path $ProjectDir -Leaf
$SPFolder    = $ProjectName
$SPFolder = "$ProjectName/RFI"

# **Ensure Folder Exists**
Ensure-SharePointFolder -FolderPath $SPFolder

# **Upload PDFs (Only New or Modified)**
$LocalFiles = Get-ChildItem -Path $GivenPath -File -Filter "*.pdf"
if ($LocalFiles.Count -gt 0) {
    Upload-SharePointFiles -FilesToUpload $LocalFiles -FolderPath $SPFolder
}

# Get remote children once
$SPFiles = @(Get-SharePointFiles -FolderPath $SPFolder)

# Build a case-insensitive set of local PDF names (exactly what you upload)
$localPdfNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($f in $LocalFiles) { [void]$localPdfNames.Add($f.Name) }

# Consider only remote FILES that are PDFs
$remotePdfFiles = $SPFiles | Where-Object {
    $_.file -and ([IO.Path]::GetExtension($_.name).ToLower() -eq ".pdf")
}

# Anything in SP that isn't present locally should be deleted
$FilesToDelete = @()
foreach ($item in $remotePdfFiles) {
    if (-not $localPdfNames.Contains($item.name)) { $FilesToDelete += $item }
}

# (Optional) quick log to see counts/names
# Log-Message "SP PDFs: $($remotePdfFiles.Count)  ToDelete: $($FilesToDelete.Count)"
# if ($FilesToDelete.Count) { Log-Message ("Will delete: " + ($FilesToDelete.name -join ", ")) }

if ($FilesToDelete.Count -gt 0) {
    Delete-SharePointFiles -FilesToDelete $FilesToDelete -FolderPath $SPFolder
}

Log-Message "Sync complete."
