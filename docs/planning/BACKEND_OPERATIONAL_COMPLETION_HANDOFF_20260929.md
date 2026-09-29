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
