# Audit Scope — 2026-08-20

Owner-defined boundary. The audit covers the **Operations Brain** — the integrated suite — plus
the engineering-side tooling that feeds it. Standalone utilities that merely share a disk are out.

## In scope

| area | what |
|---|---|
| `Operations` | The WPF suite (14 feature areas), MCP AI layer, BD Brain, FileSync, EmailFiler, `tools/` |
| `Redirector\Kor.Transmittals.Redirector` | **Confirmed in scope by owner 2026-08-20: it tracks the transmittal links, so it is part of the Operations Brain.** The Info Exchange replacement. |
| `KOR.Drafter` | Revit bridge, KorStandards corpus, drafting rules |
| `KOR.RevitTools` | Revit add-ins |
| `KOR Inspections Bookings` | Tier 2 |
| `App Demo Maker`, `SAFE` | Tier 3 — inventory line only |

## Out of scope — excluded by owner 2026-08-20

**Not part of the Operations Brain.** Standalone tools and sites; removed from all suite-wide
counts and from the module-audit queue.

- `Contract Radar` (111 .cs)
- `Deltek Project Creation` (23 .cs)
- `Portfolio Website` (19 .cs)
- `DeltekProjectDeadlines` (9 .cs)

Total removed: **162 .cs files**.

## Effect on the version-control finding

The original scan found 165 .cs files under no version control. With the four exclusions applied,
**one item remains**:

| directory | .cs | why it still matters |
|---|---|---|
| `Redirector\Kor.Transmittals.Redirector` | 3 | Client-facing, deployed, demo-critical. No `.git` means no history and no way to prove the source on disk matches what is serving live transmittal links. |

The finding is now small but not dismissible: it is the one untracked thing that external parties
actually touch. Remains `BEFORE-DEMO`.

## Revised suite scale

Excluding the four out-of-scope directories, the Operations Brain and its engineering tooling
total approximately **351,000 lines of C#** (from 364,531 before exclusions).
