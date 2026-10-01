# Runs as SYSTEM on each workstation. NON-DISRUPTIVE: stages V22 next to the live install; touches nothing in use.
$ErrorActionPreference = 'Stop'
$share = '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V22.zip'
$expected = 'AC1BD49F3F883BDCCDE4D0F778B6AC718F6318E414D682D10A87EAAAAA4E708B'
$zip = 'C:\Windows\Temp\KOR-V22.zip'
$stage = 'C:\Newerforma_new'
try {
    Copy-Item -LiteralPath $share -Destination $zip -Force
    $sha = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($sha -ne $expected) { throw "hash mismatch: $sha" }
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
    Remove-Item $zip -Force
    $files = @(Get-ChildItem "$stage\V22" -Recurse -File).Count
    $exe = Test-Path "$stage\V22\Kor.Operations.App.exe"
    $vsto = ([xml](Get-Content "$stage\V22\EmailFilerv2.vsto" -Raw)).assembly.assemblyIdentity.version
    [pscustomobject]@{ Staged = ($exe -and $files -ge 900); Files = $files; Addin = $vsto; Result = "staged $files files, add-in $vsto" }
}
catch {
    [pscustomobject]@{ Staged = $false; Result = "FAILED: " + $_.Exception.Message }
}
