# BRIEF 4 — Three places the app shows something untrue when something fails

Covers register items **14, 16, 17**. Each fix ships with the gate that locks the *decision*, not the UI.

**Do NOT run `dotnet build`/`dotnet test`.** I run them here, and I run each new test against the
**pre-fix** code to confirm it fails there. One gate in brief 3 passed either way and is recorded as
not a gate. **Do NOT run any destructive git operation.**

## 1 · An AI failure renders as a success — item 14
`Services/AppAiService.cs` returns error text as an ordinary string (`:72`, `:135`, `:149`). Both
callers treat any non-empty string as a good answer — `PursuitBriefWindow.Approach.cs:65-77` stamps
it *"Drafted HH:mm from live intel"*; `Controls/AiQueryPanel.xaml.cs:111` appends it to `_history` as
an assistant turn, poisoning every later question. Fix the cause: make an error unrepresentable as an
answer. No consumers outside `Kor.Operations.App`. Add a cancel affordance — `:31` is a 4-minute
timeout against `CancellationToken.None`.

## 2 · A failed permission check force-shows salary data — item 16
`HomeWindow.xaml.cs` — bare `catch` sets `FinancialsTileHost`, `CompensationTileHost` and five more
to `Visible`. **Fails open** on the lookup that decides entitlement. Collapse those two; log the
exception.

## 3 · Raw exception text on screen — item 17
Org dossier **only** — the register says nine sites, the real count is 72.
`OrgDossierViewModel.cs:501,556`, the outer catch, and `OrgDossierView.xaml:925`. One shared mapper;
`SqlException`/`OdbcException` read as a VPN problem, not a login failure naming an account.

## 4 · The gates
All three live in WPF code-behind, which does not test. Move each **decision** out of the UI into
something callable, then gate the decision. No UI tests.

- **4.1** an error return cannot be read as a successful answer; a failed response is not appended to history.
- **4.2** extract the entitlement decision from `HomeWindow` into a pure function; assert Financials
  and Compensation are **collapsed** when the lookup fails. Most important — it is a fail-open on data access.
- **4.3** `SqlException`/`OdbcException` map to the VPN sentence; no mapped message contains a type,
  interface or method name; the original still reaches the logger.

Tests in `Kor.Operations.App/Kor.Transmittals.App.Tests/`.

## 5 · What I need back
Per item **held** / **did not hold**. Then the AI result shape and why, and **for each gate, the one
line of production code that if reverted makes it fail.** I revert each and check. If you think a
gate cannot fail on its defect, say so — that is a legitimate answer and far better than a test that
always passes.
