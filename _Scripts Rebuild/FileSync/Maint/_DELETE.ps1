# -------------------------------
# Define SharePoint Authentication Variables
# -------------------------------
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

# -------------------------------
# User-Defined Variables
# -------------------------------
$GarbageFolder = "2025"  # Folder to clean

# Log File
$LogFile = "C:\Users\Ian Lalonde.ENGINEERING\Desktop\SharePointDeleteLog.txt"

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
# Function to Get All Folder Contents with Pagination
# -------------------------------
Function Get-FolderContents {
    param ($FolderId)
    $Items = @()
    $NextLink = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$FolderId/children"

    do {
        try {
            $Response = Invoke-RestMethod -Method Get -Uri $NextLink -Headers @{Authorization = "Bearer $AccessToken"}
            $Items += $Response.value
            $NextLink = $Response.'@odata.nextLink'  # Get the next page link if exists
        } catch {
            return @()  # Return empty array if an error occurs
        }
    } while ($NextLink)  # Keep requesting until there are no more pages

    return $Items
}

# -------------------------------
# Function to Delete a SharePoint Item (File or Folder)
# -------------------------------
Function Delete-SharePointItem {
    param ($ItemId, $ItemName, $IsFolder)

    $DeleteUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$ItemId"
    
    try {
        Invoke-RestMethod -Method Delete -Uri $DeleteUri -Headers @{Authorization = "Bearer $AccessToken"}
        Add-Content -Path $LogFile -Value "$(Get-Date) - SUCCESS: Deleted '$ItemName' ($($IsFolder ? "Folder" : "File"))."
        Write-Host "SUCCESS: Deleted '$ItemName' ($($IsFolder ? "Folder" : "File"))."
    } catch {
        $ErrorMessage = $_.Exception.Message
        Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Failed to delete '$ItemName' - $ErrorMessage"
        Write-Host "ERROR: Failed to delete '$ItemName' - $ErrorMessage"
    }
}

# -------------------------------
# Recursive Function to Delete All Contents of a Folder
# -------------------------------
Function Empty-FolderRecursively {
    param ($FolderId)

    # Get all files & subfolders (using pagination)
    $Items = Get-FolderContents -FolderId $FolderId

    # First, delete all files
    foreach ($Item in $Items) {
        if ($Item.folder -eq $null) {  # It's a file
            Delete-SharePointItem -ItemId $Item.id -ItemName $Item.name -IsFolder $false
        }
    }

    # Then, delete all folders (after they are empty)
    foreach ($Item in $Items) {
        if ($Item.folder -ne $null) {  # It's a folder
            Empty-FolderRecursively -FolderId $Item.id
            Delete-SharePointItem -ItemId $Item.id -ItemName $Item.name -IsFolder $true
        }
    }
}

# -------------------------------
# Main Function: Empty "Garbage" Folder
# -------------------------------
Function Empty-GarbageFolder {
    $GarbageFolderId = Get-SharePointFolder -FolderName $GarbageFolder

    if ($GarbageFolderId) {
        Empty-FolderRecursively -FolderId $GarbageFolderId
        Write-Host "The Garbage folder has been emptied."
        Add-Content -Path $LogFile -Value "$(Get-Date) - INFO: The Garbage folder has been emptied."
    } else {
        Write-Host "ERROR: Garbage folder not found."
        Add-Content -Path $LogFile -Value "$(Get-Date) - ERROR: Garbage folder not found."
    }
}

# -------------------------------
# Execute Folder Cleanup
# -------------------------------
Empty-GarbageFolder
