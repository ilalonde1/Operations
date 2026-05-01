# Define the base project directory
$BaseDirectory = "E:\NetworkShares\Projects\Projects"
$TargetSubPath = "04 Construction Admin\02 SSI (Structural Site Instructions)"
$FolderToDelete = "SSI Sheets"

# Get all project category folders
$ProjectCategoryFolders = Get-ChildItem -Path $BaseDirectory -Directory | Where-Object { $_.Name -match '^\d{2} .+' }

foreach ($CategoryFolder in $ProjectCategoryFolders) {
    # Get all project folders within each category
    $ProjectFolders = Get-ChildItem -Path $CategoryFolder.FullName -Directory

    foreach ($ProjectFolder in $ProjectFolders) {
        # Build full path to the "SSI Sheets" folder
        $TargetFolderPath = Join-Path -Path $ProjectFolder.FullName -ChildPath $TargetSubPath
        $SSISheetsPath = Join-Path -Path $TargetFolderPath -ChildPath $FolderToDelete

        # Delete the folder if it exists
        if (Test-Path $SSISheetsPath) {
            try {
                Remove-Item -Path $SSISheetsPath -Recurse -Force
                Write-Host "Deleted 'SSI Sheets' folder at: $SSISheetsPath"
            } catch {
                Write-Host "Failed to delete: $SSISheetsPath - $_"
            }
        } else {
            Write-Host "No 'SSI Sheets' folder found at: $SSISheetsPath"
        }
    }
}
