# Builds a netops-run copy of tools\Expand-WrappedExecutables.ps1 with its parameters fixed in (netops run passes no
# arguments) and every `exit` turned into `return` (the agent/network runner publishes no result after exit).
param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Out, [switch]$Apply, [switch]$Quick, [switch]$NestedOnly, [switch]$Summary, [string[]]$SkipRoot = @())
$src = 'C:\VIsual Studio Projects\Operations\tools\Expand-WrappedExecutables.ps1'
$param = 'param([Parameter(Mandatory)][string]$Root, [switch]$Apply, [switch]$Quick, [switch]$NestedOnly, [switch]$Summary, [string[]]$SkipRoot = @())'
$b = { param($v) if ($v) { '$true' } else { '$false' } }
$skip = '@(' + (($SkipRoot | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', ') + ')'
$fixed = "`$Root = '$($Root.Replace("'", "''"))'; `$Apply = $(& $b $Apply); `$Quick = $(& $b $Quick); `$NestedOnly = $(& $b $NestedOnly); `$Summary = $(& $b $Summary); `$SkipRoot = $skip   # fixed for netops run"
$body = [IO.File]::ReadAllText($src)
$n = ([regex]::Matches($body, [regex]::Escape($param))).Count
if ($n -ne 1) { throw "expected the param line once, found $n" }
$body = $body.Replace($param, $fixed)
$body = [regex]::Replace($body, '(?m)(?<=^|[;{]\s*)exit\b[^\r\n}]*', 'return ')
[IO.File]::WriteAllText($Out, $body)
"param lines left: $(([regex]::Matches($body, [regex]::Escape($param))).Count); exit statements left: $(([regex]::Matches($body, '(?m)(^|[;{]\s*)exit\b')).Count)"
(Select-String -LiteralPath $Out -SimpleMatch 'fixed for netops run').Line
