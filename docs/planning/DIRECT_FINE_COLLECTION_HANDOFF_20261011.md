# Direct Fine Collection and Fines workspace restructuring

Date: October 11, 2026. Checkout: `C:\dev\stalltrack\eemo`. Branch: `codex/vegetable-fruit-space-workspace`. Starting and current HEAD: `204a53f1` (nothing committed yet; awaiting review).

Codex built the feature and ran out of usage before final verification. Claude Sonnet 5.5 inherited the working tree under an explicit, task-limited ownership transfer, repaired the remaining defects and re-verified against the current tree.

## Status

| Item | Status |
| --- | --- |
| Direct fine workflow, API, DTOs, authority entity, additive migration, source-authority mapping | Implemented and verified (disposable PostgreSQL) |
| Fines workspace `Collections / Assessments / Penalty settings`, Direct fine drawer, Assess-then-pay banner | Implemented and verified (component tests); authenticated browser acceptance pending |
| Real Cantilan direct-fine activation | **Blocked by missing authority.** No `DirectFineAuthority` rows are seeded and no setup writer exists; the drawer shows an explicit unavailable state |
| Head cash remittance/accountability | **Gap, not invented.** See below |
| Authenticated desktop/tablet/mobile browser review | Not performed |

## Financial contract

- Direct fine is actual money received, with no registered account, contract, assessment or fabricated payor. It is Head-only (`SuperAdmin`) at the controller and again inside `DirectFineCollectionWorkflow`.
- A category exists only through a reviewed `DirectFineAuthority` (tenant-bound FK to an exact `PenaltyDefinition` version, governing and approval references, effective dates, reason/reference/payer-particulars requirements, uncapped flag). Resolution requires the version to be active, in force on the server business date, and Fixed or Direct-approved basis; a direct-approved version with no ceiling needs explicit `AllowsUncappedAmount`.
- Posting delegates to the existing `CanonicalCollectionPostingCoordinator` with `CollectionSourceKind.DirectFineCollection` (16), `SourceId = DirectFineAuthority.Id`, the dedicated Fines & Penalties classification and an Official Receipt policy. No second posting engine, no CollectionAllocation, no FineAssessment, no Contract, no ObligationAccount.
- Idempotency: stable `ClientOperationId` plus intent fingerprint over actor/origin/normalized payload. Same intent replays the original SRC; changed intent is a 409; serializable transaction with recovery from a concurrent unique-key loss.
- Business date must equal the server Philippine date. Amount is checked against the approved fixed amount or ceiling in whole centavos.
- The three workflows stay separate: direct fine (cash, no liability), Assess fine (liability, no cash), Collect assessed fine (existing settlement of an exact saved assessment). Direct fine never pays an assessment and an unpaid assessment never appears as collected.

## Migration

`20261011005707_DirectFineCollectionAuthority20261011` is additive: new `DirectFineAuthorities` table (tenant-scoped alternate key, composite FK to `PenaltyDefinitions`, date check), a stored generated column `CollectionLines.DirectFineAuthorityId` (SourceKind 16 only) with a composite tenant-bound FK and index, and a widened `CK_CollectionLines_SourceShape`. Down reverses it. **Deployment note:** adding a stored generated column and recreating the check constraint rewrites and revalidates `CollectionLines` under a lock; production applies migrations at startup, so schedule accordingly. It was applied only to the disposable test container. No existing database was migrated and no API/Client was started.

## Repairs made in this pass

1. `FinesWorkspaceTests`: two stale tests updated to the approved Collections-first structure (tab renamed from "Collected" to "Collections"; the assessments empty state now belongs to the Assessments tab). Assertions were kept and extended, not weakened: nothing is recorded or number-entered until the Head opens an explicit drawer, on either view.
2. **Backup coverage defect:** `DirectFineAuthorities` was tenant-owned but in no backup set, so a backup would omit rows that posted CollectionLines reference. Added to `TenantDataTables.Restorable` and `TenantExportRepository`. Restore skips generated columns, so `DirectFineAuthorityId` is recomputed exactly as the SRC code is.
3. The `CollectionsRegisterDto.NetAmount` compile error in the old PostgreSQL log was stale (source already used `Net`). The old "expected 100, actual 0" PostgreSQL failure does not reproduce.

## Validation (current tree; new logs in `artifacts/claude-*.log`)

| Run | Result |
| --- | --- |
| Focused Fines/FineAssessment/SpaceRental Web | 52 passed, 0 failed, 0 skipped |
| Full Web components | 952 passed, 0 failed, 0 skipped |
| Full Unit (run separately) | 2531 passed, 0 failed, 0 skipped (first run failed 2 backup-coverage tests, fixed above) |
| PostgreSQL `FineAssessmentTests` (Testcontainers `postgres:16-alpine`, own container, migrations applied) | 69 passed, 0 failed, 0 skipped |
| API Release / Client Release | 0 warnings, 0 errors each |
| Scoped CSS braces (Fines, DirectFineCollectionDrawer) | balanced |
| `git diff --check` (tracked, excluding protected planning docs) | clean |

Direct-fine PostgreSQL coverage: Head success with canonical Collection/SRC and Fines revenue; Admin/Collector/Payor rejected (403); zero, negative, over-precision and over-ceiling rejected with no cash; required evidence; wrong tenant and FK failure; replay, changed-payload conflict and concurrent duplicate; uncapped and fixed rules; retirement stops new cash but replays existing; reversal removes effective income and keeps the SRC; forced cash failure rolls back and the same intent retries.

The earlier `SpaceRentalConsistencyTests` ICE/NCC failure did not reproduce in the focused or full run. It was not caused by this work; treat it as a possible order-dependent flake and re-check if it recurs.

## Reporting integration

- Monthly Income: Fines & Penalties from the canonical line; reversal returns it to zero.
- Fines Collections register, Collection Activity and Collections Register: direct rows labelled "Direct fine collection", with the actual line amount (never a mixed OR total) and no blank-payor error.
- Remittance: **gap.** `RemittanceWorkflow` considers only collector-attributed collections (`CollectorId != null`). Direct fine cash has no collector and is correctly not attributed to one, so it appears in no remittance position (verified: collected 0). The office needs to rule how Head-held cash is accounted for and what OR serial/accountable-document treatment applies. Nothing was fabricated.

## Remaining decisions and gates

- Verified Cantilan fine categories, amount rules, payer/evidence requirements and no-registration approval reference before any `DirectFineAuthority` row is created, plus the reviewed setup path that will create it.
- Head cash accountability and physical OR serial handling for direct fines (SRC does not prove an OR was issued).
- Authenticated Head acceptance at 1440/1024/390 px, including drawer states and a disposable-database posting. Not done: starting the API/Client here would apply migrations at startup to the configured development database, which was not authorized.
- Deployment timing for the migration lock noted above.

## Task-owned files

New: `DirectFinesController.cs`, `DirectFineCollectionWorkflow.cs`, `DirectFineDtos.cs`, `DirectFineAuthority.cs`, `DirectFineAuthorityConfiguration.cs`, the migration and Designer, `DirectFineCollectionDrawer.razor` and `.razor.css`, `direct-fine-intent.js`, `FineAssessmentComponentTests.DirectCollections.cs`, `FineAssessmentTests.DirectCollections.cs`, this handoff.

Modified: `IPenaltiesApiClient`, `PenaltiesApiClient`, `IAppDbContext`, `AppDbContext`, `AppDbContextModelSnapshot`, `CollectionLineConfiguration`, `CollectionSourceKind`, `CollectionSourceAuthorityMap`, `PenaltyDefinitionWorkflow`, `CollectionsReportWorkflow`, `CollectionActivityReader`, `DependencyInjection`, `TenantDataTables`, `TenantExportRepository`, `Fines.razor` and `.razor.css`, `FineAssessmentsRegister.razor`, `FineAssessmentComponentTests.FinancialUx.cs` and `.Refinement.cs`, `FinesWorkspaceTests.cs`, `ApplicationEfBoundaryTests.cs`.

Intentionally excluded (protected, untouched): `Login.razor` and CSS, `AuditTrail.razor` and CSS, `Menu.razor`, `ACTIVE_WORKSTREAMS.md`, `CURRENT_RELEASE_STATE.md`, `VEGETABLE_FRUIT_WORKSPACE_20261008.md`, and everything under `artifacts/`.

## Refinement pass (after `43fb76bb`)

Presentation and read-model refinement only; no new writer, migration, source kind or remittance behavior.

- **Hero:** the shared civic-blue `V3Hero` (compact) replaces the plain header. Three figures come from a new server summary, `GET api/penalties/summary`, which reuses the register query so totals cannot disagree with the list: collected this month (effective, correction-aware fine lines), direct fine collections, and outstanding assessments (correction-aware unpaid balance as of today, never counted as collected). A failed read shows "Unavailable right now" instead of a zero.
- **Register rows** now carry a typed `Kind` (Direct / Assessed / Definition) plus account, facility and reason. The Collections table shows type, fine, reason, payer or account and facility, status and the effective line amount; a direct fine with no payer reads "Direct fine collection", never an account. A type filter was added.
- **Actions:** Collections offers **Record direct fine** (primary) and **Assess fine** (opens the existing assessment drawer on the Assessments view); Assessments keeps **Assess fine** and the row **Record payment**. The register notifies the page so the hero refreshes after an assessment or payment.
- **Direct fine drawer:** category, amount, violation reason, payer name / receipt particulars and supporting reference, the last three optional unless the authority requires them. Free text supplements but never creates or bypasses an authorized category; the server still enforces category, fixed amount/ceiling, date and required evidence.
- **Defect fixed:** the amount rendered as `₱50.00.ToString("N2")` because `@( ... ).ToString` ended the Razor expression. It now uses the page-local `Money` helper; a regression test fails when the defect is reintroduced (verified once).
- **Validation:** focused Fines Web 48/48; full Web 957/957; full Unit 2531/2531; PostgreSQL `FineAssessmentTests` 70/70 on the disposable container (new test covers summary, register kinds and reversal); API and Client Release 0 warnings, 0 errors; scoped CSS braces balanced; `git diff --check` clean.
- **Still pending:** authenticated 1440/1024/390 px browser review (starting the API/Client would apply migrations to the configured development database), real Cantilan authority, Head cash accountability and physical OR treatment, and migration deployment timing, all as recorded above.

## Professional polish pass

Presentation only; no financial behavior, contract or migration change.

- **Hero:** the Official Receipt marker is a white pill with a gold code tile and navy text, legible on the blue band. Scoped to this page; the shared `InstrumentChip` is unchanged.
- **Section tabs:** Collections / Assessments / Penalty settings are one `.fn-nav` container with `position: sticky; top: 0`, a solid canvas background and a hairline rule, so sections can be switched while a long register scrolls. Buttons keep `aria-pressed`.
- **Registers:** Collections, Assessments, Penalty settings and Where fines apply share one rhythm: roomier rows, bold names with secondary context beneath, a source tag (Direct fine / Assessed fine payment / Fine on receipt) on its own line, text-labelled status pills (the shared `.status-pill`, plus additive `pill-danger` and `pill-pending`), right-aligned tabular amounts, a reversed or corrected amount that shows what it contributes and what was posted, and styled empty states.
- **Row actions:** View details, Record payment and Activity carry an icon plus a visible label and are grouped at the row end; Activity has an accessible name that includes the SRC.
- **Drawers:** the case detail drawer is a structured case view (account block, assessed / collected / outstanding strip, fine details, assessment and payment history tables). Assess fine is a stepped flow (Account, Fine details, Review) with a structured account card (facility, unit, area, recorded term, account type, status), a Change account control, a clean no-approved-policy state and a review with a prominent amount. Record payment and Direct fine review and success use the same amount-first structure.
- **Tests:** 5 new component regressions plus a populated-workspace fixture test (which also asserts every tab renders); full Web 963/963, Unit 2531/2531, Client and API Release 0 warnings and 0 errors, CSS braces balanced in every touched stylesheet and the generated bundle.
- **Rendered check:** headless Chrome screenshots of file fixtures against the real generated CSS bundle at 1440, 1024 and a true 390 px frame showed no page-level overflow; wide registers scroll inside their surface. These are fixture renders, not authenticated acceptance.
