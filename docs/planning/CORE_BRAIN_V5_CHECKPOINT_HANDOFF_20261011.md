# Core Brain V5 checkpoint handoff — 2026-10-11

Branch `codex/vegetable-fruit-space-workspace`. Commits (local only; nothing pushed, merged or deployed):

| Commit | Scope |
|---|---|
| `3e7eea75` | Fines workspace refinements, summaries, direct fine collection UI |
| `5730c8c4` | Dashboard Operations / Revenue sources views, IFT card, toolbar and heading polish |
| `fbfb6014` | Closed Accounts coverage, Monthly Collection Report, Fines under Space Rental / Arrears under Rent Income |
| `cacc9a88` | NPM versioned weekly operating calendar, overrides, migration `20261011040007` (source only) |
| `e40ec400` | Stall & Space Holder Register incl. space rentals and Fiesta/Araw event lots |
| `b8ec636a` | NPM payment by amount (whole payable days only) |

## Implemented and verified
- Operating calendar: Head-only prospective schedule, per-day overrides (Admin today-or-later, Head for past days with a reason), one effective-day authority for settlement, reports, Web and Mobile. No effective date is seeded: the Head states it and its governing reference.
- Billing: under `RentGoal` each closed day is credited at one daily installment (e.g. ₱900 reference, ₱30 daily, 8 closed days → ₱660 payable); under `PureDays` closed days are not billed. Collected money on a later-closed day stays Paid.
- Holder Register: stall rentals, Vegetable/Fruits, Kanmanggay and Fiesta/Araw event lots (by event year, Event-based, no invented contract or rent figure). Export columns Source, Holding, Contract Reference, Status, Billing Basis (+ Event, Event Date, Event Amount, Location / Note).
- Payment by amount: the amount is previewed server-side; only an amount equal to a sum of whole payable days (oldest first, closures and month ceiling applied) is recorded, through the existing `SettleNpmDays` path.

## Awaiting official financial authority
- Partial installments and unapplied advance cash for NPM. No canonical ruling establishes an unapplied-cash ledger; the app therefore refuses such amounts and shows the nearest valid amounts. The extra money is never turned into a paid day.

## Not implemented
- Office back-entry posting. `/api/office-sources/record` requires an assigned Collector; there is no Head/Admin writer with recorded-by vs collected-by, reason, evidence reference and original `ClientOperationId`, and the server cannot see an unsynced offline Mobile operation. Needs a backend contract and a reconciliation state first. The Office collections hub stays read-only.

## Not applied
- Migration `20261011040007_NpmOperatingCalendar20261011` has not been applied to any persistent database (target `localhost:5432 / EEMOCantilanSDS.DB` could not be verified as a safe development database and no backup tool was available).

## Skipped PostgreSQL tests (intentional)
`OccupancyHistorySnapshotTests` (5) and `RegisterAndFollowUpAgreementTests` (2) need `STALLTRACK_SNAPSHOT_DB` pointing at an isolated restored snapshot. They are also the route to a real-month reconciliation, which has not been run.
