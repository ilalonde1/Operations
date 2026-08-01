# Define Variables
$UploadedFiles = 0
$TotalFiles = 0

# Initialize
Log-Message "Starting file sync process..."
Get-SharePointFolders
Ensure-SharePointFolder -ParentPath "/" -FolderName "2025"

# Create Runspace Pool for Parallel Processing
$RunspacePool = [runspacefactory]::CreateRunspacePool(1, 5)
$RunspacePool.Open()
$Runspaces = @()

# Process Local Projects
foreach ($Category in Get-ChildItem -Path $BaseDirectory -Directory) {
    foreach ($Project in Get-ChildItem -Path $Category.FullName -Directory) {
        $LocalStickfilePath = Join-Path -Path $Project.FullName -ChildPath $TargetFolderPath
        if (-Not (Test-Path $LocalStickfilePath)) { continue }
        $LocalFiles = Get-ChildItem -Path $LocalStickfilePath -File -Filter "*.pdf"
        if ($LocalFiles.Count -eq 0) { continue }

        $TotalFiles += $LocalFiles.Count # Track total files

        $SPProjectFolder = "$SPBaseFolder/$($Project.Name)"
        Ensure-SharePointFolder -ParentPath $SPBaseFolder -FolderName $Project.Name
        Ensure-SharePointFolder -ParentPath $SPProjectFolder -FolderName "Reports"

        # Upload Files in Parallel
        foreach ($File in $LocalFiles) {
            $Runspace = [powershell]::Create().AddScript({
                param ($FilePath, $SPProjectFolder, $SiteID, $DriveID, $AccessToken, [ref]$UploadedFiles)
                $SPFilePath = "$SPProjectFolder/$($FilePath.Name)"
                $Uri = "https://graph.microsoft.com/v1.0/sites/$SiteID/drives/$DriveID/root:/$( [uri]::EscapeDataString($SPFilePath) ):/content"
                $Headers = @{ "Authorization" = "Bearer $AccessToken"; "Content-Type" = "application/octet-stream" }
                try {
                    Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Put -InFile $FilePath.FullName -ErrorAction Stop
                    Log-Message "Successfully uploaded: $SPFilePath"
                    [System.Threading.Interlocked]::Increment($UploadedFiles.Value)
                } catch {
                    Log-Message "Failed to upload: $SPFilePath - $_"
                }
            }).AddArgument($File).AddArgument($SPProjectFolder).AddArgument($SiteID).AddArgument($DriveID).AddArgument((Get-AccessToken)).AddArgument([ref]$UploadedFiles)
            
            $Runspace.RunspacePool = $RunspacePool
            $Runspaces += [PSCustomObject]@{ Pipe = $Runspace; Status = $Runspace.BeginInvoke() }
        }
    }
}

# Live Counter Thread
$LiveCounter = {
    param ($TotalFiles, [ref]$UploadedFiles)
    while ($UploadedFiles.Value -lt $TotalFiles) {
        Write-Host "`rUploaded: $($UploadedFiles.Value) / $TotalFiles" -NoNewline
        Start-Sleep -Seconds 1
    }
    Write-Host "`rUploaded: $($UploadedFiles.Value) / $TotalFiles - Completed!`n"
}

# Start Counter in a Separate Job
$CounterJob = Start-Job -ScriptBlock $LiveCounter -ArgumentList $TotalFiles, ([ref]$UploadedFiles)

# Wait for all runspaces to complete
foreach ($Job in $Runspaces) {
    $Job.Pipe.EndInvoke($Job.Status)
    $Job.Pipe.Dispose()
}

# Cleanup
$RunspacePool.Close()
$RunspacePool.Dispose()

# Stop Live Counter
Stop-Job -Job $CounterJob
Remove-Job -Job $CounterJob

# Display final upload count
Write-Host "Upload Completed: $UploadedFiles / $TotalFiles files successfully uploaded."
Log-Message "File sync completed. Uploaded $UploadedFiles / $TotalFiles files."
