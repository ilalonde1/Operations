$ErrorActionPreference='Stop'
$src = "C:\VIsual Studio Projects\Operations\docs\enrichment-2026-06-20-pm\thin-developers.json"
$outSql = "C:\VIsual Studio Projects\Operations\Kor.Opportunities.Data\Schema\238_PmThinDeveloperContacts.sql"
$exclude = @(54551)   # Landmark Premiere - DO NOT PURSUE (receivership)
$validSrc = @('Hunter','asis','PatternInferred')
function Sql([string]$s){ if($null -eq $s){return ''}; return ($s -replace "'","''") }
function Clip([string]$s,[int]$n){ if($null -eq $s){return ''}; $s=$s.Trim(); if($s.Length -gt $n){return $s.Substring(0,$n)}; return $s }

$j = Get-Content $src -Raw | ConvertFrom-Json
$rows=@()
foreach($o in $j.orgs){
  if($exclude -contains [int]$o.orgId){ continue }
  foreach($p in $o.people){
    if([string]::IsNullOrWhiteSpace($p.name)){ continue }
    $email = if([string]::IsNullOrWhiteSpace($p.email)){ $null } else { $p.email.Trim() }
    $src2=$null; $conf=$null
    if($email){ $src2="$($p.emailSource)".Trim(); if($validSrc -notcontains $src2){$src2='asis'}; $conf=[int]$p.emailConfidence; if($conf -lt 0){$conf=0}; if($conf -gt 100){$conf=100} }
    $rows += [pscustomobject]@{ OrgId=[int]$o.orgId; Name=(Clip $p.name 200); Title=(Clip $p.title 200); Email=$email; Src=$src2; Conf=$conf; Note=(Clip $p.note 380) }
  }
}
$orgIds = ($rows | Select-Object -ExpandProperty OrgId -Unique | Sort-Object)
$sb=New-Object System.Text.StringBuilder
[void]$sb.AppendLine("USE [KorOpportunitiesDb];`r`nGO`r`nSET QUOTED_IDENTIFIER ON;`r`nGO`r`nSET XACT_ABORT ON;`r`nGO")
[void]$sb.AppendLine("/* Migration 238: PM thin-developer contacts. Generated from")
[void]$sb.AppendLine("   the docs/enrichment-2026-06-20-pm thin-developers JSON. $($rows.Count) contacts / $($orgIds.Count) orgs.")
[void]$sb.AppendLine("   Excludes Landmark Premiere (54551, receivership / DO NOT PURSUE). */")
[void]$sb.AppendLine("DECLARE @Provider nvarchar(60) = N'PmEnrich2026-06-20';")
[void]$sb.AppendLine("BEGIN TRAN;")
[void]$sb.AppendLine("DECLARE @orgs TABLE (OrgId bigint);")
[void]$sb.AppendLine("INSERT INTO @orgs VALUES " + (($orgIds | ForEach-Object {"($_)"}) -join ',') + ";")
[void]$sb.AppendLine("MERGE opportunities.CanonicalOrgEnrichment AS T USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider WHEN NOT MATCHED THEN INSERT (CanonicalOrgId,ProviderName,Status,Attempts,CreatedAtUtc,UpdatedAtUtc) VALUES (S.OrgId,@Provider,N'Manual',0,sysdatetimeoffset(),sysdatetimeoffset());")
[void]$sb.AppendLine("DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));")
$vals=@()
foreach($r in $rows){
  $e=if($r.Email){"N'$(Sql $r.Email)'"}else{'NULL'}; $s=if($r.Src){"N'$(Sql $r.Src)'"}else{'NULL'}; $c=if($null -ne $r.Conf){"$($r.Conf)"}else{'NULL'}; $nt=if($r.Note){"N'$(Sql $r.Note)'"}else{'NULL'}
  $vals += "($($r.OrgId), N'$(Sql $r.Name)', N'$(Sql $r.Title)', $e, $s, $c, $nt)"
}
[void]$sb.AppendLine("INSERT INTO @people (OrgId,PersonName,Title,Email,Src,Conf,Note) VALUES`r`n" + ($vals -join ",`r`n") + ";")
$strip="REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM({0}))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')"
$pn=($strip -f 'p.PersonName'); $tn=($strip -f 'p.Title')
# Audit-v2 #12: emit the usp_ResolveOrCreateIntelPerson identity-anchor key
# (email first, else name|org:<orgId>) — NEVER the bare-name key the proc
# forbids and migration 255 spent a whole migration undoing.
$nk="CASE WHEN p.Email IS NOT NULL THEN CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(LOWER(LTRIM(RTRIM(p.Email))) AS VARCHAR(8000))),2) ELSE CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT($pn,'|org:',CAST(p.OrgId AS varchar(20))) AS VARCHAR(8000))),2) END"
[void]$sb.AppendLine(";WITH src AS (SELECT p.OrgId,p.PersonName,p.Title,p.Email,p.Src,p.Conf,p.Note,LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,e.Id AS EnrId,$nk AS NK FROM @people p JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
[void]$sb.AppendLine("MERGE opportunities.IntelPerson AS T USING src AS S ON T.NaturalKey=S.NK WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(),Corroborations=T.Corroborations+1,UpdatedAtUtc=sysdatetimeoffset(),Email=COALESCE(T.Email,S.Email),EmailSource=COALESCE(T.EmailSource,S.Src),EmailConfidence=COALESCE(T.EmailConfidence,S.Conf),Notes=COALESCE(T.Notes,S.Note)")
[void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,DisplayName,NormalizedName,Corroborations,Email,EmailSource,EmailConfidence,EmailCheckedAtUtc,Notes) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonName,S.Lowered,1,S.Email,S.Src,S.Conf,CASE WHEN S.Email IS NULL THEN NULL ELSE sysdatetimeoffset() END,S.Note);")
[void]$sb.AppendLine(";WITH aff AS (SELECT ip.Id AS PersonId,p.OrgId,p.Title,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',$tn) AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey=$nk JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)")
[void]$sb.AppendLine("MERGE opportunities.IntelPersonAffiliation AS T USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title),IsCurrent=1,LastSeenAtUtc=sysdatetimeoffset(),UpdatedAtUtc=sysdatetimeoffset()")
[void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,IntelPersonId,CanonicalOrgId,Title,IsCurrent) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonId,S.OrgId,S.Title,1);")
[void]$sb.AppendLine("PRINT 'Migration 238: PM thin-developer contacts ingested ($($rows.Count) / $($orgIds.Count) orgs).';")
[void]$sb.AppendLine("COMMIT TRAN;`r`nGO")
Set-Content -Path $outSql -Value $sb.ToString() -Encoding ASCII
"WROTE $outSql ; rows=$($rows.Count) orgs=$($orgIds.Count)"
