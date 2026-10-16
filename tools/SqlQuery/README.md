# SqlQuery — the session's SQL helpers

`oq.py` runs a query or a `.sql` file against KorOpportunitiesDb through `sqlcmd`, using the app's own
connection string from the `KOR_OPPORTUNITIES_OPPORTUNITIESDB` environment variable. No credential is
stored here. `oqwide.py` is the same with unlimited column width, for long text.

    python -I tools/SqlQuery/oq.py -q "SELECT 1"
    python -I tools/SqlQuery/oq.py -i C:/path/to/file.sql

Both pass `-l 15 -t 120` (login 15 s, query 120 s), so a VPN stall is an error, never a hang. That is
what the bash guard's SQL rule (rule 6 in `tools/claude_bash_guard.py`) relies on: a raw `sqlcmd`
without `-t` is denied; these helpers are allowed.

`sqlcmd` defaults `QUOTED_IDENTIFIER` OFF. Any script that writes to a table with a filtered index
(IntelPerson has one) must start with `SET QUOTED_IDENTIFIER ON;`.
