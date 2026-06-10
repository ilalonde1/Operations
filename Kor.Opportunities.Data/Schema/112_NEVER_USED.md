# Migration 112 — number never used

There is no `112_*.sql` and there never was. The number was reserved by
`docs/BD-UI-Plan-2026-06-08.md` for the BdReportAuditLog migration, but
the 2026-06-09 audit remediation applied 113–120 before the UI build
started, so the audit-log migration takes the next free number instead
(see the plan doc). Nothing is missing — don't hunt for a lost file.
