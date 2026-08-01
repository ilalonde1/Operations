# Rename script to _SYNC_SSI.ps1

# ==============================
# CONFIGURATION VARIABLES
# ==============================

$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

$BaseDirectory = "\\KOR-FS01\Projects\Projects"
$TargetFolderPath = "04 Construction Admin\07 Photos"
$SPBaseFolder = ""
$LogFile = "C:\_APPS\FileSync\Production\Logs\Photos_Sync_Log.txt"

Write-Host "DEBUG: Target Folder Path = $TargetFolderPath"

# ==============================
# GLOBAL VARIABLES
# ==============================

$global:AccessToken = $null
$global:TokenExpiration = $null
$global:UploadedFiles = @()
$global:DeletedFiles = @()



# ==============================
# FUNCTIONS
# ==============================

# Function to Get Access Token
function Get-AccessToken {
    if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) {
        return $global:AccessToken
    }
    $Body = @{ 
        client_id = $ClientID
        scope = "https://graph.microsoft.com/.default"
        client_secret = $ClientSecret
        grant_type = "client_credentials"
    }
    try {
        $TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
        $global:AccessToken = $TokenResponse.access_token
        $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
        return $global:AccessToken
    } catch {
        throw "Error retrieving access token: $_"
    }
}

# Logging Function
function Log-Message {
    param ($Message)
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$Timestamp - $Message" | Out-File -Append -FilePath $LogFile
}

# Ensure function is available before usage
$ScriptStartTime = Get-Date
Log-Message "Starting SharePoint sync process at $ScriptStartTime"

# Function to Create a New SharePoint Folder
function New-SharePointFolder {
    param ($ParentPath, $FolderName)

    $EffectiveParent = if ([string]::IsNullOrWhiteSpace($ParentPath)) { "/" } else { $ParentPath }
$Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($EffectiveParent)):/children"

    $Body = @{ name = $FolderName; folder = @{} } | ConvertTo-Json -Depth 1
    $Headers = @{
        "Authorization" = "Bearer $(Get-AccessToken)"
        "Content-Type"  = "application/json"
    }

    try {
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method POST -Body $Body -ErrorAction Stop
        Log-Message "Created folder: $ParentPath/$FolderName"
    } catch {
        Log-Message "Failed to create folder: $ParentPath/$FolderName - $_"
    }
}

# Function to Check if a Folder Exists on SharePoint
# Global hashtable to store folder existence results
$global:CachedSPFolders = @{}

function Test-SharePointFolder {
    param ($FolderPath)

    # Check if we've already tested this folder
    if ($global:CachedSPFolders.ContainsKey($FolderPath)) {
        return $global:CachedSPFolders[$FolderPath]
    }

    $EffectivePath = if ([string]::IsNullOrWhiteSpace($FolderPath)) { "/" } else { $FolderPath }
$Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($EffectivePath))"

    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }

    try {
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop | Out-Null
        $global:CachedSPFolders[$FolderPath] = $true
        return $true
    } catch {
        $global:CachedSPFolders[$FolderPath] = $false
        return $false
    }
}

function Get-SharePointFiles {
    param ($FolderPath)

    if (-not $FolderPath -or $FolderPath -eq "") {
        Log-Message "WARNING: Skipping Get-SharePointFiles due to empty FolderPath."
        return @()
    }

    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($FolderPath) ):/children?$expand=children"
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }
    $Files = @()
    $MaxRetries = 3
    $Attempts = 0

    try {
        Log-Message "Retrieving files from SharePoint folder: $FolderPath"

        do {
            try {
                # Attempt API call
                $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop

                if ($Response.value) {
                    $Files += $Response.value
                    Log-Message "Retrieved $($Response.value.Count) files from $FolderPath"
                }

                # Handle API pagination
                $Uri = $Response."@odata.nextLink"

                # Reset retry attempts on successful retrieval
                $Attempts = 0

            } catch {
                $Attempts++
                if ($Attempts -lt $MaxRetries) {
                    $WaitTime = [math]::Pow(2, $Attempts)  # Exponential backoff (2, 4, 8 seconds)
                    Log-Message "Retrying Get-SharePointFiles ($Attempts/$MaxRetries) after $WaitTime seconds due to API limit..."
                    Start-Sleep -Seconds $WaitTime
                } else {
                    Log-Message "ERROR: Failed to retrieve file list from SharePoint after $MaxRetries retries."
                    return @()
                }
            }
        } while ($Uri)

        return $Files
    } catch {
        Log-Message "ERROR: Unable to retrieve files from SharePoint: $_"
        return @()
    }
}

function Upload-SharePointFiles {
    param ($FilesToUpload, $FolderPath)

    if ($FilesToUpload.Count -eq 0) {
        Log-Message "No new files to upload for $FolderPath"
        return
    }

    $Headers = @{
        "Authorization" = "Bearer $(Get-AccessToken)"
        "Content-Type"  = "application/octet-stream"
    }

    $MaxParallelUploads = 5  # Adjust this based on system performance

    $UploadResults = $FilesToUpload | ForEach-Object -Parallel {
        $SPFilePath = "$using:FolderPath/$($_.Name)"
        $Uri = "https://graph.microsoft.com/v1.0/sites/$using:SiteID/drives/$using:DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) ):/content"

        try {
            Invoke-RestMethod -Uri $Uri -Headers $using:Headers -Method Put -InFile $_.FullName -ErrorAction Stop
            [PSCustomObject]@{ Status = "Success"; File = $_.Name }
        } catch {
            [PSCustomObject]@{ Status = "Error"; File = $_.Name; ErrorMessage = $_ }
        }
    } -ThrottleLimit $MaxParallelUploads

    # Process Upload Results
    foreach ($Result in $UploadResults) {
        if ($Result.Status -eq "Success") {
            $global:UploadedFiles += $Result.File
            Log-Message "Successfully uploaded: $($Result.File)"
        } else {
            Log-Message "ERROR: Failed to upload $($Result.File) - $($Result.ErrorMessage)"
        }
    }
}


# Function to Delete File from SharePoint
function Delete-SharePointFiles {
    param ($FilesToDelete, $FolderPath)

    if ($FilesToDelete.Count -eq 0) {
        Log-Message "No files to delete from SharePoint in $FolderPath"
        return
    }

    $BatchUri = "https://graph.microsoft.com/v1.0/\$batch"
    $Headers = @{
        "Authorization" = "Bearer $(Get-AccessToken)"
        "Content-Type"  = "application/json"
    }

    $BatchRequests = @()
    $RequestID = 1

    foreach ($File in $FilesToDelete) {
        $SPFilePath = "$FolderPath/$File"
        $Request = @{
            id     = "$RequestID"
            method = "DELETE"
            url    = "/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) )"
        }
        $BatchRequests += $Request
        $RequestID++
    }

    $Body = @{ requests = $BatchRequests } | ConvertTo-Json -Depth 2

    try {
        $Response = Invoke-RestMethod -Uri $BatchUri -Headers $Headers -Method Post -Body $Body -ErrorAction Stop
        Log-Message "Batch delete request sent for $($FilesToDelete.Count) files in $FolderPath"

        #Update Deleted Files List Correctly
        foreach ($File in $FilesToDelete) {
            $SPFilePath = "$FolderPath/$File"
            $global:DeletedFiles += $SPFilePath  # <-- Track deleted file
            Log-Message "Successfully deleted: $SPFilePath"
        }

    } catch {
        Log-Message "ERROR: Failed to delete files in batch - $_"
    }
}



# ==============================
# MAIN EXECUTION LOGIC
# ==============================

Log-Message "Starting dynamic project file sync process..."

$ProjectFolders = Get-ChildItem -Path $BaseDirectory -Directory -Force
Log-Message "DEBUG: Found $($ProjectFolders.Count) projects"

foreach ($Category in $ProjectFolders) {
    Log-Message "Scanning category: $($Category.Name)"
    $Projects = Get-ChildItem -Path $Category.FullName -Directory

    foreach ($Project in $Projects) {
        Log-Message "DEBUG: Processing Project: $($Project.FullName)"

        # Skip projects with an exclusion file
        $ExclusionFilePath = Join-Path -Path $Project.FullName -ChildPath "NOT SYNCED TO ACTIVE PROJECTS.txt"
        if (Test-Path $ExclusionFilePath) {
            Log-Message "Skipping project: $($Project.FullName) (Exclusion file found)"
            continue
        }

        # Define paths
        $LocalPhotosPath = Join-Path -Path $Project.FullName -ChildPath $TargetFolderPath
        $SPProjectFolder = "$SPBaseFolder/$($Project.Name)"
        $SPPhotosFolder = "$SPProjectFolder/Photos"

        # Check if local folder exists
        $LocalFiles = @()
        if (Test-Path $LocalPhotosPath) {
            $LocalFiles = Get-ChildItem -Path $LocalPhotosPath -File
        } else {
            Log-Message "WARNING: Local Photos folder does not exist for project: $($Project.Name). Proceeding to delete SharePoint files if necessary."
        }

        # Check if SharePoint folders exist
        if (-not $global:CachedSPFolders.ContainsKey($SPPhotosFolder)) {
    $global:CachedSPFolders[$SPPhotosFolder] = Test-SharePointFolder -FolderPath $SPPhotosFolder
}
$PhotosFolderExists = $global:CachedSPFolders[$SPPhotosFolder]

        # Retrieve SharePoint files (always do this, even if there are no local files)
        $SPFiles = @{}  
        if ($PhotosFolderExists) {
            foreach ($File in (Get-SharePointFiles -FolderPath $SPPhotosFolder)) {
                $SPFiles[$File.name] = $File.size

            }
        }

        # UPLOAD FILES - Skip if no files
        if ($LocalFiles.Count -gt 0) {
            # Ensure folders exist before uploading
            if (-not $global:CachedSPFolders.ContainsKey($SPProjectFolder)) {
    $global:CachedSPFolders[$SPProjectFolder] = Test-SharePointFolder -FolderPath $SPProjectFolder
}
$ProjectFolderExists = $global:CachedSPFolders[$SPProjectFolder]

if (-not $ProjectFolderExists) {
    New-SharePointFolder -ParentPath $SPBaseFolder -FolderName $Project.Name
    $global:CachedSPFolders[$SPProjectFolder] = $true  # Update cache
}

            if (-not $PhotosFolderExists) {
                New-SharePointFolder -ParentPath $SPProjectFolder -FolderName "Photos"
                $PhotosFolderExists = $true
            }

            # Upload files
            # Prepare list of files to upload
$FilesToUpload = @()
# Convert SPFiles dictionary to include size info
$SPFilesMeta = @{}
foreach ($File in (Get-SharePointFiles -FolderPath $SPPhotosFolder)) {
    $SPFilesMeta[$File.name] = $File.size
}

# Build upload list based on size comparison
$FilesToUpload = @()
foreach ($File in $LocalFiles) {
    if (-not $SPFilesMeta.ContainsKey($File.Name)) {
        Log-Message "File not found in SharePoint, will upload: $($File.Name)"
        $FilesToUpload += $File
    } elseif ($File.Length -ne $SPFilesMeta[$File.Name]) {
        Log-Message "File size mismatch, will upload: $($File.Name) (Local: $($File.Length), SP: $($SPFilesMeta[$File.Name]))"
        $FilesToUpload += $File
    } else {
        Log-Message "Skipping upload (same name and size): $($File.Name)"
    }
}


# Call the batch upload function
if ($FilesToUpload.Count -gt 0) {
    Upload-SharePointFiles -FilesToUpload $FilesToUpload -FolderPath $SPPhotosFolder
} else {
    Log-Message "No new or updated files to upload for $SPPhotosFolder"
}

        }

       # DELETE FILES - Always execute, even if LocalFiles is empty
if ($SPFiles.Count -gt 0) {
    $LocalFileNames = $LocalFiles | ForEach-Object { $_.Name }
    foreach ($SPFile in $SPFiles.Keys) {
        if ($LocalFileNames -notcontains $SPFile) {
            $SPFilePath = "$SPPhotosFolder/$SPFile"
            Log-Message "Deleting file from SharePoint: $SPFilePath"
            try {
                $DeleteUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) )"
                Invoke-RestMethod -Uri $DeleteUri -Headers @{ "Authorization" = "Bearer $(Get-AccessToken)" } -Method Delete
                Log-Message "Successfully deleted: $SPFilePath"

                # Ensure Deleted File is Tracked
                $global:DeletedFiles += $SPFilePath  # <-- Add this

            } catch {
                Log-Message "ERROR: Failed to delete file: $SPFilePath - $_"
            }
        }
    }
} else {
    Log-Message "No files found in SharePoint for deletion."
}

    }
}

Log-Message "File sync completed."

# Generate Summary Report
$ScriptEndTime = Get-Date
$Duration = $ScriptEndTime - $ScriptStartTime

$SummaryReport = @"
=============================================
SUMMARY REPORT - SharePoint Sync
=============================================
Execution Time: $ScriptStartTime - $ScriptEndTime ($($Duration.TotalSeconds -as [int]) seconds)

Total Files Uploaded: $($global:UploadedFiles.Count)
Total Files Deleted: $($global:DeletedFiles.Count)

Uploaded Files:
$($global:UploadedFiles -join "`n")

Deleted Files:
$($global:DeletedFiles -join "`n")

=============================================
"@

# Log the report
Log-Message $SummaryReport

# Output to console as well
Write-Host $SummaryReport