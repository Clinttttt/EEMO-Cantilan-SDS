# Fine Assessment canonical settlement — October 10, 2026

Checkout: `C:\dev\stalltrack\eemo`. Branch: `codex/vegetable-fruit-space-workspace`. Starting HEAD: `b6562405`.

Implementation/tests/migration commit: `28e15446` — `feat(finance): settle fine assessments through canonical OR drafts`. This handoff and the foundation supersession note are committed separately.

## Status and authority

Assessment-specific Web settlement is developed locally. Operational activation remains gated by actual governing penalty/source evidence, approval to migrate the intended database, authenticated runtime acceptance and release authorization. No existing local tenant or hosted database was migrated, no application was started against one, and no release/integration/push occurred.

The owner accepted full remaining fine payment, standalone OR payment, and compatible rental/fine OR composition for the same liable registration. Creation remains Head-only (`SuperAdmin`); settlement does not create another assessment. Closure, renewal/transfer and definition retirement do not move or invalidate an existing valid liability. These software decisions do not independently verify an ordinance or penalty schedule.

## Source, quote and posting

`CollectionSourceKind.FineAssessment = 15` is additive. `PenaltyDefinition = 9` retains its existing meaning: a new approved penalty charge, not assessment settlement. `CollectionSourceAuthorityMap` treats assessment settlement as canonical. Mobile payment and Mobile cash correction are not enabled for this source.

`FineAssessmentCollectionSource` resolves frozen assessment/policy evidence, current approved OR classification, and attributable canonical allocations plus correction effects. Fines register and account-linked reads use that same balance. No editable paid counter exists. Unknown/unavailable evidence fails closed rather than returning a fabricated zero. Current penalty-version retirement does not reprice the assessment.

`CollectionComposerWorkflow.Fines` supplies typed source authorization, quotes, source-aware draft discovery and exact liable-account context checks. Existing Composer review/post and `CanonicalCollectionPostingCoordinator` remain the cash writer. The API is `api/fine-settlements`:

- `GET /{id}/quote`: frozen source/evidence, principal, effective collected, remaining, version/token and OR eligibility.
- `GET /{id}/current-draft`: caller-owned compatible draft; unrelated, stale or CT drafts fail closed.
- `GET /{id}/history`: actual assessment allocations, canonical SRC/date and correction-adjusted effect.
- `POST /drafts`: `AssessmentId`, `QuoteToken`, full `Amount`, optional existing-draft `ExpectedRevision`.
- `GET /drafts/{id}`; `POST /drafts/{id}/review`; `POST /drafts/{id}/post`: existing revision and durable `ClientOperationId` contracts. `POST /drafts/{id}/discard` reuses non-cash draft discard; Collector permission is restricted to their own authorized standalone fine draft.

Partial and excessive fine payments are rejected server-side. Review/post re-resolve the quote, source policy and original payer evidence. No OR stock is manufactured or made a prerequisite for SRC-first posting. Physical receipt evidence remains separate.

## Permissions and mixed OR boundary

Head/Admin retain their established office collection authority. Collector settlement requires an authenticated tenant, active Collector record, and the original account's actual facility assignment or operation assignment. Collector cannot create/adjust assessments or gain access to arbitrary office drafts. Collector Web entry is `/collections/fine-payment/{assessmentId}`; it permits a standalone fine draft only. Mobile settlement remains deferred.

Office users may add a fine to an existing canonical OR rental draft for the exact same original Contract or ObligationAccount. KM rent-first + fine is verified against PostgreSQL. Equal names on different accounts are explicitly rejected. CT, unsupported sources, Legacy/Pending settlement authority and incompatible historical occupant contexts remain rejected. The drawer displays existing rental lines before review and every resulting OR line before confirming the total. Collector mixed composition and broader source combinations require their own proven capability paths; they are not implied by this office composition support.

An existing source-owned account's absent Business Payor is normalized from an empty preview ID to a null draft linkage. No new Business Payor dependency or name-based link was added.

## Atomicity, replay and corrections

Posting increments the assessment's EF concurrency version as a settlement claim inside the same `SaveChanges` transaction as Collection/SRC, typed allocation, posted draft and PostingOperation. Two reviewed drafts racing for one fine have one effective winner. Durable posting identity/fingerprint, review revision and quote snapshot protect lost-response retries and changed-intent reuse. A forced PostgreSQL cash-insert failure rolls back the assessment claim, allocations and durable outcome; retry then succeeds.

`CollectionAllocations.FineAssessmentId` is nullable for legacy sources; source 15 requires it to equal `SourceId`. A restricted composite tenant+assessment foreign key prevents cross-tenant attribution. There is no unique forever-paid flag: an effective reversal can restore the original fine balance without deleting its first SRC.

Reads apply allocation-level correction effects to the fine, never a whole mixed Collection amount. The collected-fines register applies line-attributable effects, preserving original cash evidence and reporting an effective total. Tests record an accepted canonical reversal event and verify restored outstanding, unchanged original SRC and correct later cash recognition. This proves projection compatibility; it does not activate a new fine cash-correction command. Mobile correction of fine-bearing collections is deliberately rejected pending that separate protocol.

Head cancellation/reduction/waiver, liability below already collected cash, refunds and cash correction UI remain deferred. No adjustment writer, negative cash workaround or historical backfill was added.

## UI and reporting

Fines keeps Assessments, Collected and existing versioned Penalty Setup. Assessments expose actual balances, details/payment history and payment entry. Collected includes both legacy definition-based fine cash and assessment settlements. No unpaid assessment contributes cash revenue.

The accepted Account Review structure, rental year ledger and footer remain intact. Other assessed charges uses authoritative assessment reads and an assessment-specific payment link. The payment drawer uses the existing municipal drawer shell, stable footer, review-before-post, unchanged full quoted amount and stable retry operation identity. Successful posting displays its actual SRC and refreshes assessment positions. Fine details expose recorded SRC allocations and their effective correction amounts.

Canonical and official Monthly Income classify actual fine lines as Fines/Penalties, separate from rental. Tests verify the official Fines row and canonical totals: zero cash before payment, fine cash after posting, zero effective revenue after reversal and no double recognition after later payment. Mixed OR verification keeps the rental amount out of the official Fines row. Remittance position records actual authorized collector cash; unpaid assessments do not enter it. Collector report reads now include assessment-settlement cash alongside legacy fine lines.

## Migration and validation

New additive migration: `20261010021751_FineAssessmentCanonicalSettlement20261010`. It adds the allocation reference/FK/index/check and widens existing draft/Collection source-shape constraints for 15. It rewrites no historical data. Along with the two foundation migrations, it was applied only by `PostgresFixture` to a blank, disposable Docker `postgres:16-alpine` database named `stalltrack_tests`, cleaned up after the run. No user-local or hosted connection was used.

Executed validation:

- Focused PostgreSQL `--filter FullyQualifiedName~FineAssessmentTests`: 37 passed, 0 failed, 0 skipped; final read/history changes were rerun.
- Full integration suite: 409 passed, 0 failed, 7 skipped (416 total). The seven restored-snapshot tests require `STALLTRACK_SNAPSHOT_DB`; none is configured. Disposable PostgreSQL tests ran.
- Full Unit suite, separately from Web: 2,529 passed, 0 failed, 0 skipped.
- Focused Web assessment/payment tests: 16 passed, 0 failed, 0 skipped. Earlier full Web: 895 passed. Final full Web: 895 passed, 1 failed, 0 skipped (896 total). The one failure is `AriaStateAttributeTests.NoAriaStateIsBoundStraightToABool`, in concurrently edited `AuditTrail.razor` (`aria-pressed` bound directly to booleans). Settlement tests passed; that actively edited unrelated file was preserved and not repaired/staged here. Full Web acceptance is therefore pending its owner's correction.
- Client and API Release builds passed. Final incremental builds: each 0 errors, 0 warnings. Earlier compiling builds reported existing application/client/analyzer warnings; the new drawer's nullable dereference warning was corrected.
- Source enum regression test was observed failing before the canonical authority-map extension, then passed in the full Unit suite.
- All 163 source scoped CSS files and generated Client scoped bundle are brace-balanced; `git diff --check` passed. EF `migrations has-pending-model-changes --configuration Release --no-build` reports no pending model change and did not connect/migrate a tenant database.

Fixture screenshots were rendered and inspected at 1440/1024/390px for the register, account charges, quote, reviewed standalone payment and reviewed mixed OR. All 15 measured layouts have no document-width overflow; drawer footer controls stay within the viewport. These are rendered component fixtures, not authenticated runtime acceptance. Authenticated acceptance remains pending in an explicitly approved migrated disposable/runtime environment; existing tenant startup migration was not risked to obtain screenshots.

Commands used (each test suite ran separately from the Unit/Web counterpart):

```powershell
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~FineAssessmentTests
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter FullyQualifiedName~FineAssessmentComponentTests
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj --configuration Release
dotnet ef migrations has-pending-model-changes --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Infrastructure --configuration Release --no-build
git diff --check
```

## Exact implementation file inventory

- `EEMOCantilanSDS.Api/Controllers/Revenue/FineSettlementsController.cs`
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IPenaltiesApiClient.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/CollectionComposerWorkflow.Fines.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/CollectionComposerWorkflow.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/FineAssessmentCollectionSource.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/FineAssessmentWorkflow.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/MobileCollectionCorrectionWorkflow.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/PenaltyDefinitionWorkflow.cs`
- `EEMOCantilanSDS.Application/Dtos/Revenue/FineAssessmentDtos.cs`
- `EEMOCantilanSDS.Application/Dtos/Revenue/PenaltyDtos.cs`
- `EEMOCantilanSDS.Client/Components/Pages/Collections/FinePayment.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Fines.razor`
- `EEMOCantilanSDS.Client/Components/Shared/AccountFineAssessments.razor`
- `EEMOCantilanSDS.Client/Components/Shared/AddNewDrawer.razor`
- `EEMOCantilanSDS.Client/Components/Shared/FineAssessmentsRegister.razor`
- `EEMOCantilanSDS.Client/Components/Shared/FinePaymentDrawer.razor`
- `EEMOCantilanSDS.ComponentTests/Pages/FineAssessmentComponentTests.Settlement.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/FineAssessmentComponentTests.cs`
- `EEMOCantilanSDS.Domain/Constants/CollectionSourceAuthorityMap.cs`
- `EEMOCantilanSDS.Domain/Entities/Revenue/CollectionAllocation.cs`
- `EEMOCantilanSDS.Domain/Entities/Revenue/FineAssessment.cs`
- `EEMOCantilanSDS.Domain/Enums/CollectionSourceKind.cs`
- `EEMOCantilanSDS.HttpClients/ApiClients/PenaltiesApiClient.cs`
- `EEMOCantilanSDS.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/CollectionAllocationConfiguration.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/CollectionLineConfiguration.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/WebCollectionDraftAllocationConfiguration.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/WebCollectionDraftLineConfiguration.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Migrations/20261010021751_FineAssessmentCanonicalSettlement20261010.Designer.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Migrations/20261010021751_FineAssessmentCanonicalSettlement20261010.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/CollectorReportQueries.cs`
- `EEMOCantilanSDS.IntegrationTests/FineAssessmentTests.Settlement.cs`
- `EEMOCantilanSDS.IntegrationTests/FineAssessmentTests.cs`
- `EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs`

Documentation: this handoff and the supersession note in `FINE_ASSESSMENT_FOUNDATION_HANDOFF_20261010.md`.

## Remaining increment and protected work

Next: approved isolated authenticated Web acceptance, source-by-source mixed OR capability verification, then a separately audited liability-adjustment/cash-correction protocol. Collector mixed drafts and Mobile discovery/assignment/offline/retry support are separate increments. Do not enable automatic fines, partial settlement, account reopening or renewal from this implementation.

The three dirty planning documents were preserved unstaged (original diff counts 4/0, 7/1, 10/0). Concurrent Login, Menu and Audit Trail edits also appeared during validation; they are unrelated and excluded from settlement commits. Canonical status reconciliation remains with their owners. The foundation handoff's settlement-deferred statement is a historical checkpoint, superseded for local development by this handoff; its operational activation gates remain valid.
