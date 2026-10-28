# Where DrawingMirror keeps each six-set source locally, and whether it is there. Touches no share.
$f = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Intake\SixSetsBuildAsBankedTests.cs'
$paths = Select-String -LiteralPath $f -Pattern '@"(\\\\Kor-fs01[^"]+\.pdf)"' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
$root = Join-Path ([IO.Path]::GetTempPath()) 'kor-drawings'
$sha = [Security.Cryptography.SHA1]::Create()
foreach ($p in $paths) {
    $dir = [IO.Path]::GetDirectoryName($p)
    $key = ([Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($dir.ToLowerInvariant())))).Substring(0, 16)
    $local = Join-Path (Join-Path $root $key) ([IO.Path]::GetFileName($p))
    $ok = Test-Path -LiteralPath $local
    "{0} {1,12} {2}" -f ($(if ($ok) { 'ok     ' } else { 'MISSING' })), $(if ($ok) { (Get-Item -LiteralPath $local).Length } else { '' }), $local
}
