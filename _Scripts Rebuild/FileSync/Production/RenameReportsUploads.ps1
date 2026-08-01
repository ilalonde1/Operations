# ==============================
# CONFIGURATION
# ==============================
$TenantID     = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID     = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID       = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID      = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile      = "C:\_APPS\FileSync\Production\Logs\Rename_Uploads_Log.txt"
$TestMode     = $false   # set to $false to actually rename

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
    $global:AccessToken   = $TokenResponse.access_token
    $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
    return $global:AccessToken
}

# Helper: fetch ALL paginated results from Graph
function Get-GraphAll {
    param(
        [Parameter(Mandatory=$true)][string]$InitialUri,
        [Parameter(Mandatory=$true)][hashtable]$Headers
    )
    $all = @()
    $uri = $InitialUri

    while ($uri) {
        try {
            $resp = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        } catch {
            # If token expired mid-run, refresh once and retry this page
            $statusCode = $null
            if ($_.Exception.Response) { $statusCode = $_.Exception.Response.StatusCode.value__ }
            if ($statusCode -eq 401) {
                $Headers["Authorization"] = "Bearer $(Get-AccessToken)"
                $resp = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
            } else {
                throw
            }
        }

        if ($resp.value) { $all += $resp.value }
        $uri = $resp.'@odata.nextLink'
    }
    return $all
}

function Rename-UploadedReportFiles {
    Log-Message "Starting rename check"

    # Build headers with fresh token
    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }

    # 1) Get ALL project folders at the drive root (handles pagination)
    $BaseUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    try {
        Log-Message "Calling URI: $BaseUri"
        $Projects = Get-GraphAll -InitialUri $BaseUri -Headers $Headers
        Log-Message ("Found {0} root items; {1} folders" -f $Projects.Count, ($Projects | Where-Object { $_.folder }).Count)
    } catch {
        Log-Message "ERROR: Failed to retrieve project folders - $($_.Exception.Message)"
        return
    }

    foreach ($project in $Projects) {
        if ($project.folder -and $project.name -match '^[0-9]{5}-[0-9]{2}' -and $project.name -ne "_Archived") {

            $projectNumber = $project.name.Split(' ')[0]
            $reportFolder  = "$($project.name)/Reports"
            $reportUri     = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($reportFolder)):/children"

            # 2) Get ALL files in the Reports folder (handles pagination)
            try {
                Log-Message "Checking folder: $reportUri"
                $files = Get-GraphAll -InitialUri $reportUri -Headers $Headers
            } catch {
                Log-Message "Skipping '$reportFolder' (not found or inaccessible): $($_.Exception.Message)"
                continue
            }

            if (-not $files -or $files.Count -eq 0) {
                Log-Message "No items in '$reportFolder'"
                continue
            }

            # Find max counter already used
            $maxCounter = 0
            foreach ($file in $files) {
                if ($file.folder) { continue }
                if ($file.name -match "^$projectNumber CRM \d{4}-\d{2}-\d{2} Report (\d{2})") {
                    $counterVal = [int]$matches[1]
                    if ($counterVal -gt $maxCounter) { $maxCounter = $counterVal }
                }
            }

            $counter = $maxCounter + 1

            foreach ($file in $files | Sort-Object name) {
                if ($file.folder) { continue }

                $ext = [System.IO.Path]::GetExtension($file.name)

                # Case A: Looks correctly formatted but may have a wrong date; fix to createdDateTime
                if ($file.name -match "^($projectNumber CRM )\d{4}-\d{2}-\d{2}( Report \d{2}$([regex]::Escape($ext)))$") {
                    $prefix     = $matches[1]
                    $suffix     = $matches[2]
                    $fileDate   = Get-Date $file.createdDateTime
                    $fileStamp  = $fileDate.ToString("yyyy-MM-dd")
                    $correctName = "$prefix$fileStamp$suffix"

                    if ($file.name -ne $correctName) {
                        $patchUri     = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)"
                        $patchHeaders = $Headers + @{ "Content-Type" = "application/json" }
                        $patchBody    = @{ name = $correctName } | ConvertTo-Json
                        try {
                            if ($TestMode) {
                                Log-Message "[TEST MODE] Would rename '$($file.name)' to '$correctName'"
                            } else {
                                Invoke-RestMethod -Uri $patchUri -Headers $patchHeaders -Method PATCH -Body $patchBody
                                Log-Message "Renamed '$($file.name)' to '$correctName'"
                            }
                        } catch {
                            Log-Message "ERROR renaming '$($file.name)': $($_.Exception.Message)"
                        }
                    } else {
                        Log-Message "Date already correct in: $($file.name)"
                    }
                    continue
                }

                # Case B: Anything else -> apply standard pattern using createdDateTime
                $fileDate  = Get-Date $file.createdDateTime
                $fileStamp = $fileDate.ToString("yyyy-MM-dd")
                $newName   = "{0} CRM {1} Report {2:D2}{3}" -f $projectNumber, $fileStamp, $counter, $ext

                $patchUri     = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)"
                $patchHeaders = $Headers + @{ "Content-Type" = "application/json" }
                $patchBody    = @{ name = $newName } | ConvertTo-Json

                try {
                    if ($TestMode) {
                        Log-Message "[TEST MODE] Would rename '$($file.name)' to '$newName'"
                    } else {
                        Invoke-RestMethod -Uri $patchUri -Headers $patchHeaders -Method PATCH -Body $patchBody
                        Log-Message "Renamed '$($file.name)' to '$newName'"
                        $counter++
                    }
                } catch {
                    Log-Message "ERROR renaming '$($file.name)': $($_.Exception.Message)"
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
