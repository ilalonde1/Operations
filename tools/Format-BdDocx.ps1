<#
.SYNOPSIS
  Render a BD markdown report to a professionally-formatted .docx.
  Pandoc builds the structure; Word COM applies real table styles
  (shaded header, banded rows), a clean type hierarchy, and KOR brand colour.

.EXAMPLE
  .\Format-BdDocx.ps1 -Md docs\report.md -Docx "$env:USERPROFILE\Desktop\Report.docx" -Toc
#>
param(
  [Parameter(Mandatory)][string]$Md,
  [Parameter(Mandatory)][string]$Docx,
  [switch]$Toc,
  [switch]$NoPageBreaks,   # one-pagers (agenda): don't force each section onto its own page
  [string]$TableStyle = "Grid Table 4"
)
$ErrorActionPreference = "Stop"

# ---- 1. pandoc: markdown -> docx structure ----
# Preprocess into a temp copy so tables ALWAYS render, regardless of source hygiene:
#  (1) insert a blank line before any pipe-table header that follows a non-blank line
#      (pandoc silently drops a table that isn't preceded by a blank line);
#  (2) -f markdown-tex_math_dollars treats every '$' as literal currency, never LaTeX
#      math (a '$' in a table HEADER otherwise opens a math span that swallows the
#      delimiter row and turns the whole table into a paragraph).
$src = [System.IO.File]::ReadAllLines($Md)
$pp = New-Object System.Collections.Generic.List[string]
foreach ($cur in $src) {
  $isTable = $cur.TrimStart().StartsWith("|")
  $isList  = $cur -match '^\s*([-*+]\s|\d+\.\s)'
  if ($isTable -or $isList) {
    $prev = if ($pp.Count -gt 0) { $pp[$pp.Count-1] } else { "" }
    $prevIsBlock = $prev.TrimStart().StartsWith("|") -or ($prev -match '^\s*([-*+]\s|\d+\.\s)')
    if ($prev.Trim() -ne "" -and -not $prevIsBlock) { $pp.Add("") }
  }
  $pp.Add($cur)
}
$tmpMd = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "bd-fmt-" + [System.IO.Path]::GetRandomFileName() + ".md")
[System.IO.File]::WriteAllLines($tmpMd, $pp, (New-Object System.Text.UTF8Encoding $false))
$pandocArgs = @($tmpMd, "-f", "markdown-tex_math_dollars", "-o", $Docx)
if ($Toc) { $pandocArgs += @("--toc", "--toc-depth=2") }
& pandoc @pandocArgs
[System.IO.File]::Delete($tmpMd)
if (-not (Test-Path $Docx)) { throw "pandoc did not produce $Docx" }

# ---- 2. Word COM: apply professional styling ----
function RGB($r,$g,$b){ return [int]($r + ($g * 256) + ($b * 65536)) }   # WdColor = 0x00BBGGRR
$brandNavy = RGB 31 59 95      # headings
$brandSlate = RGB 68 84 106    # subheads

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  $doc = $word.Documents.Open((Resolve-Path $Docx).Path)

  # base body type
  $normal = $doc.Styles.Item("Normal")
  $normal.Font.Name = "Calibri"
  $normal.Font.Size = 10.5
  $normal.ParagraphFormat.LineSpacingRule = 0      # wdLineSpaceSingle
  $normal.ParagraphFormat.SpaceAfter = 6
  $normal.ParagraphFormat.SpaceBefore = 0

  # heading hierarchy (brand colour, tighter spacing above)
  foreach ($h in @(@("Title",20,$brandNavy),@("Heading 1",15,$brandNavy),@("Heading 2",12.5,$brandNavy),@("Heading 3",11,$brandSlate))) {
    try {
      $s = $doc.Styles.Item($h[0])
      $s.Font.Name = "Calibri"
      $s.Font.Size = $h[1]
      $s.Font.Bold = $true
      $s.Font.Color = $h[2]
      $s.ParagraphFormat.SpaceBefore = 12
      $s.ParagraphFormat.SpaceAfter = 4
      $s.ParagraphFormat.KeepWithNext = $true
      # each major section (## -> Heading 2) starts on a new page
      $s.ParagraphFormat.PageBreakBefore = ($h[0] -eq "Heading 2")
    } catch {}
  }

  # every table -> styled, header shaded, rows banded, full width
  for ($i = 1; $i -le $doc.Tables.Count; $i++) {
    $t = $doc.Tables.Item($i)
    # NOTE: Word style names use " - Accent N" (hyphen with spaces). Names without
    # the spaced hyphen silently fail to match, leaving tables unstyled. All
    # candidates below are verified built-in names.
    $applied = $false
    foreach ($cand in @($TableStyle, "Grid Table 4 - Accent 1", "List Table 6 Colorful - Accent 1", "Grid Table 4 - Accent 5")) {
      try { $t.Style = $cand; $applied = $true; break } catch {}
    }
    if (-not $applied) { Write-Warning "No table style matched for table $i (tried '$TableStyle' + fallbacks)." }
    $t.ApplyStyleHeadingRows = $true
    $t.ApplyStyleLastRow      = $false
    $t.ApplyStyleFirstColumn  = $false
    $t.ApplyStyleLastColumn   = $false
    $t.ApplyStyleRowBands     = $true
    $t.ApplyStyleColumnBands  = $false
    try { $t.AutoFitBehavior(2) } catch {}          # wdAutoFitWindow
    $t.Range.Font.Size = 9.5
    $t.Rows.Item(1).Range.Font.Bold = $true
    # repeat the header row at the top of every continuation page + don't split rows mid-cell
    try { $t.Rows.AllowBreakAcrossPages = $false } catch {}
    try { $t.Rows.Item(1).HeadingFormat = $true } catch {}
    try { $t.Rows.Item(1).AllowBreakAcrossPages = $false } catch {}
    $t.TopPadding = 2; $t.BottomPadding = 2; $t.LeftPadding = 5; $t.RightPadding = 5
  }

  # blockquotes (pandoc style "Block Text") -> branded shaded callout box:
  # light navy tint + thin navy border all around + inset. Makes ">" lines pop
  # as a summary/callout instead of an understated left-bar quote.
  $calloutTint = RGB 226 235 246
  foreach ($p in $doc.Paragraphs) {
    try {
      if ($p.Style.NameLocal -eq "Block Text") {
        $r = $p.Range
        $r.Shading.BackgroundPatternColor = $calloutTint
        foreach ($side in @(-1,-2,-3,-4)) {   # top, left, bottom, right
          $e = $r.Borders.Item($side); $e.LineStyle = 1; $e.Color = $brandNavy; $e.LineWidth = 8
        }
        $p.Format.LeftIndent = 10; $p.Format.RightIndent = 10
        $p.Format.SpaceBefore = 6; $p.Format.SpaceAfter = 6
      }
    } catch {}
  }

  # one-pager mode: tighten type, spacing and margins to fit a single page
  if ($NoPageBreaks) {
    $normal.Font.Size = 9; $normal.ParagraphFormat.SpaceAfter = 2; $normal.ParagraphFormat.SpaceBefore = 0
    foreach ($hn in @(@("Title",13),@("Heading 1",12),@("Heading 2",10.5),@("Heading 3",9.5))) {
      try { $s = $doc.Styles.Item($hn[0]); $s.Font.Size = $hn[1]; $s.ParagraphFormat.SpaceBefore = 5; $s.ParagraphFormat.SpaceAfter = 1 } catch {}
    }
    try { $doc.PageSetup.TopMargin = 43; $doc.PageSetup.BottomMargin = 43; $doc.PageSetup.LeftMargin = 50; $doc.PageSetup.RightMargin = 50 } catch {}
    for ($i = 1; $i -le $doc.Tables.Count; $i++) { try { $doc.Tables.Item($i).Range.Font.Size = 8.5 } catch {} }
  }

  # page break before EVERY major section (Heading 2) so each starts its own page,
  # and before the Table of Contents so it gets its own page after the title block.
  # Skipped for one-pagers (-NoPageBreaks), e.g. the meeting agenda run-sheet.
  if (-not $NoPageBreaks) {
    $tocBroken = $false
    foreach ($para in $doc.Paragraphs) {
      try {
        $sn = $para.Style.NameLocal
        if ($sn -eq "Heading 2") { $para.Format.PageBreakBefore = $true }
        elseif (-not $tocBroken -and $sn -like "TOC*") { $para.Format.PageBreakBefore = $true; $tocBroken = $true }
      } catch {}
    }
  }

  $tableCount = $doc.Tables.Count
  $doc.Save()
  $doc.Close()
  Write-Output ("FORMATTED: {0}  ({1} tables)" -f (Split-Path $Docx -Leaf), $tableCount)
}
finally {
  $word.Quit()
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
  [GC]::Collect(); [GC]::WaitForPendingFinalizers()
}
