> **RETIRED 2026-10-26.** This feature was removed from entry (see MONTH_DURATION_REMOVED_2026-10-26.md). The read-side rules described below remain so any stored months term keeps working.

# Space Rental contract duration in months - investigation and decision (2026-10-26)

Scope: monthly Space Rentals (Kanmanggay, Vegetable / Fruits). Stall `Contract.DurationYears`, NPM/NCC/TCC/ICE/BBQ/custom facilities and event lots are NOT touched.

## Data model chosen

One additive nullable column beside each existing years column; years stays exactly as it was.

| Where | Existing | Added |
|---|---|---|
| `ObligationAccount` (the first / descriptive term) | `ContractYears int?` | `ContractMonths int?` |
| `SpaceContractTerm` (appended renewal terms) | `Years int` | `Months int?` |

Meaning: the term is `Years * 12 + Months` calendar months. `Months` null = a yearly term (every existing row). A months-only term stores `Years = 0` and `Months = n`.

Canonical shape (one record per term): `DomainRules.NormalizeSpaceTerm(years, months)` stores whole years plus 0-11 extra months, so "12 months" and "1 year" are the same record and "18 months" is `1 / 6`. Bounds: total 1 month to 600 months (50 years); years alone keep the legacy 1-50 rule and its message; a zero-length term is refused; months need a signed contract.

The single expiry function: `DomainRules.TermLastDay(start, years, months)` = `start.AddMonths(years*12 + months).AddDays(-1)`; months null/0 delegates to the old years function. `AddYears(n)` and `AddMonths(12n)` are the same date for every date (both clamp to the shorter month, so a Feb 29 start gives Feb 28), proved by a unit table (>800 start dates x 7 yearly terms). Month-end clamp: Jan 31 + 1 month = Feb 28/29 anniversary, so the last day inside the term is Feb 27/28 (stated in the function remarks). Mid-month starts keep their day (Feb 15 + 6 months = Aug 14).

No past data is rewritten: existing accounts and terms have `Months` null and compute through the unchanged years path.

## Consumers - every read/write of ContractYears / Years / TermLastDay for Space Rental

| Consumer | Change |
|---|---|
| `ObligationAccount.SetLeaseDetails`, `CorrectHolderEvidence` | months parameter; canonical compare so 12 months == 1 year when the term is locked after assessment |
| `SpaceContractTerm.Create`, `.LastDay` | months parameter; month-aware last day |
| `DomainRules` (new overloads, `NormalizeSpaceTerm`, `DescribeTerm`) | the one rule |
| `CreateObligationAccountRequest`, `CorrectObligationHolderRequest`, `ImportSpaceHolderRow` (carries the create request), `LifecycleCommand` | trailing optional `ContractMonths` / `TermMonths` (old callers and old JSON unchanged) |
| `ObligationAccountDto`, `SpaceTermDto`, `ContractMonitoringRowDto` | trailing optional `ContractMonths` / `Months` / `DurationMonths` |
| `ObligationWorkflow` create / import / correct / account list | months passed through; Cantilan-only Kanmanggay duration policy now also guards months; correction audit JSON gains `ContractMonths` |
| `SpaceLifecycleWorkflow` (Renew, Reassign, facts, fingerprint, renewal-overlap) | months accepted; fingerprint appends months only when present, so existing idempotency records are unchanged |
| `SpaceArrears.TermLastDay` / `Basis` (ST-19 arrears basis, `HistoricalArrearsReader`, `HistoricalAccessWorkflow` status, `SpaceFollowUpWorkflow`, `SpaceFollowUpAccounts`) | read the same function through `SpaceArrears`/`SpaceContractTerm.LastDay` |
| `ObligationCollectionSource.GetQuotesAsync` / `LoadAsync` (ST-08 accrual cut-off) | read `SpaceArrears.TermLastDay`; month-aware with no code change of its own. A month is withheld when its first day is after the last day |
| `SpaceTermSplit.Split` (renewal chain, term cards) | month-aware last day; `Months` on the term DTO |
| `AccountLifecycleAdapters` (profile timeline / term expiry) | month-aware, plain wording |
| Contracts monitoring (`ContractMonitoringRules.Expiry` SQL, `ToDto`, `ContractsMonitoringQueries`) | SQL expression month-aware (yearly rows still use `AddYears`); union side for stall contracts assigns `DurationMonths = null`; `ToDto` still asserts SQL == domain function. Monitoring continues to read the account's own term only (pre-existing behaviour, renewals not read) |
| Head: Add New / edit forms (`ObligationWorkspace`), renewals page (`SpaceTermRenewals`), lifecycle drawer (`LifecycleActionDrawer`) | "Plus months (optional)" beside years; Kanmanggay years list gains "No whole years" for months-only; 3-year default unchanged |
| Head: profile (`SpaceAccountProfile`), holder details, follow-up review, registry contract column, `RentalAccountStatus`, `ClosedObligationRentalRow` | month-aware expiry and "6 months" / "1 year 6 months" wording |
| Head: roster (`SpaceHolderRoster`), Stall holder list report | roster/CSV "No. of Years" cell shows the plain term for month-based rows (yearly rows still a number); the spreadsheet keeps "No. of Years" numeric (blank for a month-based row, never a fractional year) and gains a trailing "Term in Months" column |
| Imports (`SpaceHolderImport`, Kanmanggay + Vegetable pages) | optional "Plus months" column, recognised by header; the documented positional order does not change, so a legacy file is unchanged |
| Contracts monitoring report (`ReportsContractsMonitoringSource`) | duration label month-aware |
| Mobile | No term end is shown for spaces; the collector arrears choice reads the server's `SpaceArrears` basis, which is month-aware. No Mobile change |
| Tenant export / TenantDataTables | export serialises whole entities, so the new columns are included automatically |

## Left as is (and why)

- Stall contracts (`Contract.DurationYears`, `DomainRules.TermHasExpired`) - not Space Rentals; unchanged.
- Monitoring ignoring renewal terms for spaces - pre-existing, not a months matter.
- Years+months combinations in the Add New / edit forms are entered as "years" plus "plus months"; no 1-3 month presets, because a free select 1-11 is simpler than a preset list nobody has ruled on.

## Migration

`20261026085122_SpaceContractTermMonths20261026` (after `20261026071822_HardenPayorHistoricalAccessTenantKeys20261026`): two nullable `integer` columns, no default, no backfill; `CK_SpaceContractTerms_Years` is replaced by a looser check (Years 0-50, zero years needs Months, Months 1-11). Existing rows satisfy the new check. Down restores the old check and is only safe before a months-only term exists. Verified over existing space data in `SpaceContractMonthsMigrationTests`.
