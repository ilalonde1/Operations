# -------------------------------
# Move All Files from "2025" to "garbage3" on SharePoint (Handles Pagination)
# -------------------------------

# Define Variables for SharePoint Authentication
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

# Define Source and Destination Folders on SharePoint
$SourceFolder = "2025"
$DestinationFolder = "garbage3"

# Log File
$LogFile = "C:\Temp\SharePointMoveLog.txt"

# Ensure Log Directory Exists
if (!(Test-Path "C:\Temp")) {
    New-Item -ItemType Directory -Path "C:\Temp" | Out-Null
}

# -------------------------------
# Authenticate with Microsoft Graph API
# -------------------------------
$AuthBody = @{
    client_id     = $ClientID
    scope         = "https://graph.microsoft.com/.default"
    client_secret = $ClientSecret
    grant_type    = "client_credentials"
}

$TokenResponse = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body $AuthBody
$AccessToken = $TokenResponse.access_token

# -------------------------------
# Function to Check If a SharePoint Folder Exists
# -------------------------------
Function Get-SharePointFolder {
    param ($FolderName)
    $FolderUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$FolderName`:"  # FIXED Colon Escape
    try {
        $Response = Invoke-RestMethod -Method Get -Uri $FolderUri -Headers @{Authorization = "Bearer $AccessToken"}
        return $Response.id
    } catch {
        return $null  # Folder does not exist
    }
}

# -------------------------------
# Function to Create a Folder on SharePoint (If Not Exists)
# -------------------------------
Function Create-SharePointFolder {
    param ($FolderName)
    $CreateFolderUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    $Body = @{name = $FolderName; folder = @{}; "@microsoft.graph.conflictBehavior" = "fail"} | ConvertTo-Json -Depth 3
    try {
        Invoke-RestMethod -Method Post -Uri $CreateFolderUri -Headers @{Authorization = "Bearer $AccessToken"; "Content-Type" = "application/json"} -Body $Body
        Add-Content -Path $LogFile -Value "$(Get-Date) - INFO: Created folder '$FolderName' on SharePoint."
    } catch {
        Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Could not create folder '$FolderName' - $($_.Exception.Message)"
    }
}

# -------------------------------
# Ensure "garbage3" Folder Exists
# -------------------------------
if (-not (Get-SharePointFolder -FolderName $DestinationFolder)) {
    Create-SharePointFolder -FolderName $DestinationFolder
}

# -------------------------------
# Move All Items from "2025" to "garbage3" (Handles Pagination)
# -------------------------------
$SourceFolderId = Get-SharePointFolder -FolderName $SourceFolder
$DestinationFolderId = Get-SharePointFolder -FolderName $DestinationFolder

if ($SourceFolderId -and $DestinationFolderId) {
    $ItemsUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$SourceFolder`:/children"

    do {
        # Fetch items from SharePoint
        $ItemsResponse = Invoke-RestMethod -Method Get -Uri $ItemsUri -Headers @{Authorization = "Bearer $AccessToken"}

        foreach ($Item in $ItemsResponse.value) {
            $MoveUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($Item.id)"
            $MoveBody = @{ parentReference = @{ id = $DestinationFolderId } } | ConvertTo-Json -Depth 3

            try {
                Invoke-RestMethod -Method Patch -Uri $MoveUri -Headers @{Authorization = "Bearer $AccessToken"; "Content-Type" = "application/json"} -Body $MoveBody
                Add-Content -Path $LogFile -Value "$(Get-Date) - SUCCESS: Moved '$($Item.name)' to '$DestinationFolder'."
            } catch {
                Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Failed to move '$($Item.name)' - $($_.Exception.Message)"
            }
        }

        # If there's more data, continue
        $ItemsUri = $ItemsResponse."@odata.nextLink"
    } while ($ItemsUri)

    Write-Host "Move operation completed successfully. Check log for details."
} else {
    Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Could not find both source and destination folders on SharePoint."
    Write-Host "ERROR: Could not find both source and destination folders on SharePoint."
}
