# Define the base directory where project categories are located
$BaseDirectory = "E:\NetworkShares\Projects\Projects"

# Define the relative path where "Sent to Inspectors" folders should be removed
$TargetSubPath = "04 Construction Admin\03 RFI (Request for Info)\Sent to Inspectors"

# Define the name of the subfolder to remove
$FolderToRemove = "Sent to Inspectors"

# Define log file path
$LogFile = "E:\NetworkShares\Projects\Cleanup_Log.txt"

# Start logging
"Cleanup Script Started: $(Get-Date)" | Out-File -FilePath $LogFile -Append

# Get all project category directories (e.g., 03 Residential, 04 Commercial, etc.)
$ProjectCategories = Get-ChildItem -Path $BaseDirectory -Directory

foreach ($Category in $ProjectCategories) {
    # Get all project folders inside each category
    $ProjectFolders = Get-ChildItem -Path $Category.FullName -Directory

    foreach ($Project in $ProjectFolders) {
        $TargetPath = Join-Path -Path $Project.FullName -ChildPath $TargetSubPath

        # Check if the target path exists
        if (Test-Path $TargetPath) {
            # Find all "Sent to Inspectors" subfolders inside the target path
            $Folders = Get-ChildItem -Path $TargetPath -Directory -Filter $FolderToRemove -Recurse

            foreach ($Folder in $Folders) {
                $FullPath = $Folder.FullName

                # Ensure the folder exists before attempting to delete
                if (Test-Path $FullPath) {
                    "Removing: $FullPath" | Out-File -FilePath $LogFile -Append
                    Write-Host "Removing: $FullPath"
                    Remove-Item -Path $FullPath -Recurse -Force -Confirm:$false
                }
            }
        }
    }
}

# Log completion
"Cleanup Completed: $(Get-Date)" | Out-File -FilePath $LogFile -Append
Write-Host "Cleanup completed. Log saved to: $LogFile"
