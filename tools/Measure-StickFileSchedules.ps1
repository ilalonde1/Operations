<#
.SYNOPSIS
    One command that measures what the schedule readers read off the five local KOR stick files.

.DESCRIPTION
    Runs `takeoff footings` and `takeoff pdf-takeoff` (the plan-against-its-schedule self-check)
    over the five jobs the intake work is measured on, prints every number, and compares them
    against a banked standard. It exists because on 2026-09-04 a commit stated "Footings
    unchanged: 31065 1,174 cy, 31138 353 cy, 31130 258 cy" and the same verb on the same files
    read 855 and 48 -- nothing measured every deliverable, so nothing said so (CLAUDE.md rule 9).

    WHAT IT COVERS
      - spread-footing totals and the set of footing marks read, per job
      - the set of COLUMN marks the schedule reader reads off one schedule page per job, exactly,
        and that every one of them was read from the table's own border (MarkRoute.ScheduleBorder)
        on the four jobs that draw one
      - the flat shear-wall rows on that page: every mark must be wall-shaped (a bar size like
        4-20M or a note number like 3. read as a wall mark is the fault this guards)
      - column self-check per schedule page: coverage numerator, over-detection factor, and the
        unplaced list, which must contain only mark-shaped tokens (a bar size like 8-35M or a
        wrapped word like BOT. read as a mark is exactly the fault this guards)

    WHAT IT DOES NOT COVER
      - whether a coverage number is RIGHT -- only that it does not go down
      - the DXF the self-check writes, the storeys, or anything downstream of intake
      - a page the standard does not list; add the page when a reader learns to read it
      - a schedule read with the right marks and the wrong sizes: the totals would move, the
        mark set would not, and a compensating pair of size errors is invisible here
      - the self-check's denominator on a job whose marks are bare numerals: 31202 circles its
        column marks 1..8, and its grid bubbles are circled numerals too, so "labels on the plan"
        counts both (105 on p17 for 8 marks). Its coverage floor is a floor on the numerator only
      - whether a mark whose size VARIES (31168's C03-B) is placed right: it is banked as a mark,
        and the self-check counts its columns as agreeing because the plan is the statement

    THE STANDARD
      Footings are banked at the last commit that read the marks off every job (4fd1abbe, the
      Codex 10 landing), reproduced from that commit's own build on these files, and reproduced
      again unchanged when rows were bounded by the table's border (2026-09-07). Column marks are
      banked at that border change, read off the crops of the tables by eye and matched to the
      reader's output: every mark the drawing declares, including one whose size varies. Raised
      the same day when fractional inches (PL2), a title's underline (31202) and ksi strengths
      were read. Column coverage is banked as a FLOOR from the same build and may only rise.
      31168 footings at 0 and 31202 at 0 are KNOWN: 31168's FOUNDATION SCHEDULE is a placeholder
      table on the sheet -- ruled, titled, and blank but for one reinforcing note (seen in the
      rendered crop, not inferred) -- and 31202 schedules a RAFT SLAB, which `takeoff footings`
      now says in as many words. Change the standard when a reader changes what it reads, in the
      same commit, and say why.

    The stick files are a frozen local mirror, one PDF per job, copied 2026-09-04:
      31130-01  2026-05-20 issue (the share now carries 2026-09-03; page numbers may differ)
      31138-01  2026-09-01 set     31168-01  2026-04-21 stick file
      31065-01  2026-07-08 (up to SSI-04)    31202-01  2026-09-04 full set

.EXAMPLE
    ./tools/Measure-StickFileSchedules.ps1
    ./tools/Measure-StickFileSchedules.ps1 -Takeoff C:\Users\me\AppData\Local\Temp\wt-4fd1abbe\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe
#>
[CmdletBinding()]
param(
    [string]$Takeoff    = (Join-Path $PSScriptRoot '..\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'),
    [string]$StickFiles = (Join-Path $env:LOCALAPPDATA 'Temp\kor-drawings\stickfiles'),
    [string]$OutDir     = (Join-Path $env:LOCALAPPDATA 'Temp\kor-drawings\harness')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Takeoff))    { throw "takeoff.exe not found at $Takeoff -- build Kor.Operations.EngineeringTools.TakeoffCli first" }
if (-not (Test-Path $StickFiles)) { throw "stick files not found at $StickFiles -- mirror the five PDFs there first (see the header)" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# The standard. Footing marks are the set read; column marks are the set read off SchedulePage and
# the route every one of them must carry; column floors are per page (numerator of cover).
$jobs = @(
    @{ Job='31130-01'; Pages='11-13'; Scale=96;  FootingCy=258;  FootingMarks='F1,F2,F3,F4,SF1';       Cover=@{11=14; 12=25; 13=41}
       SchedulePage=11; ColumnMarks='PC1,PC2,PC4,PC5,PC6,PC7,PC8'; ColumnRoute='ScheduleBorder' },
    @{ Job='31168-01'; Pages='11-13'; Scale=96;  FootingCy=0;    FootingMarks='';                      Cover=@{11=43; 12=65; 13=47}
       SchedulePage=11; ColumnMarks='C02-A,C02-B,C03-A,C03-B,C04-A,C04-B,GC11-C,PC01,PC02,PC03-A,PC03-B,TC01,TC02,TC03,TC04'; ColumnRoute='ScheduleBorder' },
    @{ Job='31138-01'; Pages='9-11';  Scale=96;  FootingCy=353;  FootingMarks='F1,F2,SF1,SF2';         Cover=@{9=24; 11=21}
       SchedulePage=9;  ColumnMarks='PC1,PC1A,PC2,PC3,PC3A,PC4,PC5,PC6,PC7,PC8,PC9,PL1,PL2'; ColumnRoute='ScheduleBorder' },
    @{ Job='31065-01'; Pages='14-16'; Scale=100; FootingCy=1174; FootingMarks='F1,F2,F3,F4,SF1,SF2';   Cover=@{14=24; 15=22; 16=30}
       SchedulePage=14; ColumnMarks='PC1,PC1A,PC2,PC3,PC4,PC5,ZC1,ZC2'; ColumnRoute='ScheduleBorder' },
    @{ Job='31202-01'; Pages='17-17'; Scale=96;  FootingCy=0;    FootingMarks='';                      Cover=@{17=23}
       SchedulePage=17; ColumnMarks='1,2,3,4,5,6,7,8';                                                  ColumnRoute='ScheduleBorder' }
)

# A mark as KOR draws one: C4, PC1, TC02, C02-A, PC03-A, GC11-C, SF1, PL1, PC1A -- or a bare numeral,
# which 31202 circles for its column marks. Anything else in an unplaced list is a token the reader
# took for a mark and should not have (8-35M, BOT., 3.).
$markShape = '^(?:[A-Z]{1,3}\d{1,2}[A-Z]?(?:-[A-Z0-9]{1,3})?|\d{1,2})$'

# A wall mark as KOR draws one (SWA, SWB1, W2), or a zone mark (ZA) -- see the header for why the
# zone marks are admitted here. A bar size (4-20M), a note number (3.) or a footing mark (F2) read
# as a wall is the fault this guards.
$wallShape = '^(?:SW|W|Z)[A-Z0-9]{0,3}$'

$checks = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Check=$name; Ok=$ok; Detail=$detail })
}

Write-Host "takeoff : $Takeoff"
Write-Host "sticks  : $StickFiles"
Write-Host ""

foreach ($j in $jobs) {
    $pdf = Join-Path $StickFiles "$($j.Job).pdf"
    if (-not (Test-Path $pdf)) { Check "$($j.Job) present" $false "missing $pdf"; continue }

    # ---- footings ------------------------------------------------------------------------
    $fout = Join-Path $OutDir "footings-$($j.Job).txt"
    & $Takeoff footings $pdf 2>&1 | Out-File -Encoding utf8 $fout
    $ftext = Get-Content $fout -Raw
    $total = if ($ftext -match 'TOTAL spread footings:\s*([\d,]+)\s*cy') { [int]($Matches[1] -replace ',','') } else { -1 }
    $marks = @(Get-Content $fout | Where-Object { $_ -match '^\s{4}(\S+)\s+.*DEEP' } | ForEach-Object { $Matches[1] } | Sort-Object -Unique)
    $markList = ($marks -join ',')
    $expectMarks = ($j.FootingMarks -split ',' | Where-Object { $_ } | Sort-Object) -join ','
    Write-Host ("{0}  footings {1,6} cy   marks [{2}]" -f $j.Job, $total, $markList)
    Check "$($j.Job) footing total = $($j.FootingCy) cy"  ($total -eq $j.FootingCy) "read $total"
    Check "$($j.Job) footing marks = [$expectMarks]"      ($markList -eq $expectMarks) "read [$markList]"

    # ---- columns: the marks read off the schedule page, and how ------------------------------
    if ($j.SchedulePage -gt 0) {
        $sout = Join-Path $OutDir "sched-$($j.Job)-p$($j.SchedulePage).txt"
        & $Takeoff vector-sched $pdf $j.SchedulePage 2>&1 | Out-File -Encoding utf8 $sout
        $section = ''
        $colMarks = New-Object System.Collections.Generic.List[string]
        $colRoutes = New-Object System.Collections.Generic.List[string]
        $wallMarks = New-Object System.Collections.Generic.List[string]
        foreach ($line in Get-Content $sout) {
            if ($line -match '^Column schedule \(')  { $section = 'column'; continue }
            if ($line -match '^Flat wall rows \(')   { $section = 'wall';   continue }
            if ($line -match '^\S')                  { $section = ''; continue }
            if ($line -notmatch '^\s+(\S+)\s.*\[(\w+)\]\s*$') { continue }
            if ($section -eq 'column') { $colMarks.Add($Matches[1]); $colRoutes.Add($Matches[2]) }
            elseif ($section -eq 'wall') { $wallMarks.Add($Matches[1]) }
        }
        $readMarks = ($colMarks | Sort-Object -Unique) -join ','
        $expectCols = ($j.ColumnMarks -split ',' | Where-Object { $_ } | Sort-Object) -join ','
        $offRoute = @($colRoutes | Where-Object { $_ -ne $j.ColumnRoute })
        $badWalls = @($wallMarks | Where-Object { $_ -notmatch $wallShape })
        Write-Host ("          p{0,-3} columns [{1}]" -f $j.SchedulePage, $readMarks)
        Write-Host ("          p{0,-3} walls   [{1}]" -f $j.SchedulePage, ($wallMarks -join ','))
        Check "$($j.Job) p$($j.SchedulePage) column marks = [$expectCols]" ($readMarks -eq $expectCols) "read [$readMarks]"
        Check "$($j.Job) p$($j.SchedulePage) every column mark read via $($j.ColumnRoute)" ($offRoute.Count -eq 0) ("other routes: [" + (($offRoute | Sort-Object -Unique) -join ',') + "]")
        Check "$($j.Job) p$($j.SchedulePage) wall marks are wall-shaped" ($badWalls.Count -eq 0) ("not walls: [" + ($badWalls -join ',') + "]")
    }

    # ---- columns: the sheet against its own schedule ----------------------------------------
    if (-not $j.Pages) { Write-Host "          columns: no schedule pages banked"; continue }
    $dxf = Join-Path $OutDir "$($j.Job).dxf"
    $cout = Join-Path $OutDir "columns-$($j.Job).txt"
    & $Takeoff pdf-takeoff $pdf $dxf --pages $j.Pages --scale $j.Scale 2>&1 | Out-File -Encoding utf8 $cout
    $seen = @{}
    foreach ($line in Get-Content $cout) {
        if ($line -notmatch '^\s*(\d+)\s+.*cover (\d+)/(\d+) labelled, emitted (\d+) \(([\d.]+)x\)(?:; unplaced (.*))?$') { continue }
        $page = [int]$Matches[1]; $num = [int]$Matches[2]; $den = [int]$Matches[3]; $emit = [int]$Matches[4]; $factor = $Matches[5]
        $unplaced = @(if ($Matches[6]) { $Matches[6] -split ',' | ForEach-Object { $_.Trim() } } )
        $seen[$page] = $true
        Write-Host ("          p{0,-3} cover {1,3}/{2,-3} emitted {3,4} ({4}x)   unplaced [{5}]" -f $page, $num, $den, $emit, $factor, ($unplaced -join ','))
        if ($j.Cover.ContainsKey($page)) {
            Check "$($j.Job) p$page coverage >= $($j.Cover[$page])" ($num -ge $j.Cover[$page]) "read $num/$den"
        }
        $bad = @($unplaced | Where-Object { $_ -and ($_ -notmatch $markShape) })
        Check "$($j.Job) p$page unplaced are mark-shaped" ($bad.Count -eq 0) ("not marks: [" + ($bad -join ',') + "]")
    }
    foreach ($p in $j.Cover.Keys) {
        if (-not $seen.ContainsKey($p)) { Check "$($j.Job) p$p self-check line present" $false "no 'cover' line for page $p (older build, or the page read nothing)" }
    }
}

Write-Host ""
$failed = @($checks | Where-Object { -not $_.Ok })
foreach ($f in $failed) { Write-Host ("FAIL  {0}  -- {1}" -f $f.Check, $f.Detail) }
Write-Host ("passed {0} of {1} checks" -f ($checks.Count - $failed.Count), $checks.Count)
if ($failed.Count -gt 0) { exit 1 }
