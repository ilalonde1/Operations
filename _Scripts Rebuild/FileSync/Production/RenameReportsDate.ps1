# ==============================
# CONFIGURATION
# ==============================
$TenantID    = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID    = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret= "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID      = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID     = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile     = "C:\_APPS\FileSync\Production\Logs\Rename_Uploads_Log.txt"

$TestMode = $false   # <<<<<<<<<<<<<<<<<<< !!!!!!!! SET TO $false TO DO REAL RENAME

# ==============================
# FUNCTIONS
# ==============================

function Log-Message {
    param ([string]$Message)
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$Timestamp - $Message" | Out-File -Append -FilePath $LogFile
    Write-Host "$Timestamp - $Message"
}

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
    $TokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantID/oauth2/v2.0/token" -Method Post -ContentType "application/x-www-form-urlencoded" -Body $Body
    $global:AccessToken = $TokenResponse.access_token
    $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
    return $global:AccessToken
}

function Rename-UploadedReportFiles {
    Log-Message "Starting rename check (TestMode = $TestMode)"
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }

    $BaseUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    try {
        Log-Message "Calling URI: $BaseUri"
        $Projects = Invoke-RestMethod -Uri $BaseUri -Headers $Headers -Method Get
    } catch {
        Log-Message "ERROR: Failed to retrieve project folders - $($_.Exception.Message)"
        return
    }

    foreach ($project in $Projects.value) {
        if ($project.folder -and $project.name -match '^[0-9]{5}-[0-9]{2}' -and $project.name -ne "_Archived") {
            $projectNumber = $project.name.Split(' ')[0]
            $reportFolder = "$($project.name)/Reports"
            $reportUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($reportFolder) ):/children"

            try {
                Log-Message "Checking folder: $reportUri"
                $files = Invoke-RestMethod -Uri $reportUri -Headers $Headers -Method Get
            } catch {
                Log-Message "Skipping '$reportFolder' (not found or inaccessible): $($_.Exception.Message)"
                continue
            }

            # Find max counter already used
            $maxCounter = 0
            foreach ($file in $files.value) {
                if ($file.folder) { continue }
                if ($file.name -match "^$projectNumber-CRM-\d{4}-\d{2}-\d{2}-Report (\d{2})") {
                    $counterVal = [int]$matches[1]
                    if ($counterVal -gt $maxCounter) {
                        $maxCounter = $counterVal
                    }
                }
            }

            $counter = $maxCounter + 1

            foreach ($file in $files.value | Sort-Object name) {
                if ($file.folder) { continue }

                $ext = [System.IO.Path]::GetExtension($file.name)

                # Skip already correctly named files
                if ($file.name -match "^$projectNumber-CRM-\d{4}-\d{2}-\d{2}-Report \d{2}$ext") {
                    Log-Message "Already named correctly: $($file.name)"
                    continue
                }

                # Try to extract the original MM-dd-yyyy date
                if ($file.name -match "\d{2}-\d{2}-\d{4}") {
                    $originalDate = $matches[0]
                    try {
                        $parsedDate = [datetime]::ParseExact($originalDate, "MM-dd-yyyy", $null)
                        $reformattedDate = $parsedDate.ToString("yyyy-MM-dd")
                    } catch {
                        Log-Message "ERROR parsing date from '$($file.name)': $($_.Exception.Message)"
                        continue
                    }
                } else {
                    Log-Message "No recognizable date in '$($file.name)', skipping."
                    continue
                }

                $newName = "$projectNumber-CRM-$reformattedDate-Report {0:D2}$ext" -f $counter

                if ($file.name -eq $newName) {
                    Log-Message "Name already matches after reformatting: $newName"
                    continue
                }

                if ($TestMode) {
                    Log-Message "[TEST MODE] Would rename '$($file.name)' to '$newName'"
                } else {
                    $patchUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)"
                    $patchHeaders = $Headers + @{ "Content-Type" = "application/json" }
                    $patchBody = @{ name = $newName } | ConvertTo-Json

                    try {
                        Invoke-RestMethod -Uri $patchUri -Headers $patchHeaders -Method PATCH -Body $patchBody
                        Log-Message "Renamed '$($file.name)' to '$newName'"
                        $counter++
                    } catch {
                        Log-Message "ERROR renaming '$($file.name)': $($_.Exception.Message)"
                    }
                }
            }
        }
    }
    Log-Message "Rename check complete."
}

# ==============================
# RUN IT
# ==============================
Rename-UploadedReportFiles
