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

Write-Host ("`n{0} passed, {1} failed`n" -f $script:Pass, $script:Fail) -ForegroundColor $(if ($script:Fail) { 'Red' } else { 'Green' })
exit $(if ($script:Fail) { 1 } else { 0 })
