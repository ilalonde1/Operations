# Define Variables
$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

# Authenticate with Microsoft Graph API
$Body = @{
    client_id     = $ClientID
    scope         = "https://graph.microsoft.com/.default"
    client_secret = $ClientSecret
    grant_type    = "client_credentials"
}
$TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body -ErrorAction Stop
$AccessToken = $TokenResponse.access_token

# Function to Retrieve SharePoint Folder Contents
function Get-SharePointFolderContents {
    param ($FolderPath)

    if ($FolderPath -eq "root") {
        $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    } else {
        $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($FolderPath) ):/children"
    }

    $Headers = @{ "Authorization" = "Bearer $AccessToken" }

    try {
        $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
        if ($Response.value) {
            return $Response.value
        } else {
            return @()
        }
    } catch {
        Write-Host "Error retrieving folder contents: $FolderPath - $_"
        return @()
    }
}

# Define Folders to Check
$FoldersToCheck = @("root", "2025", "_2025")

# Loop through each folder and print contents
foreach ($Folder in $FoldersToCheck) {
    Write-Host "`nContents of folder: '$Folder'"
    $Files = Get-SharePointFolderContents -FolderPath $Folder
    if ($Files.Count -eq 0) {
        Write-Host "No files found."
    } else {
        foreach ($File in $Files) {
            Write-Host "$($File.name) - $($File.folder.childCount) items"
        }
    }
}

Write-Host "Folder listing completed."
