# EMBEDDED DEPLOY PAYLOAD (Deploy.migrate-to-korops.ps1). Dispatched by NetworkOps to a workstation's agent and run there
# as SYSTEM via MachineRunner -> AgentHub (poll-based; survives a multi-minute job; needs no WinRM/RPC, which are blocked
# fleet-wide). The dispatcher PREPENDS `$Sha = '<V25.zip sha256>'` -- this script has no param() because the agent runs a
# script body, not a file with arguments.
#
# Migrates the flat C:\Newerforma install to C:\KOR-Operations and drops the "Newerforma" name, OR updates an
# already-migrated PC in place. MEASURED 2026-10-03: the fleet is uniform -- flat C:\Newerforma, EmailFilerv2 add-in
# 1.0.0.52, NO launcher; shortcuts stale -> replaced. The add-in finds the app RIGHT (no env-var hack): rebuilt as
# 1.0.0.55 with HostExeResolver's config + fallback pointing at C:\KOR-Operations. Also carries the VSTO load-time fix.
#
# TRANSACTIONAL (audit CODEX-KOROPS-MIGRATE-OR-UPDATE-AND-ADDIN-AUDIT, 2026-10-04): the build is PLACED and validated
# BEFORE any per-user add-in uninstall, and a placement failure RESTORES the previous install -- so a failed run never
# leaves the box without an app or a user without an add-in. A re-run RECONCILES users (it does not skip repair just
# because the disk looks current), and the script returns an explicit `Ok` the dispatcher checks, so a partial failure
# is never recorded as success.
if ([string]::IsNullOrWhiteSpace($Sha)) { throw 'migrate-to-korops: the dispatcher did not provide $Sha (the package hash)' }
$ErrorActionPreference = 'Stop'
$Pkg      = '1.0.0.55'                                  # the add-in (.vsto) version this package carries
$PkgGen   = 25                                          # package generation: drives V<N>.zip AND the Active Setup version, so they can never drift
$Guid     = '{6F2C1E0A-7B4D-4E8F-9C31-2A5B8D0E4F52}'   # Active Setup component id for the KOR email filer add-in
$Dir      = 'C:\ProgramData\KOR\EmailFiler'
$ResultsDir = Join-Path $Dir 'results'
$root     = 'C:\KOR-Operations'
$oldRoot  = 'C:\Newerforma'
$share    = "\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V$PkgGen.zip"
$appExe   = Join-Path $root 'Kor.Operations.App.exe'
$asVersion = "$PkgGen,0,0,0"                            # Active Setup Version (HKLM vs HKCU gate); derived from $PkgGen
$kept     = $null                                       # previous install, renamed aside, kept for rollback until success
$rolledBack = $false
$steps = New-Object System.Collections.Generic.List[string]
function S($m) { $steps.Add($m) }

# The per-user add-in step, run AS the signed-in user (a VSTO add-in registers in HKCU). 'reinstall' always uninstalls
# the current registration (whatever path) then installs 1.0.0.55 from C:\KOR-Operations and confirms the new path --
# one scheduled task, so the fan-out is ONE wait per user, not two. It writes its outcome to a per-user result file in a
# subdir the user can write; the SCRIPT itself is not user-writable (see the ACL below).
$UserStep = @'
param([ValidateSet('uninstall','install','reinstall')][string]$Mode = 'reinstall')
$vi   = Join-Path $env:CommonProgramFiles 'microsoft shared\VSTO\10.0\VSTOInstaller.exe'
$new  = 'file:///C:/KOR-Operations/EmailFilerv2.vsto'
$reg  = 'HKCU:\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2'
$out  = "C:\ProgramData\KOR\EmailFiler\results\result-$env:USERNAME.txt"
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
# reinstall ALWAYS uninstalls+installs. Active Setup fires it once per version bump (HKCU Version < HKLM gate), so this is
# how an offline user is brought to the on-disk version. Keying an early-return on the PATH alone left old-version users
# behind -- the path was right but the add-in stale. "Already current" is decided by the main script, not here.
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

# Run user-step.ps1 AS a signed-in user via a transient scheduled task. Budget-aware: never wait past the op's remaining
# time (minus a reserve for cleanup/rollback), and REAP the task + its VSTOInstaller on timeout rather than leaving an
# installer running into later work. The authoritative per-user verdict is the result FILE, read by the caller; this
# return string is for the step log only.
function As-User([string]$user, [System.Diagnostics.Stopwatch]$opSw) {
    $name = 'KOR-KorOps-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode reinstall"
    $pri = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive
    Register-ScheduledTask -TaskName $name -Action $act -Principal $pri -Force | Out-Null
    try {
        $remaining = 900 - 120 - $opSw.Elapsed.TotalSeconds     # 900s op budget, 120s reserve for cleanup/rollback
        $cap = [Math]::Max(30, [Math]::Min(240, $remaining))    # per-user wait, bounded by what the budget allows
        Start-ScheduledTask -TaskName $name
        Start-Sleep -Seconds 2                                   # let it enter Running before polling (avoid reading a not-yet-started task as finished)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ((Get-ScheduledTask -TaskName $name).State -eq 'Running' -and $sw.Elapsed.TotalSeconds -lt $cap) { Start-Sleep -Seconds 2 }
        $st = (Get-ScheduledTask -TaskName $name).State
        if ($st -eq 'Running') { Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue; return "TIMEOUT after $([int]$sw.Elapsed.TotalSeconds)s (reaped)" }
        $i = Get-ScheduledTaskInfo -TaskName $name
        return "task result $($i.LastTaskResult) state $st"
    }
    finally { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue }   # transient: never left behind
}

# A signed-in user's current add-in registration (read from their loaded hive under HKEY_USERS by SID).
function User-Manifest([string]$sid) {
    try { (Get-ItemProperty "Registry::HKEY_USERS\$sid\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2" -ErrorAction Stop).Manifest } catch { $null }
}

function New-Shortcut([string]$path, [string]$target) {
    $wsh = New-Object -ComObject WScript.Shell
    $lnk = $wsh.CreateShortcut($path)
    $lnk.TargetPath = $target; $lnk.WorkingDirectory = (Split-Path $target -Parent)
    $lnk.IconLocation = "$target,0"; $lnk.Description = 'KOR Operations'
    $lnk.Save()
}

try {
    $opSw = [Diagnostics.Stopwatch]::StartNew()

    # 0. Helper dir. Reset it so no over-permissive ACL survives from an earlier version of this op, then lock it down:
    #    SYSTEM + Admins full; Users read+execute ONLY. The user-step script runs AS each user so they must READ it, but
    #    they must NOT be able to replace it (that would be code-exec as another user). Results go in a separate subdir the
    #    user CAN write. (Audit finding #2.)
    if (Test-Path $Dir) { try { Remove-Item $Dir -Recurse -Force -ErrorAction Stop } catch {} }
    New-Item -ItemType Directory -Force $Dir | Out-Null
    icacls $Dir /inheritance:r /grant '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /Q | Out-Null
    Set-Content -Path "$Dir\user-step.ps1" -Value $UserStep -Encoding UTF8
    icacls "$Dir\user-step.ps1" /inheritance:r /grant '*S-1-5-18:F' '*S-1-5-32-544:F' '*S-1-5-32-545:RX' /Q | Out-Null   # the script is not user-writable
    New-Item -ItemType Directory -Force $ResultsDir | Out-Null
    icacls $ResultsDir /grant '*S-1-5-32-545:(OI)(CI)M' /Q | Out-Null
    Get-ChildItem $ResultsDir -Filter 'result-*.txt' -ErrorAction SilentlyContinue | Remove-Item -Force

    # 1. State. Is the build on disk already this version? Which signed-in users need their add-in (re)installed? We only
    #    do the DISRUPTIVE work (close Outlook, place, per-user install) that is actually needed -- a fully current box is
    #    a no-op for the user, and we never skip a user who still needs repair just because the disk looks current.
    $installedVsto = if (Test-Path "$root\EmailFilerv2.vsto") { try { ([xml](Get-Content "$root\EmailFilerv2.vsto" -Raw)).assembly.assemblyIdentity.version } catch { $null } } else { $null }
    $diskCurrent = (Test-Path $appExe) -and ($installedVsto -eq $Pkg)
    $users = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner; $s = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
        [pscustomobject]@{ User = "$($o.Domain)\$($o.User)"; Short = $o.User; Sid = $s.Sid } } | Sort-Object User -Unique)
    $needWork = @($users | Where-Object { (-not $diskCurrent) -or -not ((User-Manifest $_.Sid) -like '*C:/KOR-Operations/EmailFilerv2.vsto*') })
    $needPlace = -not $diskCurrent
    $needClose = $needPlace -or ($needWork.Count -gt 0)
    S ("signed in: " + (($users.User) -join ', ') + "; diskCurrent=$diskCurrent; needPlace=$needPlace; needWork=" + ($needWork.Short -join ','))

    # 2. If placing, pull + verify + stage the package FIRST (non-disruptive, and before anything is touched).
    if ($needPlace) {
        $stage = 'C:\KOR-Operations_new'
        $zip = "C:\Windows\Temp\KOR-V$PkgGen.zip"
        Copy-Item -LiteralPath $share -Destination $zip -Force
        $got = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($got -ne $Sha) { throw "V$PkgGen hash mismatch: $got (expected $Sha)" }
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
        Remove-Item $zip -Force
        $staged = Join-Path $stage "V$PkgGen"
        $sFiles = @(Get-ChildItem $staged -Recurse -File).Count
        if (-not (Test-Path (Join-Path $staged 'Kor.Operations.App.exe')) -or -not (Test-Path (Join-Path $staged 'EmailFilerv2.vsto')) -or $sFiles -lt 900) { throw "staged V$PkgGen incomplete ($sFiles files)" }
        S "staged V${PkgGen}: $sFiles files"
    }

    # 3. Close the app and Outlook (and only the web views they own) -- only if we are going to place or touch a user.
    if ($needClose) {
        $targets = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in 'OUTLOOK.EXE', 'Kor.Operations.App.exe' })
        $ids = $targets.ProcessId
        $views = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $ids -contains $_.ParentProcessId })
        foreach ($p in $targets + $views) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop } catch { } }
        Start-Sleep -Seconds 5
        S ("closed: Outlook/app " + $targets.Count + ", their web views " + $views.Count)
    }

    # 4. Place V25 at C:\KOR-Operations -- TRANSACTIONALLY, and BEFORE any per-user uninstall. Rename the existing install
    #    aside (kept for rollback), move the staged build in, validate. On ANY failure, restore the previous install so the
    #    box is never left without an app -- and because no user has been uninstalled yet, nobody is left without an add-in.
    if ($needPlace) {
        try {
            if (Test-Path $root) {
                $kept = "$root`_old_" + (Get-Date -Format 'yyyyMMddHHmmss')
                Rename-Item $root $kept -ErrorAction Stop
                S 'existing C:\KOR-Operations renamed aside (rollback point)'
            }
            Move-Item $staged $root -ErrorAction Stop
            $files = @(Get-ChildItem $root -Recurse -File).Count
            if (-not (Test-Path $appExe) -or -not (Test-Path "$root\EmailFilerv2.vsto") -or $files -lt 900) { throw "placed C:\KOR-Operations incomplete ($files files)" }
            S "C:\KOR-Operations = V$PkgGen ($files files)"
        }
        catch {
            try { if (Test-Path $root) { Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue } } catch {}
            if ($kept -and (Test-Path $kept)) {
                try { Rename-Item $kept $root -ErrorAction Stop; $rolledBack = $true; $kept = $null; S 'ROLLED BACK: previous C:\KOR-Operations restored' }
                catch { S "ROLLBACK FAILED: previous install is at $kept" }
            }
            throw
        }
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 5. (Re)install the add-in for each user who needs it -- one task per user (uninstall+install together). Runs AFTER the
    #    build is confirmed in place, so a user is only ever briefly without an add-in on a box that DOES have the new app.
    foreach ($u in $needWork) { S ("reinstall as " + $u.Short + ": " + (As-User $u.User $opSw)) }

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

    # 7. Who ended up OK? A user not in $needWork was already on this version; a user we touched is OK iff their result says
    #    so. Only OK users get their Active Setup HKCU stamp -- a FAILED user keeps a lower stamp, so Active Setup re-fires
    #    the reinstall for them at next sign-in. The version is derived from $PkgGen so HKLM and HKCU can never drift.
    $okUsers = @($users | Where-Object { ($needWork.Short -notcontains $_.Short) -or ((Get-Content "$ResultsDir\result-$($_.Short).txt" -ErrorAction SilentlyContinue) -match 'OK=True') })
    $failed  = @($users | Where-Object { $okUsers.Short -notcontains $_.Short })
    $as = "HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\$Guid"
    New-Item $as -Force | Out-Null
    Set-ItemProperty $as -Name '(default)' -Value "KOR Operations email add-in $Pkg (KOR-Operations)"
    Set-ItemProperty $as -Name StubPath -Value "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode reinstall"
    Set-ItemProperty $as -Name Version -Value $asVersion
    Set-ItemProperty $as -Name IsInstalled -Value 1 -Type DWord
    foreach ($u in $okUsers) {
        $k = "Registry::HKEY_USERS\$($u.Sid)\Software\Microsoft\Active Setup\Installed Components\$Guid"
        New-Item $k -Force | Out-Null; Set-ItemProperty $k -Name Version -Value $asVersion
    }
    S "Active Setup $asVersion registered; OK users stamped: $($okUsers.Short -join ',')"

    # 8. Remove C:\Newerforma (cleanup -- resolution already points at C:\KOR-Operations via the add-in config).
    if (Test-Path $oldRoot) {
        $old = $oldRoot + '_removed_' + (Get-Date -Format 'yyyyMMddHHmmss')
        $moved = $false
        for ($i = 0; $i -lt 6 -and -not $moved; $i++) { try { Rename-Item $oldRoot $old -ErrorAction Stop; $moved = $true } catch { Start-Sleep -Seconds 5 } }
        if (-not $moved) { S 'C:\Newerforma is locked; left in place (resolution is unaffected -- config points to C:\KOR-Operations)' }
        else { try { Remove-Item $old -Recurse -Force -ErrorAction Stop; S 'C:\Newerforma removed' } catch { S "C:\Newerforma renamed away (leftover at $old)" } }
    }

    # 9. Placement succeeded and users are reconciled -- the rollback copy is no longer needed.
    if ($kept -and (Test-Path $kept)) { try { Remove-Item $kept -Recurse -Force -ErrorAction Stop; S 'previous install removed' } catch { S "previous install left at $kept (locked)" } }

    $appOk = (Test-Path $appExe) -and (Test-Path "$root\EmailFilerv2.vsto")
    $ok    = $appOk -and ($failed.Count -eq 0)
    $results = foreach ($f in Get-ChildItem $ResultsDir -Filter 'result-*.txt' -ErrorAction SilentlyContinue) { $f.BaseName.Substring(7) + ': ' + ((Get-Content $f.FullName) -join ' | ') }
    [pscustomobject]@{
        Ok             = $ok
        Done           = $true
        AppInstalled   = $appOk
        DiskWasCurrent = $diskCurrent
        Placed         = $needPlace
        NewerformaGone = (-not (Test-Path "$oldRoot\Kor.Operations.App.exe"))
        UsersSignedIn  = $users.Count
        AddinOk        = $okUsers.Count
        AddinFailed    = ($failed.Short -join ',')
        Result         = if ($ok) { ($(if ($needPlace) { "migrated/updated to $Pkg" } else { "already $Pkg" }) + "; $($okUsers.Count)/$($users.Count) signed-in users OK") } else { "INCOMPLETE: app=$appOk; users still failing: $($failed.Short -join ',')" }
        UserResults    = ($results -join ' || ')
        Steps          = ($steps -join ' || ')
    }
}
catch {
    [pscustomobject]@{
        Ok                   = $false
        Done                 = $false
        Error                = $_.Exception.Message
        Result               = "FAILED: $($_.Exception.Message)"
        AppInstalled         = (Test-Path $appExe)
        RolledBack           = $rolledBack
        NewerformaStillThere = (Test-Path "$oldRoot\Kor.Operations.App.exe")
        Steps                = ($steps -join ' || ')
    }
}
