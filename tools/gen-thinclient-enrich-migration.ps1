param(
  [string]$Dir = "C:\VIsual Studio Projects\Operations\docs\enrichment-2026-06-20-thin-clients",
  [string[]]$Files,
  [string]$OutSql = "C:\VIsual Studio Projects\Operations\Kor.Opportunities.Data\Schema\246_ThinClientEnrichment.sql",
  [int]$Migration = 246,
  [string]$Provider = 'ThinClientEnrich2026-06-20'
)
$ErrorActionPreference='Stop'
$dir = $Dir
if(-not $Files){ $Files = @("$dir\agent-A.json","$dir\agent-B.json","$dir\agent-C.json") }
$files = $Files
$outSql = $OutSql
$validSrc = @('Hunter','asis','PatternInferred','website','news')
function Sql([string]$s){ if($null -eq $s){return ''}; return ($s -replace "'","''") }
function Clip([string]$s,[int]$n){ if($null -eq $s){return ''}; $s=$s.Trim(); if($s.Length -gt $n){return $s.Substring(0,$n)}; return $s }

$orgsMeta=@()   # OrgId, Website, ProfileNote
$rows=@()       # people
foreach($f in $files){
  if(-not (Test-Path $f)){ Write-Warning "MISSING $f"; continue }
  $j = Get-Content $f -Raw | ConvertFrom-Json
  foreach($o in $j.orgs){
    if(-not $o.identityConfirmed){ Write-Host "skip (unconfirmed) $($o.orgId) $($o.displayName)"; continue }
    $web = if([string]::IsNullOrWhiteSpace($o.website)){ $null } else { $o.website.Trim() }
    $note = if([string]::IsNullOrWhiteSpace($o.profileNote)){ $null } else { (Clip $o.profileNote 380) }
    if($web -or $note){ $orgsMeta += [pscustomobject]@{ OrgId=[int]$o.orgId; Website=$web; ProfileNote=$note } }
    foreach($p in $o.people){
      if([string]::IsNullOrWhiteSpace($p.name)){ continue }
      $email = if([string]::IsNullOrWhiteSpace($p.email)){ $null } else { $p.email.Trim() }
      $src2=$null; $conf=$null
      if($email){ $src2="$($p.emailSource)".Trim(); if($validSrc -notcontains $src2){$src2='asis'}; $conf=[int]$p.emailConfidence; if($conf -lt 0){$conf=0}; if($conf -gt 100){$conf=100} }
      $rows += [pscustomobject]@{ OrgId=[int]$o.orgId; Name=(Clip $p.name 200); Title=(Clip $p.title 200); Email=$email; Src=$src2; Conf=$conf; Note=(Clip $p.note 380) }
    }
  }
}
$orgIds = (($rows | Select-Object -ExpandProperty OrgId) + ($orgsMeta | Select-Object -ExpandProperty OrgId)) | Select-Object -Unique | Sort-Object
$sb=New-Object System.Text.StringBuilder
[void]$sb.AppendLine("USE [KorOpportunitiesDb];`r`nGO`r`nSET QUOTED_IDENTIFIER ON;`r`nGO`r`nSET XACT_ABORT ON;`r`nGO")
[void]$sb.AppendLine("/* Migration $($Migration): thin-client deep enrichment. Generated from")
[void]$sb.AppendLine("   docs/enrichment-2026-06-20-thin-clients agent-A/B/C JSON.")
[void]$sb.AppendLine("   $($rows.Count) contacts / $($orgsMeta.Count) org-meta updates / $($orgIds.Count) orgs. */")
[void]$sb.AppendLine("DECLARE @Provider nvarchar(60) = N'$Provider';")
[void]$sb.AppendLine("BEGIN TRAN;")
# --- org-level Website + Notes backfill (COALESCE: never clobber existing) ---
if($orgsMeta.Count -gt 0){
  [void]$sb.AppendLine("DECLARE @meta TABLE (OrgId bigint, Website nvarchar(400), ProfileNote nvarchar(400));")
  $mvals=@()
  foreach($m in $orgsMeta){ $w=if($m.Website){"N'$(Sql $m.Website)'"}else{'NULL'}; $n=if($m.ProfileNote){"N'$(Sql $m.ProfileNote)'"}else{'NULL'}; $mvals += "($($m.OrgId), $w, $n)" }
  [void]$sb.AppendLine("INSERT INTO @meta (OrgId,Website,ProfileNote) VALUES`r`n" + ($mvals -join ",`r`n") + ";")
  [void]$sb.AppendLine("UPDATE o SET o.Website=COALESCE(o.Website,m.Website), o.Notes=COALESCE(o.Notes,m.ProfileNote), o.UpdatedAtUtc=sysdatetimeoffset() FROM opportunities.CanonicalOrg o JOIN @meta m ON m.OrgId=o.Id;")
}
[void]$sb.AppendLine("DECLARE @orgs TABLE (OrgId bigint);")
[void]$sb.AppendLine("INSERT INTO @orgs VALUES " + (($orgIds | ForEach-Object {"($_)"}) -join ',') + ";")
[void]$sb.AppendLine("MERGE opportunities.CanonicalOrgEnrichment AS T USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider WHEN NOT MATCHED THEN INSERT (CanonicalOrgId,ProviderName,Status,Attempts,CreatedAtUtc,UpdatedAtUtc) VALUES (S.OrgId,@Provider,N'Manual',0,sysdatetimeoffset(),sysdatetimeoffset());")
if($rows.Count -gt 0){
  [void]$sb.AppendLine("DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));")
  $vals=@()
  foreach($r in $rows){
    $e=if($r.Email){"N'$(Sql $r.Email)'"}else{'NULL'}; $s=if($r.Src){"N'$(Sql $r.Src)'"}else{'NULL'}; $c=if($null -ne $r.Conf){"$($r.Conf)"}else{'NULL'}; $nt=if($r.Note){"N'$(Sql $r.Note)'"}else{'NULL'}
    $vals += "($($r.OrgId), N'$(Sql $r.Name)', N'$(Sql $r.Title)', $e, $s, $c, $nt)"
  }
  [void]$sb.AppendLine("INSERT INTO @people (OrgId,PersonName,Title,Email,Src,Conf,Note) VALUES`r`n" + ($vals -join ",`r`n") + ";")
  $strip="REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM({0}))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')"
  $pn=($strip -f 'p.PersonName'); $tn=($strip -f 'p.Title')
  [void]$sb.AppendLine(";WITH src AS (SELECT p.OrgId,p.PersonName,p.Title,p.Email,p.Src,p.Conf,p.Note,LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST($pn AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
  [void]$sb.AppendLine("MERGE opportunities.IntelPerson AS T USING src AS S ON T.NaturalKey=S.NK WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(),Corroborations=T.Corroborations+1,UpdatedAtUtc=sysdatetimeoffset(),Email=COALESCE(T.Email,S.Email),EmailSource=COALESCE(T.EmailSource,S.Src),EmailConfidence=COALESCE(T.EmailConfidence,S.Conf),Notes=COALESCE(T.Notes,S.Note)")
  [void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,DisplayName,NormalizedName,Corroborations,Email,EmailSource,EmailConfidence,EmailCheckedAtUtc,Notes) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonName,S.Lowered,1,S.Email,S.Src,S.Conf,CASE WHEN S.Email IS NULL THEN NULL ELSE sysdatetimeoffset() END,S.Note);")
  [void]$sb.AppendLine(";WITH aff AS (SELECT ip.Id AS PersonId,p.OrgId,p.Title,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',$tn) AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST($pn AS VARCHAR(8000))),2) JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
  [void]$sb.AppendLine("MERGE opportunities.IntelPersonAffiliation AS T USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title),IsCurrent=1,LastSeenAtUtc=sysdatetimeoffset(),UpdatedAtUtc=sysdatetimeoffset()")
  [void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,IntelPersonId,CanonicalOrgId,Title,IsCurrent) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonId,S.OrgId,S.Title,1);")
}
[void]$sb.AppendLine("PRINT 'Migration $($Migration): thin-client enrichment ($($rows.Count) contacts / $($orgsMeta.Count) org-meta).';")
[void]$sb.AppendLine("COMMIT TRAN;`r`nGO")
Set-Content -Path $outSql -Value $sb.ToString() -Encoding ASCII
"WROTE $outSql ; contacts=$($rows.Count) orgMeta=$($orgsMeta.Count) orgs=$($orgIds.Count)"
