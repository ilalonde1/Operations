# New-ReaderSecret.ps1 - the password of the read-only KorStandards login the ModelCoverageTests use on KOR-210, made ON
# KOR-210 and kept there (2026-10-28, Ian's yes via the Virtual Drafter; PDF Ingestor).
#
# The password is generated here, stored DPAPI-protected for this machine (LocalMachine scope) in a file only SYSTEM and
# Administrators can read, and NEVER printed or sent. What leaves is SQL Server's own salted hash of it (the 2012+ format:
# 0x0200 + 4-byte salt + SHA2_512(UTF-16LE password + salt)), base64-encoded - 96 characters, the -Confirm phrase of
# KOR.Drafter db/<n>_korstandards_rules_reader.sql, which creates the login WITH PASSWORD = <hash> HASHED. The hash is a
# verifier, not the password; a 40-character random password cannot be recovered from it.
#
#   New-ReaderSecret.ps1                      make the secret if none exists; print the hash's base64 (refuses to overwrite)
#   New-ReaderSecret.ps1 -Rotate              make a new one over the old (the login must then be re-created with the new hash)
#   New-ReaderSecret.ps1 -SelfTest <pw>       print the hash of a THROWAWAY password (proving the format against PWDCOMPARE)
#
# Covers: generation, storage, the hash. Does not: create the login (the db script, applied by the Virtual Drafter as sa), or
# read the secret back (Invoke-ModelCoverage210.ps1 does, into its own process only).
param([switch]$Rotate, [string]$SelfTest)
Add-Type -AssemblyName System.Security

$Store = 'C:\ProgramData\KOR\korstandards-reader.bin'

function Get-SqlHashBase64([string]$password) {
    $salt = New-Object byte[] 4
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)
    $pw = [System.Text.Encoding]::Unicode.GetBytes($password)
    $sha = [System.Security.Cryptography.SHA512]::Create()
    $digest = $sha.ComputeHash([byte[]]($pw + $salt))
    $hash = [byte[]](@(0x02, 0x00) + $salt + $digest)
    [Convert]::ToBase64String($hash)
}

if ($SelfTest) {
    [pscustomobject]@{ SelfTestHashBase64 = Get-SqlHashBase64 $SelfTest }
    return
}

if ((Test-Path $Store) -and -not $Rotate) {
    throw "$Store exists - a secret is already made. -Rotate makes a new one (and the login must be re-created with its hash)."
}

# 40 characters from a 62-letter alphabet, rejection-sampled so every letter is equally likely
$alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$chars = New-Object System.Collections.Generic.List[char]
$b = New-Object byte[] 1
while ($chars.Count -lt 40) {
    $rng.GetBytes($b)
    if ($b[0] -lt 248) { $chars.Add($alphabet[$b[0] % 62]) }
}
$password = -join $chars

New-Item -ItemType Directory -Force -Path (Split-Path $Store) | Out-Null
$protected = [System.Security.Cryptography.ProtectedData]::Protect([System.Text.Encoding]::UTF8.GetBytes($password), $null,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
[IO.File]::WriteAllBytes($Store, $protected)

# only SYSTEM and Administrators may read it: inheritance off, everything else removed
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($who in 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators') {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($who, 'FullControl', 'Allow')))
}
Set-Acl -Path $Store -AclObject $acl

$hashB64 = Get-SqlHashBase64 $password
$password = $null
[pscustomobject]@{
    Stored           = $Store
    StoredBytes      = (Get-Item $Store).Length
    ReadableBy       = ((Get-Acl $Store).Access | ForEach-Object { "$($_.IdentityReference):$($_.FileSystemRights)" }) -join '; '
    HashBase64       = $hashB64
    HashBase64Length = $hashB64.Length
}
