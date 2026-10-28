# Feeds one .e2k to Griffin's running KorConcrete engine through its own endpoints, exactly as his UI does:
# /section/etabs/parse, then /section/etabs/assemble for one storey's first pier. Prints what his engine returns.
param([string]$E2k, [int]$Port = 8150, [string]$Units = 'SI')
$base = "http://127.0.0.1:$Port"
$page = (Invoke-WebRequest -UseBasicParsing "$base/").Content
$token = [regex]::Match($page, 'name="korconcrete-token"\s+content="([^"]+)"').Groups[1].Value
if (-not $token) { $token = [regex]::Match($page, 'content="([0-9a-f]{64})"').Groups[1].Value }
"token found: $([bool]$token)"
$h = @{ 'x-korconcrete-token' = $token }
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($E2k))
$parse = Invoke-RestMethod -Method Post "$base/section/etabs/parse" -Headers $h -ContentType 'application/json' `
    -Body (@{ filename = [IO.Path]::GetFileName($E2k); contentB64 = $b64 } | ConvertTo-Json -Compress)
"his parse: forceUnit $($parse.forceUnit)  lengthUnit $($parse.lengthUnit)  stories $(@($parse.stories).Count)"
$withPiers = @($parse.piersByStory.PSObject.Properties | Where-Object { @($_.Value.piers).Count -gt 0 })
"stories with piers: $($withPiers.Count)"
$story = $withPiers[[int]($withPiers.Count / 2)]
$pierId = @($story.Value.piers)[0].id
"assembling story '$($story.Name)', pier '$pierId'  (piers on that story: $(@($story.Value.piers).Count))"

# The same wall as OUR model states it: the panel assigned that pier on that storey, its plan length and section.
$lines = Get-Content -LiteralPath $E2k
$assign = $lines | Where-Object { $_ -match "AREAASSIGN\s+""([^""]+)""\s+""$([regex]::Escape($story.Name))"".*PIER\s+""$pierId""" } | Select-Object -First 1
$area = [regex]::Match($assign, 'AREAASSIGN\s+"([^"]+)"').Groups[1].Value
$section = [regex]::Match($assign, 'SECTION\s+"([^"]+)"').Groups[1].Value
$conn = $lines | Where-Object { $_ -match "^\s*AREA\s+""$area""\s+PANEL" } | Select-Object -First 1
$ids = [regex]::Matches(($conn -replace '^\s*AREA\s+"[^"]+"\s+PANEL\s+\d+\s+', ''), '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
$pt = @{}; foreach ($l in $lines) { $m = [regex]::Match($l, '^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)'); if ($m.Success) { $pt[$m.Groups[1].Value] = @([double]$m.Groups[2].Value, [double]$m.Groups[3].Value) } }
$a = $pt[$ids[0]]; $b = $pt[$ids[1]]
$len = [Math]::Sqrt([Math]::Pow($a[0]-$b[0],2) + [Math]::Pow($a[1]-$b[1],2))
$thk = [regex]::Match(($lines | Where-Object { $_ -match "SHELLPROP\s+""$section""" } | Select-Object -First 1), 'WALLTHICKNESS\s+([\d.]+)').Groups[1].Value
"OUR model: panel $area, section $section - wall length $([Math]::Round($len,1)) and thickness $thk, in the model's unit (mm)"
$asm = Invoke-RestMethod -Method Post "$base/section/etabs/assemble" -Headers $h -ContentType 'application/json' `
    -Body (@{ filename = [IO.Path]::GetFileName($E2k); contentB64 = $b64; story = $story.Name; pierIds = @($pierId); units = $Units } | ConvertTo-Json -Compress)
"his assembled section (overall): $($asm.etabs.overall | ConvertTo-Json -Compress)"
"his f'c values: $($asm.etabs.fcValues | ConvertTo-Json -Compress)"
"his warnings: $($asm.etabs.warnings | ConvertTo-Json -Compress)"
