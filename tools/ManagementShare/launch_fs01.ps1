# Launch a local .ps1 ON KOR-FS01 via Win32_Process Create (RPC). The script writes its own result file under C:\Windows\Temp.
param([Parameter(Mandatory)][string]$Script)
$text = Get-Content -Raw -Path $Script
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($text))
if ($enc.Length -gt 30000) { throw "Encoded script too long ($($enc.Length)); stage it instead." }
$r = Invoke-CimMethod -ComputerName KOR-FS01 -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $enc" }
"ReturnValue=$($r.ReturnValue) ProcessId=$($r.ProcessId)"
