# Define Variables
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

# Define Log File Path
$LogFile = "E:\NetworkShares\Folder_List_Log.txt"

# Authenticate with Microsoft Graph API
$Body = @{
    client_id     = $ClientID
    scope         = "https://graph.microsoft.com/.default"
    client_secret = $ClientSecret
    grant_type    = "client_credentials"
}
$TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
$AccessToken = $TokenResponse.access_token

# Logging Function
function Log-Message {
    param ($Message)
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$Timestamp - $Message" | Out-File -Append -FilePath $LogFile
}

Log-Message "Starting root folder extraction process..."

# Function to Get Folders in the Root of SharePoint
function Get-SharePointRootFolders {
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    $Headers = @{ "Authorization" = "Bearer $AccessToken" }

    try {
        $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
        return $Response.value | Where-Object { $_.folder -ne $null }  # Only return folders
    } catch {
        Log-Message "Error retrieving root folders from SharePoint: $_"
        return @()
    }
}

# Retrieve Root Folders
$RootFolders = Get-SharePointRootFolders

# Process and Log Extracted Folder Names
foreach ($Folder in $RootFolders) {
    $FolderName = $Folder.name
    $ExtractedName = $FolderName.Substring(0, [Math]::Min(8, $FolderName.Length))  # Get first 8 characters
    Log-Message "Extracted Root Folder Name: $ExtractedName"
}

Write-Host "Root folder name extraction completed."
Log-Message "Root folder name extraction completed."
