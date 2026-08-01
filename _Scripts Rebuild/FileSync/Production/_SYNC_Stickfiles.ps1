# Define Variables
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

$BaseDirectory = "\\KOR-FS01\Projects\Projects"
$TargetFolderPath = "05 Stickfile"
$SPBaseFolder = ""
$LogFile = "C:\_APPS\FileSync\Production\Logs\Stickfile_SYNC_Log.txt"

# Global Variables
$global:AccessToken = $null
$global:TokenExpiration = $null
$FolderCache = @{}
$UploadedFiles = 0
$TotalFiles = 0

# Function to Log Messages
function Log-Message {
    param ($Message)
    "$((Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) - $Message" | Out-File -Append -FilePath $LogFile
    Write-Host $Message
}

# Function to Get Access Token
function Get-AccessToken {
    if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) {
        return $global:AccessToken
    }
    $Body = @{ client_id = $ClientID; scope = "https://graph.microsoft.com/.default"; client_secret = $ClientSecret; grant_type = "client_credentials" }
    try {
        $TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
        $global:AccessToken = $TokenResponse.access_token
        $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
        Log-Message "New access token acquired."
        return $global:AccessToken
    } catch {
        Log-Message "Failed to retrieve access token: $_"
        throw "Error retrieving access token"
    }
}

$AccessToken = Get-AccessToken
$Headers = @{ "Authorization" = "Bearer $AccessToken" }

function Get-SharePointFiles {
    param ($SPProjectFolder, $Headers)

    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($SPProjectFolder)):/children"
    $SPFileDict = @{}

    try {
        do {
            $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
            if ($Response.value.Count -gt 0) {
                foreach ($File in $Response.value) {
    if ($File.file) { 
        $SPFileDict[$File.name] = $File.size  # store size instead of just true
    }
}

            }
            $Uri = $Response.'@odata.nextLink'
        } while ($Uri)

        Log-Message "Retrieved file list for ${SPProjectFolder} (Total: $($SPFileDict.Count) files)"
        return $SPFileDict  # Return Hash Table Directly
    } catch {
       Log-Message ("Error retrieving file list for " + $SPProjectFolder + ": " + $_)
        return @{}
    }
}



# Function to Ensure SharePoint Folder Exists (Optimized)
function Ensure-SharePointFolder {
    param ($ParentPath, $FolderName, $Headers)

    if ($FolderCache.ContainsKey("$ParentPath/$FolderName")) { return }

    $EffectivePath = if ($ParentPath -eq "") { "$FolderName" } else { "$ParentPath/$FolderName" }
$Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($EffectivePath))"


    try {
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
        Log-Message "Folder already exists: $ParentPath/$FolderName"
    } catch {
        $EffectiveParent = if ($ParentPath -eq "") { "/" } else { $ParentPath }
$CreateUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($EffectiveParent)):/children"

        $Body = @{ name = $FolderName; folder = @{} } | ConvertTo-Json -Depth 1

        $Headers["Content-Type"] = "application/json"

        try {
            Invoke-RestMethod -Uri $CreateUri -Headers $Headers -Method POST -Body $Body -ErrorAction Stop
            Log-Message "Successfully created folder: $ParentPath/$FolderName"

            # Automatically create Reports folder inside
                        # Ensure parent exists before creating Reports
            if ($FolderName -ne "Reports") {
                if (-not $FolderCache.ContainsKey("$ParentPath/$FolderName")) {
                    Ensure-SharePointFolder -ParentPath "$ParentPath" -FolderName "$FolderName" -Headers $Headers
                }
                
                # Short delay to allow SharePoint API propagation
                Start-Sleep -Seconds 2

                # Now create the Reports folder
                Ensure-SharePointFolder -ParentPath "$ParentPath/$FolderName" -FolderName "Reports" -Headers $Headers
            }

        } catch {
            Log-Message "Failed to create folder: $ParentPath/$FolderName - $_"
        }
    }

    $FolderCache["$ParentPath/$FolderName"] = $true
}




function Delete-SharePointFile {
    param ($SPFilePath, $Headers)  # Add Headers as a parameter
    
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($SPFilePath))"

    try {
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Delete -ErrorAction Stop
        Log-Message "Deleted file from SharePoint: $SPFilePath"
    } catch {
        Log-Message "Failed to delete file: $SPFilePath - $_"
    }
}


# Process Local Projects
foreach ($Category in Get-ChildItem -Path $BaseDirectory -Directory -ErrorAction Stop) {
foreach ($Project in Get-ChildItem -Path $Category.FullName -Directory -ErrorAction Stop) {
    # Define the exclusion file name
    $ExclusionFileName = "NOT SYNCED TO ACTIVE PROJECTS.txt"
    $ExclusionFilePath = Join-Path -Path $Project.FullName -ChildPath $ExclusionFileName

    # Check if the exclusion file exists
    if (Test-Path $ExclusionFilePath) {
        Write-Host "Skipping project: $($Project.FullName) (Exclusion file found)"
        Log-Message "Skipping project: $($Project.FullName) (Exclusion file found)"
        continue
    }

    $LocalStickfilePath = Join-Path -Path $Project.FullName -ChildPath $TargetFolderPath
    if (-Not (Test-Path $LocalStickfilePath)) { continue }
    $LocalFiles = Get-ChildItem -Path $LocalStickfilePath -File -Filter "*.pdf" -ErrorAction Stop
 
# Correctly assign the SharePoint project folder **before anything else**
$SPProjectFolder = "$SPBaseFolder/$($Project.Name)"
Log-Message "Processing project: $SPProjectFolder"

# Ensure SharePoint project folders exist before retrieving files
Ensure-SharePointFolder -ParentPath $SPBaseFolder -FolderName $Project.Name -Headers $Headers

# Now safely retrieve SharePoint files
$SPFileDict = Get-SharePointFiles -SPProjectFolder $SPProjectFolder -Headers $Headers


# Convert local file list to a hash table for quick lookup
$LocalFileNames = @{}
foreach ($File in $LocalFiles) {
    $LocalFileNames[$File.Name] = $File  # Store file object instead of just True for quick access
}

# Delete files from SharePoint if they are missing locally
foreach ($SPFile in $SPFileDict.Keys) {  # Correct variable
    if (-not $LocalFileNames.ContainsKey($SPFile)) {
        $SPFilePath = "$SPProjectFolder/$SPFile"
        Delete-SharePointFile -SPFilePath $SPFilePath -Headers $Headers
        Log-Message "Deleted from SharePoint (not found locally): $SPFilePath"
    }
}


# Ensure the SharePoint folders exist
Ensure-SharePointFolder -ParentPath $SPBaseFolder -FolderName $Project.Name -Headers $Headers

# Upload missing or changed files
foreach ($FileName in $LocalFileNames.Keys) {
    $File = $LocalFileNames[$FileName]  # Retrieve actual file object
    $EncodedSPFilePath = [uri]::EscapeDataString("$SPProjectFolder/$FileName")

    if ($SPFileDict.ContainsKey($FileName)) {
    $SPSize = $SPFileDict[$FileName]
    if ($File.Length -eq $SPSize) {
        Log-Message "Skipping upload (same size): $SPProjectFolder/$FileName"
        continue
    } else {
        Log-Message "Uploading due to size mismatch: $SPProjectFolder/$FileName (Local: $($File.Length), SP: $SPSize)"
    }
} else {
    Log-Message "Uploading new file: $SPProjectFolder/$FileName"
}


    Log-Message "Uploading: $SPProjectFolder/$FileName"
    $UploadUrl = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( $EncodedSPFilePath ):/content?@microsoft.graph.conflictBehavior=replace"

 $Headers["Content-Type"] = "application/octet-stream"

    try {
        Invoke-WebRequest -Uri $UploadUrl `
            -Headers $Headers `
            -Method Put `
            -InFile $File.FullName `
            -UseBasicParsing `
            -ErrorAction Stop
        Log-Message "Uploaded: $SPProjectFolder/$FileName"

        # Allow time for SharePoint to register file before deleting missing files
        Start-Sleep -Seconds 3

    } catch {
        Log-Message "Failed to upload: $SPProjectFolder/$FileName - $_"
    }
}


    }
}

Log-Message "File sync completed!"
