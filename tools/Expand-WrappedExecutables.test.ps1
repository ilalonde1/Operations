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
} finally {
    if (Test-Path -LiteralPath $t) { Remove-Item -LiteralPath $t -Recurse -Force }
}
"$(if ($fails) { "FAILED $fails" } else { 'ALL PASSED' }) ($($PSVersionTable.PSVersion))"
exit $fails
