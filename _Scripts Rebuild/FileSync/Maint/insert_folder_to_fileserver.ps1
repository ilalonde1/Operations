# Define the base project directory
$BaseDirectory = "E:\NetworkShares\Projects\Projects"
$TargetSubPath = "04 Construction Admin\02 SSI (Structural Site Instructions)"
$NewFolderName = "SSI Sheets"

# Get all project category folders (e.g., '10 Commercial', '20 Residential', etc.)
$ProjectCategoryFolders = Get-ChildItem -Path $BaseDirectory -Directory | Where-Object { $_.Name -match '^\d{2} .+' }

foreach ($CategoryFolder in $ProjectCategoryFolders) {
    # Get all individual project folders inside each category
    $ProjectFolders = Get-ChildItem -Path $CategoryFolder.FullName -Directory

    foreach ($ProjectFolder in $ProjectFolders) {
        # Build the full path to the "02 SSI (Structural Site Instructions)" subfolder
        $TargetFolderPath = Join-Path -Path $ProjectFolder.FullName -ChildPath $TargetSubPath
        
        # Build the path for the new "SSI Sheets" folder
        $SSISheetsPath = Join-Path -Path $TargetFolderPath -ChildPath $NewFolderName

        # Only attempt to create the folder if the target path exists
        if (Test-Path $TargetFolderPath) {
            if (-not (Test-Path $SSISheetsPath)) {
                New-Item -Path $SSISheetsPath -ItemType Directory | Out-Null
                Write-Host "Created 'SSI Sheets' in: $SSISheetsPath"
            } else {
                Write-Host "'SSI Sheets' already exists in: $SSISheetsPath"
            }
        } else {
            Write-Host "Skipped - Target path does not exist: $TargetFolderPath"
        }
    }
}
