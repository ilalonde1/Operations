# ON KOR-DC01, read-only: are this month's two updates installed? Hotfix list + the full update history for those KBs.
$hf = 'KB5126646','KB5127690' | ForEach-Object { $k = $_; $x = Get-HotFix -Id $k -ErrorAction SilentlyContinue; "$k=$(if ($x) { "installed $($x.InstalledOn)" } else { 'not in Get-HotFix' })" }
$h = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher(); $n = $h.GetTotalHistoryCount()
$hist = $h.QueryHistory(0, $n) | Where-Object { $_.Title -match 'KB5126646|KB5127690' } |
  ForEach-Object { "$($_.Date.ToLocalTime().ToString('MM-dd HH:mm')) rc=$($_.ResultCode) hr=0x$('{0:X8}' -f $_.HResult) $($_.Title)" }
$build = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion')
[pscustomobject]@{ HotFix = $hf -join '; '; History = @($hist); HistoryTotal = $n; Build = "$($build.CurrentBuild).$($build.UBR)" }
