# APP01 SQL Server update — runbook for Ian (tonight's APP01 slot)

**Instance:** KOR-APP01\SQLEXPRESS (Developer Edition). **Today:** 16.0.1210.3 = SQL 2022 RTM-GDR, Oct 2026 security (KB5128493).
**Installing:** **KB5126934 — security update for SQL Server 2022 CU27, 13 Oct 2026 → build 16.0.4305.5** (CU27 plus the October security fix; Microsoft lists CU27 as the latest CU).
Sources: build list https://support.microsoft.com/servicing/sql/sql-server-2022/kb5023451-build-versions · KB https://support.microsoft.com/servicing/sql/sql-server-2022/cumulative-update/kb5126934-cu27-gdr-october-2026
Note: this moves the instance from the GDR line to the CU line. From then on it takes CUs, not the GDR-only patches.

## 1. The file (already staged on APP01; you only run it)
`D:\Installers\SQL2022-CU27-KB5126934\SQLServer2022-KB5126934-x64.exe`
- Source: Microsoft Update Catalog (https://www.catalog.update.microsoft.com/Search.aspx?q=KB5126934), downloaded on APP01 itself: `https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/secu/2026/10/sqlserver2022-kb5126934-x64_01742bd8accd82ea7314264b0a5608e28bdd8af0.exe (action 11418, 15:05)`
- Size: 491,045,896 bytes. The catalog lists 491,045,896 → **matches**
- SHA-256: `8F1A7F0AC5F47FF98F5825687C8CCD897563AA835915F2271F908273E962E7E2`. Microsoft's KB publishes 8F1A7F0AC5F47FF98F5825687C8CCD897563AA835915F2271F908273E962E7E2 → **matches**
- Signature: **Valid** — CN=Microsoft Corporation, issued by Microsoft Code Signing PCA 2024 (read back, action 11420)

## 2. Before you start (I do these and tell you "ready")
- Last night's FULL backups, read from msdb: all 18 user databases 01:47–01:54, and the 3 system databases 01:33, to \\Kor-fs01\Library\ADMIN\SQL SERVER\BACKUPS. Every job succeeded (30 of 30 nights).
- The Virtual Drafter takes a copy-only backup of KorStandards just before and reads it back.
- Windows updates are already installed on APP01, without a restart (my step). The floor is clear: no session is using SQL, NetworkOps or the app.

## 3. Steps on APP01 (RDP as an administrator)
1. In Explorer, open `D:\Installers\SQL2022-CU27-KB5126934\`. Right-click `SQLServer2022-KB5126934-x64.exe` and choose **Run as administrator**. It extracts for about 1 minute.
2. **License Terms:** tick *I accept*, then **Next**.
3. **Select Features:** the instance **SQLEXPRESS** is ticked with all its features. Leave it as it is, then **Next**.
4. **Check Files In Use:** it lists the SQL services it will stop: SQL Server, SQL Agent, Launchpad, Full-text, PolyBase and Telemetry. That's expected. **Next**.
5. **Ready to Update:** **Update**. It takes about 10–15 minutes, and SQL is down for that time.
6. **Complete:** expect *"Passed"*, possibly with *"a restart is required"*. **Close**. Don't restart yourself: I restart APP01 next and read everything back.

**Optional, only if the slot has room:** remove PolyBase. 0 of 21 databases use it, and the installed setup can do it without media.
Settings → Apps → *Microsoft SQL Server 2022 (64-bit)* → Uninstall → **Remove** → instance SQLEXPRESS → tick only **PolyBase Query Service for External Data** → Next → Remove (about 5 minutes).

## 4. Time and what's down
About 15–20 minutes of your time, then my restart (about 5–10 minutes). SQL, NetworkOps, the Opportunities Worker and the KOR Operations app are down from step 1 until APP01 is back.

## 5. Read-back (mine, after the restart)
- `SELECT @@VERSION` shows **16.0.4305.5**.
- Services Running: MSSQL$SQLEXPRESS, SQLAgent$SQLEXPRESS, SQLBrowser (PolyBase services gone if removed).
- NetworkOps /api/ping answers; the Opportunities Worker service is Running; one netops run returns.
- Tonight's 01:47 backup job runs on the new build (checked in the morning).

## 6. Rollback
Settings → Apps → Installed updates (or Control Panel → Programs and Features → *View installed updates*). Select **Security Update for SQL Server 2022 CU27 (KB5126934)**, then **Uninstall**. The instance returns to the previous build, 16.0.1210.3. Restart APP01 afterwards. The databases are untouched either way, and last night's backups are on FS01.
