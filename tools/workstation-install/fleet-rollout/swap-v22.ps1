# Runs as SYSTEM on each workstation at the swap. Requires C:\Newerforma_new\V22 (prestage-v22.ps1).
#  1. close the app + Outlook (+ only the Edge web views THEY started - Teams is left alone)
#  2. per signed-in user: uninstall the old EmailFilerv2 add-in AS THAT USER
#  3. replace C:\Newerforma with V22 (rename-then-delete, never delete before the new one is in place)
#  4. per signed-in user: install add-in 1.0.0.52 AS THAT USER, confirm it registered
#  5. Active Setup: everyone not signed in now gets the same uninstall/install once, at next sign-in
$ErrorActionPreference = 'Stop'
$Pkg = '1.0.0.52'
$Guid = '{6F2C1E0A-7B4D-4E8F-9C31-2A5B8D0E4F52}'   # Active Setup component id for the KOR email filer add-in
$Dir = 'C:\ProgramData\KOR\EmailFiler'
$steps = New-Object System.Collections.Generic.List[string]
function S($m) { $steps.Add($m) }

$UserStep = @'
param([ValidateSet('uninstall','install','reinstall')][string]$Mode = 'reinstall')
$Pkg = '1.0.0.52'
$vi = Join-Path $env:CommonProgramFiles 'microsoft shared\VSTO\10.0\VSTOInstaller.exe'
$new = 'file:///C:/Newerforma/EmailFilerv2.vsto'
$reg = 'HKCU:\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2'
$mark = 'HKCU:\Software\KOR\EmailFiler'
$out = "C:\ProgramData\KOR\EmailFiler\result-$env:USERNAME.txt"
# NOT "R": that is a built-in alias (Invoke-History) and aliases win over functions.
function Write-Step($m) { Add-Content -Path $out -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) }
function Invoke-Vsto($a) { $p = Start-Process $vi -ArgumentList $a -Wait -PassThru -WindowStyle Hidden; return $p.ExitCode }
# The same per-user trust entry Outlook writes when someone clicks Install: this add-in's location + its signing key.
# Uninstall removes it, and a silent install cannot ask, so it fails with -300 without it.
$publicKey = '<RSAKeyValue><Modulus>1aZvA6JHYY5dFg5O7/BkdAJG4Fwf7OZ1mkdbY9w4Bbvh6jyr7n+unsU3IgqsLE8/MlpC5bVh8xOjjBxBcxR6TtptErlGLTQA0sTNcp+pcAAoqeFPYnvq3IHMNBBtR38dFqg9YGb7veLEUZZrHME49kQVEOob2ZuDJAazjSLQh7k=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>'
function Set-Trust {
    $root = 'HKCU:\Software\Microsoft\VSTO\Security\Inclusion'
    New-Item $root -Force | Out-Null
    $have = Get-ChildItem $root | Where-Object { (Get-ItemProperty $_.PSPath).Url -eq $new }
    if (-not $have) {
        $k = New-Item (Join-Path $root ([guid]::NewGuid().ToString())) -Force
        New-ItemProperty $k.PSPath -Name Url -Value $new -Force | Out-Null
        New-ItemProperty $k.PSPath -Name PublicKey -Value $publicKey -Force | Out-Null
    }
}
if ($Mode -eq 'reinstall' -and (Get-ItemProperty $mark -ErrorAction SilentlyContinue).Version -eq $Pkg) { Write-Step "already $Pkg"; return }
if ($Mode -in 'uninstall','reinstall') {
    $old = (Get-ItemProperty $reg -ErrorAction SilentlyContinue).Manifest
    if ($old) { $url = ($old -split '\|')[0]; Write-Step ("uninstall " + $url + " -> exit " + (Invoke-Vsto "/uninstall `"$url`" /silent")) } else { Write-Step 'no old add-in registered' }
}
if ($Mode -in 'install','reinstall') {
    Set-Trust
    $code = Invoke-Vsto "/install `"$new`" /silent"
    $now = (Get-ItemProperty $reg -ErrorAction SilentlyContinue).Manifest
    $ok = ($code -eq 0) -and ($now -like '*C:/Newerforma/EmailFilerv2.vsto*')
    if ($ok) { New-Item $mark -Force | Out-Null; Set-ItemProperty $mark -Name Version -Value $Pkg }
    Write-Step ("install exit " + $code + " registered=" + [bool]$now + " OK=" + $ok)
}
'@

function As-User([string]$user, [string]$mode) {
    $name = 'KOR-EmailFiler-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode $mode"
    $pri = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive
    Register-ScheduledTask -TaskName $name -Action $act -Principal $pri -Force | Out-Null
    try {
        Start-ScheduledTask -TaskName $name
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Seconds 2; $i = Get-ScheduledTaskInfo -TaskName $name; $st = (Get-ScheduledTask -TaskName $name).State } while ($st -eq 'Running' -and $sw.Elapsed.TotalSeconds -lt 240)
        return "task result $($i.LastTaskResult) state $st"
    }
    finally { Unregister-ScheduledTask -TaskName $name -Confirm:$false }   # transient: never left behind
}

try {
    if (-not (Test-Path 'C:\Newerforma_new\V22\Kor.Operations.App.exe')) { throw 'not pre-staged' }
    New-Item -ItemType Directory -Force $Dir | Out-Null
    icacls $Dir /grant '*S-1-5-32-545:(OI)(CI)M' /T /Q | Out-Null     # Users: Modify (result files)
    Set-Content -Path "$Dir\user-step.ps1" -Value $UserStep -Encoding UTF8
    Get-ChildItem $Dir -Filter 'result-*.txt' | Remove-Item -Force

    # Signed-in users (explorer owners), with SIDs.
    $users = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner; $s = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
        [pscustomobject]@{ User = "$($o.Domain)\$($o.User)"; Short = $o.User; Sid = $s.Sid } } | Sort-Object User -Unique)
    S ("signed in: " + (($users.User) -join ', '))

    # 1. Close the app and Outlook, and only the web views they own.
    $targets = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in 'OUTLOOK.EXE', 'Kor.Operations.App.exe' })
    $ids = $targets.ProcessId
    $views = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $ids -contains $_.ParentProcessId })
    foreach ($p in $targets + $views) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop } catch { } }
    Start-Sleep -Seconds 5
    S ("closed: Outlook/app " + $targets.Count + ", their web views " + $views.Count)

    # 2. Uninstall the old add-in as each signed-in user.
    foreach ($u in $users) { S ("uninstall as " + $u.Short + ": " + (As-User $u.User 'uninstall')) }

    # 3. Swap the install folder: rename the old one aside, move V22 in, then remove the old.
    $old = $null
    if (Test-Path 'C:\Newerforma') {
        $old = 'C:\Newerforma_old_' + (Get-Date -Format 'yyyyMMddHHmmss')
        $moved = $false
        for ($i = 0; $i -lt 6 -and -not $moved; $i++) { try { Rename-Item 'C:\Newerforma' $old -ErrorAction Stop; $moved = $true } catch { Start-Sleep -Seconds 5 } }
        if (-not $moved) { throw 'C:\Newerforma is locked; nothing changed' }
    }
    Move-Item 'C:\Newerforma_new\V22' 'C:\Newerforma'
    Remove-Item 'C:\Newerforma_new' -Recurse -Force -ErrorAction SilentlyContinue
    $files = @(Get-ChildItem 'C:\Newerforma' -Recurse -File).Count
    if (-not (Test-Path 'C:\Newerforma\Kor.Operations.App.exe') -or $files -lt 900) { throw "new install incomplete ($files files)" }
    S "C:\Newerforma = V22 ($files files)"
    if ($old) { try { Remove-Item $old -Recurse -Force -ErrorAction Stop; S 'old install removed' } catch { S "old install left at $old (locked)" } }

    # 4. Install the new add-in as each signed-in user.
    foreach ($u in $users) { S ("install as " + $u.Short + ": " + (As-User $u.User 'install')) }

    # 5. Active Setup: once per user at next sign-in; marked done for the users handled now.
    $as = "HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\$Guid"
    New-Item $as -Force | Out-Null
    Set-ItemProperty $as -Name '(default)' -Value "KOR Email Filer add-in $Pkg"
    Set-ItemProperty $as -Name StubPath -Value "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode reinstall"
    Set-ItemProperty $as -Name Version -Value ($Pkg -replace '\.', ',')
    Set-ItemProperty $as -Name IsInstalled -Value 1 -Type DWord
    S 'Active Setup registered for everyone else'

    $results = foreach ($f in Get-ChildItem $Dir -Filter 'result-*.txt') { $f.BaseName.Substring(7) + ': ' + ((Get-Content $f.FullName) -join ' | ') }
    $okUsers = @($users | Where-Object { (Get-Content "$Dir\result-$($_.Short).txt" -ErrorAction SilentlyContinue) -match 'OK=True' })
    foreach ($u in $okUsers) {
        $k = "Registry::HKEY_USERS\$($u.Sid)\Software\Microsoft\Active Setup\Installed Components\$Guid"
        New-Item $k -Force | Out-Null; Set-ItemProperty $k -Name Version -Value ($Pkg -replace '\.', ',')
    }
    [pscustomobject]@{
        Done = $true
        AppSwapped = $true
        UsersSignedIn = $users.Count
        AddinOk = $okUsers.Count
        AddinFailed = @($users | Where-Object { $okUsers.Short -notcontains $_.Short }).Short -join ','
        UserResults = ($results -join ' || ')
        Steps = ($steps -join ' || ')
    }
}
catch {
    [pscustomobject]@{ Done = $false; AppSwapped = (Test-Path 'C:\Newerforma\EmailFilerv2.vsto') -and ((Get-Content 'C:\Newerforma\EmailFilerv2.vsto' -Raw) -match '1\.0\.0\.52'); Error = $_.Exception.Message; Steps = ($steps -join ' || ') }
}
