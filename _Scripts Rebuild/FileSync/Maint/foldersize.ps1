$basePath = "E:\NetworkShares\Projects\Projects"

function Get-FolderSizeMB {
    param ([string]$FolderPath)
    $totalSize = 0
    if (Test-Path $FolderPath) {
        Get-ChildItem -Path $FolderPath -Recurse -File | ForEach-Object {
            $totalSize += $_.Length
        }
    }
    return [math]::Round($totalSize / 1MB, 2)
}

$categoryFolders = Get-ChildItem -Path $basePath -Directory

foreach ($category in $categoryFolders) {
    $projects = Get-ChildItem -Path $category.FullName -Directory

    foreach ($project in $projects) {
        $emailsPath = Join-Path $project.FullName "Emails"
        if (Test-Path $emailsPath) {
            $sizeMB = Get-FolderSizeMB -FolderPath $emailsPath
            Write-Host "$($project.FullName) - Emails folder size: $sizeMB MB"
        }
    }
}
