# StallTrack V2 — Active Workstream

**Status:** Consolidated pre-final V2 development
**Canonical checkout:** `C:\dev\stalltrack-v2-clean`
**Canonical branch:** `interface-v2/clean-adoption`
**Shared preview:** `https://localhost:7167`

## Current development mode

The parallel A–M UI sprint is closed for new implementation.

From this point forward, StallTrack V2 UI work continues sequentially in the canonical checkout:

`C:\dev\stalltrack-v2-clean`

Do not start new feature implementation in the old A–M worktrees unless Clint explicitly reopens a workstream.

The goal of this phase is to produce one coherent pre-final V2 repository with one accepted visual language, one integration history, and one localhost review target before later merging V2 back into the main StallTrack repository.

## Consolidated work

The following V2 work has been integrated into the canonical checkout:

| Area | Source | Canonical route | State |
|---|---|---|---|
| Operations discovery | Primary V2 checkout | `/operations` | Integrated |
| Market Fees | Session C | `/operations/market-fees` | Integrated |
| Market Fees Report | Session C | `/operations/market-fees/report` | Integrated |
| Water Consumption Fees / WCF | Session D | `/operations/water-consumption-fees` | Integrated |
| WCF Accounts | Session D | `/operations/water-consumption-fees/accounts` | Integrated |
| WCF Report | Session D | `/operations/water-consumption-fees/report` | Integrated |
| General Distribution / ECF | Session E | `/operations/ecf` | Integrated |
| ECF Accounts | Session E | `/operations/ecf/accounts` | Integrated |
| ECF Report | Session E | `/operations/ecf/report` | Integrated |
| Tabo / TPM workspace | Session F | `/facility/tpm` | Integrated |
| Tabo structured report | Session F | `/facility/tpm/report` | Integrated |

The WCF and ECF entries are wired from `Operations.razor`. Tabo continues to use its existing canonical facility route.

## Historical worktrees

The old worktrees remain on disk for history/reference, but they are frozen for new work:

- `C:\dev\stalltrack-v2-worktrees\a-rent-income`
- `C:\dev\stalltrack-v2-worktrees\b-space-rental`
- `C:\dev\stalltrack-v2-worktrees\d-wcf`
- `C:\dev\stalltrack-v2-worktrees\e-ecf`
- `C:\dev\stalltrack-v2-worktrees\f-tabo`
- `C:\dev\stalltrack-v2-worktrees\g-fish-meat-vendor`
- `C:\dev\stalltrack-v2-worktrees\h-landing-berthing`
- `C:\dev\stalltrack-v2-worktrees\i-transportation`
- `C:\dev\stalltrack-v2-worktrees\j-weight-measure`
- `C:\dev\stalltrack-v2-worktrees\k-large-cattle`
- `C:\dev\stalltrack-v2-worktrees\l-ice-plant`
- `C:\dev\stalltrack-v2-worktrees\m-integration`
- `C:\dev\stalltrack-v2-worktrees\v2-planner`

At consolidation time, A, B, G, H, I, J, K, L, M, and V2 contained no implementation changes beyond the shared baseline. There was therefore no feature code from those branches to merge.

If a useful unmerged change is later discovered in one of those worktrees, inspect it first and integrate it deliberately into the canonical checkout. Do not resume independent UI evolution there.

## Remaining V2 feature work

Unimplemented or still-to-be-refined contextual pages should now be developed directly and sequentially in the canonical checkout, including as applicable:

- Rent Income contextual refinements
- Space Rental contextual refinements
- Fish / Meat Vendor Fees
- Landing / Berthing
- Transportation Fees
- Weight & Measure / Registration
- Large Cattle Transfer
- Ice Plant
- remaining report refinements
- cross-page visual consistency cleanup

Do not recreate a parallel worktree sprint for these unless Clint explicitly asks.

## Single implementation flow

For each next page:

1. Start from the latest `interface-v2/clean-adoption` HEAD.
2. Read `AGENTS.md` and the relevant canonical business/decision documents.
3. Inspect the most recently accepted V2 pages before designing another pattern.
4. Implement only the requested page/workflow.
5. Run `git diff --check`.
6. Run the relevant Release build/tests.
7. Preview through `https://localhost:7167`.
8. Clint visually reviews the result.
9. Make one focused commit after approval.
10. Continue to the next page from that new accepted baseline.

The next page must inherit established visual patterns unless its domain genuinely requires a different workflow.

## Shared localhost preview

All browser review uses:

`https://localhost:7167`

Only one StallTrack Client preview runs at a time.

Local helper:

`C:\Users\ASUS VIVOBOOK\Downloads\Preview-StallTrack.ps1`

PowerShell convenience command:

`preview C`

Since the canonical checkout is now the development source, Session C / the clean checkout is the normal preview target. Custom routes can still be opened directly under port 7167.

Do not assign permanent feature ports such as 7267/7367.

Do not change committed `launchSettings.json` solely for preview switching.

## V2 visual contract

All new/refined pages must preserve one system-wide language:

- dark navy + restrained municipal gold for application identity;
- white/warm-white content surfaces;
- compact administrative spacing;
- strong readable typography;
- restrained gray dividers/borders;
- no random page-specific colors;
- no automatic green for ordinary states such as Active or Paid;
- no decorative SaaS-style charts unless the information genuinely benefits from one;
- consistent hero proportions and icon treatment;
- consistent button hierarchy;
- consistent modal/form spacing;
- one-border money inputs;
- no unnecessary Notes fields;
- no giant native dropdowns for searchable entities;
- formal government-document styling for financial reports;
- official EEMO report structure where evidence exists;
- no invented financial/business semantics.

A page may differ structurally when its domain differs, but it should not invent a new design system.

## Business consistency

Consolidation does not change business authority.

Continue to preserve:

- Facility != Revenue Classification
- Obligation != Collection
- Collection != Remittance
- Billing Basis != Payment Cadence
- Cash Ticket collection != Cash Ticket inventory/accountability
- Collection Efficiency != Revenue Target Attainment
- Delinquency != Arrears
- WCF = CT
- ECF = OR
- Tabo = CT

Do not invent unresolved Cantilan rules merely to complete a UI.

## Commit policy

The canonical branch is the pre-final V2 integration history.

Use small, focused commits after visual approval.

Do not stage unrelated evidence files, secrets, local artifacts, generated binaries, or temporary files.

Before committing:

- `git diff --check`
- inspect `git status --short`
- stage exact paths only
- verify `git diff --cached --name-only`

Do not push/deploy to production until the pre-final V2 review is explicitly complete.

## Final merge target

The canonical V2 checkout is not production by itself.

Current pre-final source:

`C:\dev\stalltrack-v2-clean`
`interface-v2/clean-adoption`

Later, after visual/business validation, this branch will be merged deliberately back into the main StallTrack repository/production integration path.
