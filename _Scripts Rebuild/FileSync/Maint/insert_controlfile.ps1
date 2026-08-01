$basePath = "E:\NetworkShares\Projects\Projects"
$txtFilePath = "C:\Users\administrator.ENGINEERING\Desktop\FileSync\Latest\Production\Active Projects Sync.txt"
$logFilePath = "C:\Users\administrator.ENGINEERING\Desktop\FileSync\sync_log.txt"

# List of project folder prefixes to filter
$validProjectPrefixes = @(
    "01577-01", "01589-01", "01599-01", "01608-01", "01622-01", "01668-01", "01669-01", "01672-01", "01700-01",
    "30477-01", "30519-02", "30540-04", "30607-05", "30638-15", "30654-04", "30658-01", "30659-03", "30662-01",
    "30694-01", "30695-14", "30695-16", "30696-01", "30696-13", "30751-01", "30754-01", "30754-02", "30758-02",
    "30768-01", "30770-01", "30770-06", "30770-08", "30770-09", "30771-01", "30777-01", "30780-01", "30783-01",
    "30784-01", "30785-01", "30788-01", "30789-01", "30798-01", "30799-01", "30800-01", "30804-01", "30805-01",
    "30807-01", "30809-01", "30813-01", "30819-01", "30820-01", "30820-02", "30821-01", "30823-01", "30824-01",
    "30825-01", "30826-01", "30828-01", "30832-01", "30835-01", "30836-01", "30837-01", "30844-01", "30847-01",
    "30848-01", "30849-01", "30850-10", "30850-11", "30853-01", "30854-01", "30862-01", "30864-01", "30866-01",
    "30867-01", "30869-01", "30873-01", "30874-01", "30878-01", "30878-02", "30879-01", "30885-01", "30889-01",
    "30891-01", "30892-01", "30893-01", "30894-01", "30895-01", "30898-01", "30905-01", "30906-01", "30907-01",
    "30908-01", "30909-01", "30911-01", "30912-01", "30913-01", "30919-01", "30924-01", "30926-01", "30927-01",
    "30928-01", "30929-01", "30930-01", "30932-01", "30933-01", "30934-01", "30938-01", "30940-01", "30941-01",
    "30942-01", "30943-01", "30945-01", "30946-01", "30947-01", "30949-01", "30953-01", "30955-01", "30956-01",
    "30957-01", "30958-01", "30960-02", "30963-01", "30967-01", "30973-01", "30974-01", "30975-01", "30977-01",
    "30978-01", "30980-01", "30984-01", "30985-01", "30988-01", "30995-01", "30996-01", "30996-02", "30996-03",
    "31004-01", "31005-01", "31007-01", "31008-01", "31014-01", "31015-01", "31019-01", "31020-01", "31029-05",
    "31032-01", "31037-01", "31040-01", "31042-01", "31042-02", "31044-02", "31045-01", "31048-01", "31052-01",
    "31055-01", "31067-01", "31076-01", "31083-02", "31131-01", "40100-01", "40106-01", "40108-01", "40112-01",
    "40112-03", "50041-01", "50041-04", "50043-01", "50043-04", "50044-01", "50046-08", "60054-01", "60060-01",
    "60062-01", "70056-01", "70056-02", "70057-01", "70059-01", "70061-01", "80062-01", "90093-01", "90095-01",
    "90103-01"
)

# Function to log messages
function Log-Message {
    param ([string]$Message)
    Add-Content -Path $logFilePath -Value $Message
    Write-Output $Message
}

Log-Message "Starting file synchronization..."

# Check if the source file exists
if (-Not (Test-Path $txtFilePath)) {
    Log-Message "Source file not found: $txtFilePath"
    Exit
}

# Get all category folders
$categoryFolders = Get-ChildItem -Path $basePath -Directory

# Process all valid project prefixes
foreach ($projectPrefix in $validProjectPrefixes) {
    $projectFolderPath = $null

    # Find the correct project folder
    foreach ($category in $categoryFolders) {
        $matchingProject = Get-ChildItem -Path $category.FullName -Directory | Where-Object { 
            $_.Name -match "^$projectPrefix"
        }

        if ($matchingProject) {
            $projectFolderPath = $matchingProject.FullName
            Log-Message "Found project: $projectFolderPath"
            break  # Stop searching once we find the first match
        }
    }

    # If no project was found, log and continue
    if (-Not $projectFolderPath) {
        Log-Message "No matching project folder found for: $projectPrefix"
        Continue
    }

    # Search for Issues folders inside "03 Drafting\02 Struct\"
    $structFolder = Join-Path $projectFolderPath "03 Drafting\02 Struct\"
    if (Test-Path $structFolder) {
        $issuesFolders = Get-ChildItem -Path $structFolder -Directory | Where-Object { $_.Name -match "^Issues" }
        
        if ($issuesFolders) {
            foreach ($issuesFolder in $issuesFolders) {
                Log-Message "Issues folder found: $($issuesFolder.FullName)"
                $destinationFile = Join-Path $issuesFolder.FullName (Split-Path $txtFilePath -Leaf)
                Copy-Item -Path $txtFilePath -Destination $destinationFile -Force
                Log-Message "Copied file to: $destinationFile"
            }
        } else {
            Log-Message "No Issues folder found in: $structFolder"
        }
    } else {
        Log-Message "03 Drafting\02 Struct\ does not exist in: $projectFolderPath"
    }
}

Log-Message "File synchronization completed."
