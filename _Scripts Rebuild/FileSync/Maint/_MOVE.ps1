# -------------------------------
# Move a SharePoint Folder to Another Folder (Test API Connectivity)
# -------------------------------

# Define SharePoint Authentication Variables
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

# -------------------------------
# User-Defined Variables
# -------------------------------
$SourceFolder = ""  # Folder to move
$DestinationFolder = "Archived"  # New locationS

# Log File
$LogFile = "C:\Users\Ian Lalonde.ENGINEERING\Desktop\SharePointMoveLog.txt"

# Ensure Log Directory Exists
if (!(Test-Path "C:\Users\Ian Lalonde.ENGINEERING\Desktop")) {
    New-Item -ItemType Directory -Path "C:\Users\Ian Lalonde.ENGINEERING\Desktop" | Out-Null
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
# Function to Get SharePoint Folder ID
# -------------------------------
Function Get-SharePointFolder {
    param ($FolderName)
    $FolderUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$FolderName`:"
    try {
        $Response = Invoke-RestMethod -Method Get -Uri $FolderUri -Headers @{Authorization = "Bearer $AccessToken"}
        return $Response.id
    } catch {
        return $null  # Folder does not exist
    }
}

# -------------------------------
# Move SharePoint Folder
# -------------------------------
Function Move-SharePointFolder {
    param ($SourceFolderName, $DestinationFolderName)

    # Get Source Folder ID
    $SourceFolderId = Get-SharePointFolder -FolderName $SourceFolderName
    $DestinationFolderId = Get-SharePointFolder -FolderName $DestinationFolderName

    if ($SourceFolderId -and $DestinationFolderId) {
        $MoveUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$SourceFolderId"
        $MoveBody = @{ parentReference = @{ id = $DestinationFolderId } } | ConvertTo-Json -Depth 3

        try {
            Invoke-RestMethod -Method Patch -Uri $MoveUri -Headers @{
                Authorization = "Bearer $AccessToken"
                "Content-Type" = "application/json"
            } -Body $MoveBody -ErrorAction Stop

            Add-Content -Path $LogFile -Value "$(Get-Date) - SUCCESS: Moved '$SourceFolderName' to '$DestinationFolderName'."
            Write-Host "SUCCESS: Folder '$SourceFolderName' moved to '$DestinationFolderName'."
        } catch {
            $ErrorMessage = $_.Exception.Message
            Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Failed to move '$SourceFolderName' - $ErrorMessage"
            Write-Host "ERROR: Failed to move '$SourceFolderName' - $ErrorMessage"
        }
    } else {
        Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: One or both folders not found on SharePoint."
        Write-Host "ERROR: One or both folders not found on SharePoint."
    }
}

# -------------------------------
# Execute Folder Move (Test Connectivity)
# -------------------------------
Move-SharePointFolder -SourceFolderName $SourceFolder -DestinationFolderName $DestinationFolder
