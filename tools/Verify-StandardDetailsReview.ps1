<#
.SYNOPSIS
  Re-derives every mechanical claim in Desktop\KOR-StandardDetails-Review-2026-09-07.txt and prints
  EXPECTED vs ACTUAL with MATCH / DIFF / ERROR per line. Read-only everywhere.

.DESCRIPTION
  This is the verification harness for the 2026-09-07 Standard Details state review. Anyone can run it
  and compare the raw numbers to the review without trusting the reviewer. Run it again after a fix to
  see exactly which lines changed.

  Scope: KorStandards + KorTransmittals (SELECT only, creds read from App.config), the Drafting share,
  KOR-302N, KOR-204, the Newerforma share, the three repos, and the StandardDetails source.
  NOT in scope: anything it cannot do in under a minute. There is NO recursive enumeration over SMB.
  The 94k-file bridge outbox count is behind -IncludeBridgeCounts (about two minutes) and off by default.

  What this does NOT check (say so, per repo rule 11): the .rvt contents, the bridge verbs, the meeting
  transcript, any code defect. Those need Revit, or a code reader.

  PowerShell here is the prototype; the C# port is owed like the rest of tools/.

.EXAMPLE
  pwsh -File "C:\VIsual Studio Projects\Operations\tools\Verify-StandardDetailsReview.ps1"
  pwsh -File ".\tools\Verify-StandardDetailsReview.ps1" -IncludeBridgeCounts
#>
[CmdletBinding()]
param(
    [switch]$IncludeBridgeCounts,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DrafterRepo = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'KOR.Drafter'),
    [string]$RevitToolsRepo = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'KOR.RevitTools')
)

$ErrorActionPreference = 'Continue'
$script:Rows = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param([string]$Section, [string]$Claim, [string]$Expected, [scriptblock]$Actual)
    $value = $null; $status = 'ERROR'
    try {
        $value = & $Actual
        if ($null -eq $value) { $value = '(null)' }
        $value = [string]$value
        $status = if ($value.Trim() -eq $Expected.Trim()) { 'MATCH' } else { 'DIFF' }
    } catch {
        $value = 'ERROR: ' + $_.Exception.Message
    }
    $script:Rows.Add([pscustomobject]@{ Section = $Section; Claim = $Claim; Expected = $Expected; Actual = $value; Status = $status })
}

function Get-ConnParts {
    param([string]$Name)
    $cfg = Get-Content (Join-Path $RepoRoot 'Kor.Operations.App\App.config') -Raw
    $cs = [regex]::Match($cfg, 'name="' + $Name + '"\s+connectionString="([^"]+)"').Groups[1].Value
    if (-not $cs) { throw "connection string $Name not found in App.config" }
    [pscustomobject]@{
        Server = [regex]::Match($cs, 'Server=([^;]+)').Groups[1].Value
        Database = [regex]::Match($cs, 'Database=([^;]+)').Groups[1].Value
        User = [regex]::Match($cs, 'User Id=([^;]+)').Groups[1].Value
        Password = [regex]::Match($cs, 'Password=([^;]+)').Groups[1].Value
    }
}

function Invoke-Sql {
    # One SELECT, one result row, columns joined with '|'. SELECT only by construction of the callers.
    param([object]$Conn, [string]$Query)
    if ($Query -notmatch '^\s*(SET NOCOUNT ON;\s*)?SELECT') { throw 'Invoke-Sql accepts SELECT only' }
    $out = & sqlcmd -S $Conn.Server -d $Conn.Database -U $Conn.User -P $Conn.Password -C -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
    $lines = @($out | Where-Object { $_ -and ($_ -notmatch '^\s*$') })
    if ($lines.Count -eq 0) { return '(no rows)' }
    if ($lines[0] -match '^Msg \d+') { throw ($lines -join ' ') }
    return (($lines | ForEach-Object { $_.Trim() }) -join ' ; ')
}

function Test-DllContains {
    param([string]$Path, [string]$Needle)
    if (-not (Test-Path -LiteralPath $Path)) { return 'MISSING' }
    return [bool](Select-String -Path $Path -Pattern $Needle -Quiet)
}

function Get-ExplicitAceCount { param([string]$Path) return (((Get-Acl -LiteralPath $Path).Access | Where-Object { -not $_.IsInherited }) | Measure-Object).Count }

function Get-FileStamp { param([string]$Path) if (-not (Test-Path -LiteralPath $Path)) { return 'MISSING' }; $i = Get-Item -LiteralPath $Path -Force; return ('{0:N1}MB {1:yyyy-MM-dd HH:mm}' -f ($i.Length / 1MB), $i.LastWriteTime) }

# Named Invoke-Git, and calls git.exe explicitly: a function called "Git" that runs "git" resolves to itself
# (PowerShell command lookup is case-insensitive and prefers functions) and dies with a call-depth overflow.
# The parameter is NOT called $Args: that is PowerShell's automatic unbound-arguments variable, and naming a
# parameter after it hands git an empty list (it printed its usage text on the first run of this script).
function Invoke-Git { param([string]$Repo, [string[]]$GitArgs) return ((& git.exe -C $Repo @GitArgs 2>&1) -join "`n") }

# ---------------------------------------------------------------- KorStandards
$ks = Get-ConnParts 'KorStandardsDb'
$S = 'KorStandards'
Add-Check $S 'Detail total|active|retired' '612|608|4' { Invoke-Sql $ks "SELECT COUNT(*), SUM(CASE WHEN RetiredAtUtc IS NULL THEN 1 ELSE 0 END), SUM(CASE WHEN RetiredAtUtc IS NOT NULL THEN 1 ELSE 0 END) FROM detail.Detail" }
Add-Check $S 'active content-verified|unverified|human-confirmed' '603|5|0' { Invoke-Sql $ks "SELECT SUM(CASE WHEN Confidence='content-verified' THEN 1 ELSE 0 END), SUM(CASE WHEN Confidence='unverified' THEN 1 ELSE 0 END), SUM(CASE WHEN Confidence='human-confirmed' THEN 1 ELSE 0 END) FROM detail.Detail WHERE RetiredAtUtc IS NULL" }
Add-Check $S 'unverified set (all VariantsDiverge=1)' 'KOR-D-00100,KOR-D-00133,KOR-D-00173,KOR-D-00501,KOR-D-00526|5' { Invoke-Sql $ks "SELECT STRING_AGG(DetailNumber, ',') WITHIN GROUP (ORDER BY DetailNumber), SUM(CAST(VariantsDiverge AS int)) FROM detail.Detail WHERE RetiredAtUtc IS NULL AND Confidence='unverified'" }
Add-Check $S 'Discipline Concrete|Wood Frame|General|Steel' '294|148|132|34' { Invoke-Sql $ks "SELECT SUM(CASE WHEN Discipline='Concrete' THEN 1 ELSE 0 END), SUM(CASE WHEN Discipline='Wood Frame' THEN 1 ELSE 0 END), SUM(CASE WHEN Discipline='General' THEN 1 ELSE 0 END), SUM(CASE WHEN Discipline='Steel' THEN 1 ELSE 0 END) FROM detail.Detail WHERE RetiredAtUtc IS NULL" }
Add-Check $S 'Kind typical|custom|general-note|NULL ; IsSheet=1' '377|73|157|1|156' { Invoke-Sql $ks "SELECT SUM(CASE WHEN Kind='typical' THEN 1 ELSE 0 END), SUM(CASE WHEN Kind='custom' THEN 1 ELSE 0 END), SUM(CASE WHEN Kind='general-note' THEN 1 ELSE 0 END), SUM(CASE WHEN Kind IS NULL THEN 1 ELSE 0 END), SUM(CAST(IsSheet AS int)) FROM detail.Detail WHERE RetiredAtUtc IS NULL" }
Add-Check $S 'Kind/IsSheet contradictions' 'KOR-D-00001,KOR-D-00367,KOR-D-00368' { Invoke-Sql $ks "SELECT STRING_AGG(DetailNumber, ',') WITHIN GROUP (ORDER BY DetailNumber) FROM detail.Detail WHERE RetiredAtUtc IS NULL AND ((Kind='general-note' AND IsSheet=0) OR Kind IS NULL OR (Kind<>'general-note' AND IsSheet=1))" }
Add-Check $S 'vw_PaletteCatalog placeable details|rows ; unplaceable details|rows' '603|1049|5|26' { Invoke-Sql $ks "SELECT COUNT(DISTINCT CASE WHEN IsPlaceable=1 THEN DetailNumber END), SUM(CASE WHEN IsPlaceable=1 THEN 1 ELSE 0 END), COUNT(DISTINCT CASE WHEN IsPlaceable=0 THEN DetailNumber END), SUM(CASE WHEN IsPlaceable=0 THEN 1 ELSE 0 END) FROM detail.vw_PaletteCatalog" }
Add-Check $S 'RenderedImage detail rows|png|pdf ; component rows|png|pdf' '604|604|604|287|287|0' { Invoke-Sql $ks "SELECT SUM(CASE WHEN EntityKind='detail' THEN 1 ELSE 0 END), SUM(CASE WHEN EntityKind='detail' AND Png IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN EntityKind='detail' AND Pdf IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN EntityKind='component' THEN 1 ELSE 0 END), SUM(CASE WHEN EntityKind='component' AND Png IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN EntityKind='component' AND Pdf IS NOT NULL THEN 1 ELSE 0 END) FROM detail.RenderedImage" }
Add-Check $S 'placeable lacking pdf|lacking png ; active lacking any image' '0|0|4' { Invoke-Sql $ks "SELECT (SELECT COUNT(*) FROM (SELECT DISTINCT DetailNumber FROM detail.vw_PaletteCatalog WHERE IsPlaceable=1) p LEFT JOIN detail.RenderedImage r ON r.EntityKind='detail' AND r.EntityKey=p.DetailNumber AND r.Pdf IS NOT NULL WHERE r.Id IS NULL), (SELECT COUNT(*) FROM (SELECT DISTINCT DetailNumber FROM detail.vw_PaletteCatalog WHERE IsPlaceable=1) p LEFT JOIN detail.RenderedImage r ON r.EntityKind='detail' AND r.EntityKey=p.DetailNumber AND r.Png IS NOT NULL WHERE r.Id IS NULL), (SELECT COUNT(*) FROM detail.Detail d LEFT JOIN detail.RenderedImage r ON r.EntityKind='detail' AND r.EntityKey=d.DetailNumber WHERE d.RetiredAtUtc IS NULL AND r.Id IS NULL)" }
Add-Check $S 'active details lacking any image (which)' 'KOR-D-00133,KOR-D-00173,KOR-D-00501,KOR-D-00526' { Invoke-Sql $ks "SELECT STRING_AGG(d.DetailNumber, ',') WITHIN GROUP (ORDER BY d.DetailNumber) FROM detail.Detail d LEFT JOIN detail.RenderedImage r ON r.EntityKind='detail' AND r.EntityKey=d.DetailNumber WHERE d.RetiredAtUtc IS NULL AND r.Id IS NULL" }
Add-Check $S 'vw_QuickInsertCatalog placeable|not' '288|0' { Invoke-Sql $ks "SELECT SUM(CASE WHEN IsPlaceable=1 THEN 1 ELSE 0 END), SUM(CASE WHEN IsPlaceable=0 THEN 1 ELSE 0 END) FROM detail.vw_QuickInsertCatalog" }
Add-Check $S 'DetailOccurrence rows|distinct DocumentName|max observed (date)' '1079|1|2026-08-06' { Invoke-Sql $ks "SELECT COUNT(*), COUNT(DISTINCT DocumentName), CONVERT(varchar(10), MAX(ObservedAtUtc), 120) FROM detail.DetailOccurrence" }
Add-Check $S 'DetailOccurrence DocumentName' 'Kor_Structural_Standards_Template_R25.rvt' { Invoke-Sql $ks "SELECT TOP 1 DocumentName FROM detail.DetailOccurrence GROUP BY DocumentName ORDER BY COUNT(*) DESC" }
Add-Check $S 'Detail last UpdatedAtUtc (date)|last RetiredAtUtc (date, UTC)' '2026-09-02|2026-09-04' { Invoke-Sql $ks "SELECT CONVERT(varchar(10), MAX(UpdatedAtUtc), 120), CONVERT(varchar(10), MAX(RetiredAtUtc), 120) FROM detail.Detail" }

# ---------------------------------------------------------------- KorTransmittals
$kt = Get-ConnParts 'KorTransmittalsDb'
$S = 'KorTransmittals'
Add-Check $S 'Documents|Variants|Versions|FileBlobs|ApprovalRecords|PublicationRecords|Outbox' '0|0|0|0|0|0|0' { Invoke-Sql $kt "SELECT (SELECT COUNT(*) FROM dbo.Documents),(SELECT COUNT(*) FROM dbo.DocumentVariants),(SELECT COUNT(*) FROM dbo.DocumentVersions),(SELECT COUNT(*) FROM dbo.FileBlobs),(SELECT COUNT(*) FROM dbo.ApprovalRecords),(SELECT COUNT(*) FROM dbo.PublicationRecords),(SELECT COUNT(*) FROM dbo.StandardDetailPromotionOutbox)" }
Add-Check $S 'AuditEvents|DocumentGroups|group names' '43|4|Concrete,General,Steel,Wood Frame' { Invoke-Sql $kt "SELECT (SELECT COUNT(*) FROM dbo.AuditEvents), (SELECT COUNT(*) FROM dbo.DocumentGroups), (SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM dbo.DocumentGroups)" }

# ---------------------------------------------------------------- Files on the share and 302N
$S = 'Files'
$fsRoot = '\\Kor-fs01\Drafting\KOR-Standards'
$n302 = '\\KOR-302N\C$\KOR.Drafter\tasks\template'
Add-Check $S 'FS01 AUTHORING .rvt' '120.8MB 2026-09-02 17:13' { Get-FileStamp "$fsRoot\template\AUTHORING\KOR-Standards-Authoring-R25.rvt" }
Add-Check $S 'FS01 MASTER .rvt' '120.8MB 2026-09-02 17:40' { Get-FileStamp "$fsRoot\template\MASTER\KOR-Standards-Master-R25.rvt" }
Add-Check $S '302N AUTHORING .rvt' '120.8MB 2026-09-02 17:13' { Get-FileStamp "$n302\AUTHORING\KOR-Standards-Authoring-R25.rvt" }
Add-Check $S '302N MASTER .rvt' '120.8MB 2026-09-02 17:40' { Get-FileStamp "$n302\MASTER\KOR-Standards-Master-R25.rvt" }
Add-Check $S 'MASTER contains a .0002.rvt Revit backup (finding: should not)' 'True' { Test-Path -LiteralPath "$fsRoot\template\MASTER\KOR-Standards-Master-R25.0002.rvt" }
Add-Check $S 'scratch dirs present: _detailrender,_rendertest,_verbtest,review-images,detail-previews' 'True,True,True,True,True' { (@('_detailrender','_rendertest','_verbtest','review-images','detail-previews') | ForEach-Object { Test-Path -LiteralPath "$fsRoot\$_" }) -join ',' }
Add-Check $S 'explicit ACEs on KOR-Standards|MASTER dir|MASTER .rvt' '0|0|0' { (Get-ExplicitAceCount $fsRoot), (Get-ExplicitAceCount "$fsRoot\template\MASTER"), (Get-ExplicitAceCount "$fsRoot\template\MASTER\KOR-Standards-Master-R25.rvt") -join '|' }
Add-Check $S 'BMZ_FS_Drafting_RW rights on MASTER dir (inherited)' 'Modify, Synchronize' { ((Get-Acl -LiteralPath "$fsRoot\template\MASTER").Access | Where-Object { $_.IdentityReference -like '*BMZ_FS_Drafting_RW' } | Select-Object -First 1).FileSystemRights.ToString() }
Add-Check $S 'App.config StandardDetails.MasterPath' "$fsRoot\template\MASTER\KOR-Standards-Master-R25.rvt" { $c = Get-Content (Join-Path $RepoRoot 'Kor.Operations.App\App.config') -Raw; [regex]::Match($c, 'key="StandardDetails\.MasterPath"\s+value="([^"]+)"').Groups[1].Value }
Add-Check $S 'App.config StandardDetails.BridgeRoot' '\\KOR-302N\C$\KOR.Drafter\bridge' { $c = Get-Content (Join-Path $RepoRoot 'Kor.Operations.App\App.config') -Raw; [regex]::Match($c, 'key="StandardDetails\.BridgeRoot"\s+value="([^"]+)"').Groups[1].Value }

# ---------------------------------------------------------------- Deploy state
$S = 'Deploy'
Add-Check $S 'newest zip on Newerforma share' 'V17.zip 2026-08-24' { $z = Get-ChildItem '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New' -Filter '*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; '{0} {1:yyyy-MM-dd}' -f $z.Name, $z.LastWriteTime }
Add-Check $S 'Jim KOR-204 Kor.Operations.App.exe' '2026-08-24 00:49' { $i = Get-Item -LiteralPath '\\KOR-204\C$\Newerforma\Kor.Operations.App.exe'; $i.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
Add-Check $S 'fleet KOR-Deploy 2026 version.txt' '1.0.0+2026-08-24T21:46:58 (git 72ffee6)' { (Get-Content '\\Kor-fs01\Drafting\KOR-Deploy\current\2026\version.txt' -Raw).Trim() }
Add-Check $S 'fleet KOR-Deploy 2025 version.txt' '1.0.0+2026-08-24T21:46:58 (git 72ffee6)' { (Get-Content '\\Kor-fs01\Drafting\KOR-Deploy\current\2025\version.txt' -Raw).Trim() }
Add-Check $S 'fleet 2026 KOR.RevitTools.dll contains DetailsPaletteCommand' 'False' { Test-DllContains '\\Kor-fs01\Drafting\KOR-Deploy\current\2026\KOR.RevitTools.dll' 'DetailsPaletteCommand' }
Add-Check $S '302N pilot KOR.RevitTools.dll contains DetailsPaletteCommand' 'True' { Test-DllContains '\\KOR-302N\C$\KOR\KOR-Deploy-Pilot\current\2026\KOR.RevitTools.dll' 'DetailsPaletteCommand' }
Add-Check $S '302N kor-deploy.path' 'C:\KOR\KOR-Deploy-Pilot' { (Get-Content '\\KOR-302N\C$\ProgramData\Autodesk\Revit\Addins\2026\KOR.Loader\kor-deploy.path' -Raw).Trim() }
Add-Check $S '302N kor-tools.json detailsPalette.templatePath' 'C:\KOR.Drafter\tasks\template\AUTHORING\KOR-Standards-Authoring-R25.rvt' { (Get-Content '\\KOR-302N\C$\ProgramData\KOR\kor-tools.json' -Raw | ConvertFrom-Json).detailsPalette.templatePath }
Add-Check $S '302N kor-tools.json showUnverified detailsPalette|quickInsert' 'False|False' { $j = Get-Content '\\KOR-302N\C$\ProgramData\KOR\kor-tools.json' -Raw | ConvertFrom-Json; "$($j.detailsPalette.showUnverified)|$($j.quickInsert.showUnverified)" }

# ---------------------------------------------------------------- Bridge on 302N
$S = 'Bridge'
Add-Check $S 'bridge dll 2026 FileVersion' '1.0.36.0' { (Get-Item '\\KOR-302N\C$\KOR.Drafter\app\artifacts\2026\KOR.Drafter.Bridge.dll').VersionInfo.FileVersion }
Add-Check $S 'bridge dll 2025 FileVersion' '1.0.31.0' { (Get-Item '\\KOR-302N\C$\KOR.Drafter\app\artifacts\2025\KOR.Drafter.Bridge.dll').VersionInfo.FileVersion }
Add-Check $S 'newest bridge log last line' '2026-09-04 14:34:26  Bridge down.' { $l = Get-ChildItem '\\KOR-302N\C$\KOR.Drafter\logs\bridge-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; (Get-Content $l.FullName -Tail 1) }
Add-Check $S 'inbox pending files|all ping' '3|True' { $f = @(Get-ChildItem '\\KOR-302N\C$\KOR.Drafter\bridge\inbox' -File); $allPing = ($f | ForEach-Object { (Get-Content $_.FullName -Raw) -match '"verb"\s*:\s*"ping"' }) -notcontains $false; "$($f.Count)|$allPing" }
if ($IncludeBridgeCounts) {
    Add-Check $S 'outbox files|MB (slow)' '94091|2959.6' { $o = Get-ChildItem '\\KOR-302N\C$\KOR.Drafter\bridge\outbox' -File; '{0}|{1:N1}' -f $o.Count, (($o | Measure-Object Length -Sum).Sum / 1MB) }
    Add-Check $S 'inbox\done files (slow)' '94136' { (Get-ChildItem '\\KOR-302N\C$\KOR.Drafter\bridge\inbox\done' -File | Measure-Object).Count }
} else {
    Add-Check $S 'outbox present (counts skipped; -IncludeBridgeCounts)' 'True' { Test-Path -LiteralPath '\\KOR-302N\C$\KOR.Drafter\bridge\outbox' }
}

# ---------------------------------------------------------------- Git
$S = 'Git'
# Remotes: Operations -> origin (GitHub). KOR.Drafter and KOR.RevitTools -> "backup", a bare repo on \\KOR-302N\C$.
# Counted against the NAMED remote branch, not @{u}: KOR.RevitTools main has no upstream, so @{u} silently reads 0.
Add-Check $S 'Operations branch|not on origin/develop|ahead of origin/main' 'develop|14|509' { (Invoke-Git $RepoRoot @('branch','--show-current')).Trim() + '|' + ((Invoke-Git $RepoRoot @('rev-list','--count','origin/develop..HEAD')).Trim()) + '|' + ((Invoke-Git $RepoRoot @('rev-list','--count','origin/main..HEAD')).Trim()) }
Add-Check $S 'KOR.Drafter branch|ahead of local main|not on backup/main|078 tracked' 'standard-details-rendered-image-store|5|16|False' { (Invoke-Git $DrafterRepo @('branch','--show-current')).Trim() + '|' + ((Invoke-Git $DrafterRepo @('rev-list','--count','main..HEAD')).Trim()) + '|' + ((Invoke-Git $DrafterRepo @('rev-list','--count','backup/main..HEAD')).Trim()) + '|' + ([bool]((Invoke-Git $DrafterRepo @('ls-files','db/078_RenderedPdf.sql')).Trim())) }
Add-Check $S 'KOR.Drafter 078_RenderedPdf.sql exists on disk' 'True' { Test-Path (Join-Path $DrafterRepo 'db\078_RenderedPdf.sql') }
Add-Check $S 'KOR.RevitTools HEAD|not on backup/main (review said 0: WRONG)' 'a6c8e38|2' { ((Invoke-Git $RevitToolsRepo @('rev-parse','--short=7','HEAD')).Trim()) + '|' + ((Invoke-Git $RevitToolsRepo @('rev-list','--count','backup/main..HEAD')).Trim()) }

# ---------------------------------------------------------------- Source facts
$S = 'Source'
$sd = Join-Path $RepoRoot 'Kor.Operations.App\StandardDetails'
Add-Check $S 'MasterPublisher File.Replace with backup=null|backup/archive mentions' 'True|0' { $t = Get-Content (Join-Path $sd 'MasterPublisher.cs') -Raw; ([bool]($t -match 'File\.Replace\([^)]*destinationBackupFileName:\s*null')).ToString() + '|' + ([regex]::Matches($t, '(?i)backup|archive').Count - [regex]::Matches($t, 'destinationBackupFileName').Count) }
Add-Check $S 'SheetComposerWindow still requires a sheet number' 'True' { [bool]((Get-Content (Join-Path $sd 'SheetComposerWindow.xaml.cs') -Raw) -match 'Enter a sheet number first') }
Add-Check $S 'composer sheet size constants 914.4|609.6 ; overflow logic' 'True|True|0' { $t = Get-Content (Join-Path $sd 'SheetComposerWindow.xaml.cs') -Raw; "$([bool]($t -match '914\.4'))|$([bool]($t -match '609\.6'))|$([regex]::Matches($t, '(?i)overflow|next sheet').Count)" }
Add-Check $S 'App.config Approvers|Publishers' 'ilalonde@korstructural.com|ilalonde@korstructural.com' { $c = Get-Content (Join-Path $RepoRoot 'Kor.Operations.App\App.config') -Raw; [regex]::Match($c, 'StandardDetailsApprovers\.Members"\s+value="([^"]+)"').Groups[1].Value + '|' + [regex]::Match($c, 'StandardDetailsPublishers\.Members"\s+value="([^"]+)"').Groups[1].Value }
# Counts test FILES in a *Tests*\StandardDetails\ folder (tests OF the module). A name-only grep also hits
# Kor.Operations.Architecture.Tests\ScopedViewTests.cs, which names the same classes as scene boxes.
# Review said 1|1. Post-fix 2026-09-07: + MasterPublisherPdfMatchingTests (7 facts) for the ship-blocker.
Add-Check $S 'test files in a *Tests*\StandardDetails\ folder|[Fact]s in them' '2|8' {
    $files = @(Get-ChildItem $RepoRoot, $DrafterRepo, $RevitToolsRepo -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '(?i)tests?[^\\]*\\StandardDetails\\' -and $_.FullName -notmatch '\\(bin|obj)\\' })
    $facts = 0; foreach ($f in $files) { $facts += ([regex]::Matches((Get-Content $f.FullName -Raw), '\[(Fact|Theory)')).Count }
    "$($files.Count)|$facts"
}
# Same measure as the review: wc -l over *.cs AND *.xaml (newline count). Review said 8458; the ship-blocker
# fix (MasterPublisher matcher, 2026-09-07) made it 8506. -Include needs the wildcard path or it returns nothing.
Add-Check $S 'StandardDetails .cs + .xaml line count (wc -l)' '8506' { (Get-ChildItem (Join-Path $sd '*') -Include '*.cs','*.xaml' -File | ForEach-Object { ([regex]::Matches([IO.File]::ReadAllText($_.FullName), "`n")).Count } | Measure-Object -Sum).Sum }
Add-Check $S 'census writers = migrations 005/005b/014/017/024 only (KOR.Drafter db)' '005_LoadDetailObservations.sql,005b_RepairAndCompleteLoad.sql,014_ReloadCensusV2.sql,017_ReloadCensusV3.sql,024_ReloadCensusV4.sql' { (Get-ChildItem (Join-Path $DrafterRepo 'db') -Filter '*.sql' | Where-Object { Select-String -Path $_.FullName -Pattern 'INSERT INTO detail\.DetailOccurrence' -Quiet } | Sort-Object Name | ForEach-Object { $_.Name }) -join ',' }

# ---------------------------------------------------------------- Report
$Rows | Format-Table -AutoSize -Wrap Section, Status, Claim, Expected, Actual | Out-String -Width 260 | Write-Output
$summary = $Rows | Group-Object Status | ForEach-Object { "$($_.Name)=$($_.Count)" }
Write-Output ("SUMMARY: {0} checks; {1}" -f $Rows.Count, ($summary -join ' '))
Write-Output "NOT CHECKED BY THIS SCRIPT: .rvt contents, bridge verb behaviour, the meeting transcript, any code defect."
