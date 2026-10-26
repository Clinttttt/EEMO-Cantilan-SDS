# Custom Space Rental contract duration in months (2026-10-26)

Scope: a Custom Facility declared **Space Rental** (e.g. Cold Storage) records a stall contract term in months as well as years. Custom Stall Rent, NPM/NCC/TCC/ICE/BBQ/TPM and Kanmanggay / Vegetable-Fruit (own obligation model, already month-aware) are unchanged.

## Model

One additive nullable column: `Contracts.DurationMonths integer NULL` (migration `20261026123309_ContractTermMonths20261026`; no default, no backfill, no check constraint). Term = `DurationYears * 12 + DurationMonths` calendar months. `DurationMonths` null = a yearly term = every contract that exists today. A months-only term stores `DurationYears = 0`. Terms are stored in the canonical shape of the Space Rental rule (`DomainRules.NormalizeSpaceTerm`): 12 months is the same record as 1 year; 18 months is 1 year + 6 months; 1 month to 50 years.

## One rule, read everywhere

`Contract.ExpiryDate`, `BillsCalendarMonth`, `IsExpiredOn` (and so `IsCollectableOn`, `OverlapsPeriod`, `Stall.Occupancies`, vacancy, unpaid-month, arrears, closed-account, report and Mobile readers that call them) read `DomainRules.TermLastDay(start, years, months)`. A yearly term returns exactly the old year result (`AddYears(n)` equals `AddMonths(12n)`; proved by existing tables). Month-end starts clamp before the day is removed (Jan 31 + 1 month = Feb 28/29, last day Feb 27/28).

Sites that computed expiry from years alone were switched to the month-aware rule: `StallContractStatus.IsCurrentVendor` (facility page "current vendor"), vendor registry visibility, `ToggleStallStatus` (collectable-date checks), facility Breakdowns report, `FacilityRepository` payor eligibility, `StallRepository.Attention`, `MonthlyRentCollectionSourceAdapter` (online obligations), `UtilityRegisterReader` (SQL), contracts monitoring (SQL + monitoring record), plus DTOs (`StallDto`, `VendorListItemDto`, `ClosedStallAccountDto`, `StallHolderRowDto`, `StallComplianceDto`) with a trailing optional months field.

## Entry points

- Create (`CreateStallCommand.ContractMonths`), edit (`UpdateStallCommand.ContractMonths`), renew (`RenewStallContractCommand/Request.DurationMonths`): months are accepted **only** when the stall's facility `AllowsMonthTerms` (custom + Space Rental); anything else is refused (`Invalid`) and nothing is saved. A months-only term is refused for a signed contract only when no months are given.
- Head UI: Add Vendor / Space modal shows "Plus months (optional)" and "No whole years" for a Space Rental facility only; renew dialog adds months for the same; registry, profile and facility page word terms plainly ("6 months", "1 year 6 months") and expire by the month-aware date. Edit passes the stored months through, so an edit can never reset a months term.

## Known limits (not changed)

- "Renew all" (`/operations/{facility}/renewals`) renews with one common number of years; a months-only account (0 years) is not offered there and is renewed from its own row. Accounts with years+months are offered and renewed for the chosen whole years.
- The custom Space Rental stallholder import still refuses these facilities (earlier decision); no months column was added to imports.
- Assign-past-occupant stays yearly.

## Verified

Unit: `ContractMonthTermTests` (17 new; month-end, leap day, 12 months == 1 year, yearly unchanged for 1/3/5 years across 12 years of months, refused zero/over-50, unsigned occupancy). PostgreSQL: `CustomSpaceRentalMonthTermTests` (6: 6-month create + expiry + billed months + current-vendor window; months refused outside Space Rental with nothing saved; yearly contract unchanged incl. Feb-29 start; renewal clamps the old term to its own expiry and starts 1y6m; renewal months refused for Stall Rent; expired 6-month account keeps exactly 6 unpaid months / ₱24,000 and another tenant sees nothing). Component: `AddVendorModalMonthTermTests` (4). Full suites on this tree: unit 3,285 / component 1,511 / PostgreSQL 958 (14 documented skips) / `has-pending-model-changes` clean.
