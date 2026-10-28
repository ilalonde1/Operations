# NetworkOps server maintenance scripts (27 Oct 2026 window)

Moved from operations-e6's session scratchpad so nothing that matters lives only there. Each script's header says
where it runs and what it changes. Run scripts on servers through NetworkOps (`netops run --script <file> --hosts
<bare name>`), never over raw WinRM/DCOM.

| File | What |
|---|---|
| sa-read.ps1 | Read-only SQL as sa (DPAPI cred file decrypted in memory, never printed); refuses any write verb |
| sa-shrink-emaillog.ps1 | The one-off KorEmailIndex log shrink (done 27 Oct 17:11, board row 36) |
| app01-stage-cu.ps1 | Downloads + verifies a SQL CU from the Update Catalog on APP01 (CU27 staged 27 Oct) |
| RUNBOOK-SQL2022-CU27-APP01.md | The CU runbook Ian ran from |
| app01-restart.ps1 | Restarts APP01 via a one-off SYSTEM task 60 s out (NetworkOps lives on APP01) |
| app01-readback.ps1 / fs01-readback.ps1 / dc01-readback.ps1 / dc01-kb.ps1 | Read-only checks after a server restart |
| bk01-jobs-now.ps1 | Read-only: every Veeam job's enabled/running state on BK01 |
| mcp-call.ps1 | HTTP JSON-RPC to APP01 /mcp with `netops token`, cert pinned |
| bank-cards.ps1 / accept-cards.ps1 | Bank and accept NetworkOps knowledge cards through the service's own API |
| cards-*.json / card-shrink-*.json | The cards banked 27 Oct (cards 14-33) |
| fs01-wrapped-exe-census.ps1 | Read-only on FS01: zip-wrapped executables in Library\03 Programs, top vs nested per product folder (28 Oct, action 11530) |
| make-remote-unwrap.ps1 | Builds a netops-run copy of tools\Expand-WrappedExecutables.ps1 with its parameters fixed in and `exit` turned into `return` (a script that exits publishes no result) |
| make-fs01-unwrap-task.ps1 | From that copy, builds a launcher (a one-off SYSTEM task under E:\IT\netops-unwrap\<stamp>, for runs longer than netops' 90 s) and a status reader (shared read of result.txt; unregisters the task once done.txt exists). Used 28 Oct: 1,271 nested installer exes restored on FS01 (actions 11553, 11724-11726) |

Evidence and decisions live in KorStandards `oversight` (board rows 30-46, Mistake 69) and NetworkOps.KnowledgeCards.
