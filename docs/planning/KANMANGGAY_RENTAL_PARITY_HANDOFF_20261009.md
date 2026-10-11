# Kanmanggay / monthly space rental parity — local implementation handoff (2026-10-09)

## Scope
Checkout: C:\dev\stalltrack\eemo. Feature branch: codex/vegetable-fruit-space-workspace. Existing source code was changed directly; ICE/NCC backend identities are unchanged.

## Delivered
- Existing shared registration components remain the presentation authority for ICE/NCC and monthly space operations: RentalLesseeFields, RentalContractFields, RentalEffectivityField, RentalUnitNumberField and RentalRateFields.
- New monthly space registration records distinct Name on Contract and exact signed-contract effectivity date. Existing ActiveFrom month still determines billing/assessment.
- New KM signed contracts record the confirmed three-year standard term from the registration form as descriptive evidence only. Older/imported signed leases with no documented term retain a null term; the server does not invent one. Recorded KM durations other than three years are rejected. This does not automatically expire, renew, reopen or close accounts.
- Optional area/area-location evidence persists; missing historical information remains null. Contract name and contract reference are separate.
- Next Space No. is a source-scoped suggestion. Server transaction remains the final allocator; concurrent registrations can change the number.
- Shared rental actions preserve source account IDs. The history/details modal displays twelve-month authoritative assessed/settled/outstanding balances, plus real canonical activity references for a selected month.
- Registered rental overview, account management and official space roster/exports use recorded contract metadata where supported.

## Data safety
An additive EF migration KanmanggaySpaceLeaseEvidence20261009 adds nullable ObligationAccounts columns ContractName, ContractEffectiveDate, ContractYears, AreaSqm, AreaLocation. No historical backfill and no posted Collection/SRC changes. API account creation retains tenant scope and Head authorization. Migration generated but NOT applied to any database.

KM No contract (space only) remains until the office clarifies exceptions. VF monthly records optional signed metadata but does not inherit a three-year term. FA event lots remain distinct.

## Financial invariants
No changes to canonical Collection/SRC history, period assessments, settled allocations, effective-dated rates, NPM Daily/Whole, event-lot billing or receipts. No auto-renewal, reopening or cross-source payment writer.

## Receipt/history limitation
ICE's receipt modal is not a KM payment writer. The KM peso action opens read-only period position (not a fabricated receipt). History shows authoritative period balances and separately canonical collection references. The existing activity feed exposes only the first allocation of a line: complete multi-allocation source history remains a later backend task.

## Verification and release gate
- Client and API Release builds passed with zero errors (pre-existing warnings).
- EF has-pending-model-changes reported no model drift.
- Focused lease evidence tests passed 6/6, including preservation of unknown historical contract duration.
- Full Web component suite passed 844/844 after updating obsolete contract-field expectations.
- Full unit test suite passed 2,525/2,525. Client and API builds retained pre-existing warnings but no errors.
- Focused PostgreSQL integration test project compiled, but all 13 selected tests were SKIPPED because the PostgreSQL test fixture was unavailable. Its new signed-name/date/term/area save-and-reload test therefore still needs execution against an authorized test database; skipped is not passed.
- Database migration and authenticated browser review NOT performed. Before testing the new API against a database, apply the migration only after local/test authorization. Production migration requires separate release approval.
- Restart API and Client from this checkout after migration. Verify saved/reloaded signed name/date, KM 3-year term, area, scoped number, old Space Only behavior, history, and narrow viewport.
- Complete renewal/expiration/successor-account lifecycle remains separately gated.
