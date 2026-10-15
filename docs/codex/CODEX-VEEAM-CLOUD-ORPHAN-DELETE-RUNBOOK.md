# KOR-BK01: orphaned Cloud Connect data and Checkpoint Removal

Prepared 2026-10-07 against the incident brief and Veeam's **v12** documentation. Target: VBR 12.3.2, PostgreSQL 15, database `VeeamBackup`. This is an operator runbook; nothing has been executed on KOR-BK01 or T-Net. All eight PowerShell blocks passed local parser validation; Veeam cmdlet execution and SQL have not been tested against the server.

**Result of the research:** the supplied evidence does not identify a persisted deletion work item, and I could not verify an exact PostgreSQL table/row or a published Veeam-supported SQL repair for this incident. Naming one would be speculation. The runbook therefore provides executable evidence/backup/verification steps, followed by an explicit **STOP — needs Veeam Support** before configuration repair. It is not a verified end-to-end repair script.

**T-Net is required to finish if the data is outside the tenant's supported access, or the remaining problem is provider quota accounting.** The brief does not establish the underlying repository type or the physical location of the 13.3 TB. Consequently, a universal claim that all tenant-side recovery is impossible would also be unsupported. Step 6 defines the conditional tenant route and the precise provider boundary.

## 1. Establish what must be fixed, without assuming a database row

Veeam documents Checkpoint Removal as a continuously running background activity. An idle process that starts again with the service is not, by itself, proof of a persisted interrupted delete. The system job UID `bb70be84-d59c-419d-878c-e5d04d97405c`, its session ID, and backup ID `83615926-5858-4170-a1ef-e2273a8caeea` are different identifiers. Do not substitute one for another in SQL. Veeam also places Cloud Connect checkpoint processing on different sides depending on backup type and connection mode. [V12 Background Checkpoint Removal Job](https://helpcenter.veeam.com/archive/backup/120/vsphere/checkpoint_removal_job.html).

The lock error is evidence of a deletion conflict at the time of the failed operation. It does not establish that the currently idle checkpoint worker owns that lock. The failed rescan establishes that the orphan was not rediscovered; it does **not** independently prove that its VBM is physically absent. Provider inspection must establish that. A displayed backup size/free-space counter does not establish physical allocated bytes either.

The acceptable outcome for part (1) is **no outstanding orphan-specific deletion/lock/retry after service restart**, with normal maintenance still functioning. Permanently suppressing the global checkpoint worker could affect real backups and is not an acceptable substitute. If Support proves this is only the normal idle worker, there is no orphan-specific respawn record to clear.

| Protected object | Required identity before proceeding |
| --- | --- |
| Kor-FS01 | `1417178d-7802-4389-b026-cfa76d0e6743`; incident baseline 33 restore points |
| Kor-FS01 Offsite | `f32cb7e5-c9df-4107-946c-c8f957781ae2`; incident baseline 5 restore points |
| Six reported replicas/children | Kor-RDS01, Kor-DC01, KOR-UNIFI01, Kor-APP01, KOR-MESH01, Vcenter - vcenter; discover and record their actual IDs |
| Per-VM parent | Discover and record its ID and all child IDs, including the six above |
| Everything else | Preserve all other backup, restore-point, job, repository and replica identities/configuration |

The brief calls six per-VM children “replicas.” Verify their actual Veeam types; a backup repository name containing “Replication” is not proof of a VM replica. Protect them in either case. Names alone are insufficient deletion guards.

## 2. Collect a tenant baseline (read-only)

Run in **64-bit Windows PowerShell on KOR-BK01 through Mesh**, in one session. These commands only create local evidence files and read Veeam/PostgreSQL. Use a secure location with enough space for a database dump. Do not put the resulting database/logs in this Git repository or a public ticket. If SYSTEM cannot connect to Veeam PowerShell, stop that part and have Support supply an authorized connection method; do not change Veeam roles/authentication to bypass it.

```powershell
$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -ne 'KOR-BK01') { throw 'Run on KOR-BK01 only.' }
$orphan = [guid]'83615926-5858-4170-a1ef-e2273a8caeea'
$protectedFS = @(
    [guid]'1417178d-7802-4389-b026-cfa76d0e6743',
    [guid]'f32cb7e5-c9df-4107-946c-c8f957781ae2'
)
if ($protectedFS -contains $orphan) { throw 'Target overlaps protected IDs.' }
$caseDir = Join-Path 'C:\ProgramData\Veeam' ('OrphanCase-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $caseDir | Out-Null
$pgBin = 'C:\Program Files\PostgreSQL\15\bin'
$psql = Join-Path $pgBin 'psql.exe'
$pgDump = Join-Path $pgBin 'pg_dump.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
$pgArgs = @('-w', '-h', '127.0.0.1', '-p', '5432', '-U', 'postgres')

function Save-DbRead([string]$Label, [string]$Sql) {
    if ($Label -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid evidence label.' }
    $sqlPath = Join-Path $caseDir ($Label + '.sql')
    $outPath = Join-Path $caseDir ($Label + '.txt')
    if (Test-Path -LiteralPath $outPath) { throw "Do not overwrite evidence: $outPath" }
    @"
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '3s';
$Sql
ROLLBACK;
"@ | Set-Content -LiteralPath $sqlPath -Encoding UTF8
    & $psql @pgArgs -X -q -A -t -d VeeamBackup -v ON_ERROR_STOP=1 -f $sqlPath -o $outPath
    if ($LASTEXITCODE -ne 0) { throw "Database read failed: $Label; preserve partial output." }
}

Save-DbRead 'connection' @'
SELECT current_database(), current_user, version();
SELECT id FROM public."backup.model.backups"
WHERE id IN ('83615926-5858-4170-a1ef-e2273a8caeea',
             '1417178d-7802-4389-b026-cfa76d0e6743',
             'f32cb7e5-c9df-4107-946c-c8f957781ae2') ORDER BY id;
'@
Get-Content -LiteralPath (Join-Path $caseDir 'connection.txt')

Get-CimInstance Win32_Process -Filter "Name='Veeam.Backup.Manager.exe'" |
    Select-Object ProcessId,ParentProcessId,CreationDate,ExecutablePath,CommandLine |
    Export-Clixml -LiteralPath (Join-Path $caseDir 'manager-before.xml')
Get-CimInstance Win32_Service -Filter "Name LIKE 'Veeam%'" |
    Select-Object Name,State,StartMode,PathName |
    Export-Clixml -LiteralPath (Join-Path $caseDir 'services-before.xml')
Get-Item -LiteralPath 'C:\Program Files\Veeam\Backup and Replication\Backup\Veeam.Backup.Manager.exe' |
    Select-Object -ExpandProperty VersionInfo |
    Format-List FileVersion,ProductVersion |
    Out-File -LiteralPath (Join-Path $caseDir 'version.txt')

Import-Module Veeam.Backup.PowerShell
Connect-VBRServer -Server localhost
Get-VBRJob | Export-Clixml -LiteralPath (Join-Path $caseDir 'jobs-before.xml')
Get-VBRBackupRepository | Export-Clixml -LiteralPath (Join-Path $caseDir 'repos-before.xml')
Get-VBRCloudProvider | Export-Clixml -LiteralPath (Join-Path $caseDir 'providers-before.xml')

function Save-BackupGuard([string]$Label) {
    $items = @(@(Get-VBRBackup) + @(Get-VBRReplica)) |
        Sort-Object -Property Id -Unique
    foreach ($id in $protectedFS) {
        if (@($items | Where-Object { $_.Id -eq $id }).Count -ne 1) {
            throw "Protected backup missing/ambiguous: $id"
        }
    }
    $lines = @(
        foreach ($b in $items) {
            if ($b.Id -eq $orphan) { continue }
            [pscustomobject]@{ Kind='Backup'; BackupId=[string]$b.Id; Name=$b.Name } |
                ConvertTo-Json -Compress
            foreach ($rp in @(Get-VBRRestorePoint -Backup $b)) {
                if (-not $rp.Id) { throw 'Restore-point identity unavailable.' }
                [pscustomobject]@{
                    Kind='Point'; BackupId=[string]$b.Id; PointId=[string]$rp.Id; Name=$rp.Name
                } | ConvertTo-Json -Compress
            }
        }
    )
    $path = Join-Path $caseDir ($Label + '-guard.jsonl')
    if (Test-Path -LiteralPath $path) { throw 'Do not overwrite a guard snapshot.' }
    $lines | Sort-Object | Set-Content -LiteralPath $path -Encoding UTF8
    $items | Export-Clixml -LiteralPath (Join-Path $caseDir ($Label + '-objects.xml'))
    Save-DbRead ($Label + '-backup-rows') @'
SELECT to_jsonb(b)::text FROM public."backup.model.backups" b
WHERE id <> '83615926-5858-4170-a1ef-e2273a8caeea' ORDER BY id;
'@
}
Save-BackupGuard 'initial'

foreach ($id in $protectedFS) {
    $b = @(Get-VBRBackup | Where-Object { $_.Id -eq $id })
    if ($b.Count -ne 1) { throw "Expected one protected backup: $id" }
    [pscustomobject]@{ Id=$id; Name=$b[0].Name; Points=@(Get-VBRRestorePoint -Backup $b[0]).Count }
}
Get-Content -LiteralPath (Join-Path $caseDir 'initial-guard.jsonl')
$caseDir
```

Expected: database `VeeamBackup`, PostgreSQL 15; exactly the **two protected** backup IDs in `connection.txt`, no orphan row; the two restore-point counts match the incident baseline unless subsequent legitimate activity is documented. If the executable path differs, record the version from the actual `ExecutablePath` instead. Do not infer the provider build from the local binary.

Before **any mutation**, create a reviewed identity manifest from these exports: each of the six machines' backup/replica ID, restore-point IDs, parent ID, repository ID, and provider file/object mapping. Reconcile it with Veeam's inventory. If the cmdlets omit a child, obtain its identity from Support/provider inventory. **Missing identities fail the gate; they are not permission to protect only the two known GUIDs.**

The guard compares all returned backups/replicas and points, not just the two named backups. It is metadata evidence, not proof that physical files/blocks are intact. Step 7 additionally requires a provider manifest comparison and restore verification. [V12 Get-VBRRestorePoint, including replica queries](https://helpcenter.veeam.com/archive/backup/120/powershell/get-vbrrestorepoint.html).

## 3. Preserve logs and expose the actual schema (read-only)

Preserve the incident-time `Svc.VeeamBackup*` logs and complete directories `C:\ProgramData\Veeam\Backup\Utils\DeleteBackup` and `C:\ProgramData\Veeam\Backup\System\CheckpointRemoval` through the Support log collection process. Do not remove old sessions or logs. Include process/session IDs, timestamps/time zone, and any original lock-error logs. Ask Support to correlate **both** GUIDs with the individual task/session, owning lock, and startup code path. If necessary, let Support request a process dump to distinguish an idle wait from a deadlock.

```powershell
Save-DbRead 'schema-columns' @'
SELECT table_schema, table_name, ordinal_position, column_name, data_type, udt_name
FROM information_schema.columns
WHERE table_schema = 'public'
ORDER BY table_name, ordinal_position;
'@
Save-DbRead 'schema-keys' @'
SELECT n.nspname, c.relname, k.conname, k.contype, pg_get_constraintdef(k.oid)
FROM pg_constraint k
JOIN pg_class c ON c.oid = k.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
ORDER BY c.relname, k.conname;
'@
Save-DbRead 'known-empty-tables' @'
SELECT 'trackedactions.locks', count(*) FROM public."backup.trackedactions.locks"
UNION ALL SELECT 'trackedactions.lockitems', count(*) FROM public."backup.trackedactions.lockitems"
UNION ALL SELECT 'trackedactions.lockstoleases', count(*) FROM public."backup.trackedactions.lockstoleases"
UNION ALL SELECT 'directbackupcheckpoints', count(*) FROM public."backup.model.backups.directbackupcheckpoints"
UNION ALL SELECT 'externalcheckpoints', count(*) FROM public."backup.model.backups.externalcheckpoints"
UNION ALL SELECT 'bsessions-null-end', count(*) FROM public.bsessions WHERE end_time IS NULL;
'@
```

Expected: the six counts reproduce the brief's zeroes, or the differences are retained for investigation. A null `end_time` filter is not a schema-independent test for all running work. Do not conclude all sessions are terminal from that query alone.

**Specific persisted record: UNKNOWN, not established by these facts.** The empty checkpoint/lock tables and absent `backup.model.backups` row rule out deleting the reported rows there; they do not identify a different queue. A GUID match in history/XML is not proof of a work item. Support must identify the exact quoted schema/table, primary key, backup/session linkage and retry predicate, or confirm that no such pending request exists. Request analysis of the dump on an isolated restored copy rather than an unbounded JSON/LIKE scan of every production table.

## 4. Database backup and rollback gate

Take an initial dump now. Repeat with a **new filename immediately before any Support-directed database write**, after Support has arranged a quiet maintenance window. Jobs, retention, restores, replica processing and provider changes must not race a repair. Stopping VeeamBackupSvc temporarily affects scheduling for other jobs: it cannot be represented as zero operational impact. If uninterrupted operation is mandatory, stop at diagnostics until Support supplies an appropriate method.

```powershell
$dump = Join-Path $caseDir ('VeeamBackup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump')
if (Test-Path -LiteralPath $dump) { throw 'Dump already exists.' }
& $pgDump @pgArgs -d VeeamBackup -F c -b -f $dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed. NO DATABASE WRITES.' }
if ((Get-Item -LiteralPath $dump).Length -eq 0) { throw 'Empty dump. NO DATABASE WRITES.' }
& $pgRestore --list $dump | Set-Content -LiteralPath ($dump + '.toc.txt') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Unreadable archive. NO DATABASE WRITES.' }
Get-FileHash -LiteralPath $dump -Algorithm SHA256 |
    Export-Csv -LiteralPath ($dump + '.sha256.csv') -NoTypeInformation
```

Expected: exit code zero for each native command, nonempty archive/table-of-contents and recorded hash. Copy the archive securely off BK01 and compare hashes. `pg_restore --list` only checks the archive catalog: **Support must test restoration on an isolated PostgreSQL 15 instance before accepting this as the repair rollback gate**. Preserve required roles, database ownership/settings and the existing Veeam encrypted configuration backup/password as well; `pg_dump` is one database, not a backup of repository data, catalog files, cluster roles or the whole server. [Veeam KB1471](https://www.veeam.com/kb1471), [PostgreSQL 15 pg_dump](https://www.postgresql.org/docs/15/app-pgdump.html).

Rollback hierarchy for a database repair:

1. Inside the repair transaction, any wrong row count/protected-object change means `ROLLBACK`, not `COMMIT`.
2. After commit, use the **case-specific inverse script** restoring all original row values/relationships, with expected current-value guards, supplied and rehearsed by Support.
3. Whole-database recovery is a last resort under Support. With application writers stopped, restore the tested pre-write archive using Support's recovery plan and verify the baseline before allowing writers. Do not paste a generic `pg_restore --clean` against the live database. A full restore rewinds every job's configuration, not just this orphan; if any other work advanced, it cannot satisfy “other backups untouched” without reconciliation.

A database restore cannot undo a cloud file deletion. No storage deletion is allowed until step 6 has a separate, tested data rollback arrangement.

## 5. STOP — needs Veeam Support: permanent orphan-specific repair

Open a VBR/Cloud Connect case with the brief and steps 2–4 artifacts. This is an operator instruction; this runbook has not contacted anyone. If the entitlement is held under T-Net's rental/provider license, T-Net may need to open/escalate the case.

Ask for the following case-specific deliverable, **before stopping services or writing SQL**:

> On VBR 12.3.2, identify whether STARTCHECKPOINTREMOVAL job UID bb70be84-d59c-419d-878c-e5d04d97405c is normal background processing or owns a pending deletion for backup 83615926-5858-4170-a1ef-e2273a8caeea. The backup row, direct/external checkpoints, tracked-action locks and bsessions with null end_time are absent. Supply the exact persistence location and primary key, the evidence linking it to this backup, and the supported corrective operation/hotfix. If SQL is required, provide a PostgreSQL 15 script for the observed schema/build plus inverse script and service procedure. Do not disable global checkpoint cleanup or change other backups, children, parent objects, repositories or jobs.

The required repair sequence is:

1. **Before:** complete the nine-object identity manifest (two FS01 objects, six children, their parent; account for any identity overlap explicitly), provider exclusion manifest, and fresh quiet-window `Save-BackupGuard 'prepatch'`. Verify both FS01 point sets and every protected child/parent. Take and validate the fresh dump. Record the service/schedule state and allow legitimate sessions to finish; no broad process-kill command.
2. **Quiesce:** follow Support's exact service list/order; PostgreSQL and Mesh remain available. If a service must stop, its recorded prior state is the rollback target. Do not stop every Veeam service indiscriminately. Never disable/remove real jobs to clear the orphan.
3. **SELECT before:** Support's script must select the exact primary-key row(s), show the link to the orphan and expected retry state, and prove the change set excludes the protected IDs and all their dependencies. Examine foreign keys, triggers, cascade effects and parent relationships. A negative check against only two GUIDs is insufficient.
4. **Write:** Support supplies the actual `UPDATE`/`DELETE` and expected row count. Use a transaction and exact keys plus old-value predicates, save the original rows, and abort on any mismatch. No name filter, blanket checkpoint/session delete, guessed status value or fabricated backup insert. No executable mutation is supplied here because the target record is unverified.
5. **SELECT after, before commit:** require the expected orphan request state and zero unauthorized changes to protected rows/relationships; otherwise roll back. Save the result in the case. Commit only the case-approved change. Apply SQL using the Support procedure in [Veeam KB1443](https://www.veeam.com/kb1443), which specifically addresses scripts supplied by Support.
6. **Restart/after:** restore the recorded services/schedules per Support. Run `Save-BackupGuard 'postpatch'` and compare as below. Review fresh service/checkpoint/deletion logs for the exact orphan GUID and the identified request/session. Historical log matches do not mean a new retry. Prove the identified queue row is gone/terminal using Support's exact SELECT.
7. **Persistence check:** during the maintenance window, perform one Support-directed VeeamBackupSvc restart and repeat the targeted SELECT/log checks. Observe for at least the previously seen <5-second respawn interval and the longer retry interval identified by Support. Recheck after the next normal maintenance cycle. A normal background worker may still exist. If the orphan-specific request/lock returns, preserve evidence and roll back/escalate; do not repeat speculative deletions.

```powershell
# After the two named snapshots exist; no database mutation here.
foreach ($suffix in @('guard.jsonl', 'backup-rows.txt')) {
    $before = @(Get-Content -LiteralPath (Join-Path $caseDir ('prepatch-' + $suffix)))
    $after  = @(Get-Content -LiteralPath (Join-Path $caseDir ('postpatch-' + $suffix)))
    $diff = @(Compare-Object -ReferenceObject $before -DifferenceObject $after)
    if ($diff.Count) {
        $diff | Out-File -LiteralPath (Join-Path $caseDir ('DIFF-' + $suffix))
        throw "Protected inventory changed: $suffix. STOP and investigate with Support."
    }
}
'PASS: captured non-orphan backup rows and backup/restore-point identities unchanged.'
```

Expected: no diff. Raw row differences caused by a timestamp/USN change must be explained field by field by Support; do not silently discard them or replace the baseline. Also compare job/repository definitions and schedules to the initial exports; any maintenance-only schedule change must be reversed. The metadata PASS does not replace physical-data checks.

The parked `E:\VBRCatalog\Index\Machines\Kor-FS01.OLD-20261007` is shared by machine name, not proven exclusive to the orphan. Leave it in place during evidence collection. Support must decide whether to restore it after resolving catalog unpublication. Before a rename, verify the original destination is absent, capture directory/file manifests, quiesce the catalog writer, and confirm the directory's ownership/use by the real FS01 backups. Afterward compare manifests and verify real FS01 catalog search. Rollback is the reverse rename only if neither path was recreated/modified in the meantime; otherwise Support must reconcile both. Do not merge, overwrite or delete either directory.

## 6. Reclaim or definitively release the storage

### 6A. Determine whether a tenant-only path actually exists

After step 5, have Support inspect whether a supported exact-orphan backup object can be recovered from existing metadata or a prior configuration **in an isolated investigation environment**. Do not restore an old production database merely to resurrect this backup. Do not rebuild VBM files or register a made-up backup row.

If Support recovers a valid object on BK01 and validates its complete storage manifest and parent/child scope, the ordinary tenant deletion command is:

```powershell
# CONDITIONAL ONLY: object recovered and exact storage scope approved by Support.
$target = @(Get-VBRBackup | Where-Object { $_.Id -eq $orphan })
if ($target.Count -ne 1) { throw 'No unique recovered orphan. STOP; do not substitute a parent/name.' }
if ($protectedFS -contains $target[0].Id) { throw 'Protected target.' }
# The reviewed six-child/parent exclusion and storage rollback gates below are mandatory too.
# Save-BackupGuard 'predelete' must already exist, with provider/file manifests attached.
Remove-VBRBackup -Backup $target[0] -FromDisk -Confirm
```

**Before:** quiet window; fresh tested DB dump; full protected manifest and no shared file/object dependencies; restorable copy/snapshot of only the deletion scope and metadata, confirmed by Support. **After:** command completes successfully, exact orphan disappears, its files/accounting change as intended, `Save-BackupGuard 'postdelete'` matches `predelete` using the comparison block with those labels, and provider manifests/restore checks pass. **Rollback:** deletion has no built-in undo; restore the separate data copy and metadata through Support's procedure. The DB dump alone cannot recover data. If the command hangs again, preserve its session/logs and stop; do not kill/retry in a loop. If four-eyes authorization prevents PowerShell deletion, use the supported authorized console workflow; do not disable the control. Config-only removal of Cloud Connect backups requires Support; `-FromDBIfSPUnavailable` is not evidence of disk reclaim. [V12 Remove-VBRBackup](https://helpcenter.veeam.com/archive/backup/120/powershell/remove-vbrbackup.html).

The Files view is a **conditional capability, not a validated orphan-repair recipe**: v12 documents tenant file access, SOBR read-only restrictions and deletion capability with insider protection. The Cloud Connect deletion instructions also warn against manual backup-file deletion. Thus, Files-view deletion for this corrupt, unidentified chain needs case-specific Support approval, exact file mapping and data rollback first. It also needs an interactive console; the available Mesh SYSTEM command channel is not such a console. No documented headless v12 orphan-file purge cmdlet was verified. [V12 Cloud Connect limitations](https://helpcenter.veeam.com/archive/backup/120/cloud/cloud_connect_limitations.html), [V12 deleting from disk](https://helpcenter.veeam.com/archive/backup/120/cloud/delete_backup_from_disk.html).

**STOP — needs provider** if there is no recoverable tenant backup object and no supported, accessible, precisely identified file-deletion path. This is the boundary reached by the currently supplied evidence. Repeated rescans, retention changes and “forget” operations are not a demonstrated solution for the missing object.

### 6B. T-Net: identify the exact resource and measure it

Run on the **T-Net Cloud Connect server**, using the provider's Veeam session. T-Net supplies its real KOR tenant name; it is not necessarily `KOR`. Read inventory first:

```powershell
$ErrorActionPreference = 'Stop'
Import-Module Veeam.Backup.PowerShell
Connect-VBRServer -Server localhost
Get-VBRCloudTenant | Select-Object Id,Name
$tenantName = Read-Host 'Exact KOR tenant name from the reviewed provider inventory'
$tenant = @(Get-VBRCloudTenant -Name $tenantName)
if ($tenant.Count -ne 1) { throw 'Tenant is not unique.' }
$tenant[0].Resources | Format-List *
Get-VBRCloudTenantBackup -Tenant $tenant[0] -Id '83615926-5858-4170-a1ef-e2273a8caeea' |
    Format-List *
```

Expected: exactly one verified KOR tenant; record its GUID. Zero returned backups does not establish physical absence: v12's `Get-VBRCloudTenantBackup` only returns non-encrypted backups of the documented tenant types. Provider-side IDs may require explicit correlation to the tenant backup GUID. [V12 Get-VBRCloudTenantBackup](https://helpcenter.veeam.com/archive/backup/120/powershell/get-vbrcloudtenantbackup.html).

T-Net must record: tenant GUID; resource/repository GUID; tenant quota path; backing type (simple filesystem, SOBR and every extent/tier, or object storage); backup/chain IDs; precise files or object/checkpoint namespace; logical bytes; allocated bytes; immutability expiry; and recycle-bin membership. Map this through the failed DeleteBackup logs and provider metadata to **`83615926-5858-4170-a1ef-e2273a8caeea`**, not merely a folder named Kor-FS01 or a directory of roughly the right size. Prove no overlap with the two real FS01 chains, six children/replicas, parent or shared blocks. Include their before/after manifests in the case.

A supported **accounting refresh**, not deletion, is available on the provider. Select the exact resource by both friendly name and provider repository GUID, and preserve its counters:

```powershell
$repoId = [guid](Read-Host 'Verified provider repository GUID for Kor-Replication')
$resource = @($tenant[0].Resources | Where-Object {
    $_.RepositoryFriendlyName -eq 'Kor-Replication' -and $_.Repository.Id -eq $repoId
})
if ($resource.Count -ne 1) { throw 'Resource not unique; STOP.' }
$resource[0] | Format-List RepositoryFriendlyName,RepositoryQuotaPath,RepositoryQuota,UsedSpace
# Save this output in the case before refreshing.
Sync-VBRCloudTenantResource -CloudTenantResource $resource[0]
# Fetch new objects; do not report the cached resource as an after measurement.
$tenantAfter = @(Get-VBRCloudTenant -Name $tenantName)
if ($tenantAfter.Count -ne 1 -or $tenantAfter[0].Id -ne $tenant[0].Id) { throw 'Tenant changed.' }
$resourceAfter = @($tenantAfter[0].Resources | Where-Object {
    $_.RepositoryFriendlyName -eq 'Kor-Replication' -and $_.Repository.Id -eq $repoId
})
if ($resourceAfter.Count -ne 1) { throw 'Resource changed.' }
$resourceAfter[0] | Format-List RepositoryFriendlyName,RepositoryQuotaPath,RepositoryQuota,UsedSpace
```

Expected: refresh succeeds; capture the new counters with units and UTC time. This updates usage reporting and does not reclaim files. No data rollback is applicable; if counters are wrong, retain both measurements and escalate, never write quota/used-space fields manually. [V12 Sync-VBRCloudTenantResource](https://helpcenter.veeam.com/archive/backup/120/powershell/sync-vbrcloudtenantresource.html), [V12 resource properties](https://helpcenter.veeam.com/archive/backup/120/powershell/vbrcloudtenantresource.html).

### 6C. Provider decision and destructive-action gates

| Finding | Supported route / required provider action | Before, after and rollback |
| --- | --- | --- |
| Data is already deleted; only quota counters are stale | T-Net refreshes the exact resource as in 6B and supplies backing-store evidence | Before/after counters and unchanged manifests; no file deletion or SQL counter editing |
| Files are in insider-protection recycle bin | Tenant cannot operate on these files. T-Net confirms retention/expiry; allow normal expiry, or obtain Support's exact scope-specific procedure if early cleanup is required | Before: file/chain mapping, quota and allocated bytes. After: quota release separately from physical deletion. Rollback after physical purge requires a separate recoverable copy; do not purge without it |
| Identified orphan files on a simple filesystem; tenant cannot safely remove them | **STOP — needs provider and Support.** T-Net obtains the exact file-manifest-based cleanup and metadata reconciliation procedure for its build | Before: tenant/repo identity, excludes all protected paths/files, independent data backup plus provider DB dump. After: only manifest-listed files gone, protected files/points unchanged, counters refreshed. Rollback: restore those files/metadata with Support; no parent/tenant-directory recursive delete |
| SOBR/capacity/archive tier or direct object storage | **STOP — needs provider and Support.** Correlate all tiers/checkpoints/references and obtain the backend-specific Veeam cleanup | Before: all-tier reference/immutability analysis and recovery plan. After: logical references, quota and actual allocated bytes checked separately. Rollback needs the supported data/metadata recovery plan. Never delete an inferred object prefix or a shared block |
| Immutable data | T-Net establishes the actual expiration and supported subsequent cleanup | No retention/immutability bypass. An expiry date is a pending condition, not evidence of reclaimed space |
| No metadata can identify ownership safely | **STOP — needs provider and Support investigation** | No destruction; preserve data and protect the known chains until ownership/exclusivity is proved |

For insider protection, Veeam explicitly states recycle-bin data **does not consume tenant quota**, while it still occupies provider storage. Thus it cannot by itself explain a confirmed stale tenant-quota charge; establish which counter the reported 2,136.6 GB represents. Moving data out of quota can satisfy **release** but does not prove physical space reclamation. [V12 Insider Protection](https://helpcenter.veeam.com/archive/backup/120/cloud/cloud_connect_bin.html).

There is **no verified universal v12.3.2 provider purge command for this metadata-less orphan** in the sources reviewed. The exact destructive provider command and object/path set remain case-specific deliverables, not something this runbook can honestly invent. In particular:

- `Remove-VBRCloudTenantBackup` is new in **13.0.1**; do not use a current v13 example, including `Get-VBRCloudTenantBackup -All`, as a v12 runbook. Do not upgrade merely to try it. [V13.0.1 new cmdlets](https://helpcenter.veeam.com/docs/vbr/powershell/new_features_v13.0.1.html).
- `Forget-VBRCloudUnavailableBackupFile` removes database references to **missing** files; it does not remove existing files from disk. It is not a 13.3 TB garbage collector. If Support needs it after provider cleanup, use only reviewed exact file objects, not all unavailable tenant files or `-IncludeDependentFiles` indiscriminately. [Veeam KB3187](https://www.veeam.com/kb3187).
- Do not remove the tenant, repository, per-VM parent or entire Kor-VMs-New directory. Do not launch broad background retention against all tenant backups as an orphan-specific repair.

## 7. Tenant refresh and acceptance checks

After the provider repair, arrange the documented quiet window for affected cloud jobs, preserving their initial enabled/scheduled state. Select only T-Net and rescan the **service provider**, distinct from the repository rescan already attempted:

```powershell
# On KOR-BK01, in the tenant Veeam PowerShell session.
Get-VBRCloudProvider | Format-List Id,Name
$providerId = [guid](Read-Host 'Reviewed provider GUID for remote.t-net.ca:6180')
$provider = @(Get-VBRCloudProvider -Id $providerId)
if ($provider.Count -ne 1) { throw 'Provider not unique.' }
# Verify the selected provider against the step 2 export before proceeding.
Rescan-VBREntity -Entity $provider[0] -Wait
Save-BackupGuard 'final'
```

Expected: successful rescan, no orphan reappearance unless Support explicitly restored it for controlled deletion, and all protected identities/point sets preserved. Compare `final` with the quiet-window baseline using step 5's comparison block with the appropriate labels. Restore the original schedules; compare jobs/repositories to their before exports. A rescan updates metadata; it has no file-deletion rollback. If it unexpectedly changes protected references, stop and reconcile with Support/provider before further action. [V12 Rescan-VBREntity](https://helpcenter.veeam.com/archive/backup/120/powershell/rescan-vbrentity.html), [KB3187 tenant rescan procedure](https://www.veeam.com/kb3187).

Close the incident only when all of these are recorded:

1. **Respawn cause resolved or disproved:** Support names the actual table/key and final state if a pending record existed, or documents normal worker behavior with evidence that no orphan deletion/lock remains. Verify across service restart and the relevant retry/maintenance cycle. An absent process is not the acceptance criterion.
2. **Protected configuration:** the two real FS01 GUIDs, every original restore-point ID, all six discovered child/replica identities and their parent remain present; job/repository definitions are unchanged. Explain legitimate new points/retention outside the quiet window rather than relying only on counts.
3. **Protected data:** T-Net's file/object manifests show no unauthorized change, and representative restores from both real FS01 backups succeed to an alternate location. Verify the six replicas with an isolated/non-production recovery test appropriate to their actual type, or verify restores if they are backup children. Do not perform production failover. Metadata counts alone do not prove recoverability; representative tests are evidence, not an exhaustive block-integrity proof.
4. **Space outcome:** record tenant quota and provider physical allocation separately, in the same units and measurement window. With no other changes, a quota release of 13,344.8 GB would move the displayed 2,136.6 GB free to approximately **15,481.4 GB**. Do not require this exact physical-disk delta: shared/reflink/deduplicated blocks, recycle-bin protection and concurrent activity can change it. T-Net must account for any discrepancy against the orphan manifest.
5. **Definitive release alternative:** provider confirms the orphan no longer consumes KOR quota, with refreshed counters and exact retained bytes/expiry if storage remains. A quota increase, a successful rescan, a queued delete or a promised future purge is not proof that the orphan was released.

If those facts cannot be established, report the incident as **unresolved**, identifying the outstanding Support/provider deliverable. The missing exact PostgreSQL row and backend-specific deletion procedure are explicit gaps in the available evidence, not completed repairs.
