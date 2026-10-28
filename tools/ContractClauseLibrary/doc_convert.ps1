# Windows PowerShell 5.1. Converts every legacy .doc (exact extension; -Filter *.doc also matches .docx
# through 8.3 names) under <Root>\pulled to text via Word COM, writing <Root>\ext-ocr\<sha12>.txt (same
# key as extract_clauses.py). Word cannot open paths over 260 characters ("couldn't find your file"), so
# each file is copied to a short temp path first.
param([Parameter(Mandatory)][string]$Root)
$out = Join-Path $Root 'ext-ocr'
$tmp = 'C:\Temp\kor-doc'
New-Item -ItemType Directory -Force $out | Out-Null
New-Item -ItemType Directory -Force $tmp | Out-Null
$w = New-Object -ComObject Word.Application
$w.Visible = $false
$w.DisplayAlerts = 0
$n = 0; $fail = 0; $errs = @()
$sha1 = [System.Security.Cryptography.SHA1]::Create()
$files = Get-ChildItem -LiteralPath (Join-Path $Root 'pulled') -Recurse -File | Where-Object { $_.Extension -ieq '.doc' }
"candidates=$($files.Count)"
foreach ($file in $files) {
  try {
    $id = [BitConverter]::ToString($sha1.ComputeHash([IO.File]::ReadAllBytes($file.FullName))).Replace('-', '').Substring(0, 12).ToLower()
    $short = Join-Path $tmp "$id.doc"
    Copy-Item -LiteralPath $file.FullName -Destination $short -Force
    $d = $w.Documents.Open($short, $false, $true, $false)
    if ($d -eq $null) {
      $pv = $w.ProtectedViewWindows.Open($short)
      if ($pv -ne $null) { $d = $pv.Edit() }
    }
    if ($d -eq $null) { throw "Word returned no document (count=$($w.Documents.Count), pv=$($w.ProtectedViewWindows.Count))" }
    $txt = $d.Content.Text
    $d.Close(0)
    [IO.File]::WriteAllText((Join-Path $out "$id.txt"), $txt, [Text.Encoding]::UTF8)
    $n++
  } catch {
    $fail++
    if ($errs.Count -lt 4) { $errs += ($file.Name + ' :: ' + $_.Exception.Message) }
  } finally {
    if (Test-Path $short) { Remove-Item $short -Force -ErrorAction SilentlyContinue }
  }
}
$w.Quit()
"converted=$n failed=$fail"
$errs
