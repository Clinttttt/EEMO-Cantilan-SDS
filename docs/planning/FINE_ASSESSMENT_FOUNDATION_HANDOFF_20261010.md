# Fine Assessment foundation — October 10, 2026

Later local settlement development is recorded in [Fine Assessment canonical settlement](FINE_ASSESSMENT_SETTLEMENT_HANDOFF_20261010.md). Settlement limitations below describe this foundation checkpoint; activation and adjustment/cash-correction gates remain applicable.

Checkout: `C:\dev\stalltrack\eemo`; branch `codex/vegetable-fruit-space-workspace`; starting HEAD `06b13179`.

Implementation and tests: `51d1493c` — `feat(finance): add governed fine assessment foundation and account reads`. This handoff and the focused authority/supersession notes are committed separately. Settlement is **not implemented**.

## Accepted product behavior and governing evidence

The owner's October 10 implementation prompt supersedes the software-policy gates in [the prior design review](FINE_ASSESSMENT_DESIGN_REVIEW_20261010.md): Head/SuperAdmin creates assessments without a second approval; Admin/Collector cannot create or adjust them; assessment business date is server-current while violation date is separately recorded; closed historical registrations may remain liable; renewal/transfer does not move fines; distinct incidents in one month are allowed. Fine liability and cash collection remain different facts.

This is software behavior approval, **not independent verification of municipal ordinances, actual approved penalty schedules, notices or uncapped discretionary authority**. No policy/category is automatically seeded or enabled. Head-recorded governing evidence is necessary for each actual definition-version/source combination. The Head must verify its reference, applicable date basis and any closed-account/uncapped authority. The system records that assertion; it cannot independently authenticate the cited paper instrument.

Full remaining settlement, standalone payment, compatible classified OR lines, authorized collector collection and continuing payability after closure/definition retirement are accepted **future settlement requirements**. No settlement writer, Mobile capability or receipt-stock gate is activated here. Refund/cash correction remains separate. Cancellation/reduction/waiver is accepted in direction but precise liability-versus-cash semantics still require its own verified command increment; those controls remain disabled.

## Implemented source and data boundaries

- `FineAssessment` stores immutable original amount, exact ContractId or ObligationAccountId, source kind, occupant/unit/source snapshot, definition-version and policy IDs, violation/business dates, optional normalized incident reference, description, actor/time, request fingerprint and concurrency version.
- `FineAssessmentPolicy` records explicit governing evidence for one definition version and actual source/facility. It does not replace `PenaltyDefinition`. Separate Head-only policy API records reference, assessment-versus-violation version basis, uncapped authority and closed-account authority. No guessed policy backfill.
- `FineAssessmentEvent` records creation and frozen authorization evidence atomically with the assessment. No adjustment or cash event is simulated.
- Facility adapter covers NPM/NCC/TCC/BBQ/ICE **actual contracts**, resolving the existing derived occupancy window. There is no synthetic OccupancyId. It rejects dates outside that registration's verified window rather than charging the current replacement occupant.
- Obligation adapter covers KM and Vegetable/Fruits Monthly. FA is a distinct event-account kind requiring its own explicit policy; it is never a monthly lease. Unsupported sources and mismatched account kinds fail closed.
- Valid definitions retain FixedAmount equality or DirectApprovedAmount cent/ceiling validation. Uncapped direct amounts require an explicit policy assertion. Retired/inapplicable versions cannot create new assessments. The policy must explicitly specify which date selects the version for historical incidents; independent legal validity of that choice remains an office evidence question.

Assessment creation does not call Composer or create Collection, CollectionLine, allocation, SRC, PostingOperation, receipt, remittance, rental period or rate. Current settled amount is genuinely zero because no assessment settlement source/adapter is introduced in this increment; remaining equals the stored original principal. Future settlement must replace this assessment-only projection with canonical attributable allocations, not edit a counter or reuse a definition-version ID.

## Replay, incident detection and persistence

Tenant + ClientOperationId has a database unique constraint. A normalized fingerprint includes actor, typed account, policy, violation date, normalized incident reference, description and cent-normalized amount. Identical authorized replay returns the same assessment; altered intent conflicts. Assessment and creation audit use one SaveChanges transaction. PostgreSQL uniqueness resolves concurrent replays.

An explicit case reference is normalized uppercase and unique per tenant + liable registration + penalty code, independent of month and definition version. Recommended reference format includes office/year/case identity to avoid annual serial collisions; actual office numbering evidence still needs confirmation. Distinct reference values permit distinct incidents within the same month. Without a dependable case reference, similar account/penalty/date/description creates a warning requiring explicit distinct-incident confirmation. This is **not complete real-world duplicate prevention**, particularly for differently described or simultaneous unreferenced incidents.

Additive migration `20261010013040_GovernedFineAssessments20261010` creates three fine tables, restricted tenant-scoped evidence/source foreign keys, operation/incident indexes and the Contract tenant+ID alternate key. No data backfill or existing financial row update. The model snapshot is updated. Tenant backup/restore includes all three tables, with dependency ordering derived from the model. Production and the user's existing local tenant database are not migration targets in this task.

Companion migration `20261010014548_FineAssessmentPolicyIntegrity20261010` constrains the recorded version-date basis and binds each assessment's tenant, policy, penalty version and account kind together through a composite foreign key. Both migrations were applied only by Testcontainers to disposable PostgreSQL 16 during integration tests. Neither was applied to the existing local tenant database or any hosted database.

## API and presentation

Authenticated `api/fine-assessments` supports register/account-scoped reads, eligible account search, exact account read, governing policies, Head-only creation and audit history. Head-only `POST policies` and `POST` have service-level role/tenant guards as well as controller authorization. Reads permit existing office Admin/SuperAdmin roles; knowing an ID grants no cross-tenant access. Account reads never join by name. Register reads fail explicitly above 200 matching rows rather than silently truncate a balance; larger scopes need narrowing until pagination is added.

`/operations/fines` preserves its route and existing definitions/Collected workflows. It adds Assessments search/status/register/details and a shared `FineAssessmentDrawer`: Account Information → Fine Details → Review → Confirm Assessment. Original Collected rows remain definition-version canonical lines. Policy setup is part of the existing Penalty Setup area. Search results contain actual source IDs, including eligible historical records, not a second payor catalog.

Accepted Account Review keeps its existing year ledger, rental totals, footer and shell. Only Other assessed charges gains the same authoritative assessment read, exact ID/amounts and explicit loading/failure/empty behavior. Failed fine reads do not become zero. Head Assess Fine navigation is shown only for an explicit eligible source policy and preserves typed account context through the Fines route. Opening that drawer is navigation, not cash posting. Rental and fines are not merged into a new receivable total.

## Deferred implementation

- No `CollectionSourceKind.FineAssessment`, settlement quote, Composer assessment draft line, payment POST, standalone payment activation or Mobile writer.
- No cancellation/reduction/waiver command. Original principal and creation evidence remain intact; future adjustments must retain previous/resulting liability, actor/reason and audit history. Already-collected money must use the separately verified Collection correction/refund contract.
- No automatic fines, rental reassessment, account reopening/renewal, Arrears reclassification or liability transfer.
- No policy records claiming unverified legal categories are seeded. Office notice requirements, historical offense/version authority and dependable case numbering need actual governing evidence before operational activation.

Next increment: verify accepted adjustment semantics and implement audited unpaid-liability commands, or separately implement the full-only assessment-specific canonical settlement protocol. Neither is silently included in this foundation.

## Verification

Executed Release commands:

| Validation | Result |
| --- | --- |
| `dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~FineAssessmentTests` | 24 passed, 0 failed, 0 skipped; real disposable PostgreSQL 16 |
| `dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter FullyQualifiedName~FineAssessmentComponentTests` | 9 passed, 0 failed, 0 skipped |
| Full Web component suite, same project with no filter | 889 passed, 0 failed, 0 skipped |
| Full Unit suite: `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release` | 2,528 passed, 0 failed, 0 skipped |
| `dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release` | Passed; latest rebuilding run reported 9 existing warnings, 0 errors |
| `dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj --configuration Release` | Passed; latest incremental run reported 0 warnings, 0 errors |
| Scoped CSS source and generated bundle | 164 files brace-balanced |
| `git diff --check` | Passed |
| EF `migrations has-pending-model-changes` (Release, no build) | No pending model changes |

Earlier validation failures were corrected: no-tracking cyclic contract lookup, missing fine-read mocks, drawer error literal, backup export coverage, and EF boundary allowlist. Docker was initially unavailable (22 tests skipped at that earlier attempt); the final PostgreSQL run has no skips. Existing unrelated nullable/analyzer warnings remain. A deliberate uncapped-policy guard mutation caused its Unit test to fail and was restored. A new sub-cent replay regression was demonstrated failing before adding precision validation before fingerprint normalization; the final PostgreSQL suite passes it. Valid decimal scale variants still replay the same assessment.

The PostgreSQL cases verify Head versus Admin/Collector, tenant/source boundaries, historical occupant windows, five facility contracts plus KM/VF/explicit-policy FA, ceilings/fixed/uncapped amounts, version-date evidence, distinct incidents, replay/conflict/concurrency, creation audit, authoritative reads, and unchanged legacy fine Collection/SRC evidence. Assessment creation leaves Collections, CollectionLines, PostingOperations, rental periods and remittances unchanged. This proves no assessment cash recognition; the entire reporting or PostgreSQL suite was not claimed as executed.

Rendered component fixtures were inspected at 1440, 1024 and 390px: assessment register, account charges, creation drawer and confirmation drawer (12 viewport checks). No document overflow; wide tables scroll within their own surface, and drawer footer controls remain inside the viewport. Screenshot inspection led to reusing the drawer's actual textarea style and compact shared toolbar. Artifacts are local and excluded from commits. **Authenticated runtime acceptance remains pending**: restarting the API would auto-apply migrations to the existing tenant database, which is expressly outside this authorization. No Android or Mobile settlement validation is claimed.

Earlier audit findings are historical; the accepted Account Review design remains frozen. Protected dirty ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE edits remain excluded and their original diffs preserved. No push, merge, deploy, production migration or APK publication.

## Scoped file inventory

- Domain: `Entities/Revenue/FineAssessment.cs`.
- Application: `Common/Revenue/FineAssessmentWorkflow.cs`, `Dtos/Revenue/FineAssessmentDtos.cs`, `Common/Interface/Persistence/IAppDbContext.cs`, `Common/Interface/ApiClients/IPenaltiesApiClient.cs`, `DependencyInjection.cs`.
- API: `Controllers/Revenue/FineAssessmentsController.cs`.
- Infrastructure: `Persistence/AppDbContext.cs`, `Persistence/Configuration/FineAssessmentConfiguration.cs`, the two named migrations and their designer files, `Migrations/AppDbContextModelSnapshot.cs`, `Repositories/SystemHealth/TenantDataTables.cs`, `Repositories/SystemHealth/TenantExportRepository.cs`.
- HttpClients: `ApiClients/PenaltiesApiClient.cs` (existing authenticated client registration reused).
- Client: `Components/Pages/Menus/Fines.razor`, `Components/Shared/FollowUpAccountReview.razor`, `AccountFineAssessments.razor`, `FineAssessmentDrawer.razor`, `FineAssessmentsRegister.razor`, `FinePolicySetup.razor` under the shared directory.
- Tests: `IntegrationTests/FineAssessmentTests.cs`, `ComponentTests/Pages/FineAssessmentComponentTests.cs`, `ComponentTests/Pages/FollowUpAccountReviewTests.cs`, `Testing/Application/Revenue/FineAssessmentDomainTests.cs`, `Testing/Architecture/ApplicationEfBoundaryTests.cs` (each under its EEMOCantilanSDS project).
- Documentation: this focused handoff, `FINE_ASSESSMENT_DESIGN_REVIEW_20261010.md` supersession note, and `docs/business/EEMO_OPERATIONAL_RULEBOOK.md` accepted product-direction note. No protected planning files staged.
