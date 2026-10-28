# Expand-WrappedExecutables.test.ps1 -- known-bad first: a tree shaped like the 28 Oct KOR-210 Revit 2026 copy, with
# Setup.exe at the top and executables wrapped two and three folders down, after ONLY the top-level zip was unwrapped
# (what was done by hand). -Check must name every nested one; -Apply must restore every one, byte-identical; a zip
# holding several files and an .exe that already exists must be left alone. Runs under pwsh 7 and Windows PowerShell 5.1.
#   pwsh -NoProfile -File tools\Expand-WrappedExecutables.test.ps1 ; powershell -NoProfile -File tools\Expand-WrappedExecutables.test.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tool = Join-Path $PSScriptRoot 'Expand-WrappedExecutables.ps1'
$shell = (Get-Process -Id $PID).Path
$t = Join-Path ([IO.Path]::GetTempPath()) ("wrapped-exe-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$fails = 0
function Check($ok, $what) { if ($ok) { "PASS $what" } else { "FAIL $what"; $script:fails++ } }
function Wrap([string]$exePath, [byte[]]$bytes) {
    $dir = [IO.Path]::GetDirectoryName($exePath); [void][IO.Directory]::CreateDirectory($dir)
    $zip = [IO.Path]::ChangeExtension($exePath, '.zip')
    $a = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try { $e = $a.CreateEntry([IO.Path]::GetFileName($exePath), 'NoCompression'); $s = $e.Open(); $s.Write($bytes, 0, $bytes.Length); $s.Dispose() } finally { $a.Dispose() }
}
function Bytes([int]$n, [int]$seed) { $r = New-Object Random $seed; $b = New-Object byte[] $n; $r.NextBytes($b); ,$b }
try {
    $nested = [ordered]@{
        'ODIS\DownloadManager.exe' = (Bytes 300000 1)
        'x64\RVT\setup\Installer.exe' = (Bytes 2500000 2)      # three folders down, bigger than one read buffer
        '3rdParty\dotNetFramework\48\dotNetFx48.exe' = (Bytes 70000 3)
        'x86\empty.exe' = (Bytes 0 4)                          # a zero-byte entry
    }
    $setup = Bytes 50000 5
    Wrap (Join-Path $t 'Setup.exe') $setup
    foreach ($k in $nested.Keys) { Wrap (Join-Path $t $k) $nested[$k] }
    # a zip holding two files: not a wrapped executable, must be left alone
    $two = [IO.Compression.ZipFile]::Open((Join-Path $t 'x64\Content.zip'), 'Create')
    try { foreach ($n in 'a.exe', 'b.txt') { $s = $two.CreateEntry($n).Open(); $s.WriteByte(1); $s.Dispose() } } finally { $two.Dispose() }
    # what was done by hand on 28 Oct: only the top-level Setup.zip unwrapped
    [IO.File]::WriteAllBytes((Join-Path $t 'Setup.exe'), $setup)

    $out = & $shell -NoProfile -File $tool -Root $t; $code = $LASTEXITCODE
    Check ($code -eq 4) "known-bad: -Check exits 4 (the 4 nested missing), got $code"
    foreach ($k in $nested.Keys) { Check (@($out | Where-Object { $_ -eq "MISSING: $(Join-Path $t $k)" }).Count -eq 1) "known-bad: -Check names $k" }
    Check (@($out | Where-Object { $_ -like '*Setup.exe*' }).Count -eq 0) "-Check does not report the top-level Setup.exe already restored"

    $out = & $shell -NoProfile -File $tool -Root $t -Apply; $code = $LASTEXITCODE
    Check ($code -eq 0) "-Apply exits 0, got $code"
    foreach ($k in $nested.Keys) {
        $p = Join-Path $t $k
        Check ((Test-Path -LiteralPath $p) -and ([Convert]::ToBase64String([IO.File]::ReadAllBytes($p)) -eq [Convert]::ToBase64String($nested[$k]))) "restored byte-identical: $k"
        Check (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($p, '.zip'))) "zip kept: $k"
    }
    Check (-not (Test-Path -LiteralPath (Join-Path $t 'x64\a.exe'))) "a two-file zip is left alone"
    Check (@($out | Where-Object { $_ -like 'TOTAL 5 wrapped*restored 4, already there and identical 1, failed 0; other zips left alone 1' }).Count -eq 1) "total line: $($out[-1])"

    $out = & $shell -NoProfile -File $tool -Root $t; $code = $LASTEXITCODE
    Check ($code -eq 0) "after -Apply, -Check finds 0 missing, got exit $code ($($out[-1]))"

    $out = & $shell -NoProfile -File $tool -Root $t -Quick; $code = $LASTEXITCODE
    Check ($code -eq 0 -and "$out" -like '*present, same length (length-checked only) 5*') "-Quick says length-checked, never identical: $out"
    $out = & $shell -NoProfile -File $tool -Root $t -Apply -Quick; $code = $LASTEXITCODE
    Check ($code -eq 2) "-Apply -Quick is refused (exit 2), got $code"

    # an existing .exe that differs from its zip is reported and never overwritten
    $p = Join-Path $t 'ODIS\DownloadManager.exe'; [IO.File]::WriteAllBytes($p, [byte[]](1, 2, 3))
    $out = & $shell -NoProfile -File $tool -Root $t -Apply; $code = $LASTEXITCODE
    Check ($code -eq 1 -and [IO.File]::ReadAllBytes($p).Length -eq 3) "a different existing .exe is reported (exit 1) and left as is, got exit $code"

    # -NestedOnly on a SHARE-shaped tree (what Ian approved for FS01): each installer root keeps its own zips wrapped,
    # everything wrapped below a root is restored. Two roots: a Revit-like tree, and a driver folder of side-by-side exes.
    $s = Join-Path $t 'share'
    $rv = Join-Path $s 'Autodesk\Revit 2026 - (EN)'
    $files = [ordered]@{
        "$rv\Setup.exe" = (Bytes 4000 10); "$rv\ODIS\DownloadManager.exe" = (Bytes 9000 11); "$rv\x64\a\b\deep.exe" = (Bytes 7000 12)
        "$s\Brother\Drv1.exe" = (Bytes 3000 13); "$s\Brother\Drv2.exe" = (Bytes 3100 14); "$s\Brother\Sub\inner.exe" = (Bytes 3200 15)
    }
    foreach ($k in $files.Keys) { Wrap $k $files[$k] }
    $keep = "$rv\Setup.exe", "$s\Brother\Drv1.exe", "$s\Brother\Drv2.exe"
    $unwrap = "$rv\ODIS\DownloadManager.exe", "$rv\x64\a\b\deep.exe", "$s\Brother\Sub\inner.exe"
    $out = & $shell -NoProfile -File $tool -Root $s -NestedOnly -Summary; $code = $LASTEXITCODE
    Check ($code -eq 3) "-NestedOnly dry run: 3 nested missing (exit 3), got $code"
    Check ("$out" -like '*CHECK 3 NESTED (installer-root zips kept as they are: 3) wrapped executables*in 2 installer roots: missing 3*') "-NestedOnly dry run total: $($out[-1])"
    Check ("$out" -like '*Autodesk\Revit 2026 - (EN)*' -and "$out" -like '*Brother*' -and "$out" -notlike '*MISSING:*') "-Summary prints one row per installer root, no per-file lines"
    $out = & $shell -NoProfile -File $tool -Root $s -NestedOnly -Summary -SkipRoot '*Brother*'; $code = $LASTEXITCODE
    Check ($code -eq 2 -and "$out" -like '*SKIPPED by -SkipRoot: 3 wrapped executables in 1 installer root(s): Brother*' -and "$out" -like '*CHECK 2 NESTED*') "-SkipRoot leaves a whole installer root out: $out"
    $out = & $shell -NoProfile -File $tool -Root $s -NestedOnly -Apply; $code = $LASTEXITCODE
    Check ($code -eq 0) "-NestedOnly -Apply exits 0, got $code"
    foreach ($p in $unwrap) { Check ((Test-Path -LiteralPath $p) -and ([Convert]::ToBase64String([IO.File]::ReadAllBytes($p)) -eq [Convert]::ToBase64String($files[$p]))) "-NestedOnly restored byte-identical: $($p.Substring($s.Length + 1))" }
    foreach ($p in $keep) { Check (-not (Test-Path -LiteralPath $p)) "-NestedOnly left the installer root's own zip wrapped: $($p.Substring($s.Length + 1))" }
    Check (@(Get-ChildItem -LiteralPath $s -Recurse -Filter *.zip).Count -eq 6) "-NestedOnly deleted no zip (6 of 6 still there)"
} finally {
    if (Test-Path -LiteralPath $t) { Remove-Item -LiteralPath $t -Recurse -Force }
}
"$(if ($fails) { "FAILED $fails" } else { 'ALL PASSED' }) ($($PSVersionTable.PSVersion))"
exit $fails
