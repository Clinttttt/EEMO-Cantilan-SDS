# Follow-up: verified liable account and in-context fine assessment

Date: 2026-10-10. Checkout: `C:\dev\stalltrack\eemo`. Branch: `codex/vegetable-fruit-space-workspace`.
Starting HEAD for this increment: `7fbda0cb`; the preceding NPM presentation/verification increment started at `6f8585a4`.

## Defect and actual fix

Live facility queue rows intentionally retain a StallId with null ContractId for current occupancy. Review Account previously required an explicit contract for its fine-account reference. Rental reads and settlement targeting were correct; changing their null-contract meaning would have changed liability semantics.

The existing FineAssessmentWorkflow now exposes a read-only identity bridge through `GET api/fine-assessments/facility-account`, with `stallId`, typed `facility`, `snapshotDate` and optional `contractId`. Existing Head/Admin tenant guards apply. It resolves an exact tenant-scoped Stall and its contracts, validates the facility, and returns the existing FineAccountOption/FineAccountRef contract.

- Explicit historical ContractId must belong to that exact tenant and stall. It remains the original liable registration after reassignment.
- Null ContractId requires one unambiguous current, active occupancy, a non-closed stall, and a domain occupancy window covering the queue snapshot. Missing, competing or reassigned occupancy fails closed with reload/source-account guidance.
- No name matching, number lookup, new account writer, schema change or guessed historical contract is introduced. Same stall numbers across Fish, Meat and Vegetable areas cannot exchange identities.
- The bridge supports NPM/NCC/TCC/BBQ/ICE. Existing source-owned obligation-account mapping remains unchanged.

Review Account uses that verified identity for Other assessed charges and the occupant label. Its payment-month and eligible-day reads still receive the queue's original ContractId, including null. The existing accepted rental ledger, footer and balances remain unchanged.

## Reuse and transition

AccountFineAssessments retains its authoritative balance projection. A Head with an applicable approved policy receives a compact Assess fine button. No applicable policy is a distinct state from an unverifiable account or failed API read; no policy is seeded.

The button opens the existing FineAssessmentDrawer with the exact account already selected. The review shell remains mounted but inactive; only one dialog is active. Returning restores the existing rental disclosure/scroll region. Saving increments only the fine-data refresh version, without reloading rental periods or changing payment targeting. Existing focus-trap binding follows the active dialog. Admin/Collector creation remains unavailable; server Head-only creation authorization is unchanged.

The existing assessment writer and its no-Collection/no-SRC behavior are reused. No settlement, cash correction, waiver, renewal or reopening writer changed.

## Verification

The null-contract regression test was run before the fix and failed because no AccountFineAssessments component existed. After the fix:

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Focused Review Account/Fine Assessment Web | 43 | 0 | 0 |
| Full Web component suite | 925 | 0 | 0 |
| Full Unit suite, separately | 2529 | 0 | 0 |
| Focused real disposable PostgreSQL fine/migration suite | 50 | 0 | 0 |

The first full Web run found one composition fixture missing IPenaltiesApiClient registration (924 passed, one failed). That fixture was corrected; the final full run passed. Existing source-payment transition remains covered.

PostgreSQL cases cover current and explicit historical identities across five facilities, reassignment, competing contracts, duplicate numbers across areas, tenant/source rejection, Head/Admin/Collector boundaries, and create/read using the resolved account without cash/allocation side effects. Existing fine settlement, correction, reporting and migration compatibility tests also pass.

Commands used: separate Release Web and Unit project tests; focused Web filter `FullyQualifiedName~FollowUpAccountReviewTests|FullyQualifiedName~FineAssessmentComponentTests`; PostgreSQL filter `FullyQualifiedName~FineAssessment`; Release builds of Client and API; `git diff --check`; scoped CSS source/generated brace validation. Final Client/API builds each have zero errors and zero warnings. Compilation/test logs also retain existing nullable/analyzer warnings and the AngleSharp NU1902 advisory. No dependencies changed.

All 167 scoped source/generated CSS files are brace-balanced. Three rendered fixture stages (assessment, confirmation, saved-review) at 1440/1024/390px produced nine layout checks: one active dialog, no document overflow and footer buttons within the viewport. Screenshots were inspected, including narrow assessment/confirmation and the returned expanded rental review. Fine tables retain their existing contained horizontal scrolling. Fixtures are not authenticated acceptance.

## Runtime and activation boundaries

The preceding handoff's local read-only inspection remains the evidence: the configured development target `localhost:5432 / EEMOCantilanSDS.DB` already contains the four fine/association migrations, two penalty definitions, zero fine eligibility policies and zero assessments. This task did not apply those migrations or create tenant data. Running-process connection overrides were not independently verified.

No API/Client process was restarted: development startup migration application is enabled and an unsafe restart must not be used to obtain browser acceptance. Release binaries were built, but the existing Debug runtime predates this fix. Browser automation surfaces were unavailable; authenticated Head assessment/save/payment acceptance remains pending. Tests used disposable PostgreSQL, not the local tenant database.

The prior NPM verification handoff's unresolved live-queue identity gap is superseded by this implementation. Its local-policy and authenticated-acceptance limitations remain current. Smallest activation step: verify actual governing evidence, record a legitimately applicable policy through existing Head setup, and perform authenticated checks in an approved migration-safe environment. Do not seed a fictitious fine for demonstration.

Head cancellation/reduction/waiver/refunds, partial payment, Mobile settlement and unverified Collector mixed drafts remain separate deferred work. Existing standalone and office-compatible mixed settlement were verified, not rebuilt.

## Task-owned file inventory

- `EEMOCantilanSDS.Api/Controllers/Revenue/FineAssessmentsController.cs`
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IPenaltiesApiClient.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/FineAssessmentWorkflow.cs`
- `EEMOCantilanSDS.HttpClients/ApiClients/PenaltiesApiClient.cs`
- `EEMOCantilanSDS.Client/Components/Shared/AccountFineAssessments.razor`
- `EEMOCantilanSDS.Client/Components/Shared/FollowUpAccountReview.razor`
- `EEMOCantilanSDS.ComponentTests/Pages/FineAssessmentComponentTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/FollowUpAccountReviewTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/FollowUpAccountReviewTests.Fines.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/FollowUpQueueCompositionTests.cs`
- `EEMOCantilanSDS.IntegrationTests/FineAssessmentTests.AccountResolution.cs`
- This handoff.

Protected ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE edits plus concurrent Login/AuditTrail/Menu changes remain excluded. No worktree/branch switch, tenant migration, push, merge, deployment or production action.
