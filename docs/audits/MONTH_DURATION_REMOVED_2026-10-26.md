# Contract duration in months: removed (2026-10-26)

Decision: contract durations are recorded in **whole years** (1, 2, 3, 5 or a custom number of years). A holder who pays monthly without a signed contract is recorded as **"No contract (space only)"**, which already carries monthly rent assessment, Occupying Since, arrears, collections and monitoring.

## What existed

1. **Custom stall contracts** (`Contracts.DurationMonths`, commit `22e5fd03`, created today): never pushed, never applied to a persistent database. **Fully reverted** (`4d93b48e`), including its migration `ContractTermMonths20261026`. Yearly contracts are untouched.
2. **Space obligations** (Kanmanggay, Vegetable / Fruit, Fiesta / Araw; `ObligationAccount.ContractMonths`, `SpaceContractTerm.Months`, migration `20261026085122_SpaceContractTermMonths20261026`, merge `e2ad8ffb`): never pushed (no remote branch contains it), applied to the local scratch database only, which holds **no** month-based rows (checked: 0 accounts, 0 terms).

## What changed (behavior only; schema retained)

- **Refused on every write path** with one message (`DomainRules.WholeYearsOnlyNotice`): create (`ObligationWorkflow`), stallholder/space import rows, holder correction, lifecycle Renew and Reassign (preview blocks it, so nothing executes), and the stall create/update/renew commands (reverted). No UI offers it any more: "Plus months", "No whole years", the import "Plus months" column, the renewal/drawer months boxes and the spreadsheet "Term in Months" column are removed. Years-only presets (1, 2, 3, 5, Custom) and "Not recorded" for Vegetable / Fruit are as they were before months.
- **Kept, deliberately:** the two nullable columns and the month-aware READ code (`DomainRules.TermLastDay(start, years, months)` with a null/0 months reading exactly the year result, `SpaceArrears`, term split, monitoring SQL, plain-words display). A term that is already stored in months would never be converted, deleted or invalidated: it still expires on its month-based last day (ST-08 cut-off intact), still shows "6 months" / "1 year 6 months", and an edit that does not touch the duration leaves it exactly as recorded. None is known to exist.
- **Migration implications:** no new migration, nothing reverted on a shared database. `SpaceContractTermMonths20261026` stays in the chain (additive: two nullable columns and a relaxed term check that existing rows satisfy). Dropping those columns later is a separate decision once the office confirms no month-based term exists anywhere; it should be a new forward migration, never an edit of this one.
- ST-08 expiry/arrears fixes, ST-19 arrears basis, yearly contracts, Custom Facility functionality and the no-contract path are unchanged.

## Verified

Unit 3,269; component (the four test files the months work had altered are back to their yearly expectations); PostgreSQL `ObligationAccountTests` (32): whole-year presets and a custom 7-year term record as before, a No-contract holder needs no term; a months term is refused on create / import / correction / renew with nothing saved; a stored 6-month term still stops accruing after its last day and reads as recorded; an unrelated edit leaves it untouched; another tenant cannot read, correct or renew it. Full PostgreSQL suite: 950 passed, 14 skipped, 0 failed after fixing one test of mine.

## Risks

- A month-based term that exists in a database this review could not see (production, another developer's) keeps working but can no longer be created or changed to another months value; it can be corrected to whole years through the normal correction rules.
- The unused columns remain until a later cleanup migration.
