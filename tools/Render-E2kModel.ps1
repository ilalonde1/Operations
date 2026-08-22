param(
    [Parameter(Mandatory = $true)][string]$E2k,
    [Parameter(Mandatory = $true)][string]$OutPng,
    [string]$Title = '',
    # One storey only. Looking at a whole 23-storey stack in plan tells you nothing about which
    # panel on which floor is wrong; the engineer marks up one level at a time and so must this.
    [string]$Storey = ''
)

# Draws an ETABS .e2k the way ETABS will build it, so the model can be looked at rather than
# only measured. Joints carry plan position only; a member's elevation comes from the storeys
# its ASSIGN lines name. Generated members (names starting K) are coloured, the reference grey.

$lines = Get-Content -LiteralPath $E2k

# --- storeys: same rule as E2kDocument.ReadStories, including the site-model tower rule -------
$sec = ''; $parsed = @(); $baseElev = 0.0
foreach ($ln in $lines) {
    if ($ln.StartsWith('$')) { $sec = $ln; continue }
    if ($sec -notlike '*STORIES*') { continue }
    $t = $ln.Trim()
    if ($t -match '^STORY\s+"([^"]+)"\s+ELEV\s+(\S+)') { $baseElev = [double]$matches[2]; continue }
    if ($t -match '^STORY\s+"([^"]+)"\s+HEIGHT\s+(\S+)') { $parsed += [pscustomobject]@{ N = $matches[1]; H = [double]$matches[2] } }
}
[array]::Reverse($parsed)
$stack = @(); $e = $baseElev
foreach ($s in $parsed) { $e += $s.H; $stack += [pscustomobject]@{ N = $s.N; Top = $e; H = $s.H } }

function TagOf($n) { if ($n.Length -gt 2 -and [char]::IsLetter($n[0]) -and $n[1] -eq '-') { return $n.Substring(0,1).ToUpper() } return '' }

$MAXH = 480.0
$story = @{}
for ($i = 0; $i -lt $stack.Count; $i++) {
    $cur = $stack[$i]; $tag = TagOf $cur.N; $below = $null
    if ($tag -ne '') {
        for ($j = $i - 1; $j -ge 0; $j--) { if ((TagOf $stack[$j].N) -eq $tag) { $below = $stack[$j].Top; break } }
    } else {
        for ($j = $i - 1; $j -ge 0; $j--) { if (($cur.Top - $stack[$j].Top) -gt 12.0) { $below = $stack[$j].Top; break } }
    }
    if ($null -eq $below) { $below = $cur.Top - [Math]::Min($cur.H, $MAXH) }
    else { $below = [Math]::Max($below, $cur.Top - $MAXH) }
    $story[$cur.N] = [pscustomobject]@{ Top = $cur.Top / 12; Bot = $below / 12 }
}

# --- plan joints ------------------------------------------------------------------------------
$px = @{}; $py = @{}; $sec = ''
foreach ($ln in $lines) {
    if ($ln.StartsWith('$')) { $sec = $ln; continue }
    if ($sec -notlike '*POINT COORD*') { continue }
    if ($ln.Trim() -match '^POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)') {
        $px[$matches[1]] = [double]$matches[2] / 12
        $py[$matches[1]] = [double]$matches[3] / 12
    }
}

# --- which storeys each object is assigned to --------------------------------------------------
$assign = @{}
foreach ($ln in $lines) {
    if ($ln.Trim() -match '^(AREAASSIGN|LINEASSIGN)\s+"([^"]+)"\s+"([^"]+)"') {
        $o = $matches[2]; $s = $matches[3]
        if (-not $assign.ContainsKey($o)) { $assign[$o] = New-Object System.Collections.ArrayList }
        if ($story.ContainsKey($s) -and -not $assign[$o].Contains($s)) { [void]$assign[$o].Add($s) }
    }
}
# ETABS builds one instance of an object per storey it is assigned to, and each instance runs
# from the storey immediately below it in the model's own list. An object on many storeys is
# therefore repeated, not stretched — Andrea's columns are assigned to all 19 of hers.
$ordered = @($stack | Sort-Object Top)
$globalBot = @{}
for ($i = 0; $i -lt $ordered.Count; $i++) {
    $n = $ordered[$i].N
    $globalBot[$n] = if ($i -eq 0) { $story[$n].Bot } else { $story[$ordered[$i - 1].N].Top }
}
# An object also carries a storey SPAN in its own connectivity line: a wall panel's corner
# offsets and a column's trailing count. A span of 1 reaches the storey immediately below; N
# reaches N storeys down, passing through the ones between without a break. That is how a tower
# in a site model keeps its walls whole across the other tower's floor levels, so the renderer
# has to honour it or it draws a continuous wall as a stack of separate ones.
$span = @{}
foreach ($ln in $lines) {
    $t = $ln.Trim()
    if ($t -match '^AREA\s+"([^"]+)"\s+PANEL\s+4\s+(?:"[^"]+"\s+){4}(\d+)') { $span[$matches[1]] = [int]$matches[2] }
    elseif ($t -match '^LINE\s+"([^"]+)"\s+COLUMN\s+"[^"]+"\s+"[^"]+"\s+(\d+)') { $span[$matches[1]] = [int]$matches[2] }
}
$indexOf = @{}
for ($i = 0; $i -lt $ordered.Count; $i++) { $indexOf[$ordered[$i].N] = $i }

function Instances($name) {
    if (-not $assign.ContainsKey($name) -or $assign[$name].Count -eq 0) { return @() }
    $n = if ($span.ContainsKey($name) -and $span[$name] -gt 0) { $span[$name] } else { 1 }
    $out = New-Object System.Collections.ArrayList
    foreach ($s in $assign[$name]) {
        if ($Storey -ne '' -and $s -ne $Storey) { continue }
        $i = $indexOf[$s]
        $bot = if ($null -ne $i -and ($i - $n) -ge 0) { $story[$ordered[$i - $n].N].Top } else { $globalBot[$s] }
        [void]$out.Add([pscustomobject]@{ Bot = $bot; Top = $story[$s].Top })
    }
    return $out
}

# --- members -----------------------------------------------------------------------------------
$walls = New-Object System.Collections.ArrayList
$floors = New-Object System.Collections.ArrayList
$cols = New-Object System.Collections.ArrayList

foreach ($ln in $lines) {
    if ($ln.Trim() -match '^AREA\s+"([^"]+)"\s+(PANEL|FLOOR)\s+\d+\s+(.+)$') {
        $nm = $matches[1]; $kind = $matches[2]
        $ids = @([regex]::Matches($matches[3], '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $px.ContainsKey($_) })
        foreach ($ex in (Instances $nm)) {
            if ($kind -eq 'PANEL') {
                $u = @(); foreach ($i in $ids) { if ($u -notcontains $i) { $u += $i } }
                if ($u.Count -lt 2) { continue }
                [void]$walls.Add([pscustomobject]@{ A = $u[0]; B = $u[1]; Bot = $ex.Bot; Top = $ex.Top; Mine = $nm.StartsWith('K') })
            } else {
                if ($ids.Count -lt 3) { continue }
                [void]$floors.Add([pscustomobject]@{ Ids = $ids; Z = $ex.Top; Mine = $nm.StartsWith('K') })
            }
        }
        continue
    }
    if ($ln.Trim() -match '^LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"\s+"([^"]+)"') {
        $nm = $matches[1]
        if (-not $px.ContainsKey($matches[2])) { continue }
        foreach ($ex in (Instances $nm)) {
            [void]$cols.Add([pscustomobject]@{ A = $matches[2]; B = $matches[3]; Bot = $ex.Bot; Top = $ex.Top; Mine = $nm.StartsWith('K') })
        }
    }
}

function P3([double]$x, [double]$y, [double]$z, $view) {
    switch ($view) {
        'iso' { return @((($x - $y) * 0.866), (-(($x + $y) * 0.5) + $z * 1.0)) }
        'ex'  { return @($x, $z) }
        'ey'  { return @($y, $z) }
        'plan' { return @($x, $y) }
    }
}

function BuildSvg($view, $w, $h, $label) {
    $pts = New-Object System.Collections.ArrayList
    foreach ($o in $walls) { foreach ($id in @($o.A, $o.B)) { foreach ($z in @($o.Bot, $o.Top)) { [void]$pts.Add((P3 $px[$id] $py[$id] $z $view)) } } }
    foreach ($o in $cols) { foreach ($z in @($o.Bot, $o.Top)) { [void]$pts.Add((P3 $px[$o.A] $py[$o.A] $z $view)) } }
    foreach ($o in $floors) { foreach ($id in $o.Ids) { [void]$pts.Add((P3 $px[$id] $py[$id] $o.Z $view)) } }
    if ($pts.Count -eq 0) { return "<svg width='$w' height='$h'></svg>" }

    $minX = ($pts | ForEach-Object { $_[0] } | Measure-Object -Minimum).Minimum
    $maxX = ($pts | ForEach-Object { $_[0] } | Measure-Object -Maximum).Maximum
    $minY = ($pts | ForEach-Object { $_[1] } | Measure-Object -Minimum).Minimum
    $maxY = ($pts | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum

    $pad = 28
    $s = [Math]::Min(($w - 2 * $pad) / [Math]::Max(1e-6, $maxX - $minX), ($h - 2 * $pad) / [Math]::Max(1e-6, $maxY - $minY))
    $ox = $pad + (($w - 2 * $pad) - ($maxX - $minX) * $s) / 2
    $oy = $pad + (($h - 2 * $pad) - ($maxY - $minY) * $s) / 2
    function Pt($p) { '{0:F1},{1:F1}' -f ($ox + ($p[0] - $minX) * $s), ($h - ($oy + ($p[1] - $minY) * $s)) }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("<svg width='$w' height='$h' viewBox='0 0 $w $h'><rect width='$w' height='$h' fill='#fff'/>")

    foreach ($f in $floors) {
        $d = (@($f.Ids | ForEach-Object { Pt (P3 $px[$_] $py[$_] $f.Z $view) })) -join ' '
        $fill = if ($f.Mine) { '#8f9daa' } else { '#dfe4e9' }
        [void]$sb.Append("<polygon points='$d' fill='$fill' fill-opacity='0.40' stroke='#5b6874' stroke-width='0.45'/>")
    }
    foreach ($c in $cols) {
        $a = (Pt (P3 $px[$c.A] $py[$c.A] $c.Bot $view)) -split ','
        $b = (Pt (P3 $px[$c.A] $py[$c.A] $c.Top $view)) -split ','
        $col = if ($c.Mine) { '#2f6fb0' } else { '#b8c2cc' }
        [void]$sb.Append("<line x1='$($a[0])' y1='$($a[1])' x2='$($b[0])' y2='$($b[1])' stroke='$col' stroke-width='1'/>")
    }
    foreach ($wl in $walls) {
        $c1 = Pt (P3 $px[$wl.A] $py[$wl.A] $wl.Top $view)
        $c2 = Pt (P3 $px[$wl.B] $py[$wl.B] $wl.Top $view)
        $c3 = Pt (P3 $px[$wl.B] $py[$wl.B] $wl.Bot $view)
        $c4 = Pt (P3 $px[$wl.A] $py[$wl.A] $wl.Bot $view)
        $fill = if ($wl.Mine) { '#a83232' } else { '#c9a0a0' }
        [void]$sb.Append("<polygon points='$c1 $c2 $c3 $c4' fill='$fill' fill-opacity='0.55' stroke='#7a2230' stroke-width='0.45'/>")
    }

    [void]$sb.Append("<text x='14' y='22' font-family='Segoe UI' font-size='15' font-weight='600' fill='#333'>$label</text></svg>")
    return $sb.ToString()
}

$iso = BuildSvg 'iso' 900 900 'Isometric'
$ex = BuildSvg 'ex' 900 900 'Elevation - looking along Y'
$ey = BuildSvg 'ey' 900 900 'Elevation - looking along X'
$pl = BuildSvg 'plan' 900 900 'Plan - all storeys'

$html = @"
<!doctype html><html><head><meta charset='utf-8'><style>
body{margin:0;background:#fff;font-family:Segoe UI,Arial,sans-serif}
h1{font-size:17px;margin:10px 14px 4px;color:#7a2230}
.g{display:grid;grid-template-columns:900px 900px;gap:6px;padding:6px 14px}
.c{border:1px solid #ccc}
</style></head><body><h1>$Title</h1>
<div class='g'><div class='c'>$iso</div><div class='c'>$ex</div><div class='c'>$ey</div><div class='c'>$pl</div></div>
</body></html>
"@

$htmlPath = [System.IO.Path]::ChangeExtension($OutPng, '.html')
Set-Content -LiteralPath $htmlPath -Value $html -Encoding UTF8

$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edge)) { $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
if (Test-Path $OutPng) { Remove-Item -LiteralPath $OutPng -Force }
Start-Process -FilePath $edge -Wait -NoNewWindow -ArgumentList @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-sandbox',
    "--screenshot=$OutPng", '--window-size=1836,1860', ([System.Uri]$htmlPath).AbsoluteUri)

$wh = @($walls | ForEach-Object { $_.Top - $_.Bot })
$ch = @($cols | ForEach-Object { $_.Top - $_.Bot })
"walls=$($walls.Count) floors=$($floors.Count) columns=$($cols.Count)"
if ($wh.Count) { '  wall height  ft : min {0:N2}  median {1:N2}  max {2:N2}   under 6ft: {3}' -f ($wh | Measure-Object -Min).Minimum, (($wh | Sort-Object)[[int]($wh.Count/2)]), ($wh | Measure-Object -Max).Maximum, @($wh | Where-Object { $_ -lt 6 }).Count }
if ($ch.Count) { '  column height ft: min {0:N2}  median {1:N2}  max {2:N2}   under 6ft: {3}' -f ($ch | Measure-Object -Min).Minimum, (($ch | Sort-Object)[[int]($ch.Count/2)]), ($ch | Measure-Object -Max).Maximum, @($ch | Where-Object { $_ -lt 6 }).Count }
if (Test-Path $OutPng) { "rendered: $OutPng" } else { 'RENDER FAILED' }
