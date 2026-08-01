$basePath = "E:\NetworkShares\Projects\Projects"
$logFilePath = "C:\Ian\FileSync\Maint\copy_emails_log.txt"

# Logging function
function Log-Message {
    param ([string]$Message)
    Add-Content -Path $logFilePath -Value ("[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message")
    Write-Output $Message
}

# Optional: project filter list
$validPrefixes = @(
    "30108-03", "30167-63", "30410-13", "30461-15"  # Modify as needed
)

Log-Message "==== Starting Newforma email copy ===="

# Loop through category folders
$categoryFolders = Get-ChildItem -Path $basePath -Directory
foreach ($category in $categoryFolders) {
    $projects = Get-ChildItem -Path $category.FullName -Directory

    foreach ($project in $projects) {
        $projectName = $project.Name


        $sourceEmailsPath = Join-Path -Path $project.FullName -ChildPath "Newforma\email"
        $destEmailsPath = Join-Path -Path $project.FullName -ChildPath "Emails"

        if (Test-Path $sourceEmailsPath) {
            Log-Message "Found source: $sourceEmailsPath"

            if (-not (Test-Path $destEmailsPath)) {
                New-Item -Path $destEmailsPath -ItemType Directory | Out-Null
                Log-Message "Created destination folder: $destEmailsPath"
            }

            # Copy files and folders
            Get-ChildItem -Path $sourceEmailsPath -Recurse | ForEach-Object {
                $relativePath = $_.FullName.Substring($sourceEmailsPath.Length).TrimStart("\")
                $destination = Join-Path $destEmailsPath $relativePath

                if ($_.PSIsContainer) {
                    if (-not (Test-Path $destination)) {
                        New-Item -Path $destination -ItemType Directory | Out-Null
                    }
                } else {
                    Copy-Item -Path $_.FullName -Destination $destination -Force
                    Log-Message "Copied: $($_.FullName) -> $destination"
                }
            }
        } else {
            Log-Message "No source found for: $projectName"
        }
    }
}

Log-Message "==== Copy operation completed. ===="
