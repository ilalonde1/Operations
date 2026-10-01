# As SYSTEM: run the (already copied) user-step INSTALL for every signed-in user; report each one's result.
$Dir = 'C:\ProgramData\KOR\EmailFiler'
$Guid = '{6F2C1E0A-7B4D-4E8F-9C31-2A5B8D0E4F52}'
$users = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object {
    $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner; $s = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
    [pscustomobject]@{ User = "$($o.Domain)\$($o.User)"; Short = $o.User; Sid = $s.Sid } } | Sort-Object User -Unique)
foreach ($u in $users) {
    Remove-Item "$Dir\result-$($u.Short).txt" -ErrorAction SilentlyContinue
    $name = 'KOR-EmailFiler-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\user-step.ps1`" -Mode install"
    Register-ScheduledTask -TaskName $name -Action $act -Principal (New-ScheduledTaskPrincipal -UserId $u.User -LogonType Interactive) -Force | Out-Null
    try {
        Start-ScheduledTask -TaskName $name
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Seconds 2 } while ((Get-ScheduledTask -TaskName $name).State -eq 'Running' -and $sw.Elapsed.TotalSeconds -lt 240)
    } finally { Unregister-ScheduledTask -TaskName $name -Confirm:$false }
    $res = (Get-Content "$Dir\result-$($u.Short).txt" -ErrorAction SilentlyContinue) -join ' | '
    $ok = $res -match 'OK=True'
    if ($ok) { $k = "Registry::HKEY_USERS\$($u.Sid)\Software\Microsoft\Active Setup\Installed Components\$Guid"; New-Item $k -Force | Out-Null; Set-ItemProperty $k -Name Version -Value '1,0,0,52' }
    [pscustomobject]@{ User = $u.Short; Ok = $ok; Result = $res
        AddinNow = (Get-ItemProperty "Registry::HKEY_USERS\$($u.Sid)\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2" -ErrorAction SilentlyContinue).Manifest }
}
