<#
Compose the morning facts, ask the installed Claude Code session to write/import the hour,
then independently verify a fresh Morning hour in the database through the published CLI.
For a scheduled task on KOR-APP01, under the account already signed in to Claude Code.
Installs nothing, registers no task, sends no messages, and stores no secrets or session output.
Requires published MorningBrief.exe and BdKnowledgeIngest.exe; it never builds either tool.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CliFolder,
    [Parameter(Mandatory = $true)][string]$ComposerExe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$logPath = $null
$pushed = $false
$savedPath = $env:PATH
$exitCode = 1
$stage = 'initialization'
try {
    $date = Get-Date -Format 'yyyy-MM-dd'
    $destination = Join-Path 'C:\KorMorning' $date
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $logPath = Join-Path $destination 'run.log'
    $cliDirectory = (Resolve-Path -LiteralPath $CliFolder).ProviderPath
    $composer = (Resolve-Path -LiteralPath $ComposerExe).ProviderPath
    $cli = Join-Path $cliDirectory 'BdKnowledgeIngest.exe'
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw 'Published CLI is missing.' }
    if (-not (Test-Path -LiteralPath $composer -PathType Leaf)) { throw 'Published composer is missing.' }
    $claude = (Get-Command claude -CommandType Application,ExternalScript -ErrorAction Stop).Source
    # A task host may predate the machine setting. Refresh only this process; never log the value.
    $database = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'Machine')
    if (-not [string]::IsNullOrWhiteSpace($database)) { $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB = $database }
    $database = $null
    Push-Location -LiteralPath $cliDirectory
    $pushed = $true
    $env:PATH = $cliDirectory + [IO.Path]::PathSeparator + $savedPath
    $stage = 'compose'
    & $composer compose --date $date --to $destination *> $null
    if ($LASTEXITCODE -ne 0) { throw "Composer exited $LASTEXITCODE." }
    $prompt = Join-Path $destination "morning-$date.md"
    if (-not (Test-Path -LiteralPath $prompt -PathType Leaf)) { throw 'Composer produced no prompt.' }
    $started = [DateTimeOffset]::UtcNow.ToString('O')
    $stage = 'Claude Code session'
    & $claude -p "Read $prompt and follow it." --dangerously-skip-permissions *> $null
    if ($LASTEXITCODE -ne 0) { throw "Claude Code exited $LASTEXITCODE." }
    $stage = 'database verification'
    & $cli hour verify $date --kind Morning --after $started *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Database verification found no valid fresh Morning hour.' }
    Add-Content -LiteralPath $logPath -Value "$([DateTimeOffset]::Now.ToString('O')) OK Morning hour for $date verified."
    $exitCode = 0
}
catch {
    # Only our fixed stage failures or the exception type are logged, never provider output or credentials.
    $line = "$([DateTimeOffset]::Now.ToString('O')) FAILED at $stage ($($_.Exception.GetType().Name))."
    [Console]::Error.WriteLine($line)
    if ($null -ne $logPath) {
        try { Add-Content -LiteralPath $logPath -Value $line }
        catch { [Console]::Error.WriteLine('Could not write the morning runner log.') }
    }
}
finally {
    $env:PATH = $savedPath
    if ($pushed) { Pop-Location }
}
exit $exitCode
