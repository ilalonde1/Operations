# BdQueueDrainBatchGenerate

Generates input batches for the Sonnet drain queues with INPUT-QUALITY
filters that prevent garbled / multi-entity / all-caps names from
reaching Sonnet.

Background: On 2026-06-07 an ad-hoc batch generation pulled ~60
garbled Deltek legacy names ("AB DALLA-LANA ARCHITECT INC, ET C")
into an orgs drain batch. Sonnet ran in circles trying to research
them and burned 5 hours of Claude subscription time with zero output.
This tool captures the input-quality filters in version control so
that failure cannot silently recur.

## Usage

```powershell
# Generate the next orgs batch (e.g., batch-006 with 200 candidates)
pwsh ./Generate-Batch.ps1 -Kind orgs -BatchNumber 6

# Smaller batch for a focused drain
pwsh ./Generate-Batch.ps1 -Kind people -BatchNumber 8 -Take 50

# Custom output dir
pwsh ./Generate-Batch.ps1 -Kind projects -BatchNumber 5 -OutDir D:\temp\
```

## Supported kinds

- `orgs` — pulls stale active high-value CanonicalOrg rows for FirmNarrative refresh
- `projects` — pulls eligible MPI rows missing a ProjectBrief enrichment
- `people` — pulls newly-surfaced IntelProjectKeyPerson rows not yet in IntelPerson
- `proponents` — pulls active MPI rows with NULL ProponentName

## After generating

Launch Sonnet at the queue directory. The PROMPT.md auto-discovery
will find the new batch automatically:

```powershell
cd C:\ProgramData\KorOperations\QueueDrain\<kind>
claude --model claude-sonnet-4-6 --permission-mode bypassPermissions
```

To check progress from another shell while Sonnet is running:

```powershell
Get-Content C:\ProgramData\KorOperations\QueueDrain\<kind>\outputs\_status.json | ConvertFrom-Json | Format-List
```

When the drain finishes:

```powershell
dotnet run --project tools/BdQueueDrainIngest -- --kind <kind>
```
