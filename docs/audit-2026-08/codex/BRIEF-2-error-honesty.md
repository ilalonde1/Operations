# BRIEF 2 — Stop the app reporting failure as success

Covers register items **14, 16, 17, 19**.

---

**IMPORTANT — do NOT run `dotnet build` or `dotnet test` after applying.** Verification happens on
the dev box on my side; your test runner hangs for 15+ minutes here and burns credits. Apply the
edits, grep your own diff if useful, then report. Stop there.

**Do NOT run any destructive git operation** — no `git clean`, no `git reset --hard`, no force push.

---

## 1 · Why this exists

Four places where the app tells the user something worked, or shows something it should not, when
the opposite is true. All four are on the demo click-path. This is the audit's systemic finding #1 —
*the system reports success it has not earned* — and these are its cheapest instances.

## 2 · The AI failure that renders as a success — item 14

`Kor.Operations.App/Services/AppAiService.cs` **returns error text as an ordinary result** instead of
throwing: `:72` (`"AI is not configured…"`), `:135` (`"AI service returned HTTP …"`), `:149`
(`"Unable to reach AI service: …"`). Its `catch` blocks feed those returns.

Both call sites treat any non-empty string as a successful answer:

- `BusinessDevelopment/Workspace/PursuitBriefWindow.Approach.cs:65-77` — only guards
  `IsNullOrWhiteSpace`, so `"Unable to reach AI service: No such host is known."` is rendered as the
  drafted approach and stamped **`Drafted 14:32 from live intel`**.
- `Controls/AiQueryPanel.xaml.cs:111` — worse, and **not in the register**: the error string is
  appended to `_history` as an `assistant` turn, so the failure becomes part of the conversation the
  model sees on every later question.

**Fix the cause, not the symptom.** The register's suggestion — match the three error prefixes at the
call sites — leaves failure representable as success and makes the fourth prefix a new bug. Change
`AppAiService` so a caller *cannot* mistake an error for an answer: return something that carries
success/failure distinctly, and update both call sites to branch on it. `AppAiService`/`IAppAiService`
have **no consumers outside `Kor.Operations.App`** — I checked — so this is contained to three files.

Then, at the two call sites: render a real failure state (not the success line, not the timestamp),
and in `AiQueryPanel` do **not** add a failed response to `_history`.

Also add a cancel affordance to the Pursuit Brief draft. `AppAiService.cs:31` gives the MCP client a
**4-minute** timeout and the call passes `CancellationToken.None`, so a down gateway is four minutes
of dead UI with no spinner and no way out.

## 3 · The catch that force-shows salary data — item 16

`HomeWindow.xaml.cs` — the tile-visibility method ends in a bare `catch` that sets
`FinancialsTileHost`, `CompensationTileHost` and five more hosts to `Visible`.

It **fails open**: the `try` decides which tiles a user is entitled to see, and the failure path shows
them anyway. The realistic trigger is launching off-LAN before the VPN is up, which is exactly the
condition at a client site.

Collapse `FinancialsTileHost` and `CompensationTileHost` on the failure path instead. Leave the other
five as they are — this brief is about the two that carry salary and financial data. Log the
exception; today it is swallowed silently.

## 4 · Raw exception text on screen — item 17

**Scope this to the org dossier only.** The register calls it nine sites; the real count across the
App is **72**, which is a quarter's work, not this fortnight's. The dossier is the one on the demo
path.

- `Opportunities/OrgDossierViewModel.cs:501` puts
  `"IDeltekClientContextService.LoadAsync returned null - Clendor has no row for this ClientId on the App's ODBC connection."`
  on screen — an interface name and a method name, to a client.
- `:556` renders `ex.GetType().Name + ": " + ex.Message`.
- The outer catch sets `StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}"`.
- `Opportunities/OrgDossierView.xaml:925` binds that text into the **DELTEK SNAPSHOT** panel.

Add one small shared mapper — exception → sentence a non-engineer can act on — and use it at those
sites. `SqlException` and `OdbcException` should read as a connection problem naming the VPN, not as
a login failure naming an account. Keep the full detail going to the existing logger. Put the mapper
somewhere the other 68 sites can adopt it later without moving it.

## 5 · The crash on "Open" — item 19

`EmailSearchWindow.xaml.cs:412` — the Outlook launch is wrapped in a try; the shell-open **fallback
inside that catch is not**. An unreachable or deleted path throws out of an event handler and takes
the window down. It is directly on the demo path. Wrap it and show a message box naming the file.

## 6 · What I need back

**Verify before you fix.** For each of the four items, confirm the thing described is present before
changing it, and report per item:

- **held** — cite `file:line` as *you* found it, and say what you changed.
- **did not hold** — say why, cite what is there instead, and **do not invent a fix**.

For §2 specifically, tell me what shape you chose for the AI result and why, since I did not specify
it. Then one closing paragraph: anything you touched that is not on this list.
