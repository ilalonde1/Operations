<#
.SYNOPSIS
  The one place that decides who gets a firm signature pushed to them.

.DESCRIPTION
  There are two push surfaces -- desktop Outlook (Deploy-RemoteSignatures.ps1)
  and the Transmittals app's preferences (Push-TransmittalSignatures.ps1) -- and
  on 2026-09-08 the same fault hit both: kevinw curates his own signature, and
  each script overwrote it because each decided the target list on its own.

  A name-based rule is not enough for this class. Kevin restored his own file
  nine minutes after the deploy under a NEW name ("Kor KW"), which the
  vacation/holiday/away filename filter would not have matched on the next run
  either. The user is the unit of exclusion, not the file.

  So the target list is computed once, here, from two files:

    roster.csv         who has a firm signature at all (jbryson has no row --
                       opted out 2026-07-10 -- so he needs no special case)
    exclude-users.csv  who keeps their own, and on which surface

  Surface in exclude-users.csv is 'all', 'outlook' or 'transmittals'. Anything
  else is a typo that would silently push to someone who asked to be left
  alone, so it throws.
#>

function Get-SignatureRoster {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('outlook', 'transmittals')]
        [string]$Surface,

        [string]$KitDir = $PSScriptRoot
    )

    $valid = @('all', 'outlook', 'transmittals')
    $exPath = Join-Path $KitDir 'exclude-users.csv'
    $excluded = @()
    if (Test-Path $exPath) {
        foreach ($row in Import-Csv $exPath) {
            $s = "$($row.Surface)".Trim().ToLowerInvariant()
            if ($valid -notcontains $s) {
                throw "exclude-users.csv: '$($row.Alias)' has Surface '$($row.Surface)'; expected one of: $($valid -join ', ')"
            }
            if ($s -eq 'all' -or $s -eq $Surface) { $excluded += $row }
        }
    }

    $aliases = @($excluded.Alias)
    $roster = Import-Csv (Join-Path $KitDir 'roster.csv') |
        Where-Object { $aliases -notcontains $_.Alias }

    if ($excluded.Count) {
        Write-Host "Excluded from $Surface :" -ForegroundColor Yellow
        foreach ($e in $excluded) {
            Write-Host ("  {0,-12} {1}" -f $e.Alias, $e.Reason) -ForegroundColor DarkYellow
        }
        Write-Host ""
    }

    # A silent empty roster would look like a clean no-op run.
    if (-not $roster) { throw "No roster users left for surface '$Surface' -- check roster.csv and exclude-users.csv" }

    $roster
}
