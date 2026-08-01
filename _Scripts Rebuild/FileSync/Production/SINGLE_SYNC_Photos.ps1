param (
    [string]$GivenPath
)

# Ensure TLS 1.2 for Graph calls
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ==============================
# CONFIGURATION VARIABLES
# ==============================
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile = "C:\_APPS\FileSync\Production\Logs\Single_Photos_Sync_Log.txt"

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
# FUNCTION: Ensure SharePoint Folder Exists
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
# FUNCTION: Get SharePoint Files (ask for file/folder facets)
# ==============================
function Get-SharePointFiles {
    param ($FolderPath)
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($FolderPath) ):/children?`$select=id,name,size,file,folder&`$top=200"
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
# FUNCTION: Upload Only New or Modified Photos (handles >4MB)
# ==============================
function Upload-SharePointFiles {
    param ($FilesToUpload, $FolderPath)

    $Token   = Get-AccessToken
    $BaseUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID"

    # Cache existing once for size comparison
    $ExistingFiles = Get-SharePointFiles -FolderPath $FolderPath
    $ByName = @{}
    foreach ($i in $ExistingFiles) { $ByName[$i.name] = $i }

    foreach ($File in $FilesToUpload) {
        $SPFilePath = "$FolderPath/$($File.Name)"
        $encoded    = [uri]::EscapeDataString($SPFilePath)
        $size       = [int64]$File.Length

        # Decide if we need to upload
        $needs = $true
        if ($ByName.ContainsKey($File.Name)) {
            $sp = $ByName[$File.Name]
            if ([int64]$sp.size -eq $size) {
                Log-Message "Skipping upload (same size): $($File.Name)"
                $needs = $false
            } else {
                Log-Message "Uploading (size mismatch) $($File.Name) Local=$size SP=$([int64]$sp.size)"
            }
        } else {
            Log-Message "Uploading new: $($File.Name)"
        }
        if (-not $needs) { continue }

        try {
            if ($size -le 3670016) {
                # --- Simple PUT for small files ---
                $uri = "$BaseUri/root:/${encoded}:/content"   # note ${encoded} to avoid :$var bug
                Invoke-RestMethod -Uri $uri `
                    -Headers @{ Authorization = "Bearer $Token" } `
                    -ContentType "application/octet-stream" `
                    -Method Put -InFile $File.FullName -ErrorAction Stop | Out-Null
                Log-Message "Uploaded (simple): $($File.Name)"
            } else {
                # --- Chunked upload session for large files ---
                $create = "$BaseUri/root:/${encoded}:/createUploadSession"
                $session = Invoke-RestMethod -Method POST -Uri $create `
                           -Headers @{ Authorization = "Bearer $Token" } `
                           -ContentType "application/json" `
                           -Body (@{} | ConvertTo-Json) -ErrorAction Stop
                $uploadUrl = $session.uploadUrl

                $chunkSize = 5MB
                $fs = [System.IO.File]::OpenRead($File.FullName)
                try {
                    $offset = 0L
                    $buffer = New-Object byte[] $chunkSize
                    while ($offset -lt $size) {
                        $read = $fs.Read($buffer, 0, $chunkSize)
                        if ($read -le 0) { break }

                        # Make a right-sized byte[] for this chunk
                        $chunk = New-Object byte[] $read
                        [System.Array]::Copy($buffer, 0, $chunk, 0, $read)

                        $start = $offset
                        $end   = $offset + $read - 1
                        $range = "bytes $start-$end/$size"

                        Invoke-RestMethod -Method PUT -Uri $uploadUrl `
                            -Headers @{ "Content-Range" = $range } `
                            -ContentType "application/octet-stream" `
                            -Body $chunk -ErrorAction Stop | Out-Null

                        $offset += $read
                    }
                    Log-Message "Uploaded (session): $($File.Name)"
                } finally {
                    $fs.Dispose()
                }
            }
        } catch {
            # Add Graph error body to the log for quick diagnostics
            $msg = $_.Exception.Message
            try {
                if ($_.Exception.Response) {
                    $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    $body = $sr.ReadToEnd()
                    $msg = "$msg | Body: $body"
                } elseif ($_.ErrorDetails -and $_.ErrorDetails.Message) {
                    $msg = "$msg | Details: $($_.ErrorDetails.Message)"
                }
            } catch {}
            Log-Message "ERROR: Upload failed '$($File.Name)' -> '$SPFilePath' : $msg"
        }
    }
}




# ==============================
# FUNCTION: Delete Extra Files (path-based delete)
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
if (-not $GivenPath -or -not (Test-Path -LiteralPath $GivenPath)) {
    Log-Message "ERROR: Invalid folder path: $GivenPath"
    exit 1
}

# Extract project folder (GivenPath is ...\<Project>\04 Construction Admin\07 Photos)
$ProjectDir  = Split-Path (Split-Path $GivenPath -Parent) -Parent
$ProjectName = Split-Path $ProjectDir -Leaf

# Target in SharePoint: <Project>/Photos  (no "04 Construction Admin")
$SPRoot   = $ProjectName
$SPFolder = "$SPRoot/Photos"

Log-Message "DEBUG: Project='$ProjectName' TargetFolder='$SPFolder'"

# Ensure the folders exist
if (-not (Test-SharePointFolder -FolderPath $SPRoot))   { Ensure-SharePointFolder -FolderPath $SPRoot }
if (-not (Test-SharePointFolder -FolderPath $SPFolder)) { Ensure-SharePointFolder -FolderPath $SPFolder }

# Collect PHOTOS (not PDFs); adjust list as needed
$ImageExtensions = @("*.jpg","*.jpeg","*.png","*.heic","*.bmp","*.tif","*.tiff")
$LocalFiles = Get-ChildItem -LiteralPath $GivenPath -File -Include $ImageExtensions -ErrorAction SilentlyContinue
Log-Message "Photos to sync: $($LocalFiles.Count) from '$GivenPath'"

# Upload (handles large files automatically)
if ($LocalFiles.Count -gt 0) {
    Upload-SharePointFiles -FilesToUpload $LocalFiles -FolderPath $SPFolder
}

# Prune SP photos that no longer exist locally (case-insensitive, files only, image extensions only)
$SPFiles = @(Get-SharePointFiles -FolderPath $SPFolder)

$localNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($f in $LocalFiles) { [void]$localNames.Add($f.Name) }

$remotePhotos = $SPFiles | Where-Object { $_.file -and (@(".jpg",".jpeg",".png",".heic",".bmp",".tif",".tiff") -contains ([IO.Path]::GetExtension($_.name).ToLower())) }
$FilesToDelete = @()
foreach ($item in $remotePhotos) {
    if (-not $localNames.Contains($item.name)) { $FilesToDelete += $item }
}

Log-Message "SP photos: $($remotePhotos.Count)  To delete: $($FilesToDelete.Count)"
if ($FilesToDelete.Count -gt 0) {
    Delete-SharePointFiles -FilesToDelete $FilesToDelete -FolderPath $SPFolder
}

Log-Message "Sync complete."
