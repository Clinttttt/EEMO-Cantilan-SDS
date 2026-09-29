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

## Follow-up: NPM MeatWeighing relational source-part alignment (2026-09-29)

The domain already recognizes `DailyCollection + MeatWeighing` as a valid stable source identity, but the relational source-shape checks for posted lines, allocations, Web draft lines, and Web draft allocations still allowed only parts 3 and 4. This meant the future Meat weighing line could pass domain validation but fail at the database boundary.

- Updated the four EF model constraints to allow `DailyCollection` source part 5 (`MeatWeighing`) alongside existing daily and Fish parts.
- Added the additive migration `20260929095118_AllowNpmMeatWeighingCollectionPart`; no old migration or financial row was rewritten.
- Added a domain regression case and a relational-model test covering all four constraints.
- This is schema representability only. It does not add a Collection writer, allocation, accountable-document path, exact-once source guard, Monthly Income reader, or source activation.

### Current backend readiness matrix

| Source | Source facts / assessment | Mobile writer | Offline-safe | Instrument custody | Canonical CollectionLine | Reports | Activation state |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NPM Weight & Measure | Fish and Meat weighing facts exist on legacy NPM daily rows; Meat amount/rate/date are frozen server-side | Existing NPM daily collection writer captures the facts | Existing NPM queue/replay carries the source facts | Legacy OR string only; no AccountableDocument consumption for these NPM rows | No adapter or posted line; source-part 5 is now representable only | Operational NPM facts exist; no canonical Monthly Income reader | NPM remains legacy authority; Weight & Measure cutover not activated |
| Market Fees | Tenant collection-point policy/setup exists | No operation-specific Mobile writer | No | No Market Fees CT custody path | No | No canonical Monthly Income reader | Inactive |
| WCF | UtilityBill source plus WCF obligation workflow | Web and Mobile API posting workflow exists | Server operation retry support exists; a durable device queue was not confirmed in this audit | Accountable CT workflow exists | WCF workflow creates classified source lines | Collector/source reports read WCF canonical evidence; canonical Monthly Income reader absent | No WCF change/cutover in this checkpoint; scope authority remains separately gated |
| Landing / Berthing | Historical `LandingBerthingActivity` only | No direct field writer | No | No operation-specific CT custody | No | Activity evidence only; no canonical cash reader | Inactive |
| Transfer Large Cattle | Historical governed-service assessment evidence | No direct field writer | No | No operation-specific OR custody | No | Assessment evidence is not cash; no canonical reader | Inactive |
| Vegetable / Fruit Space Rental | Governed-service assessment evidence | No mode-aware Mobile writer | No | No combined Whole/OR and Daily/CT custody path | No | Assessment evidence is not cash; no canonical reader | Inactive |
| ECF | Approved assessment and legacy utility evidence | No canonical collection writer confirmed | No | OR lifecycle/custody gate remains | No | Legacy/source evidence only; no canonical Monthly Income reader | Inactive pending OR and reconciliation gates |
| Kanmanggay | The specialized account-month source is not present in this backend checkpoint; generic assessments remain historical | No canonical Mobile settlement writer | No | OR custody path not integrated here | No | No canonical Monthly Income reader | Pending integration/cutover |
| Fiesta / Araw | Generic governed-service assessment facts | No assigned Mobile writer | No | No source-specific OR custody writer | No | Complete-period assessment/report query is not present here | Inactive |

The Weight & Measure path remains blocked before canonical cash recognition: reconcile the NPM source scope; define an exact-once adapter and correction relationship; bring Fish row-level rate/amount evidence to a safe frozen basis before projecting any historical Fish rows; use accountable-document custody rather than the legacy free-text OR value; and add a canonical report reader with an explicit correction/reporting basis. No historical backfill is approved or performed.

### Validation for this follow-up

- Focused unit/model tests: 17 passed, 0 failed (`CollectionDomainTests` and `CollectionSourceShapeModelTests`).
- Regression proof: temporarily restoring the old `CollectionLines` constraint made the new model test fail on the missing Meat source part; the corrected constraint was then restored and the focused tests passed.
- Focused PostgreSQL/Testcontainers integration test: 1 passed, 0 failed. It applied the full migration chain to a throwaway PostgreSQL 16 database and persisted a MeatWeighing source part on both a line and allocation.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes after scaffolding.
- API Release build: passed, 0 warnings, 0 errors.
- `git diff --check`: passed; Git reports expected LF-to-CRLF normalization warnings for edited text files.
- No Web Razor/CSS, Mobile UI, source writer, production data, deployment, or APK release was changed.

## Follow-up: collector non-facility operation assignment foundation (2026-09-29)

This checkpoint adds a tenant-scoped authorization record for future collector workflows. The record contains only the collector, stable operation code, assigning actor, and UTC assignment time; it contains no amount, policy, instrument, source, document, classification snapshot, or payment state.

- Added the fixed stable operation catalog: `WCF`, `VEGETABLE_FRUIT_SPACE_RENTAL`, `LANDING_BERTHING`, `TRANSFER_LARGE_CATTLE`, and `MARKET_FEES`. These are permission identities, not revenue-classification or source identities.
- `WEIGHT_AND_MEASURE` is explicitly excluded because NPM Fish/Meat weighing remains the operational source. `FISH_MEAT_VENDOR_FEE` is explicitly excluded as a standalone operation because new Fish/Meat vendor activity belongs to NPM. ECF, Kanmanggay, and Fiesta/Araw are deferred pending authoritative future Mobile roles.
- Added tenant-scoped Head-only collector GET/list and PUT/replace API under `api/Collectors/{id}/collection-operations`. Target collector lookup is tenant-bound; unknown/duplicate operation codes fail; an empty replacement clears operation assignments; facility assignments are untouched. Mutation rows are audit-interceptor tracked, with `AssignedAtUtc` and `AssignedBy` preserved as explicit evidence.
- Read-only review of the candidate on `ui-completion` confirmed the entity/table, Head endpoint and export-registration approach were useful evidence. Its operation catalog was not adopted: it derived permissions from revenue classifications/configurable services and preserved the stale standalone Fish/Meat assignment. This checkpoint uses a fixed permission catalog independent of those classifications and does not retain that stale assignment behavior.
- Added an additive table with tenant and collector foreign keys, a same-tenant composite collector FK, normalized-code check, and unique `(MunicipalityId, CollectorId, OperationCode)` constraint. The table is registered for tenant export, restore coverage, backup manifest labels, and system-health inventory.
- WCF obligation and ticket reads now require explicit `WCF` assignment for collectors. New canonical Mobile WCF posting requires both explicit WCF permission and the existing NPM facility authorization because the current WCF source is still an NPM UtilityBill. A prior idempotent `ClientOperationId` outcome is resolved before the new assignment check. An unauthorized physically issued CT is recorded through the existing reconciliation path and is not returned to stock. This does not add the future WCF walk-up source.
- No shared generic Mobile collection menu/capability contract was added. An assignment by itself does not surface a collectible action, issue a document, create a Collection, or activate a source. Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit, and Market Fees remain assignment-ready only; all their posting gates remain outstanding.
- Collector creation still requires facility assignment in its existing workflow. A collector who works only a non-facility operation needs a future atomic account-plus-operation-assignment creation path; no fake facility was introduced.

### Current assignment and writer readiness

| Operation identity | Assignment available | Source-specific Mobile writer / collectible action | Activation |
| --- | --- | --- | --- |
| WCF | Yes | Existing UtilityBill/NPM WCF writer remains; reads and new canonical Mobile posts require WCF assignment plus NPM facility authorization, CT custody and existing settlement gates | No new source activated; in-flight idempotent outcomes remain replayable |
| Market Fees | Yes | No operation-specific writer or collectibility menu; collection point, effective policy, CT custody, offline replay, allocation, correction and reporting gates remain | Inactive |
| Vegetable/Fruit Space Rental | Yes | No mode-aware writer; Whole/OR versus Daily/CT enforcement and document/offline/source gates remain | Inactive |
| Landing/Berthing | Yes | No direct-field writer; CT custody, offline replay, exact classification/allocation and correction gates remain | Inactive |
| Transfer Large Cattle | Yes | No direct-field writer; OR custody, offline replay, exact classification/allocation and correction gates remain | Inactive |
| Weight & Measure | No; explicitly excluded | NPM Fish/Meat weighing remains the source; canonical adapter and reporting are still pending | Legacy NPM authority |
| Fish/Meat Vendor Fees | No; standalone code explicitly excluded | New Fish/Meat operations remain in NPM; no second standalone collector workflow | No duplicate source |
| ECF, Kanmanggay, Fiesta/Araw | Deferred | No new assignment or writer was introduced for these operations | Existing authority unchanged |

### Migration and validation

Additive migration: `20260929154642_AddCollectorOperationAssignments`. No prior or applied migration was edited or deleted.

- Focused assignment application tests: 5 passed, 0 failed.
- Focused PostgreSQL/Testcontainers subset: 21 passed, 0 failed; Docker Linux engine was available. The subset applied the full migration chain and exercised assignment uniqueness/tenant visibility/composite tenant-collector FK plus WCF authorization, issued-ticket reconciliation, and idempotent retry behavior.
- API Release build: passed, 0 warnings, 0 errors.
- `dotnet ef migrations has-pending-model-changes`: no model changes pending.
- `git diff --check`: passed; Git emitted only expected LF-to-CRLF normalization notices.
- Full unit and integration suites were not run. Component tests were not run; no Web UI was changed.
- No historical assignment backfill, financial cutover, source activation, production-data rewrite, deployment, or APK release occurred.

## Follow-up: Cantilan instrument policy context and effective-date reconciliation (2026-09-30)

This checkpoint corrects stale seeded instrument assumptions under confirmed decisions IA-045 and IA-046, effective 2026-09-27. The earlier default-context policy history remains intact as evidence; the new context dimension is policy scope only and does not create a source, collection, or classification.

- Added `RevenuePolicyContext` with `Default`, `VegetableWholePayment`, and `VegetableDailyTransaction`. Existing generic policy commands, list/history queries, and instrument resolvers stay on `Default`; a contextual row cannot satisfy a default, Whole Payment, or Daily Transaction lookup unless that exact context is requested.
- Added `BusinessContext` to effective-dated revenue instrument policies and changed uniqueness to `(MunicipalityId, RevenueClassificationId, EffectiveDate, BusinessContext)`. The additive migration adds the column with database default `Default`, preserving existing policy IDs, instruments, effective dates, and evidence. It adds a supported-context check and updates the unique index without editing prior migrations.
- Tabo on fresh Cantilan setup before 2026-09-27 retains the earlier CT working assumption. On/after 2026-09-27 it resolves to OR. Existing pre-clarification Tabo CT policies are preserved; the seeder appends one OR Default policy effective 2026-09-27 when needed and is idempotent. TPM shadow reconciliation explicitly resolves only `Default`, remains shadow-only, and creates no Collection/CollectionLine.
- Fresh Cantilan setup on/after 2026-09-27 seeds Vegetable/Fruit `VegetableWholePayment` as OR and `VegetableDailyTransaction` as CT. Existing default-context Vegetable CT rows are retained unchanged as historical/compatibility evidence; generic policy readers do not present the two contextual rows as duplicate default policies. No new generic Default row is claimed as authority for Whole Payment. A future source writer must request its exact transaction context; collector preference cannot select the instrument.
- Cantilan mappings remain tenant-specific. Other municipalities receive no Tabo or Vegetable instrument mapping from this correction. Contextual policies require a valid context and explicit instrument; currently supported Default classifications with a null instrument remain valid.
- Policy-history display for a canonical line that references a specific immutable policy ID remains able to look up that policy's display name. Ordinary classification list/history and all generic business-date resolvers filter to `Default`, including WCF, rent, collection composition/cutover, TPM and TRM shadow queries.

### Policy checkpoint validation

- Focused domain/application/seeder/TPM tests: 30 passed, 0 failed.
- Focused PostgreSQL/Testcontainers tests: 2 passed, 0 failed. Applied the full migration chain; verified preserved Tabo CT plus appended OR, both Vegetable contexts at the same date, old default Vegetable evidence, tenant isolation, different-context coexistence, and same-context uniqueness rejection.
- API Release build: passed, 0 errors. Three pre-existing warnings remain in `VendorsController`, `SlaughterController`, and `MunicipalitiesController`.
- `dotnet ef migrations has-pending-model-changes`: no model changes pending after scaffolding.
- `git diff --check`: passed before commit (re-run at final review).
- Full unit/integration suites and component tests were not run; this was a focused policy checkpoint. Docker/Testcontainers was available for the focused PostgreSQL run.
- No source writer, Tabo cutover, Vegetable assessment/mobile writer, OR/CT issuance, canonical settlement, report cash recognition, backfill, production activation, deployment, or APK release was implemented.

### Current policy and source readiness notes

| Operation / policy | Policy authority after this checkpoint | Writer / canonical cash status |
| --- | --- | --- |
| Tabo | Cantilan Default OR from 2026-09-27; earlier Default CT policy evidence remains effective for its historical period | Existing TPM shadow reconciliation only; no canonical collection/cutover |
| Vegetable/Fruit whole payment | `VegetableWholePayment` context -> OR from 2026-09-27 | No assessment/mobile writer, OR custody, canonical line, or activation |
| Vegetable/Fruit daily transaction | `VegetableDailyTransaction` context -> CT from 2026-09-27 | No assessment/mobile writer, CT custody, canonical line, or activation |
| Generic/default revenue policies | Existing Default stream and API behavior preserved | Existing source-specific readiness unchanged |

The existing earlier readiness matrix remains valid for all other sources. In particular, WCF retains its separately assigned UtilityBill/NPM CT workflow; Market Fees, Landing/Berthing, Transfer Large Cattle, and Vegetable/Fruit do not gain collection writers from this policy model; Weight & Measure remains NPM-derived and unassigned as a standalone operation.

## Claude Backend checkpoints (2026-09-30)

Lane: Claude Backend, branch `backend/claude-gap-completion-v3` from `2d42a39a`. Gap ledger:
[CLAUDE_BACKEND_GAP_AUDIT_20260930.md](CLAUDE_BACKEND_GAP_AUDIT_20260930.md). Luna's checkpoints above are unchanged.

**Correction to the readiness matrices above:** `LandingBerthingActivity`, a Market Fees collection-point model and the
governed configurable-service assessment model are not present on this accepted baseline (nor on
`interface-v2/clean-adoption`); they exist only on the unaccepted `interface-v2/ui-completion` candidate. On the
accepted baseline, Landing/Berthing, Market Fees, Vegetable/Fruit, Transfer Large Cattle and Fiesta/Araw have no
source model at all.

### CB-01 — WCF Web channel enforcement (`fix(wcf)`)

- **Scope:** `WcfCollectionWorkflow.PostCoreAsync`. `POST api/wcf-collections/collections` (Head/Admin Web "Collect with
  CT") now durably rejects a *new* Web intent with `WEB_CHANNEL_RETIRED` before any source, custody or posting work.
  The route and the `WebWcf` origin string are retained so history stays readable.
- **Invariants:** an operation already bound under the `WebWcf` origin is resolved first, so an equivalent retry
  returns its original Collection and a changed intent still returns `IDEMPOTENCY CONFLICT`. The rejected Web request
  never issued the office ticket, so the ticket stays `InOffice` (no reconciliation marking, no consumption). The
  rejection itself is a durable `Rejected` PostingOperation, so a retry of the same key cannot post later. Historical
  Web Collections, their operations, documents and projections are untouched and are not relabelled Mobile. Mobile
  posting, legacy Water reconciliation and Head/Admin monitoring reads (obligations, activity, reconciliation) are
  unchanged.
- **Not changed:** the legacy cumulative Web Water writer (`UtilitiesController.RecordPayment`) remains the live
  Legacy-authority path and is quiesced only through the scoped Water transition (gap CB-02, BLOCKED CUTOVER).
  IA-029 still describes Web+Mobile WCF entry as the future target; Core Brain should reconcile it with the current
  Mobile-only direction (gap CB-03).
- **Tests (PostgreSQL/Testcontainers):** WCF + settlement + operation-assignment filter: 23 passed, 0 failed. New:
  retired Web posting rejected while a concurrent Mobile post succeeds exactly once (office ticket stays in stock,
  durable rejection, retry does not post); a historical `WebWcf` Collection recreated with the pre-retirement intent
  shape still replays and still conflicts on changed intent; Mobile over-outstanding rejection after issue keeps the
  ticket out of stock. Tests that previously used Web as the posting vehicle now post through Mobile. Regression proof:
  disabling the guard made the retirement test fail; restored.
- **Validation:** API Release build 0 errors; `git diff --check` clean; no model change (no migration).
- **Frontend follow-up:** the Web "Collect with CT" button now receives a conflict; Claude UI is removing it.
- No push, merge, deployment, migration, data change, activation or APK.
