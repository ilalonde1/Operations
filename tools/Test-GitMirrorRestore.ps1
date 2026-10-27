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
# PACK HEALTH (added 2026-10-26) is reported but DELIBERATELY DOES NOT AFFECT THE VERDICT.
# RESTORABLE means "a clone of this gives back the repo", and a bloated pack set still does that.
# Reported per bare: loose object count and size, pack count, total pack size, largest single pack,
# and the ratio between the two. Two invariants are worth watching:
#   RATIO - a maintained repo's whole pack set is barely larger than its biggest pack, so anything
#     at 2x or more is carrying redundant copies of the same objects.
#   LooseUnpacked (count minus prune-packable) - loose objects that NO pack holds. Diagnostic, and
#     only meaningful ALONGSIDE the size: git leaves objects loose on purpose until gc.auto fires
#     (6700 loose objects, or 50 packs), so a few hundred unpacked objects is correct behaviour.
#     A nonzero figure attached to TENS OF MB, not growing toward a pack, is repack not running.
# Flagged at ratio >= 2, packs > 20, or loose size >= 25 MB. Deliberately NOT flagged on object
# counts - see the note at the thresholds for the false positive that cost.
#
# Why it is here: on 2026-10-26 a push printed "remote: error: task 'geometric-repack' failed" and
# the message does not say WHICH bare. These numbers said it immediately - FS01 was 5 packs /
# 163 MB / ratio 1.0, KOR-302N was 23 packs / 2381 MB / ratio 15.0 with 14 near-identical 158 MB
# packs dated 08-01..08-02 and 1222 loose objects (129 MB). Same two refs, same HEAD, 15x the disk.
# Maintenance there has never succeeded; it was not disk space (784 GB free). The decider was
# LooseUnpacked: FS01 sat at 0 of 215 (every loose object already packed, a fresh small pack landing
# after each push) while 302N sat at 851 of 1237 and climbing, with no new pack for hours.
#   - It does NOT say which bare failed on its own. It reports numbers; a human compares them.
#   - It does NOT repair anything, on purpose.
#
# ⛔ THE CAUSE, FOUND THE SAME DAY ON BOTH HOSTS, AND IT IS NOT SIZE:
# a stale `multi-pack-index` held open by some process. Every push's repack then writes a fresh
# FULL-SIZE pack and dies before deleting the old ones, so the bare grows by roughly the whole
# repo PER PUSH. On FS01 that was 6 packs / 163 MB at 16:13 and 20 packs / 1471 MB by 19:29 - about
# 1.3 GB in eight hours, watched live (163 MB packs written at 19:07, 19:13 and 19:23). On 302N the
# same thing ran from 08-01 and left fourteen near-identical 158 MB packs.
# THE FIX IS ONE COMMAND AND IT IS NOT A REPACK: rename the multi-pack-index aside. It is a derived
# file git rebuilds, and maintenance then consolidates by itself - FS01 went 20 packs -> 7 -> 1 pack
# / 164 MB within minutes, wrote a fresh midx, and pruned loose objects 232 -> 4, untouched after.
# So this script now probes that file whenever it flags a bare, and says whether it is the known lock.
#   - ⚠ "Run `git repack -adf` on that host" was the advice here before, and it was wrong twice over:
#     THERE IS NO GIT ON KOR-FS01 OR KOR-302N (0 of 5 install paths each), and the cause was a locked
#     file, so a repack would have died on the same handle. Pushing to an SMB path needs no git on
#     the far end - the PUSHING git does all the work - which is why nobody noticed it was missing.
#   - Prefer these numbers to -Fsck for routine checks. count-objects is a few directory listings;
#     fsck READS EVERY OBJECT, so -Fsck against 302N's bare pulls ~2.4 GB over the link.
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
# \\KOR-302N\C$ unscoped it starts pulling KOR.Drafter's bare across the link - which as of
# 2026-10-26 is 2.4 GB, not the 164 MB the repo actually is - and that is nobody's intention for a
# health check. On a remote host, either run it ON that host, scope it with -Name, or use -NoClone.
# Against FS01 all five together are ~200 MB and take a few minutes.
#
# ===========================================================================================
# THE MORNING MIRROR WATCH - run this one line, every morning:
#
#     .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-FS01\E$\IT\git' -NoClone
#
# It takes under a minute, moves no objects, and is safe over the VPN. Read the PackRatio column:
#     ratio 1.0            = healthy. The whole pack set is one pack's worth. Expected state.
#     ratio above 1.0      = THE LOCK IS BACK on a mirror that is pushed to. Growth from here is
#                            roughly ONE FULL-SIZE PACK PER PUSH, so hours matter, not days.
#                            The PackNote line will say whether the midx is the holder.
#     ratio 0.00           = not a verdict. The largest pack rounded below 0.1 MB so no ratio could
#                            be computed. Only happens on trivially small bares; read Packs instead.
# "maintenance owed on N of M" at the end is the number to glance at. 0 of 5 is the good morning.
# ⚠ This watches HEALTH, not restorability - it clones nothing. Prove a restore separately, without
#   -NoClone, from a machine near the mirrors.
# ===========================================================================================
#
# Usage:
#   .\tools\Test-GitMirrorRestore.ps1
#   .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-FS01\E$\IT\git' -Fsck
#   .\tools\Test-GitMirrorRestore.ps1 -Name KOR.Drafter.git
#   .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-302N\C$' -Name 'KOR.RevitTools/repo.git'
#   .\tools\Test-GitMirrorRestore.ps1 -Root '\\KOR-302N\C$' -NoClone     # pack health, no transfer
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
    [switch]$KeepClones,
    # Report HEAD and pack health only - do not clone. This is the cheap routine check: it reads
    # directory listings, not objects, so it is safe to point at a remote host over the link, which
    # a cloning run is NOT. Restorability is then NOT tested, and the verdict says so.
    [switch]$NoClone
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

if (-not $NoClone) {
    if (Test-Path -LiteralPath $WorkRoot) { Remove-Item -LiteralPath $WorkRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null
}

$results = @()

foreach ($bare in $bares) {
    $label = Get-MirrorLabel $bare
    $row = [ordered]@{
        Mirror = $label; Head = ''; HeadTargetExists = $false; Cloned = $false
        Commits = 0; TrackedInClone = 0; TrackedInBare = 0; WorkingTreeClean = $false
        Fsck = ''; Verdict = 'FAIL'; Note = ''
        Loose = 0; LooseUnpacked = 0; LooseMB = 0.0; Packs = 0; PackMB = 0.0; LargestPackMB = 0.0
        PackRatio = 0.0; PackHealth = ''; PackNote = ''
    }

    try {
        # 0. Pack health first, so it is reported even for a bare whose HEAD is broken (that path
        #    returns early below). Cheap: count-objects -v is a handful of directory listings and
        #    reads no object content. It never changes $row.Verdict - see the header.
        $co = @{}
        foreach ($coLine in (git --git-dir="$($bare.FullName)" count-objects -v 2>$null)) {
            if ($coLine -match '^([a-z-]+):\s*(\d+)$') { $co[$Matches[1]] = [int64]$Matches[2] }
        }
        # count-objects reports sizes in KiB.
        $row.Loose   = [int]$co['count']
        $row.LooseMB = [math]::Round($co['size'] / 1024, 1)
        $row.Packs   = [int]$co['packs']
        $row.PackMB  = [math]::Round($co['size-pack'] / 1024, 1)
        # Loose objects NOT in any pack. count-objects' prune-packable is the loose objects that a
        # pack ALREADY holds (waiting to be pruned), so count minus prune-packable is what no pack
        # has absorbed yet. This is the sharpest signal that repack is not running: a bare whose
        # maintenance works shows prune-packable == count, i.e. zero here, within minutes of a push.
        $row.LooseUnpacked = [int]$co['count'] - [int]$co['prune-packable']

        $packDir = Join-Path $bare.FullName 'objects\pack'
        $largest = (Get-ChildItem -LiteralPath $packDir -Filter '*.pack' -ErrorAction SilentlyContinue |
                    Measure-Object -Property Length -Maximum).Maximum
        $row.LargestPackMB = if ($largest) { [math]::Round($largest / 1MB, 1) } else { 0.0 }
        $row.PackRatio = if ($row.LargestPackMB -gt 0) {
            [math]::Round($row.PackMB / $row.LargestPackMB, 1)
        } else { 0.0 }

        # Thresholds are SIZE-based on purpose. An earlier version of this flagged on object COUNT
        # (loose > 500, unpacked > 100) and immediately called a perfectly healthy 1.1 MB bare
        # broken: git deliberately leaves objects loose until gc.auto's own thresholds - 6700 loose
        # objects (gc.auto) or 50 packs (gc.autoPackLimit) - so a few hundred unpacked objects is
        # git working as designed, not maintenance failing. What is never by design is a pack set
        # several times the size of its own largest pack, or loose objects measured in tens of MB.
        $packFlags = @()
        if ($row.PackRatio -ge 2) {
            $packFlags += "pack set is $($row.PackRatio)x its largest pack ($($row.PackMB) MB total vs $($row.LargestPackMB) MB) - redundant copies"
        }
        if ($row.Packs -gt 20) { $packFlags += "$($row.Packs) packs (git's own gc.autoPackLimit is 50, but geometric repack should never leave this many)" }
        if ($row.LooseMB -ge 25) {
            $packFlags += "$($row.LooseMB) MB of loose objects, $($row.LooseUnpacked) of $($row.Loose) of them in NO pack"
        }
        $row.PackHealth = if ($packFlags) { 'MAINTENANCE' } else { 'ok' }
        if ($packFlags) {
            $midxPath = Join-Path $packDir 'multi-pack-index'
            $midxNote = ''
            if (Test-Path -LiteralPath $midxPath) {
                # The known cause (both hosts, 2026-10-26): a stale multi-pack-index held open by
                # some process, so every repack writes a fresh full-size pack and then dies before
                # deleting the old ones. Check it here, because it turns a vague "maintenance owed"
                # into the one action that fixes it.
                $mi = Get-Item -LiteralPath $midxPath
                try {
                    $h = [IO.File]::Open($midxPath, 'Open', 'Write', 'None'); $h.Close()
                    $midxNote = " multi-pack-index is writable (mtime $($mi.LastWriteTime.ToString('MM-dd HH:mm'))), so this is NOT the known lock."
                } catch {
                    $midxNote = " *** multi-pack-index CANNOT BE OPENED FOR WRITE (mtime $($mi.LastWriteTime.ToString('MM-dd HH:mm')))" +
                                " - this is the known cause. Rename it aside (it is a derived file git rebuilds) and maintenance" +
                                " consolidates by itself on the next push. Growth is ~one full-size pack PER PUSH until then."
                }
            }
            $row.PackNote = "maintenance owed (restore still works): " + ($packFlags -join '; ') + "." + $midxNote
        }

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

        if ($NoClone) {
            $row.Verdict = 'NOT TESTED'
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

Write-Host "Pack health (does NOT affect the verdict - a bloated pack set still restores):"
$results | Format-Table Mirror, Loose, LooseUnpacked, LooseMB, Packs, PackMB, LargestPackMB, PackRatio, PackHealth -AutoSize

foreach ($r in $results | Where-Object { $_.Note }) {
    Write-Host ""
    Write-Host "$($r.Mirror): $($r.Note)" -ForegroundColor Yellow
}

foreach ($r in $results | Where-Object { $_.PackNote }) {
    Write-Host ""
    Write-Host "$($r.Mirror): $($r.PackNote)" -ForegroundColor DarkYellow
}

if (-not $NoClone -and -not $KeepClones -and (Test-Path -LiteralPath $WorkRoot)) {
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force
}

$total = $results.Count
Write-Host ""

if ($NoClone) {
    $broken = @($results | Where-Object { -not $_.HeadTargetExists }).Count
    $owed   = @($results | Where-Object { $_.PackHealth -eq 'MAINTENANCE' }).Count
    Write-Host "-NoClone: RESTORABILITY WAS NOT TESTED on any of these $total mirrors." -ForegroundColor Yellow
    Write-Host "HEAD target missing on $broken of $total; maintenance owed on $owed of $total." -ForegroundColor $(if ($broken -eq 0) { 'Green' } else { 'Red' })
    Write-Host "Run without -NoClone, from a machine near the mirrors, to actually prove a restore."
    exit $(if ($broken -gt 0) { 1 } else { 0 })
}

$pass = @($results | Where-Object { $_.Verdict -eq 'RESTORABLE' }).Count
Write-Host "$pass of $total mirrors restorable." -ForegroundColor $(if ($pass -eq $total) { 'Green' } else { 'Red' })
Write-Host "Checked: HEAD target exists, clone succeeds, tracked count matches the bare's HEAD tree, working tree clean$(if ($Fsck) { ', git fsck' })."
Write-Host "Reported, not gated: pack health. MAINTENANCE means disk waste and failing repack tasks, not an unrestorable mirror."
Write-Host "NOT checked: that a mirror matches its SOURCE (use git ls-remote for that), nor whether the volume is backed up."

if ($pass -ne $total) { exit 1 }
