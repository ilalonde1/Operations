param (
    [switch]$WhatIf
)

# ==============================
# CONFIGURATION
# ==============================
$TenantID    = "d9be1f7f-aacf-461a-8d1b-5528b86d540f"
$ClientID    = "5b20a407-0b59-4c75-b2e5-d2cf970c5dbd"
$ClientSecret= "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"
$SiteID      = "e197528f-6707-4dd5-afec-04964a94c294"
$DriveID     = "b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA"

$LogFile     = "C:\_APPS\FileSync\Production\Logs\Move_Reports_ToSend_Log.txt"
$AuditMonth  = (Get-Date).ToString("MM-yyyy")
$AuditLog    = "\\KOR-FS01\Projects\Reporting\Number Of Reports\Move_ToSend_Audit_$AuditMonth.csv"

# Local paths
$RootServerPath = "\\KOR-FS01\Projects\Projects"
$TempDir        = "C:\temp\ToSend"

# ==============================
# UTIL
# ==============================
function Log-Message {
    param ([string]$Message)
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$Timestamp - $Message" | Out-File -Append -FilePath $LogFile
    Write-Host "$Timestamp - $Message"
}

function Ensure-Folder {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

# ==============================
# GRAPH
# ==============================
function Get-AccessToken {
    if ($global:AccessToken -and ($global:TokenExpiration -gt (Get-Date))) { return $global:AccessToken }
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

# Follows @odata.nextLink; refreshes token once on 401 for the current page
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
            $code = $null
            if ($_.Exception.Response) { $code = $_.Exception.Response.StatusCode.value__ }
            if ($code -eq 401) {
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

function Send-MoveSummaryEmail {
    param ([array]$SummaryList)
    $headers = @{ "Authorization" = "Bearer $(Get-AccessToken)"; "Content-Type" = "application/json" }

    $bodyContent  = "Hello,`n`nThe following files have been moved to the project server:`n"
    $bodyContent += (($SummaryList | ForEach-Object { "- $($_.FileName) => $($_.Destination)" }) -join "`n")
    $bodyContent += "`n`nThank you."

    $emailBody = @{
        message = @{
            subject = "Project Reports Moved to Server"
            body = @{ contentType = "Text"; content = $bodyContent }
            toRecipients = @(@{ emailAddress = @{ address = "admin@korstructural.com" } })
            ccRecipients = @(@{ emailAddress = @{ address = "ilalonde@korstructural.com" } })
        }
        saveToSentItems = "false"
    } | ConvertTo-Json -Depth 10

    $uri = "https://graph.microsoft.com/v1.0/users/ilalonde@korstructural.com/sendMail"
    try {
        Invoke-RestMethod -Uri $uri -Headers $headers -Method POST -Body $emailBody
        Log-Message "Summary email sent to ilalonde@korstructural.com"
    } catch {
        Log-Message "ERROR sending summary email — $($_.Exception.Message)"
    }
}

# ==============================
# MAIN
# ==============================
function Move-ReportsToProjectServer {
    Log-Message "========== Starting EOR-to-server move =========="
    if ($WhatIf) { Log-Message "Running in WHATIF mode — no files will be moved." }

    # Ensure local folders exist
    Ensure-Folder -Path (Split-Path -Path $LogFile -Parent)
    Ensure-Folder -Path (Split-Path -Path $AuditLog -Parent)
    Ensure-Folder -Path $TempDir

    $Headers = @{ "Authorization" = "Bearer $(Get-AccessToken)" }

    # Enumerate project category folders (portable across Windows PowerShell and PS7)
    $projectCategories = Get-ChildItem -Path $RootServerPath |
                         Where-Object { $_.PSIsContainer -and $_.Name -notlike "00*" }

    $monthName       = (Get-Date -Format 'MMMM')
    $controlFileName = "Acknowledge and Move To Server $monthName.txt"
    $eorBasePath     = "_FIELD REVIEWS TO INITIAL"

    # EOR folders (paginated)
    $eorFoldersUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/${eorBasePath}:/children"
    try {
        $eorFolders = Get-GraphAll -InitialUri $eorFoldersUri -Headers $Headers | Where-Object { $_.folder }
        Log-Message ("Found {0} EOR folders" -f $eorFolders.Count)
    } catch {
        Log-Message "ERROR retrieving EOR folders — $($_.Exception.Message)"
        return
    }

    $MoveSummary = @()

    foreach ($eor in $eorFolders) {
        $eorName           = $eor.name
        $eorFolderUriPath  = "$eorBasePath/$eorName"

        # Skip if control file exists
        $controlUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString("$eorFolderUriPath/$controlFileName"))"
        $controlCheck = try {
            Invoke-RestMethod -Uri $controlUri -Headers $Headers -Method Get -ErrorAction Stop
        } catch {
            if ($_.Exception.Response.StatusCode.value__ -eq 404) { $null } else {
                Log-Message "ERROR checking control file for $eorName — $($_.Exception.Message)"
                continue
            }
        }
        if ($controlCheck) {
            Log-Message "Control file found for $eorName. Skipping folder."
            continue
        }

        # Files in EOR folder (paginated)
        $filesUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$([uri]::EscapeDataString($eorFolderUriPath)):/children"
        try {
            $files = Get-GraphAll -InitialUri $filesUri -Headers $Headers | Where-Object { -not $_.folder }
        } catch {
            Log-Message "ERROR reading files for $eorName — $($_.Exception.Message)"
            continue
        }

        foreach ($file in $files) {
            $filename = $file.name
            if ($filename.Length -lt 8 -or $filename -notmatch "^[0-9]{5}-[0-9]{2}") {
                Log-Message "Skipping invalid project file name: $filename"
                continue
            }

            $projectNum = $filename.Substring(0,8)
            $matchedProjectPath = $null

            foreach ($category in $projectCategories) {
                $projMatch = Get-ChildItem -Path $category.FullName |
                             Where-Object { $_.PSIsContainer -and $_.Name -like "$projectNum*" } |
                             Select-Object -First 1
                if ($projMatch) {
                    $matchedProjectPath = Join-Path $projMatch.FullName "04 Construction Admin\01 Inspection Reports\To Send"
                    break
                }
            }

            if (-not $matchedProjectPath) {
                Log-Message "No matching project folder found for $projectNum — skipping $filename"
                continue
            }

            if (-not (Test-Path $matchedProjectPath)) {
                try {
                    New-Item -Path $matchedProjectPath -ItemType Directory -Force | Out-Null
                    Log-Message "Created folder: $matchedProjectPath"
                } catch {
                    Log-Message "ERROR creating folder $matchedProjectPath — $_"
                    continue
                }
            }

            $tempFile    = Join-Path -Path $TempDir -ChildPath $filename
            $downloadUri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)/content"
            $deleteUri   = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/items/$($file.id)"

            try {
                Invoke-RestMethod -Uri $downloadUri -Headers $Headers -OutFile $tempFile
                Log-Message "Downloaded $filename to $tempFile"

                Copy-Item -Path $tempFile -Destination (Join-Path $matchedProjectPath $filename) -Force
                Log-Message "Copied $filename to $matchedProjectPath"

                if (-not $WhatIf) {
                    Invoke-RestMethod -Uri $deleteUri -Headers $Headers -Method DELETE
                    Log-Message "Deleted original SharePoint file: $filename"
                }

                Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

                $MoveSummary += [PSCustomObject]@{
                    FileName    = $filename
                    Destination = $matchedProjectPath
                    Time        = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
                }
            } catch {
                Log-Message "ERROR processing $filename — $_"
                continue
            }
        }
    }

    if ($MoveSummary.Count -gt 0 -and -not $WhatIf) {
        $MoveSummary | Export-Csv -Path $AuditLog -NoTypeInformation -Encoding UTF8
        Log-Message "Audit log written to $AuditLog"
        Send-MoveSummaryEmail -SummaryList $MoveSummary
    }

    Log-Message "========== EOR-to-server move complete =========="
}

Move-ReportsToProjectServer
