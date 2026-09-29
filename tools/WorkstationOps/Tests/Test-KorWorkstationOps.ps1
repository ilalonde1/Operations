# Test-KorWorkstationOps.ps1
# Regression tests for Kor.WorkstationOps. Dependency-free on purpose: the only Pester on
# this box is 3.4.0 (PS 5.1 era) which will not load under pwsh 7, and a hardware probe is
# not worth blocking on a module install.
#
#   pwsh -File Test-KorWorkstationOps.ps1     -> exit 0 all pass, exit 1 on any failure
#
# The SMBIOS fixture is a real blob captured from KOR-SPARE100 on 2026-08-13. Its expected
# values below are what that machine physically is, cross-checked against the ASUS board and
# the Corsair kit part number. If the parser drifts, these fail.

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\Kor.WorkstationOps.psd1') -Force

$script:Pass = 0
$script:Fail = 0

function Assert-Equal {
    param($Expected, $Actual, [string]$Because)
    if ($Expected -eq $Actual) {
        $script:Pass++
        Write-Host "  PASS  $Because" -ForegroundColor DarkGray
    } else {
        $script:Fail++
        Write-Host "  FAIL  $Because" -ForegroundColor Red
        Write-Host "        expected [$Expected] but got [$Actual]" -ForegroundColor Red
    }
}

function Assert-Throws {
    param([scriptblock]$Body, [string]$Because)
    try {
        & $Body
        $script:Fail++
        Write-Host "  FAIL  $Because (no exception raised)" -ForegroundColor Red
    } catch {
        $script:Pass++
        Write-Host "  PASS  $Because" -ForegroundColor DarkGray
    }
}

function Get-FixtureBytes {
    param([string]$Name)
    $hex = (Get-Content (Join-Path $PSScriptRoot "fixtures\$Name") -Raw).Trim()
    $b = New-Object byte[] ($hex.Length / 2)
    for ($i = 0; $i -lt $b.Length; $i++) { $b[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
    $b
}

Write-Host "`nConvertFrom-KorSmbios against the KOR-SPARE100 fixture" -ForegroundColor Cyan
$bytes = Get-FixtureBytes 'smbios-KOR-SPARE100.hex'
$s = ConvertFrom-KorSmbios -Bytes $bytes

Assert-Equal '3.0'                      $s.Version                  'SMBIOS version is 3.0'
Assert-Equal 'ASUSTeK COMPUTER INC.'    $s.Board.Manufacturer       'board manufacturer is ASUSTeK'
Assert-Equal 'STRIX Z270H GAMING'       $s.Board.Product            'board product is STRIX Z270H GAMING'
Assert-Equal 'Desktop'                  $s.Chassis.Type             'chassis decodes to Desktop'
Assert-Equal 3                          $s.Chassis.TypeCode         'chassis type code is 3'

Write-Host "`nProcessor" -ForegroundColor Cyan
Assert-Equal 'LGA1151'                  $s.Processor.Socket         'socket is LGA1151'
Assert-Equal 4                          $s.Processor.Cores          'i7-7700K reports 4 cores'
Assert-Equal 8                          $s.Processor.Threads        'i7-7700K reports 8 threads'
Assert-Equal $true  ($s.Processor.Version -like '*i7-7700K*')       'CPU version string names the 7700K'

Write-Host "`nMemory - the numbers an upgrade decision turns on" -ForegroundColor Cyan
Assert-Equal 4      $s.Memory.Slots                                 'board has 4 DIMM slots'
Assert-Equal 4      $s.Memory.SlotsPopulated                        'all 4 slots are populated'
Assert-Equal 0      $s.Memory.SlotsFree                             'no free slots - cannot add, only replace'
Assert-Equal 32     $s.Memory.InstalledGB                           '32 GB installed'
Assert-Equal 64     $s.Memory.MaxCapacityGB                         'Z270 max capacity is 64 GB'
Assert-Equal 4      $s.Memory.Dimms.Count                           'four Type 17 structures parsed'

foreach ($d in $s.Memory.Dimms) {
    Assert-Equal 8192      $d.SizeMB       "$($d.Locator) is 8192 MB"
    Assert-Equal 'DDR4'    $d.Type         "$($d.Locator) is DDR4"
    Assert-Equal 3000      $d.RatedMTs     "$($d.Locator) is rated 3000 MT/s"
    Assert-Equal 'Corsair' $d.Manufacturer "$($d.Locator) is Corsair"
    Assert-Equal 'CMK16GX4M2B3000C15' $d.PartNumber "$($d.Locator) part number"
    Assert-Equal $true     $d.Populated    "$($d.Locator) reports populated"
}

Write-Host "`nMalformed input is refused, not guessed at" -ForegroundColor Cyan
Assert-Throws { ConvertFrom-KorSmbios -Bytes ([byte[]]@(0, 3, 0, 0)) } 'a blob too short to hold a header throws'

# A length field claiming more than the buffer holds must not walk off the end. Take a valid
# blob and overstate its length: parsing must stop at the real end and still return structures.
$lying = $bytes.Clone()
[Array]::Copy([BitConverter]::GetBytes([uint32]($bytes.Length * 4)), 0, $lying, 4, 4)
$r = ConvertFrom-KorSmbios -Bytes $lying
Assert-Equal $true ($null -ne $r.Board) 'an overstated length still parses without running off the buffer'

# A structure whose declared length is nonsense must stop the walk rather than emit garbage.
$corrupt = $bytes.Clone()
$corrupt[9] = 1        # first structure claims a 1-byte formatted area, shorter than its own header
$r2 = ConvertFrom-KorSmbios -Bytes $corrupt
Assert-Equal 0 $r2.Memory.Dimms.Count 'a corrupt structure length halts the walk instead of inventing DIMMs'

Write-Host "`nNew-KorOnTargetPayload - the script that runs as SYSTEM on the target" -ForegroundColor Cyan
# The wrapper is the one piece of run-on-target that can be proven without a machine. A
# payload that does not parse fails silently on the target (no result file, just a timeout),
# so parse it here with PowerShell's own parser, including a body carrying non-ASCII -- the
# em-dash that broke a staged script under Windows PowerShell 5.1.
$body = @'
$note = 'renamed -- or deleted' + [char]0x2014 + ' mid-run'
Get-Item C:\Windows | Select-Object Name
'@
$payload = New-KorOnTargetPayload -Script $body -OutputPath 'C:\Windows\Temp\korrun-test.json'
$tokens = $null; $errors = $null
[void][Management.Automation.Language.Parser]::ParseInput($payload, [ref]$tokens, [ref]$errors)
Assert-Equal 0 @($errors).Count 'the generated payload parses with no errors'
Assert-Equal $true ($payload.Contains($body)) 'the caller''s body is embedded verbatim'
Assert-Equal $true ($payload -match [regex]::Escape("'C:\Windows\Temp\korrun-test.json.tmp'")) 'the result is written to .tmp first'
Assert-Equal $true ($payload -match "Move-Item -LiteralPath 'C:\\Windows\\Temp\\korrun-test\.json\.tmp' -Destination 'C:\\Windows\\Temp\\korrun-test\.json'") 'then renamed into place, so a half-written file is never read'
Assert-Equal $true ($payload -match 'Ok = \$false; Error =') 'a throwing body reports its error instead of producing no file'

# Run the payload for real, locally, against a temp path: it must publish exactly one JSON
# result with Ok=true and the body's output inside -- the contract Invoke-KorOnTarget reads.
$tmpOut = Join-Path ([IO.Path]::GetTempPath()) ("korrun-selftest-{0}.json" -f [guid]::NewGuid().ToString('N'))
$okPayload = New-KorOnTargetPayload -Script "[pscustomobject]@{ Answer = 42 }" -OutputPath $tmpOut
& ([scriptblock]::Create($okPayload))
$res = Get-Content $tmpOut -Raw | ConvertFrom-Json
Assert-Equal $true $res.Ok 'a clean body reports Ok'
Assert-Equal 42 $res.Output[0].Answer 'the body''s output round-trips through the JSON file'
Assert-Equal $false (Test-Path "$tmpOut.tmp") 'no .tmp file is left behind'
Remove-Item $tmpOut -Force

$errOut = Join-Path ([IO.Path]::GetTempPath()) ("korrun-selftest-{0}.json" -f [guid]::NewGuid().ToString('N'))
& ([scriptblock]::Create((New-KorOnTargetPayload -Script "throw 'deliberate'" -OutputPath $errOut)))
$er = Get-Content $errOut -Raw | ConvertFrom-Json
Assert-Equal $false $er.Ok 'a throwing body reports Ok = false'
Assert-Equal 'deliberate' $er.Error 'and carries its error message back'
Remove-Item $errOut -Force

Write-Host "`nTest-KorSmbReachable - port 445, never ping" -ForegroundColor Cyan
Assert-Equal $false (Test-KorSmbReachable -ComputerName 'kor-no-such-host.invalid' -TimeoutMs 1500) 'an unresolvable host is unreachable, not an exception'
# 'localhost' resolves to 127.0.0.1 and this box serves SMB, so it must read reachable.
Assert-Equal $true (Test-KorSmbReachable -ComputerName 'localhost') 'a host serving SMB on any of its addresses is reachable'

Write-Host ("`n{0} passed, {1} failed`n" -f $script:Pass, $script:Fail) -ForegroundColor $(if ($script:Fail) { 'Red' } else { 'Green' })
exit $(if ($script:Fail) { 1 } else { 0 })
