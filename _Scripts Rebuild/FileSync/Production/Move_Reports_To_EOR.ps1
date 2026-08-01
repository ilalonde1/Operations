param (
    [switch]$WhatIf
)

# ==============================
# CONFIGURATION
# ==============================
$TenantID     = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID     = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret = "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID       = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID      = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"
$LogFile      = "C:\_APPS\FileSync\Production\Logs\Move_Reports_Log.txt"
$AuditMonth   = (Get-Date).ToString("MM-yyyy")
$AuditLog     = "\\KOR-FS01\Projects\Reporting\Number Of Reports\Move_Reports_Audit_$AuditMonth.csv"

$TempDir      = "C:\_APPS\FileSync\Temp"

$EOR_Emails = @{
    "Jeremy Atkinson"       = "jatkinson@korstructural.com"
    "Conor Murtagh"         = "cmurtagh@korstructural.com"
    "Jim DesRoches"         = "jdesroches@korstructural.com"
    "John Markulin"         = "jmarkulin@korstructural.com"
    "John Zickmantel"       = "admin@korstructural.com"
    "Kevin Wurmlinger"      = "kevinw@korstructural.com"
    "Omar Alcazar Pastrana" = "omara@korstructural.com"
    "Rory Beirne"           = "rbeirne@korstructural.com"
}

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
    $global:AccessToken     = $TokenResponse.access_token
    $global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
    return $global:AccessToken
}

# Pagination helper for Graph
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

function Send-GraphMail {
    param (
        [string]$To,
        [string]$Subject,
        [string]$Body
    )

    $headers = @{
        "Authorization" = "Bearer $(Get-AccessToken)"
        "Content-Type"  = "application/json"
    }

    $emailBody = @{
        message = @{
            subject = $Subject
            body = @{
                contentType = "Text"
                content     = $Body
            }
            toRecipients = @(@{ emailAddress = @{ address = $To } })
            ccRecipients = @(@{ emailAddress = @{ address = "ilalonde@korstructural.com" } })
        }
        saveToSentItems = "false"
    } | ConvertTo-Json -Depth 10

    $uri = "https://graph.microsoft.com/v1.0/users/ilalonde@korstructural.com/sendMail"
    try {
        Invoke-RestMethod -Uri $uri -Headers $headers -Method POST -Body $emailBody
        Log-Message "Notification sent to $To (cc ilalonde@korstructural.com)"
    } catch {
        Log-Message "ERROR sending email to $To - $($_.Exception.Message)"
    }
}

function Move-ReportFilesToEORFolders {
    Log-Message "========== Starting month-end report move =========="
    if ($WhatIf) { Log-Message "Running in WHATIF mode — no files will be moved." }

    if (-not (Test-Path -LiteralPath $TempDir)) {
        New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
    }

    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }
    $Moves = @()
    $NotifiedEORs = @{}

    $monthYearFolderName = (Get-Date).ToString("MMMM yyyy")

    # 1) Load EOR.csv
    $csvUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/_FIELD REVIEWS TO INITIAL/EOR.csv:/content"
    try {
        $csvContent = Invoke-RestMethod -Uri $csvUri -Headers $Headers -Method Get
        $csvData = $csvContent | ConvertFrom-Csv
        $EORMap = @{}
        foreach ($row in $csvData) {
            $projectNumber = $row.ProjectNumber.Trim().Trim('"')
            $eorLastName   = $row.EOR.Trim().Trim('"')
            if ($projectNumber) { $EORMap[$projectNumber] = $eorLastName }
        }
        Log-Message ("Loaded {0} EOR mappings" -f $EORMap.Count)
    } catch {
        Log-Message "ERROR: Failed to read EOR.csv - $($_.Exception.Message)"
        return
    }

    # 2) Get ALL existing folders under _FIELD REVIEWS TO INITIAL (pagination)
    $eorFoldersUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/_FIELD REVIEWS TO INITIAL:/children"
    try {
        $eorChildren = Get-GraphAll -InitialUri $eorFoldersUri -Headers $Headers
        $existingEorFolders = $eorChildren | Where-Object { $_.folder } | ForEach-Object { $_.name }

        # Build a lookup that maps any word in the folder name to the full folder name
        $EORFolderMap = @{}
        foreach ($folder in $existingEorFolders) {
            foreach ($word in ($folder -split ' ')) {
                $key = $word.ToLower()
                if (-not $EORFolderMap.ContainsKey($key)) { $EORFolderMap[$key] = @() }
                $EORFolderMap[$key] += $folder
            }
        }
        Log-Message ("Discovered {0} EOR folders" -f ($existingEorFolders | Measure-Object).Count)
    } catch {
        Log-Message "ERROR: Could not retrieve EOR folders - $($_.Exception.Message)"
        return
    }

    # 3) Get ALL top-level project folders (pagination)
    $ProjectsUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root/children"
    try {
        $Projects = Get-GraphAll -InitialUri $ProjectsUri -Headers $Headers
        Log-Message ("Found {0} root items; {1} folders" -f $Projects.Count, ($Projects | Where-Object { $_.folder }).Count)
    } catch {
        Log-Message "ERROR: Could not retrieve project folders - $($_.Exception.Message)"
        return
    }

    foreach ($project in $Projects) {
        if (-not ($project.folder) -or $project.name -eq "_Archived") { continue }
        if ($project.name -notmatch '^[0-9]{5}-[0-9]{2}') { continue }

        $projectNumber = $project.name.Split(' ')[0]
        $eorName = "CatchAll"

        if ($EORMap.ContainsKey($projectNumber)) {
            $eorLastName = $EORMap[$projectNumber].ToLower()
            if ($EORFolderMap.ContainsKey($eorLastName)) {
                $eorName = $EORFolderMap[$eorLastName][0]
                Log-Message "Matched '$eorLastName' to folder '$eorName'"
            } else {
                Log-Message "No folder match for '$eorLastName'. Using CatchAll."
            }
        } else {
            Log-Message "No EOR mapping for project '$projectNumber'. Using CatchAll."
        }

        # 4) Get ALL files in the project's Reports folder (pagination)
        $reportFolderPath = "$($project.name)/Reports"
        $reportFolderUri  = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($reportFolderPath)):/children"
        try {
            $reportFiles = Get-GraphAll -InitialUri $reportFolderUri -Headers $Headers
        } catch {
            Log-Message "No Reports folder found or accessible for '$($project.name)'"
            continue
        }

        if (-not $reportFiles -or $reportFiles.Count -eq 0) {
            Log-Message "No files in Reports folder for '$($project.name)'"
            continue
        }

        $baseFolderPath = "_FIELD REVIEWS TO INITIAL/$eorName"
        $destFolderPath = "$baseFolderPath"

        $controlFileName   = "Acknowledge and Move To Server $(Get-Date -Format 'MMMM').txt"
        $controlFileCreated = $false

        foreach ($file in $reportFiles) {
            if ($file.folder) { continue }

            $downloadUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)/content"
            $tempFile    = Join-Path $TempDir $file.name

            if ($WhatIf) {
                Log-Message "[WhatIf] Would move '$($file.name)' from '$reportFolderPath' to '$destFolderPath'"
            } else {
                $uploadUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString("$destFolderPath/$($file.name)")):/content"
                $deleteUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)"

                try {
                    Invoke-RestMethod -Uri $downloadUri -Headers $Headers -OutFile $tempFile
                    Log-Message "Downloaded '$($file.name)' to temp file"
                } catch {
                    Log-Message "ERROR downloading '$($file.name)': $($_.Exception.Message)"
                    continue
                }

                try {
                    Invoke-RestMethod -Uri $uploadUri -Headers $Headers -Method PUT -InFile $tempFile -ContentType "application/octet-stream"
                    Log-Message "Uploaded '$($file.name)' to '$destFolderPath'"

                    # Create control file once per EOR folder (if not CatchAll)
                    if (-not $controlFileCreated -and $eorName -ne "CatchAll") {
                        $controlFileUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString("$baseFolderPath/$controlFileName")):/content"
                        $controlText = "These reports have been automatically copied. Please acknowledge and move them to the server."
                        try {
                            $controlBytes = [System.Text.Encoding]::UTF8.GetBytes($controlText)
                            $tempControlFile = [System.IO.Path]::GetTempFileName()
                            [System.IO.File]::WriteAllBytes($tempControlFile, $controlBytes)

                            Invoke-RestMethod -Uri $controlFileUri -Headers $Headers -Method PUT -InFile $tempControlFile -ContentType "text/plain"
                            Log-Message "Created control file '$controlFileName' in '$baseFolderPath'"
                            $controlFileCreated = $true
                        } catch {
                            Log-Message "ERROR creating control file in '$baseFolderPath': $($_.Exception.Message)"
                        }
                    }

                } catch {
                    Log-Message "ERROR uploading '$($file.name)': $($_.Exception.Message)"
                    continue
                }

                try {
                    Invoke-RestMethod -Uri $deleteUri -Headers $Headers -Method DELETE
                    Log-Message "Deleted original file '$($file.name)' after copy"
                } catch {
                    Log-Message "ERROR deleting original file '$($file.name)': $($_.Exception.Message)"
                }
            }

            $Moves += [PSCustomObject]@{
                Project     = $project.name
                FileName    = $file.name
                Destination = $destFolderPath
                Time        = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
                Simulated   = $WhatIf.IsPresent
            }

            if (-not $NotifiedEORs.ContainsKey($eorName) -and $eorName -ne "CatchAll") {
                $NotifiedEORs[$eorName] = 0
            }
            if ($eorName -ne "CatchAll") {
                $NotifiedEORs[$eorName]++
            }
        }
    }

    if ($Moves.Count -gt 0 -and -not $WhatIf) {
        $Moves | Export-Csv -Path $AuditLog -NoTypeInformation -Encoding UTF8
        Log-Message "Audit log written to $AuditLog"
        Log-Message "Total files moved: $($Moves.Count)"
        foreach ($entry in $Moves) {
            Log-Message " - $($entry.FileName) => $($entry.Destination)"
        }
    } elseif ($WhatIf -and $Moves.Count -gt 0) {
        Log-Message "[WhatIf] Simulated move summary:"
        foreach ($entry in $Moves) {
            Log-Message " - $($entry.FileName) => $($entry.Destination)"
        }
    } else {
        Log-Message "No files were moved."
    }

    if (-not $WhatIf) {
        foreach ($eor in $NotifiedEORs.Keys) {
            $count = $NotifiedEORs[$eor]
            $email = $EOR_Emails[$eor]
            if (-not $email) {
                Log-Message "No email address found for '$eor'. Skipping notification."
                continue
            }
            $subject = "Reports copied to your SharePoint folder"
            $body = "Hello $eor,`r`n`r`n$count reports have been automatically copied to your SharePoint folder under _FIELD REVIEWS TO INITIAL\$eor.`r`nPlease acknowledge them by deleting the file: $controlFileName.`r`n`r`nThank you."
            Send-GraphMail -To $email -Subject $subject -Body $body
        }
    }

    Log-Message "========== Month-end report move complete =========="
}

# ==============================
# RUN IT
# ==============================
Move-ReportFilesToEORFolders
