# CODEX — ARCHITECTURE MAP: RE-AUDIT OF THE FIX COMMIT

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side. Your test runner consistently hangs here for
> 15+ minutes and spawns orphan dotnet processes that lock build artifacts. Read, reason, report.
>
> **Do NOT run any destructive git operation.** **Do NOT regenerate `docs/architecture/architecture.json`.**
> `git log`, `git show`, `git diff` for reading only.

You are a **hostile reviewer** again. Cite `file:line` or do not claim it.

---

## What happened since your last audit

Your eight findings were all confirmed against source and all fixed — the fixes are in **`fcbe7ce4`**,
which is the only commit under review. `git show fcbe7ce4` for the whole of it.

**Do not re-litigate the original eight.** They are verified fixed on the real repository:
determinism holds byte-for-byte across runs, the mapper no longer appears in its own model, verbs
went 46 → 53, the format matrix dropped test types, `SqlTimeouts` is owned by four projects, cycles
are found at any depth, and the staleness gate now fails on an added file and names it.

**Attack the NEW code and the NEW tests.** That is where the risk is, and it is the least reviewed
code in the component.

## What the fix work already cost — the class of defect to hunt

Four things went wrong that your first audit did not catch, and **three of them were invisible to
reading**. They only appeared when the code was RUN against inputs it had never been given:

- The test project **did not compile** — a new `TestRepo.File(…)` helper shadows `System.IO.File`.
- `GraphBuilder.Layered` called `.Max()` on an empty list, so extracting **any repository with no
  drawing-intake types** — every repository except this one — threw and took the whole extraction
  down with it.
- **Cycle dedup had never worked**: it keyed on the loop's members *sorted*, but a closed loop
  repeats its first node and each rotation repeats a different one, so a 14-project ring was
  reported 14 times. The depth-12 cap had hidden it for as long as it existed.
- The `Compile Include` fix turned three deliberately **shared** files into 100% duplicates.

So: **reason about inputs this code has never been given.** Empty collections, a single project, a
project with no source, a type declared in a file the compiler does not compile, a cycle of length
one, a repository that is not this one. That is where the first audit was blind, and reading alone
is what made it blind.

## Where the new code is thin

Named because they are non-obvious, not as a checklist. Go where the diff takes you.

**There are now two implementations of "how many source files does this project have."** The
extractor has one and `ArchitectureMapIsCurrentTests` has another, in a different project, with no
shared code. They must agree exactly or the gate fails spuriously forever or passes when it should
not. Compare their skip rules, their link handling, their case sensitivity, their treatment of
generated files. **This is the single most likely place for a real defect in the commit.**

**Partial-type aggregation.** One `ArchType` now represents several declarations and `File` carries
all the parts. What is the separator, and what happens to a path containing it? What happens when
the parts disagree — different accessibility, different base types, a partial split across two
projects by a `Compile Include` link, a partial whose parts sit in different namespaces? What does
the similarity comparison now receive, and is the order of parts stable for the right reason rather
than by luck?

**`Compile Include` handling.** Real project files use globs, `Compile Remove`, `Update`, wildcards,
`Link` metadata, `..\..\` paths and conditions. Which of those does the new code mishandle, and does
any of them double-count a file or attribute one to a project that does not compile it?

**The architecture-tool exclusion is now a derived prefix match.** Establish what it actually
matches and what a plausible future project name would do to it.

**Text decoding.** Strict UTF-8 with a Latin-1 fallback is now the read path. A file that is invalid
UTF-8 but valid Latin-1 decodes to something the C# compiler would not agree with. Does that reach
the model bytes, and is the fallback deterministic for a file that is invalid in both?

**Consistency between the model and the pages.** Format edges are kept in the model but excluded
from the matrices when a test owns them. Is every other page that reports on formats or types
consistent with that decision — the master sheet's "types by role" included?

**Renderer state restore.** The automation switches are now saved and restored through `dynamic`.
What are their actual COM types, does the round-trip preserve them, and what happens on the path
where the document was already closed?

**The 20 tests.** Which of them would still pass if its subject were broken? The fixture-based ones
are new and nothing tests them. Is there an assertion that is true by construction rather than by
the code being right?

## What to report

    CRITICAL   wrong output, silent data loss, a crash on a plausible input, or a number a person
               would act on and be misled by
    MATERIAL   a real defect with a plausible trigger
    MINOR      worth fixing, no consequence today

`file:line`, what is wrong, the trigger, the consequence. **No suggested patches.**

Then:

- **Exactly one ship-blocker.**
- **What input would break this that nobody has tried.** Be concrete: name the repository shape or
  the file that does it. This is the section that found the real bugs last time.
- Anything you believe is right — one short paragraph, last, only if you mean it.

Write to `docs/codex/CODEX-ARCHITECTURE-MAP-REAUDIT-RESPONSE.md`. Ping when done.
