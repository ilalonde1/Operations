# READ-ONLY probe (runs as SYSTEM on each workstation): what Newerforma + the EmailFilerv2 add-in look like now.
$root = 'C:\Newerforma'
$vsto = Join-Path $root 'EmailFilerv2.vsto'
$pkgVersion = $null
if (Test-Path $vsto) { try { $pkgVersion = ([xml](Get-Content $vsto -Raw)).assembly.assemblyIdentity.version } catch { } }

# Signed-in users (explorer.exe owners) and whether Outlook / the app are running.
$sessions = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object { $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner; "$($o.Domain)\$($o.User)" } | Sort-Object -Unique)
$outlook = @(Get-Process OUTLOOK -ErrorAction SilentlyContinue).Count
$app = @(Get-Process Kor.Operations.App -ErrorAction SilentlyContinue).Count

# Per loaded user hive: the add-in registration + VSTO inclusion (trust) entries.
$users = foreach ($sid in (Get-ChildItem Registry::HKEY_USERS | Where-Object { $_.PSChildName -match '^S-1-5-21-[\d-]+$' }).PSChildName) {
    $name = try { (New-Object System.Security.Principal.SecurityIdentifier($sid)).Translate([System.Security.Principal.NTAccount]).Value } catch { $sid }
    $k = "Registry::HKEY_USERS\$sid\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2"
    $reg = if (Test-Path $k) { Get-ItemProperty $k } else { $null }
    $incl = @(Get-ChildItem "Registry::HKEY_USERS\$sid\Software\Microsoft\VSTO\Security\Inclusion" -ErrorAction SilentlyContinue | ForEach-Object { (Get-ItemProperty $_.PSPath).Url } | Where-Object { $_ -match 'EmailFiler' })
    [pscustomobject]@{ User = $name; AddinManifest = $reg.Manifest; LoadBehavior = $reg.LoadBehavior; TrustedUrls = ($incl -join ' ; ') }
}

[pscustomobject]@{
    NewerformaExists = Test-Path $root
    NewerformaModified = if (Test-Path $root) { (Get-Item $root).LastWriteTime.ToString('yyyy-MM-dd HH:mm') } else { $null }
    ExePresent = Test-Path (Join-Path $root 'Kor.Operations.App.exe')
    PackageAddinVersion = $pkgVersion
    AddinFolders = (@(Get-ChildItem (Join-Path $root 'Application Files') -Directory -ErrorAction SilentlyContinue).Name -join ',')
    SignedIn = ($sessions -join ',')
    OutlookRunning = $outlook
    AppRunning = $app
    VstoInstaller = Test-Path "$env:CommonProgramFiles\microsoft shared\VSTO\10.0\VSTOInstaller.exe"
    Users = @($users)
}
