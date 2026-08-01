# Define the base project directory
$BaseDirectory = "E:\NetworkShares\Projects\Projects"
$TextFileName = "NOT SYNCED TO ACTIVE PROJECTS.txt"
$TextFileContent = "You must remove this file if you'd like this folder synced with Active Projects."

# Get all project directories
$ProjectFolders = Get-ChildItem -Path $BaseDirectory -Directory | Where-Object { $_.Name -match '^\d{2} .+' }

foreach ($CategoryFolder in $ProjectFolders) {
    $ProjectSubFolders = Get-ChildItem -Path $CategoryFolder.FullName -Directory
    
    foreach ($ProjectFolder in $ProjectSubFolders) {
        $TextFilePath = Join-Path -Path $ProjectFolder.FullName -ChildPath $TextFileName
        
        if (-not (Test-Path $TextFilePath)) {
            $TextFileContent | Out-File -FilePath $TextFilePath -Encoding UTF8
            Write-Host "Inserted $TextFileName into $($ProjectFolder.FullName)"
        } else {
            Write-Host "$TextFileName already exists in $($ProjectFolder.FullName)"
        }
    }
}
