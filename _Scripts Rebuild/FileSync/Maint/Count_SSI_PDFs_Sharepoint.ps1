# ==============================
# CONFIGURATION VARIABLES
# ==============================

$TenantID = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

$SPBaseFolder = "_2026"
$TargetFolderPath = "SSI"  # Inside each project folder
$LogFile = "E:\NetworkShares\SSI\_Count\_Log.txt"

# ==============================
# FUNCTIONS
# ==============================

# Function to Get Access Token
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
        $TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body
        $global:AccessToken = $TokenResponse.access_token
        $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
        return $global:AccessToken
    } catch {
        Write-Host "ERROR: Failed to retrieve access token: $_"
        throw $_
    }
}

# Function to Retrieve PDF Files in SharePoint SSI Folder
function Get-PDFCountInFolder {
    param ([string]$FolderPath)
    
    $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/${FolderPath}:/children"
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }
    
    try {
        $Response = Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get
        $PDFCount = ($Response.value | Where-Object { $_.name -match "\.pdf$" }).Count
        return $PDFCount
    } catch {
        Write-Host "WARNING: Could not retrieve file list for $FolderPath - $_"
        return 0
    }
}

# ==============================
# MAIN EXECUTION LOGIC
# ==============================

Write-Host "Starting PDF count process in SharePoint SSI folders..."
$TotalPDFs = 0

# Get list of project folders inside "_2026"
$ProjectUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/${SPBaseFolder}:/children"
$Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }

try {
    $ProjectsResponse = Invoke-RestMethod -Uri $ProjectUri -Headers $Headers -Method Get
    $ProjectFolders = $ProjectsResponse.value | Where-Object { $_.folder }  # Filter only folders

    foreach ($Project in $ProjectFolders) {
        $ProjectName = $Project.name
        $SSIFolderPath = "$SPBaseFolder/$ProjectName/$TargetFolderPath"

        # Get PDF count in each SSI folder
        $PDFCount = Get-PDFCountInFolder -FolderPath $SSIFolderPath
        Write-Host "Project: $ProjectName, PDFs: $PDFCount"
        
        $TotalPDFs += $PDFCount
    }

    Write-Host "Total PDFs in SharePoint SSI folders: $TotalPDFs"
} catch {
    Write-Host "ERROR: Failed to retrieve project folders - $_"
}

Write-Host "PDF count process completed."
