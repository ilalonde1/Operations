$ErrorActionPreference='Stop'
$dir = "C:\VIsual Studio Projects\Operations\docs\overnight-enrichment-2026-06-20"
$outSql = "C:\VIsual Studio Projects\Operations\Kor.Opportunities.Data\Schema\234_OvernightEnrichmentIngest.sql"
$exclude = @(15548, 53419, 54300)   # SMP (reclassify), Keyara (no footprint), Hotson Bakker (defunct->DIALOG)
$validSrc = @('Hunter','asis','PatternInferred')

function Sql([string]$s){ if($null -eq $s){ return '' }; return ($s -replace "'","''") }
function Clip([string]$s,[int]$n){ if($null -eq $s){ return '' }; $s=$s.Trim(); if($s.Length -gt $n){ return $s.Substring(0,$n) }; return $s }

$rows = @()
foreach($cat in 'architects','competitors','developers','buyers'){
  $j = Get-Content (Join-Path $dir "$cat.json") -Raw | ConvertFrom-Json
  foreach($o in $j.orgs){
    if($exclude -contains [int]$o.orgId){ continue }
    foreach($p in $o.people){
      if([string]::IsNullOrWhiteSpace($p.name)){ continue }
      $email = if([string]::IsNullOrWhiteSpace($p.email)){ $null } else { $p.email.Trim() }
      $src = $null; $conf = $null
      if($email){
        $src = "$($p.emailSource)".Trim()
        if($validSrc -notcontains $src){ $src = 'asis' }   # map WebSearch/web/legacy -> asis
        $conf = [int]$p.emailConfidence; if($conf -lt 0){ $conf=0 }; if($conf -gt 100){ $conf=100 }
      }
      $rows += [pscustomobject]@{
        OrgId=[int]$o.orgId; Name=(Clip $p.name 200); Title=(Clip $p.title 200);
        Email=$email; Src=$src; Conf=$conf; Note=(Clip $p.note 380)
      }
    }
  }
}

$orgIds = ($rows | Select-Object -ExpandProperty OrgId -Unique | Sort-Object)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("USE [KorOpportunitiesDb];")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("SET QUOTED_IDENTIFIER ON;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("SET XACT_ABORT ON;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("/* Migration 234: ingest overnight enrichment (2026-06-20). Generated from")
[void]$sb.AppendLine("   the four overnight-enrichment-2026-06-20 JSON files by gen-overnight-enrichment-migration.ps1.")
[void]$sb.AppendLine("   $($rows.Count) contacts across $($orgIds.Count) orgs. Excludes SMP(15548, reclassify),")
[void]$sb.AppendLine("   Keyara(53419, no footprint), Hotson Bakker(54300, defunct->DIALOG). */")
[void]$sb.AppendLine("DECLARE @Provider nvarchar(60) = N'OvernightEnrich2026-06-20';")
[void]$sb.AppendLine("BEGIN TRAN;")
# enrichment anchors
[void]$sb.AppendLine("DECLARE @orgs TABLE (OrgId bigint);")
[void]$sb.AppendLine("INSERT INTO @orgs VALUES " + (($orgIds | ForEach-Object { "($_)" }) -join ',') + ";")
[void]$sb.AppendLine("MERGE opportunities.CanonicalOrgEnrichment AS T USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider")
[void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc) VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());")
# people table
[void]$sb.AppendLine("DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));")
$vals = @()
foreach($r in $rows){
  $emailSql = if($r.Email){ "N'$(Sql $r.Email)'" } else { 'NULL' }
  $srcSql   = if($r.Src){ "N'$(Sql $r.Src)'" } else { 'NULL' }
  $confSql  = if($null -ne $r.Conf){ "$($r.Conf)" } else { 'NULL' }
  $noteSql  = if($r.Note){ "N'$(Sql $r.Note)'" } else { 'NULL' }
  $vals += "($($r.OrgId), N'$(Sql $r.Name)', N'$(Sql $r.Title)', $emailSql, $srcSql, $confSql, $noteSql)"
}
# chunk INSERT VALUES into batches of 500
for($i=0; $i -lt $vals.Count; $i+=500){
  $chunk = $vals[$i..([Math]::Min($i+499,$vals.Count-1))]
  [void]$sb.AppendLine("INSERT INTO @people (OrgId,PersonName,Title,Email,Src,Conf,Note) VALUES")
  [void]$sb.AppendLine(($chunk -join ",`r`n") + ";")
}
# strip expression reused
$strip = "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM({0}))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')"
$pnStrip = ($strip -f 'p.PersonName')
$tStrip  = ($strip -f 'p.Title')
# IntelPerson MERGE
[void]$sb.AppendLine(";WITH src AS (SELECT p.OrgId, p.PersonName, p.Title, p.Email, p.Src, p.Conf, p.Note, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered, e.Id AS EnrId, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST($pnStrip AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
[void]$sb.AppendLine("MERGE opportunities.IntelPerson AS T USING src AS S ON T.NaturalKey=S.NK")
[void]$sb.AppendLine("WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset(), Email=COALESCE(T.Email,S.Email), EmailSource=COALESCE(T.EmailSource,S.Src), EmailConfidence=COALESCE(T.EmailConfidence,S.Conf), Notes=COALESCE(T.Notes,S.Note)")
[void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations, Email, EmailSource, EmailConfidence, EmailCheckedAtUtc, Notes)")
[void]$sb.AppendLine("VALUES (@Provider, S.EnrId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1, S.Email, S.Src, S.Conf, CASE WHEN S.Email IS NULL THEN NULL ELSE sysdatetimeoffset() END, S.Note);")
# Affiliation MERGE
[void]$sb.AppendLine(";WITH aff AS (SELECT ip.Id AS PersonId, p.OrgId, p.Title, e.Id AS EnrId, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',$tStrip) AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST($pnStrip AS VARCHAR(8000))),2) JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
[void]$sb.AppendLine("MERGE opportunities.IntelPersonAffiliation AS T USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId")
[void]$sb.AppendLine("WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()")
[void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)")
[void]$sb.AppendLine("VALUES (@Provider, S.EnrId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, S.OrgId, S.Title, 1);")
[void]$sb.AppendLine("PRINT 'Migration 234: overnight enrichment ingested ($($rows.Count) contacts / $($orgIds.Count) orgs).';")
[void]$sb.AppendLine("COMMIT TRAN;")
[void]$sb.AppendLine("GO")

Set-Content -Path $outSql -Value $sb.ToString() -Encoding ASCII
"WROTE $outSql"
"rows=$($rows.Count) orgs=$($orgIds.Count)"
