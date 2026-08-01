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
$brandNavy  = RGB 31 59 95     # headings / masthead
$brandSlate = RGB 68 84 106    # subheads
$inkSoft    = RGB 89 102 116   # meta / captions
$ruleLight  = RGB 209 217 226  # hairline rules inside tables
$calloutTint = RGB 232 238 246 # callout ground

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  $doc = $word.Documents.Open((Resolve-Path $Docx).Path)

  # ---- typography: editorial pairing instead of all-Calibri ----
  # Georgia body (reads like a briefing), Segoe UI for headings/tables/meta.
  $normal = $doc.Styles.Item("Normal")
  $normal.Font.Name = "Georgia"
  $normal.Font.Size = 10
  $normal.ParagraphFormat.LineSpacingRule = 5       # wdLineSpaceMultiple
  $normal.ParagraphFormat.LineSpacing = 13.2        # ~1.1 lines: airy but compact
  $normal.ParagraphFormat.SpaceAfter = 7
  $normal.ParagraphFormat.SpaceBefore = 0

  # heading hierarchy: Segoe UI Semibold feel, brand colour, generous air above
  foreach ($h in @(@("Title",23,$brandNavy),@("Heading 1",22,$brandNavy),@("Heading 2",13.5,$brandNavy),@("Heading 3",11,$brandSlate))) {
    try {
      $s = $doc.Styles.Item($h[0])
      $s.Font.Name = "Segoe UI Semibold"
      $s.Font.Size = $h[1]
      $s.Font.Bold = ($h[1] -lt 20)   # big masthead sizes carry weight already
      $s.Font.Color = $h[2]
      $s.ParagraphFormat.SpaceBefore = 16
      $s.ParagraphFormat.SpaceAfter = 5
      $s.ParagraphFormat.KeepWithNext = $true
      $s.ParagraphFormat.PageBreakBefore = ($h[0] -eq "Heading 2")
    } catch {}
  }

  # masthead: the doc title gets a strong brand rule beneath it (like the web pages)
  foreach ($p in $doc.Paragraphs) {
    try {
      $sn = $p.Style.NameLocal
      if ($sn -eq "Title" -or $sn -eq "Heading 1") {
        $e = $p.Range.Borders.Item(-3)   # bottom
        $e.LineStyle = 1; $e.Color = $brandNavy; $e.LineWidth = 18   # 2.25pt
        $p.Format.SpaceAfter = 10
        # the bold meta line right under the title reads as the sub-masthead
        $nxt = $p.Range.Next(4, 1)       # wdParagraph
        if ($nxt) {
          $nxt.Font.Name = "Segoe UI"; $nxt.Font.Size = 8.5
          $nxt.Font.Color = $inkSoft; $nxt.Font.Bold = $false
          $nxt.Font.AllCaps = $false
        }
        break
      }
    } catch {}
  }

  # ---- tables: editorial rules, not grids ----
  # No vertical lines, no heavy boxes: a 1.5pt brand rule under the header row,
  # hairline separators between rows, generous padding — the web-page table look.
  for ($i = 1; $i -le $doc.Tables.Count; $i++) {
    $t = $doc.Tables.Item($i)
    try { $t.Style = "Table Grid" } catch {}
    # clear everything, then draw only what we want
    foreach ($side in @(-1,-2,-3,-4,-5,-6)) { try { $t.Borders.Item($side).LineStyle = 0 } catch {} }
    try {
      $t.Borders.Item(-1).LineStyle = 1; $t.Borders.Item(-1).Color = $brandNavy; $t.Borders.Item(-1).LineWidth = 8     # top rule 1pt
      $t.Borders.Item(-3).LineStyle = 1; $t.Borders.Item(-3).Color = $ruleLight; $t.Borders.Item(-3).LineWidth = 4     # bottom hairline
      $t.Borders.Item(-5).LineStyle = 1; $t.Borders.Item(-5).Color = $ruleLight; $t.Borders.Item(-5).LineWidth = 2     # row separators 0.25pt
    } catch {}
    try {
      $hdrB = $t.Rows.Item(1).Borders.Item(-3)
      $hdrB.LineStyle = 1; $hdrB.Color = $brandNavy; $hdrB.LineWidth = 12    # 1.5pt header rule
    } catch {}
    $t.Range.Font.Name = "Segoe UI"
    $t.Range.Font.Size = 9
    $hdr = $t.Rows.Item(1).Range
    $hdr.Font.Bold = $true
    $hdr.Font.Size = 8
    $hdr.Font.AllCaps = $true
    $hdr.Font.Color = $brandNavy
    try { $t.AutoFitBehavior(2) } catch {}          # wdAutoFitWindow
    try { $t.Rows.AllowBreakAcrossPages = $false } catch {}
    try { $t.Rows.Item(1).HeadingFormat = $true } catch {}
    try { $t.Rows.Item(1).AllowBreakAcrossPages = $false } catch {}
    $t.TopPadding = 4; $t.BottomPadding = 4; $t.LeftPadding = 6; $t.RightPadding = 6
    try { $t.Spacing = 0 } catch {}
  }

  # blockquotes -> left-accent callout (thick brand bar + soft tint, no full box)
  foreach ($p in $doc.Paragraphs) {
    try {
      if ($p.Style.NameLocal -eq "Block Text") {
        $r = $p.Range
        $r.Shading.BackgroundPatternColor = $calloutTint
        foreach ($side in @(-1,-3,-4)) { try { $r.Borders.Item($side).LineStyle = 0 } catch {} }
        $e = $r.Borders.Item(-2); $e.LineStyle = 1; $e.Color = $brandNavy; $e.LineWidth = 24   # 3pt left bar
        $p.Format.LeftIndent = 12; $p.Format.RightIndent = 10
        $p.Format.SpaceBefore = 8; $p.Format.SpaceAfter = 8
        $r.Font.Name = "Segoe UI"; $r.Font.Size = 10
      }
    } catch {}
  }

  # ---- footer: brand rule + doc identity + page numbers (every page) ----
  try {
    $sec = $doc.Sections.Item(1)
    $ftr = $sec.Footers.Item(1)                      # wdHeaderFooterPrimary
    $fr = $ftr.Range
    $fr.Text = ""
    $fr.Font.Name = "Segoe UI"; $fr.Font.Size = 7.5; $fr.Font.Color = $inkSoft
    $docTitle = [System.IO.Path]::GetFileNameWithoutExtension($Docx) -replace '-', ' '
    $fr.Text = "KOR Structural  ·  $docTitle" + "`t`t"
    $fr.Collapse(0)                                  # wdCollapseEnd
    $fr.Fields.Add($fr, 33) | Out-Null               # wdFieldPage
    $fr = $ftr.Range
    $fr.InsertAfter(" of ")
    $fr.Collapse(0)
    $fr.Fields.Add($fr, 26) | Out-Null               # wdFieldNumPages
    $fp = $ftr.Range.Paragraphs.Item(1)
    $e = $fp.Range.Borders.Item(-1)                  # top rule above footer
    $e.LineStyle = 1; $e.Color = $ruleLight; $e.LineWidth = 4
  } catch {}

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
