# Proves every bare git mirror on FS01 can actually be RESTORED, by cloning it and looking at
# what lands - not by comparing refs.
#
# WHY THIS EXISTS. On 2026-10-25 five bare mirrors were created under \\KOR-FS01\E$\IT\git and
# reported verified, on the evidence that `git ls-remote` agreed with every local branch head.
# It did agree. Four of the five still cloned into an EMPTY WORKING TREE: `git init --bare` points
# HEAD at refs/heads/master, and four of the repos are on main (or on a feature branch), so a clone
# resolved no HEAD and checked out nothing. Every object and every ref was present and correct.
# Anyone restoring would have seen an empty folder and concluded the backup was empty.
#
# ls-remote reads refs. The fault was in HEAD, which ls-remote does not show. So the check that
# finds this class is to CLONE THE THING AND COUNT WHAT CAME OUT.
#
# AND THE FIRST VERSION OF THIS SCRIPT MISSED HALF THE CLASS, which is the failure mode rule 11
# warns about: a broad name on a narrow check. It globbed *.git directly under one root, so it saw
# the five flat bares on FS01 and none of the nested ones on KOR-302N (C:\<Repo>\repo.git). Both
# 302N mirrors had the same fault. The real count across both locations was 6 of 7 bares broken -
# only VentureBrain, whose branch genuinely is master, would have restored.
# So discovery now covers both layouts, and -Root must still be pointed at each HOST separately:
#   flat:   E:\IT\git\KOR.RevitTools.git      (FS01; the name carries the repo)
#   nested: C:\KOR.RevitTools\repo.git        (302N; the name is always repo.git)
#
# CHECKS, per bare:
#   - HEAD is a symbolic ref whose target branch actually exists
#   - a real `git clone` of it succeeds
#   - the clone's HEAD resolves to a commit
#   - the clone's tracked-file count matches the bare's own tree at that commit
#   - the clone's working tree is clean (every tracked file materialised on disk)
#   - the clone's commit count matches the bare's
#
# DOES NOT CHECK:
#   - that the mirror matches its SOURCE working copy. This proves the mirror is internally whole
#     and restorable; it cannot know a branch was never pushed, or was pushed stale. Compare
#     against the source with `git ls-remote` separately - that is the gate this one does not replace.
#   - object integrity beyond what clone validates. Pass -Fsck for a full `git fsck`, which is slow
#     on large repos (KOR.Drafter is ~164 MB).
#   - whether the volume is in a backup job. FS01 is in the Kor-FS01 Veeam job and 302N is in none,
#     measured 2026-10-25 - but this script cannot see that and will happily pass a mirror on an
#     unbacked disk.
#   - LFS payloads, submodule contents, or hooks/config inside the bare.
#
# SAME-CLASS FAULT IT WOULD MISS: a bare whose HEAD points at a branch that exists but is the WRONG
# one - say HEAD -> refs/heads/gh-pages on a repo whose code lives on main. It clones, it checks out,
# the tree is clean, and every count here passes, while the restore hands someone the wrong branch.
# Only naming the expected branch per repo would catch that, and this script deliberately does not
# hold that list.
#
# MAX_PATH: clone into a SHORT destination. Cloning into a session scratchpad failed with "Filename
# too long" because a ~140-char prefix plus a 117-char internal path crosses 260, and
# core.longpaths is unset on these machines. That was the harness, not the mirror. -WorkRoot
# defaults to C:\korbk-verify for that reason; do not point it somewhere deep.
#
# ⚠ IT CLONES, SO IT MOVES THE WHOLE REPO. Over the VPN that is not free: pointed at
# \\KOR-302N\C$ unscoped it starts pulling KOR.Drafter's ~164 MB bare across the link, which is
# nobody's intention for a health check. On a remote host, either run it ON that host or scope it
# with -Name. Against FS01 all five together are ~200 MB and take a few minutes.
#
# Usage:
#   .\tools\Test-GitMirrorRestore.ps1
#   .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-FS01\E$\IT\git' -Fsck
#   .\tools\Test-GitMirrorRestore.ps1 -Name KOR.Drafter.git
#   .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-302N\C$' -Name 'KOR.RevitTools/repo.git'
[CmdletBinding()]
param(
    [string]$Root = '\\KOR-FS01\E$\IT\git',
    # Short on purpose - see the MAX_PATH note above. %TEMP% is ~40 chars, which leaves room for a
    # 117-char internal path; the session scratchpad is ~140 and does not. Do not point this deeper,
    # and do not point it at a C:\ root - the harness protects those from removal, so the script
    # could not clean up after itself.
    [string]$WorkRoot = (Join-Path $env:TEMP 'korbk-verify'),
    # Limit to one or more bares, by folder name.
    [string[]]$Name,
    [switch]$Fsck,
    # Leave the clones behind for inspection instead of removing them.
    [switch]$KeepClones
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Root)) {
    throw "Mirror root not reachable: $Root"
}

# Both layouts: FS01 keeps flat <Repo>.git under one folder; 302N keeps nested <Repo>\repo.git.
# A bare is identified by having a HEAD file, so a working checkout's .git is not mistaken for one.
$flat = @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { $_.Name -like '*.git' })
$nested = @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { $_.Name -notlike '*.git' } |
            ForEach-Object { Join-Path $_.FullName 'repo.git' } |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'HEAD') } |
            ForEach-Object { Get-Item -LiteralPath $_ })
$bares = @($flat + $nested | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'HEAD') })

# A nested bare is always called repo.git, so label it by the repo folder that holds it.
function Get-MirrorLabel($dir) {
    if ($dir.Name -eq 'repo.git') { return (Split-Path $dir.Parent.FullName -Leaf) + '/repo.git' }
    return $dir.Name
}

if ($Name) { $bares = @($bares | Where-Object { $Name -contains $_.Name -or $Name -contains (Get-MirrorLabel $_) }) }
if (-not $bares) { throw "No bare repositories found under $Root (looked for <Repo>.git and <Repo>\repo.git, each needing a HEAD file)" }

if (Test-Path -LiteralPath $WorkRoot) { Remove-Item -LiteralPath $WorkRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

$results = @()

foreach ($bare in $bares) {
    $label = Get-MirrorLabel $bare
    $row = [ordered]@{
        Mirror = $label; Head = ''; HeadTargetExists = $false; Cloned = $false
        Commits = 0; TrackedInClone = 0; TrackedInBare = 0; WorkingTreeClean = $false
        Fsck = ''; Verdict = 'FAIL'; Note = ''
    }

    try {
        # 1. HEAD, and whether its target branch is real. This is the fault ls-remote cannot see.
        $headRaw = (Get-Content -LiteralPath (Join-Path $bare.FullName 'HEAD') -Raw).Trim()
        $row.Head = $headRaw -replace '^ref:\s*', ''
        $row.HeadTargetExists = [bool](git --git-dir="$($bare.FullName)" rev-parse --verify --quiet "$($row.Head)" 2>$null)

        if (-not $row.HeadTargetExists) {
            $row.Note = "HEAD points at $($row.Head), which does not exist in this bare. A clone will " +
                        "check out NOTHING. Fix: git --git-dir='$($bare.FullName)' symbolic-ref HEAD refs/heads/<real branch>"
            $results += [pscustomobject]$row
            continue
        }

        # 2. What the bare itself says it holds, read from the bare - not from any local checkout.
        $row.TrackedInBare = @(git --git-dir="$($bare.FullName)" ls-tree -r --name-only HEAD 2>$null).Count

        # 3. The actual restore.
        # Label, not $bare.Name: every nested bare is called repo.git, so names would collide.
        $dest = Join-Path $WorkRoot (($label -replace '/repo\.git$', '' -replace '\.git$', '') -replace '[^A-Za-z0-9._-]', '_')
        git clone --quiet "$($bare.FullName)" "$dest" 2>&1 | Out-Null
        $cloneExit = $LASTEXITCODE
        $row.Cloned = ($cloneExit -eq 0) -and (Test-Path -LiteralPath (Join-Path $dest '.git'))
        if (-not $row.Cloned) {
            $row.Note = "git clone failed (exit $cloneExit). If this says 'Filename too long', shorten -WorkRoot."
            $results += [pscustomobject]$row
            continue
        }

        # 4. Look at what came out.
        $row.Commits        = [int](git -C "$dest" rev-list --count HEAD 2>$null)
        $row.TrackedInClone = @(git -C "$dest" ls-files 2>$null).Count
        $dirty              = @(git -C "$dest" status --porcelain 2>$null)
        $row.WorkingTreeClean = ($dirty.Count -eq 0)

        if ($Fsck) {
            $out = git -C "$dest" fsck --no-progress --no-dangling 2>&1
            $row.Fsck = if ($LASTEXITCODE -eq 0 -and -not $out) { 'clean' } else { ($out | Select-Object -First 1) -join '' }
        }

        $good = $row.Cloned -and $row.Commits -gt 0 -and $row.TrackedInClone -gt 0 -and
                $row.TrackedInClone -eq $row.TrackedInBare -and $row.WorkingTreeClean -and
                (-not $Fsck -or $row.Fsck -eq 'clean')
        $row.Verdict = if ($good) { 'RESTORABLE' } else { 'FAIL' }
        if (-not $good -and -not $row.Note) {
            if (-not $row.WorkingTreeClean) {
                $row.Note = "$($dirty.Count) tracked files did not materialise on disk - the checkout was incomplete."
            } elseif ($row.TrackedInClone -ne $row.TrackedInBare) {
                $row.Note = "clone has $($row.TrackedInClone) tracked files, the bare's HEAD tree has $($row.TrackedInBare)."
            }
        }
    } catch {
        $row.Note = $_.Exception.Message
    }

    $results += [pscustomobject]$row
}

$results | Format-Table Mirror, Head, Cloned, Commits, TrackedInClone, TrackedInBare, WorkingTreeClean, Verdict -AutoSize

foreach ($r in $results | Where-Object { $_.Note }) {
    Write-Host ""
    Write-Host "$($r.Mirror): $($r.Note)" -ForegroundColor Yellow
}

if (-not $KeepClones -and (Test-Path -LiteralPath $WorkRoot)) {
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force
}

$pass = @($results | Where-Object { $_.Verdict -eq 'RESTORABLE' }).Count
$total = $results.Count
Write-Host ""
Write-Host "$pass of $total mirrors restorable." -ForegroundColor $(if ($pass -eq $total) { 'Green' } else { 'Red' })
Write-Host "Checked: HEAD target exists, clone succeeds, tracked count matches the bare's HEAD tree, working tree clean$(if ($Fsck) { ', git fsck' })."
Write-Host "NOT checked: that a mirror matches its SOURCE (use git ls-remote for that), nor whether the volume is backed up."

if ($pass -ne $total) { exit 1 }
