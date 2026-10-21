# Rental consistency, space imports and direct-review verification V11.1 — handoff

Branch `codex/rental-consistency-v11-1`, worktree `C:\dev\stalltrack-v2-worktrees\rental-consistency-v11-1`. Built on the local unified commit `d68382d8` (Utility V9 + Onboarding V8 + Rental UI V10) with the Operations V11 branch (`codex/operations-intake-review-ui-v11`, `f0533a2b`) merged in at `e24db95a`. Local commits only; nothing pushed, merged into the integration branch, deployed or migrated in production. The eight protected files in the main checkout were not touched.

## What was reused from TCC/NCC
- The stall **View Details** slide-in is now `RentalDetailsDrawer` (shared, data-driven; the stall registry's own markup and CSS were extracted, so a space opens the same drawer).
- `MonthlyPaymentCalendar` (read-only) draws a space's months; `SpacePaymentMonths` maps the period register to the records it reads.
- `RentalRegistryHero / Toolbar / Table / ActionButtons` draw the Space Holder List (new optional parameters only: filters, context heading, hidden payment column, hidden edit/history/toggle).
- `CollectionReceiptModal` is the peso action's acknowledgement (new optional `ReferenceLabel`, `UnitLabel`).
- `AddNewDrawer` (the stall Add/Edit shell) is unchanged in look; new opt-ins `ShowCancel`, `GuardUnsaved`, `SessionKey`.

## Results
- **Details**: Header (occupant, space · operation, Active/Closed), summary (monthly rental/event amount, contract date, current month status, area), Occupant & contract, Fee structure (approved billing apart from the contract's own rental, marked evidence only), the payment calendar, footer Close / History / Edit Occupant & Space. No source identifiers. Event lots show event facts and settlement.
- **Add / Edit Occupant & Space**: stall wording, header X as the single dismiss control (no second Cancel), a question before a changed form is discarded.
- **Contract duration**: unchanged by design. The select shows what the server accepts: Cantilan's confirmed three-year Kanmanggay term; any other municipality records no duration until it confirms a policy (existing server guard). **Open decision**: a per-municipality duration policy (for example one-year terms) must be approved and modelled before the select can offer more.
- **Import**: all three space imports use the blue operation hero; Upload → Review & edit → Save; a submission carries an operation id so a double click or a retry saves once (additive column and unique index on `SpaceHolderImportRuns`). Server-numbered rows are previewed and numbered under the existing lock; an explicit duplicate number is skipped. Import History is no longer linked anywhere; the saved runs, the page and its route are kept (Head only, by URL).
- **Space Holder List**: registry look on screen; Print, Excel, CSV and PDF produce the official document (shown on paper only). **Fiesta/Araw** has `/operations/fiesta-araw/space-holders` and a visible Space Holder List action (lot, event, event date, approved event amount).
- **Peso action** (Fiesta and the other space rows): the stall registers' read-only collection acknowledgement for the month (the event for a lot), keyed by the account; it posts nothing.
- **Direct Collection Review**: the V11 redesign and View were verified in the combined runtime; View opens the collection's details inside the dialog (the URL does not change, the list state is kept). Actions read Confirm | Needs correction | View on one line at 1440 and 1024.

## Slaughterhouse revenue investigation
**Root cause.** From the Head's enablement a Collector's slaughterhouse collection is a canonical Collection (SRC, line source `GovernedService`) and no legacy `SlaughterTransaction` is written. The Slaughterhouse workspace read only `SlaughterTransactions`, so the collection appeared in Collection Activity and the Direct Collection Review but never in the Slaughterhouse overview, owner list, owner history or yearly history. The date filters were not the cause (both sides use the month).
**Fix.** `SlaughterRepository` now also reads canonical slaughterhouse collections (a reversed one excluded), once, marks them with their SRC, and the owner list hides Edit for them. Reading never creates a legacy row, and **Confirm adds only a review event**: in the running portal, two collections of ₱1,250 stayed two collections and ₱1,250 after a confirmation (one review event, no new Collection or SRC). Review does not gate posting; no approval-gated posting was introduced.

## Verification
Whole-solution Release build: 0 errors. Unit 2593, Head component 1153, Mobile component 114: all pass. PostgreSQL integration: 579 passed, 7 skipped (snapshot-database tests), 0 failed. Runtime on an isolated Docker PostgreSQL, API and Head Portal: the new migration applied on top of an existing database at API start (upgrade path); details drawer, import and holder lists checked at 1440, 1024 and 768 with no horizontal overflow.

## Limitations

- The Windows MAUI app was not run in this pass (no Mobile change).
- `RentalDetailsDrawer` is shared by the spaces; the stall registry (`Vendor.razor`) still carries its own copy of the same markup and CSS until it adopts the shared drawer.
- Owner history for a canonical slaughter row opens the existing details page by owner name; the page itself was not redesigned.

## Integration
Merge this branch onto `d68382d8` (it already contains the V11 branch at `f0533a2b`). The model snapshot carries `SpaceHolderImportRuns`, `FishMeatDirectCollectionLinks` and the new operation-id column and reports no pending model changes. Two additive migrations come from V11 and this pass (`FishMeatDirectCollectionLinks20261013`, `SpaceHolderImportRunOperationId20261013`); none was applied to production.

## Later local import review evidence — 2026-10-14

Based on `ea101648`: [Kanmanggay import review repair](KANMANGGAY_IMPORT_REVIEW_REPAIR_20261014.md). It replaces malformed row actions and narrows the Space import review list while preserving this handoff's source and billing behavior; it is not a production release or integration declaration.
