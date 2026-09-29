# Backend Operational Completion Handoff — 2026-09-29

Branch: `backend/operational-completion-v2`
Base: `024556fd` (`interface-v2/clean-adoption`)
Scope: additive NPM Meat weighing source facts and reporting support. The active `ui-completion` worktree was left untouched.

## Audit findings

- NPM Fish weighing already records `DailyCollection.FishKilos`; its effective rate is resolved through the NPM fee-rate resolver. The clean integration base has no NPM-to-canonical-`CollectionLine` adapter. Existing NPM daily collections remain on the legacy `DailyCollection` path.
- NPM daily collection has a Fish-only weighed quantity. Meat had no corresponding quantity, rate snapshot, or amount field. Meat daily stall rent is a separate rate and is not used for weighing.
- `DailyCollection` is already included in tenant export, restore coverage, and the existing offline `NpmDaily` queue. No new table or backup registration was needed.
- The clean base has no accepted `CollectorOperationAssignment` or standalone Landing/Berthing and Transfer Large Cattle mobile writer. WCF is the closest canonical CT/custody reference, but a separate operation identity, assignment authorization, source, and OR/CT issuance path are still required.

## Implemented in this checkpoint

- Added the tenant-scoped NPM `NpmMeatPerKilo` fee-rate key. Cantilan's confirmed ₱66/kg value is seeded only for the default tenant effective 29 Sep 2026 when no Meat rate exists; existing rates/history are preserved. Other tenants resolve only their own configured rate.
- Added Meat kilos, effective rate, rate effective date, and frozen amount to `DailyCollection`. Historical rows read as no Meat weighing and zero amount; no old money or quantity is backfilled.
- Paid NPM Meat-section collections can carry optional Meat kilos. The server rejects negative quantities, non-Meat/NPM contexts, unpaid entries, and missing/non-positive effective rates. The rate is resolved on the collection business date and frozen with the amount.
- Added Meat kilos to the online request and the existing offline NPM operation payload/replay. Existing `ClientOperationId` behavior is retained. Omission by an older client preserves already captured Meat facts when updating a paid record.
- Added separate Meat weighing fields to NPM Mobile and daily-collection report DTOs and included the frozen amount in NPM operational and collector cash totals. NPM financial detail exposes the amount separately as Weight & Measure; it is not folded into the Fish fee or daily stall-rent component.
- Added `CollectionSourcePart.MeatWeighing` as a stable source-part identity that a future canonical NPM adapter can use. It does not itself create or activate a canonical CollectionLine.

## Migration and authority limits

Additive migration: `20260929051645_AddNpmMeatWeighingEvidence`. It adds nullable kilos/rate/effective-date fields and a zero-default frozen amount to `DailyCollections`. No old migration was edited or removed.

This checkpoint does **not** add a canonical NPM collection writer or classified `CollectionLine` for Weight & Measure. The new Meat weighing facts remain part of the existing NPM daily source and must not be counted as canonical Monthly Income cash until a reconciled source adapter projects them exactly once to `WEIGHT_AND_MEASURE`. No source cutover/activation or production-data rewrite occurred.

Landing/Berthing (CT) and Transfer Large Cattle (OR) remain blocked for implementation here: this baseline has no non-facility operation assignment/authorization model or direct-field source/custody writer. Do not bypass authorization, accountable-document custody, or correction/reconciliation by routing them through NPM, WCF, or a generic assessment. Their Web pre-assessment requirement should be removed only alongside a complete assigned, offline-safe Mobile writer and canonical source allocation.

Market Fees, Kanmanggay, and Fiesta/Araw were not changed. Existing historical Fish evidence and Fish rate/classification behavior were not reinterpreted. Meat weighing remains distinct from Fish kilos and from Meat-area daily rent.

## Validation

- Focused unit tests: 33 passed, 0 failed (NPM Meat weighing, rate resolution, offline mapping/durability, and existing NPM daily-handler cases).
- Full `EEMOCantilanSDS.slnx` Release build: backend/API/Web and Mobile targets compiled, but the solution command exited with `NETSDK1047` because the restored Mobile assets do not contain `net10.0-maccatalyst/maccatalyst-arm64`. Android/iOS/Windows Mobile targets were reached; no component tests were run.
- Component test suite: intentionally not run, per the time-boxed instruction.
- PostgreSQL/Testcontainers tests: not run in this checkpoint; verify Docker availability before the next database-focused validation.
- No deployment, APK build/release, or production source activation occurred.

## Integration points / next gates

1. Complete and test an NPM canonical adapter that emits separate rent/vendor and Weight & Measure lines with exact source parts, explicit policy snapshots, allocations, and correction behavior; reconcile legacy NPM facts before any cutover.
2. Add a proper operation-assignment model for non-facility collection operations, then implement Landing/Berthing CT and Transfer Large Cattle OR as distinct source types with offline queue replay, custody, idempotency, and rejected-issued-document reconciliation.
3. Add source-authoritative complete-period/report aggregation only after canonical recognition is defined; exclude assessments, compatibility projections, and already-counted legacy/canonical evidence from cash totals.
4. CORE BRAIN may wire presentation to the existing NPM rate and report DTOs after integrating this branch. This branch intentionally contains no Web Razor/CSS edits.

## Follow-up: collector rate quote and NPM report facts (2026-09-29)

This follow-up adds a display-only, tenant-scoped Meat rate quote and explicit report facts. It does not complete the canonical Weight & Measure adapter or any of the direct field-operation writers below.

- Added authenticated Mobile GET `api/Mobile/npm/meat-weighing-rate?businessDate=yyyy-MM-dd`. It resolves `NpmMeatPerKilo` through the tenant-scoped fee-rate snapshot for the requested Philippine business date and returns rate/effective-date or an explicit unavailable state. Future dates are rejected. The quote is informational; the existing collection command still resolves and freezes its own server-side rate and amount. The Mobile caching client does not serve stale cached quotes offline.
- NPM report DTOs now expose Fish weighing amount separately from Fish/vendor presentation, and expose Meat kilos and the frozen Meat weighing amount separately from Meat-area daily rent. The repository report breakdown preserves Meat weighing evidence even when a monthly rent payment exists for the same stall; rent and Fish continue using their established monthly-payment deduplication. No client-side rate multiplication is used for report totals.
- Existing legacy report totals and `CollectionSourcePart.MeatWeighing` are not a canonical `CollectionLine`. The new fields are source/report facts only. No canonical Monthly Income cash recognition, source cutover, OR/CT issuance, or production activation was added.
- No schema change was needed; there is no migration in this follow-up.

### Official-row readiness audit on this backend branch

The repository has revenue classification identities, but no `MonthlyIncomeCanonical` report reader was found. A classification seed or assessment row is not evidence of posted cash.

| Classification | Current source / canonical writer | Included by a canonical Monthly Income reader? | Remaining gate |
| --- | --- | --- | --- |
| `WEIGHT_AND_MEASURE` | NPM Fish and Meat weighing facts remain on legacy NPM sources; no NPM-to-`CollectionLine` writer | No | Reconcile NPM source history, add source-part adapter and exact-once cutover/correction rules |
| `LANDING_BERTHING` | Historical `LandingBerthingActivity` only; no direct collector source/writer | No | Assigned CT Mobile source, custody, offline replay and canonical posting |
| `TRANSFER_LARGE_CATTLE` | Existing governed-service assessment evidence; no direct collector source/writer | No | Assigned OR Mobile source, custody, offline replay and canonical posting |
| `VEGETABLE_FRUIT_SPACE_RENTAL` | Governed-service assessment source; no direct mode-aware Mobile writer | No | Assigned writer and server-enforced Whole/OR versus Daily/CT instrument/custody path |
| `MARKET_FEES` | Configurable-service setup/assessment patterns; no collection-point canonical Mobile writer on this branch | No | Authorized operation assignment, point identity, CT custody and exact allocation |
| Kanmanggay space rental | Not integrated into this backend checkpoint as a specialized account-month canonical writer | No | Integrate the specialized source, then OR custody, Mobile settlement and allocation |
| WCF | WCF canonical Mobile workflow exists in this branch | No canonical Monthly Income reader found | Connect canonical posted lines to the official-row reader and reconcile compatibility projections |
| ECF | Approved assessment/legacy utility evidence; no active canonical collection writer | No | Resolve legacy overlap, OR lifecycle and cutover before recognizing cash |
| Fiesta/Araw lot rental | Generic governed-service assessment facts; assessment is not cash | No | Complete-period source/report query and later approved assigned OR writer/cutover |

The classification seeder currently supplies one instrument value per classification. Vegetable/Fruit business policy has two modes (Whole Payment = OR, Daily Transaction = CT); the eventual writer must enforce mode-specific instrument semantics and must not treat a single seed value as sufficient authorization to post both modes.

### Follow-up validation

- Focused unit tests: 6 passed, 0 failed (dated quote, unavailable/tenant-isolated quote, future-date rejection, quantity-only request, NPM Meat report mapping, and preservation of Meat weighing evidence alongside monthly rent).
- Client Release build: passed, 0 errors (28 existing warnings).
- Mobile Android Release build: passed, 0 errors (50 existing package/platform warnings). No MacCatalyst target was built.
- Full solution and full unit suites were not run in this bounded follow-up. Component tests were not run.
- PostgreSQL/Testcontainers tests: skipped; Docker Desktop Linux engine was unavailable (`dockerDesktopLinuxEngine` pipe absent).
- `git diff --check`: passed before commit.
- No Web UI files were changed. No deployment, APK release, source activation, or cutover occurred.

### Not implemented in this follow-up

The following sprint phases remain open and must not be inferred complete from the quote/report contract: canonical NPM Fish/Meat `CollectionLine` recognition and reconciliation; accepted collector-operation assignment integration; direct offline-safe Landing/Berthing CT, Transfer Large Cattle OR, and Vegetable/Fruit CT/OR writers; a canonical Monthly Income reader; and end-to-end canonical inclusion/correction tests. A candidate operation-assignment workflow was found only on the separate UI branch (commit `5f88f4de`); it was not copied or adopted here because that branch has an additional stale Fish/Meat operation identity which needs reconciliation first.
