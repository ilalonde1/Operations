# Simple script to count PDFs in SSI folders
$BaseDirectory = "E:\NetworkShares\Projects\Projects"
$TargetFolderPath = "04 Construction Admin\02 SSI (Structural Site Instructions)"

$TotalPDFCount = 0

# Get all project category folders
$ProjectFolders = Get-ChildItem -Path $BaseDirectory -Directory

foreach ($Category in $ProjectFolders) {
    $Projects = Get-ChildItem -Path $Category.FullName -Directory

    foreach ($Project in $Projects) {
        # Construct Local SSI Path
        $LocalSSIPath = Join-Path -Path $Project.FullName -ChildPath $TargetFolderPath
        
        # Skip if SSI folder does not exist
        if (!(Test-Path $LocalSSIPath)) {
            continue
        }

        # Get all PDFs in the SSI folder
        $LocalFiles = Get-ChildItem -Path $LocalSSIPath -File | Where-Object { $_.Extension -eq ".pdf" }
        $TotalPDFCount += $LocalFiles.Count
    }
}

Write-Host "Total PDF count in SSI folders: $TotalPDFCount"
