# Rental UI Standardization V10 — handoff

Branch `codex/rental-ui-consistency-v10`, baseline `3fde28ca` (Utility V9). Local commits only; nothing pushed, merged, deployed or migrated in production.

## What the audit found
Kanmanggay, Vegetable/Fruit and Fiesta/Araw already reused the shared rental UI (`AddNewDrawer`, `Rental*Fields`, the `Import*` family). The real gaps were a bespoke profile that exposed a raw source GUID, a payment history without SRC/date/collector, no contract rate / years / area / name-on-contract in import, no import history, and plain-text action buttons.

## Shared UI
- `RentalHistoryLedger` (extracted from `PaymentHistoryModal`; the TCC/NCC/ICE modal is now a host of it) — also used by the space profile.
- `RentalProfileView` — shared profile layout, used by `SpaceAccountProfile`.
- `RentalRateFields` / `RentalWorkspaceNavigation` / `SpaceHolderImportHistory` page; `SpaceAccountPresentation.For(kind)` / `ProfileHref(id)`.

## Backend (additive only)
- `ObligationAccount.ContractMonthlyRate` (evidence only — never prices anything); `SpaceHolderImportRun` log; migration `20261012145430_RentalAccountContractRateAndImportRuns20261012`.
- `GET accounts/{id}/payments` and `GET import-runs` on `ObligationsController`; import now keeps lease evidence.
- Existing rules unchanged: Kanmanggay 3-year term, contract date in billing-start month, billing from the first of the effectivity month, canonical SRC/OR/CT.

## Office register mapping (NPM paper register → fields)
Name of Lessee (Actual Occupants) → holder; Name per Signed Contract → contract name; Stall/Space No. → space; Effectivity → contract date; No. of Years → years; Area → area; Monthly Rentals per Contract → contract rate (evidence); Actual Monthly Rentals → billing rate; Whole Year Rental → displayed, not stored. Photographed data is **not** imported. Vacant/closed rows stay distinct; a row with no contract evidence waits for an explicit occupancy basis.

## Mobile
Removed the explanatory paragraph on Vegetable/Fruit (`OperationCollection`).

## Verification
- Release build of `EEMOCantilanSDS.slnx`: 0 errors.
- Unit 2561, Head component 1071, Mobile component 109: all pass.
- PostgreSQL integration: 530 passed, 7 skipped (snapshot-database tests that need an external snapshot), 0 failed.
- Browser (isolated Postgres/API/Client): office-register CSV → review (2 Ready, 1 awaiting basis) → import → history row → profile; no horizontal overflow at 1440/1024/390.
- **Not done:** Windows MAUI visual run (see report).

## Integration notes
No edits to tenant operation naming/onboarding components, `integration-v8`, or the Platform repo. The migration must be applied through the normal release process; none was applied to production.
