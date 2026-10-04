# EMBEDDED DEPLOY PAYLOAD (Deploy.migrate-to-korops.ps1). Dispatched by NetworkOps to a workstation's agent and run there
# as SYSTEM via MachineRunner -> AgentHub (poll-based; survives a multi-minute job; needs no WinRM/RPC, which are blocked
# fleet-wide). The dispatcher PREPENDS `$Sha = '<V24.zip sha256>'` -- this script has no param() because the agent runs a
# script body, not a file with arguments.
#
# Migrates the flat C:\Newerforma install to C:\KOR-Operations and drops the "Newerforma" name. MEASURED 2026-10-03: the
# fleet is uniform -- flat C:\Newerforma, EmailFilerv2 add-in 1.0.0.52, NO launcher; shortcuts stale -> replaced.
# The add-in finds the app RIGHT (no env-var hack): rebuilt as 1.0.0.54 with HostExeResolver's config + fallback pointing
# at C:\KOR-Operations. Removing C:\Newerforma is cleanup, not load-bearing. Also carries the VSTO load-time fix.
if ([string]::IsNullOrWhiteSpace($Sha)) { throw 'migrate-to-korops: the dispatcher did not provide $Sha (the V24.zip hash)' }
$ErrorActionPreference = 'Stop'
$Pkg      = '1.0.0.54'
$Guid     = '{6F2C1E0A-7B4D-4E8F-9C31-2A5B8D0E4F52}'   # Active Setup component id for the KOR email filer add-in
$Dir      = 'C:\ProgramData\KOR\EmailFiler'
$root     = 'C:\KOR-Operations'
$oldRoot  = 'C:\Newerforma'
$share    = '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V24.zip'
$appExe   = Join-Path $root 'Kor.Operations.App.exe'
$steps = New-Object System.Collections.Generic.List[string]
function S($m) { $steps.Add($m) }

# The per-user add-in step: uninstall the old registration (any path), install from C:\KOR-Operations, confirm the
# registration now points to C:\KOR-Operations. Skips only when it is ALREADY on the new path (the migration is re-run).
$UserStep = @'
param([ValidateSet('uninstall','install','reinstall')][string]$Mode = 'reinstall')
$vi   = Join-Path $env:CommonProgramFiles 'microsoft shared\VSTO\10.0\VSTOInstaller.exe'
$new  = 'file:///C:/KOR-Operations/EmailFilerv2.vsto'
$reg  = 'HKCU:\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2'
$out  = "C:\ProgramData\KOR\EmailFiler\result-$env:USERNAME.txt"
function Write-Step($m) { Add-Content -Path $out -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) }
function Invoke-Vsto($a) { $p = Start-Process $vi -ArgumentList $a -Wait -PassThru -WindowStyle Hidden; return $p.ExitCode }
$publicKey = '<RSAKeyValue><Modulus>1aZvA6JHYY5dFg5O7/BkdAJG4Fwf7OZ1mkdbY9w4Bbvh6jyr7n+unsU3IgqsLE8/MlpC5bVh8xOjjBxBcxR6TtptErlGLTQA0sTNcp+pcAAoqeFPYnvq3IHMNBBtR38dFqg9YGb7veLEUZZrHME49kQVEOob2ZuDJAazjSLQh7k=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>'
function Set-Trust {
    $trustRoot = 'HKCU:\Software\Microsoft\VSTO\Security\Inclusion'
    New-Item $trustRoot -Force | Out-Null
    $have = Get-ChildItem $trustRoot | Where-Object { (Get-ItemProperty $_.PSPath).Url -eq $new }
    if (-not $have) {
        $k = New-Item (Join-Path $trustRoot ([guid]::NewGuid().ToString())) -Force
        New-ItemProperty $k.PSPath -Name Url -Value $new -Force | Out-Null
        New-ItemProperty $k.PSPath -Name PublicKey -Value $publicKey -Force | Out-Null
    }
}
$cur = (Get-ItemProperty $reg -ErrorAction SilentlyContinue).Manifest
if ($Mode -eq 'reinstall' -and $cur -like '*C:/KOR-Operations/EmailFilerv2.vsto*') { Write-Step 'already on KOR-Operations'; return }
if ($Mode -in 'uninstall','reinstall') {
    if ($cur) { $url = ($cur -split '\|')[0]; Write-Step ("uninstall " + $url + " -> exit " + (Invoke-Vsto "/uninstall `"$url`" /silent")) } else { Write-Step 'no old add-in registered' }
}
if ($Mode -in 'install','reinstall') {
    Set-Trust
    $code = Invoke-Vsto "/install `"$new`" /silent"
    $now  = (Get-ItemProperty $reg -ErrorAction SilentlyContinue).Manifest
    $ok   = ($code -eq 0) -and ($now -like '*C:/KOR-Operations/EmailFilerv2.vsto*')
    Write-Step ("install exit " + $code + " registered=" + [bool]$now + " OK=" + $ok)
}
'@

function As-User([string]$user, [string]$mode) {
    $name = 'KOR-KorOps-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
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

function New-Shortcut([string]$path, [string]$target) {
    $wsh = New-Object -ComObject WScript.Shell
    $lnk = $wsh.CreateShortcut($path)
    $lnk.TargetPath = $target; $lnk.WorkingDirectory = (Split-Path $target -Parent)
    $lnk.IconLocation = "$target,0"; $lnk.Description = 'KOR Operations'
    $lnk.Save()
}

try {
    New-Item -ItemType Directory -Force $Dir | Out-Null
    icacls $Dir /grant '*S-1-5-32-545:(OI)(CI)M' /T /Q | Out-Null     # Users: Modify (result files)
    Set-Content -Path "$Dir\user-step.ps1" -Value $UserStep -Encoding UTF8
    Get-ChildItem $Dir -Filter 'result-*.txt' -ErrorAction SilentlyContinue | Remove-Item -Force

    # Already migrated? (re-run safety): app in place and the old folder's app gone.
    if ((Test-Path $appExe) -and -not (Test-Path "$oldRoot\Kor.Operations.App.exe")) {
        return [pscustomobject]@{ Done = $true; AlreadyMigrated = $true; Result = 'C:\KOR-Operations in place; C:\Newerforma gone' }
    }

    # 1. Pull + verify V24, stage it (non-disruptive).
    $stage = 'C:\KOR-Operations_new'
    $zip = 'C:\Windows\Temp\KOR-V24.zip'
    Copy-Item -LiteralPath $share -Destination $zip -Force
    $got = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($got -ne $Sha) { throw "V24 hash mismatch: $got (expected $Sha)" }
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
    Remove-Item $zip -Force
    $staged = Join-Path $stage 'V24'
    $sFiles = @(Get-ChildItem $staged -Recurse -File).Count
    if (-not (Test-Path (Join-Path $staged 'Kor.Operations.App.exe')) -or -not (Test-Path (Join-Path $staged 'EmailFilerv2.vsto')) -or $sFiles -lt 900) { throw "staged V24 incomplete ($sFiles files)" }
    S "staged V24: $sFiles files"

    # Signed-in users (explorer owners), with SIDs.
    $users = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner; $s = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
        [pscustomobject]@{ User = "$($o.Domain)\$($o.User)"; Short = $o.User; Sid = $s.Sid } } | Sort-Object User -Unique)
    S ("signed in: " + (($users.User) -join ', '))

    # 2. Close the app and Outlook, and only the web views they own.
    $targets = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in 'OUTLOOK.EXE', 'Kor.Operations.App.exe' })
    $ids = $targets.ProcessId
    $views = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $ids -contains $_.ParentProcessId })
    foreach ($p in $targets + $views) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop } catch { } }
    Start-Sleep -Seconds 5
    S ("closed: Outlook/app " + $targets.Count + ", their web views " + $views.Count)

    # 3. Uninstall the old add-in as each signed-in user (reads the current registration, whatever path).
    foreach ($u in $users) { S ("uninstall as " + $u.Short + ": " + (As-User $u.User 'uninstall')) }

    # 4. Place V24 at C:\KOR-Operations (rename any existing aside first, then move in).
    if (Test-Path $root) {
        $kept = "$root`_old_" + (Get-Date -Format 'yyyyMMddHHmmss')
        Rename-Item $root $kept -ErrorAction Stop
        S "existing C:\KOR-Operations renamed aside"
    }
    Move-Item $staged $root
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    $files = @(Get-ChildItem $root -Recurse -File).Count
    if (-not (Test-Path $appExe) -or $files -lt 900) { throw "C:\KOR-Operations incomplete ($files files)" }
    S "C:\KOR-Operations = V24 ($files files)"

    # 5. Install the new add-in as each signed-in user (from C:\KOR-Operations).
    foreach ($u in $users) { S ("install as " + $u.Short + ": " + (As-User $u.User 'install')) }

    # 6. Clean shortcuts: one all-users "KOR Operations"; remove stale "Newerforma" shortcuts everywhere.
    $publicDesktop = Join-Path $env:PUBLIC 'Desktop'
    $commonStart   = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
    New-Shortcut (Join-Path $publicDesktop 'KOR Operations.lnk') $appExe
    New-Shortcut (Join-Path $commonStart   'KOR Operations.lnk') $appExe
    $shortcutDirs = @($publicDesktop, $commonStart) + @(Get-ChildItem 'C:\Users' -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        (Join-Path $_.FullName 'Desktop'), (Join-Path $_.FullName 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs') })
    $removed = 0
    foreach ($d in $shortcutDirs) { Get-ChildItem $d -Filter 'Newerforma*.lnk' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-Item $_.FullName -Force; $removed++ } catch {} } }
    S "shortcuts: KOR Operations created; $removed stale Newerforma shortcut(s) removed"

    # 7. Active Setup: once per user at next sign-in, re-register to the new path.
    $as = "HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\$Guid"
    New-Item $as -Force | Out-Null
    Set-ItemProperty $as -Name '(default)' -Value "KOR Operations email add-in $Pkg (KOR-Operations)"
    Set-ItemProperty $as -Name StubPath -Value "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode reinstall"
    Set-ItemProperty $as -Name Version -Value '24,0,0,0'
    Set-ItemProperty $as -Name IsInstalled -Value 1 -Type DWord
    S 'Active Setup registered for everyone else'
    $okUsers = @($users | Where-Object { (Get-Content "$Dir\result-$($_.Short).txt" -ErrorAction SilentlyContinue) -match 'OK=True|already on KOR-Operations' })
    foreach ($u in $okUsers) {
        $k = "Registry::HKEY_USERS\$($u.Sid)\Software\Microsoft\Active Setup\Installed Components\$Guid"
        New-Item $k -Force | Out-Null; Set-ItemProperty $k -Name Version -Value '24,0,0,0'
    }

    # 8. Remove C:\Newerforma (cleanup -- resolution already points at C:\KOR-Operations via the add-in config).
    if (Test-Path $oldRoot) {
        $old = $oldRoot + '_removed_' + (Get-Date -Format 'yyyyMMddHHmmss')
        $moved = $false
        for ($i = 0; $i -lt 6 -and -not $moved; $i++) { try { Rename-Item $oldRoot $old -ErrorAction Stop; $moved = $true } catch { Start-Sleep -Seconds 5 } }
        if (-not $moved) { S 'C:\Newerforma is locked; left in place (resolution is unaffected -- config points to C:\KOR-Operations)' }
        else { try { Remove-Item $old -Recurse -Force -ErrorAction Stop; S 'C:\Newerforma removed' } catch { S "C:\Newerforma renamed away (leftover at $old)" } }
    }

    $results = foreach ($f in Get-ChildItem $Dir -Filter 'result-*.txt' -ErrorAction SilentlyContinue) { $f.BaseName.Substring(7) + ': ' + ((Get-Content $f.FullName) -join ' | ') }
    [pscustomobject]@{
        Done           = $true
        AppInstalled   = (Test-Path $appExe)
        NewerformaGone = (-not (Test-Path "$oldRoot\Kor.Operations.App.exe"))
        UsersSignedIn  = $users.Count
        AddinOk        = $okUsers.Count
        AddinFailed    = @($users | Where-Object { $okUsers.Short -notcontains $_.Short }).Short -join ','
        UserResults    = ($results -join ' || ')
        Steps          = ($steps -join ' || ')
    }
}
catch {
    [pscustomobject]@{ Done = $false; Error = $_.Exception.Message; AppInstalled = (Test-Path $appExe); NewerformaStillThere = (Test-Path "$oldRoot\Kor.Operations.App.exe"); Steps = ($steps -join ' || ') }
}
