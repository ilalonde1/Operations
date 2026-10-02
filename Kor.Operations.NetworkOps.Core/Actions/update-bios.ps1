# FIX update-bios (and check-bios-update, the same script with $DryRun): brings a Lenovo PC's BIOS up to Lenovo's current
# package, the way Lenovo's own update tools do -- everything is read from Lenovo's package descriptor for THIS PC's
# machine type and BIOS, never assumed:
#   1. the catalog for the machine type -> the first BIOS package whose rules say "installed" (current) or "installs over
#      this BIOS" (Lenovo's DetectInstall / Dependencies, evaluated below exactly as Core/Bios/LenovoRules does);
#   2. the descriptor checked against the catalog's SHA-256, the installer against the descriptor's, and the installer's
#      Authenticode signature against Lenovo;
#   3. refused on battery, where a BIOS supervisor password is set (the flash would stop at the console), and where
#      Lenovo's rules turn on a device or firmware check this fix does not read (Unknown: never flash on a guess);
#   4. BitLocker suspended for ONE restart (else the changed firmware measurements demand the recovery key at boot);
#   5. Lenovo's ExtractCommand, then Lenovo's Install command line, judged by Lenovo's own success codes;
#   6. a restart in 5 minutes with a warning on screen -- the new BIOS is written during that restart. Do not power off.
# $DryRun does steps 1-3 and reports what it would do: nothing is flashed, suspended or restarted.

# ---- Lenovo's rules: three-valued (True / False / Unknown), the same as Core/Bios/LenovoRules.cs ----
function Join-All($xs) { if ($xs -contains 'False') { 'False' } elseif ($xs -contains 'Unknown') { 'Unknown' } else { 'True' } }
function Join-Any($xs) { if ($xs -contains 'True') { 'True' } elseif ($xs -contains 'Unknown') { 'Unknown' } else { 'False' } }
function Test-LenovoNode($n, $f) {
    $kids = @($n.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
    switch ($n.LocalName) {
        { $_ -in 'And', 'DetectInstall', 'Dependencies' } { return (Join-All @($kids | ForEach-Object { Test-LenovoNode $_ $f })) }
        'Or' { return (Join-Any @($kids | ForEach-Object { Test-LenovoNode $_ $f })) }
        'Not' { $v = Join-All @($kids | ForEach-Object { Test-LenovoNode $_ $f }); return $(if ($v -eq 'True') { 'False' } elseif ($v -eq 'False') { 'True' } else { 'Unknown' }) }
        '_Bios' { return $(if (@($kids | Where-Object { $_.LocalName -eq 'Level' -and $f.BiosId -like $_.InnerText.Trim() }).Count) { 'True' } else { 'False' }) }
        '_OS' {
            $want = if ($f.Windows11) { 'WIN11' } else { 'WIN10' }
            return $(if (@($kids | Where-Object { $_.LocalName -eq 'OS' -and ($_.InnerText.Trim().ToUpper() -replace '^(WIN\d+).*$', '$1') -eq $want }).Count) { 'True' } else { 'False' })
        }
        '_CPUAddressWidth' { return $(if (@($kids | Where-Object { $_.LocalName -eq 'AddressWidth' -and $_.InnerText.Trim() -eq [string]$f.AddressWidth }).Count) { 'True' } else { 'False' }) }
        default { return 'Unknown' }
    }
}
function Get-LenovoVerdict($pkg, $f) {
    $di = $pkg.SelectSingleNode('DetectInstall')
    $installed = if ($di -and @($di.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }).Count) { Test-LenovoNode $di $f } else { 'False' }
    if ($installed -eq 'True') { return 'Current' }
    if ($installed -eq 'Unknown') { return 'Unknown' }
    $dep = $pkg.SelectSingleNode('Dependencies')
    $applies = if ($dep) { Test-LenovoNode $dep $f } else { 'False' }
    if ($applies -eq 'True') { 'Behind' } elseif ($applies -eq 'Unknown') { 'Unknown' } else { 'NotApplicable' }
}
# ==== end of Lenovo's rules ====

$ErrorActionPreference = 'Stop'
if (-not (Get-Variable DryRun -ErrorAction SilentlyContinue)) { $DryRun = $false }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Out-Result($result, $extra) {
    $o = [ordered]@{ Result = $result }
    if ($extra) { foreach ($k in $extra.Keys) { $o[$k] = $extra[$k] } }
    [pscustomobject]$o
}
function Get-Xml($url, $sha256) {
    $bytes = (New-Object Net.WebClient).DownloadData($url)
    if ($sha256) {
        $got = -join ([Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') })
        if ($got -ne $sha256.ToUpper()) { throw "Lenovo's descriptor $url does not match the SHA-256 its catalog gives: refused" }
    }
    [xml]([Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF))
}

$cs = Get-CimInstance Win32_ComputerSystem
if ($cs.Manufacturer -notmatch 'LENOVO') { return Out-Result 'Not a Lenovo: update its BIOS from the maker''s own tool.' }
$product = [string](Get-CimInstance Win32_ComputerSystemProduct).Name
$type = $product.Substring(0, [Math]::Min(4, $product.Length)).ToUpper()
$bios = ([string](Get-CimInstance Win32_BIOS).SMBIOSBIOSVersion).Trim()
$build = [int](Get-CimInstance Win32_OperatingSystem).BuildNumber
$facts0 = [pscustomobject]@{ BiosId = ($bios -split ' ', 2)[0]; Windows11 = ($build -ge 22000); AddressWidth = [int](@(Get-CimInstance Win32_Processor)[0].AddressWidth) }
$os = if ($facts0.Windows11) { 'Win11' } else { 'Win10' }

# 1. Lenovo's package for this BIOS.
$catalog = Get-Xml "https://download.lenovo.com/catalog/${type}_$os.xml"
$entries = @($catalog.packages.package | Where-Object { $_.category -match '\bBIOS\b|\bUEFI\b' -and ([string]$_.location).Trim() -like 'https://download.lenovo.com/*' })
if ($entries.Count -eq 0) { return Out-Result "Lenovo's $os catalog for $type lists no BIOS package: nothing to do." @{ MachineType = $type; Bios = $bios } }

$pkg = $null; $url = $null; $unknown = @()
foreach ($e in $entries) {
    $loc = ([string]$e.location).Trim()
    # The chain of trust: the catalog (HTTPS) vouches for the descriptor, the descriptor for the installer.
    $descSha = if ($e.checksum -and $e.checksum.type -eq 'sha256') { ([string]$e.checksum.InnerText).Trim() } else { $null }
    $d = (Get-Xml $loc $descSha).Package
    switch (Get-LenovoVerdict $d $facts0) {
        'Current' { return Out-Result "The BIOS is current: $bios has Lenovo's $($d.version) (package $($d.id))." @{ MachineType = $type; Bios = $bios; Package = [string]$d.id } }
        'Behind' { $pkg = $d; $url = $loc }
        'Unknown' { $unknown += [string]$d.id }
    }
    if ($pkg) { break }
}
if (-not $pkg) {
    if ($unknown.Count) { return Out-Result "Lenovo decides package $($unknown -join ', ') by a device or firmware check this fix does not read: nothing done. Update this one with Lenovo Commercial Vantage." @{ MachineType = $type; Bios = $bios } }
    return Out-Result "No Lenovo package installs over BIOS $bios on a $type (it is newer than, or outside, what Lenovo lists): nothing to do." @{ MachineType = $type; Bios = $bios }
}
$target = [string]$pkg.version
$exe = ([string]$pkg.Files.Installer.File.Name).Trim()
$sha = ([string]$pkg.Files.Installer.File.CRC).Trim()
$extract = ([string]$pkg.ExtractCommand).Trim()
$install = ([string]$pkg.Install.Cmdline.InnerText).Trim()
$okCodes = @(([string]$pkg.Install.rc) -split ',' | Where-Object { $_ -ne '' } | ForEach-Object { [long]$_.Trim() })
if (-not $okCodes.Count) { $okCodes = @(0) }
if ($exe -notmatch '^[\w\-.]+\.exe$') { throw "Lenovo's descriptor names an unexpected installer: '$exe'" }

# 3. Conditions the flash needs.
$battery = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)
if ($battery.Count -and -not @($battery | Where-Object { $_.BatteryStatus -eq 2 }).Count) {
    return Out-Result "On battery: plug in the charger, then run it again (a BIOS flash must not lose power)." @{ Bios = $bios; Target = $target }
}
$pw = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosPasswordSettings -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pw -and [int]$pw.PasswordState -ne 0) {
    return Out-Result "A BIOS password is set (state $($pw.PasswordState)): the flash would stop at the console for it. Update this one at the PC." @{ Bios = $bios; Target = $target }
}

# 2. The installer, verified.
$dir = Join-Path $env:ProgramData "KorOperations\bios\$($pkg.id)"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$file = Join-Path $dir $exe
$base = $url.Substring(0, $url.LastIndexOf('/'))
if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $sha) {
    (New-Object Net.WebClient).DownloadFile("$base/$exe", $file)
}
$hash = (Get-FileHash $file -Algorithm SHA256).Hash
if (-not $sha -or $hash -ne $sha) { Remove-Item $file -Force; throw "the download's SHA-256 ($hash) is not the one Lenovo's descriptor gives ($sha): refused" }
$sig = Get-AuthenticodeSignature -FilePath $file
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Lenovo(,|$)') {
    Remove-Item $file -Force; throw "the installer is not validly signed by Lenovo ($($sig.Status); $($sig.SignerCertificate.Subject)): refused"
}

$blv = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$env:SystemDrive'" -ErrorAction SilentlyContinue
$bitlocker = if ($blv -and [int]$blv.ProtectionStatus -eq 1) { 'on' } else { 'off' }

function Split-Command($cmd) {
    $cmd = $cmd.Replace('%PACKAGEPATH%', $dir)
    if ($cmd -match '%[A-Z_]+%') { throw "Lenovo's command uses a variable this fix does not know: $cmd" }
    if ($cmd -match '^"([^"]+)"\s*(.*)$' -or $cmd -match '^(\S+)\s*(.*)$') { $f = $Matches[1]; $a = $Matches[2] } else { throw "unreadable command: $cmd" }
    if (-not [IO.Path]::IsPathRooted($f)) { $f = Join-Path $dir $f }
    if (-not $f.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) { throw "Lenovo's command runs something outside the package folder: $f" }
    @($f, $a)
}
$ex = Split-Command $extract
$in = Split-Command $install

$facts = [ordered]@{ MachineType = $type; Bios = $bios; Target = $target; Package = [string]$pkg.id; Released = [string]$pkg.ReleaseDate
    Sha256 = 'matches Lenovo'; Signature = "valid: $($sig.SignerCertificate.Subject)"; BitLocker = $bitlocker
    Extract = "$($ex[0]) $($ex[1])"; Install = "$($in[0]) $($in[1])"; SuccessCodes = ($okCodes -join ',') }
if ($DryRun) { return Out-Result "Ready: $bios -> $target from Lenovo package $($pkg.id) (verified). Nothing was flashed." $facts }

# 4. BitLocker: suspended for exactly one restart; resumed again here if the flash does not happen.
if ($bitlocker -eq 'on') {
    & manage-bde.exe -protectors -disable $env:SystemDrive -RebootCount 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "BitLocker could not be suspended (manage-bde exit $LASTEXITCODE): nothing flashed" }
}
try {
    # 5. Lenovo's own two steps.
    $p = Start-Process -FilePath $ex[0] -ArgumentList $ex[1] -WorkingDirectory $dir -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { throw "Lenovo's extract step exited $($p.ExitCode)" }
    $p = Start-Process -FilePath $in[0] -ArgumentList $in[1] -WorkingDirectory $dir -Wait -PassThru -WindowStyle Hidden
    $code = [long]$p.ExitCode
    if ($okCodes -notcontains $code) { throw "Lenovo's install step exited $code (success is $($okCodes -join ', '))" }
} catch {
    if ($bitlocker -eq 'on') { & manage-bde.exe -protectors -enable $env:SystemDrive | Out-Null }
    throw
}

# 6. The restart that writes it.
$msg = "KOR IT (NetworkOps) is updating this computer's BIOS. It restarts in 5 minutes: save your work now. Do not turn it off while it restarts."
& shutdown.exe /r /t 300 /c $msg /d p:2:17
$facts.ExitCode = $code
$facts.Restart = if ($LASTEXITCODE -eq 0) { 'scheduled in 5 minutes (the user was warned on screen)' } else { "shutdown.exe refused (exit $LASTEXITCODE): restart it by hand to finish" }
Out-Result "BIOS $bios -> $target staged; it is written during the restart (BitLocker $(if ($bitlocker -eq 'on') { 'suspended for that one restart' } else { 'off' }))." $facts
