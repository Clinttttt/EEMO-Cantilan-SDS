# Fine workflow refinement — October 10, 2026

Status: **implemented and test-verified; authenticated acceptance and tenant migration pending.** No operational activation is implied.

Checkout: `C:\dev\stalltrack\eemo`; branch: `codex/vegetable-fruit-space-workspace`.
Starting HEAD: `5d156df4`. Implementation commit: `02bbe636`.
The documentation commit containing this handoff follows that implementation; `git log -2` identifies the final delivery HEAD.

## Authority and implementation baseline

Reviewed AGENTS, CONTEXT, the V2 master specification, current-release checkpoint, documentation map, operational/business rules, architecture/application patterns, decision registry, ADR-007, interface V3 and tenant/security guidance. Reviewed the fine design, foundation, settlement, migration-compatibility and in-context assessment handoffs. Their historical limitations are superseded only where the implementation and verification below establish a change.

The existing FineAssessment, penalty definitions/policies, canonical settlement, allocation projections and accepted Account Review remain authoritative. No parallel assessment catalog, registry, cash writer or receivable engine was introduced. Head means the existing `SuperAdmin` role for this workflow; Admin reads remain read-only. Collector fine payment keeps its existing assignment and settlement boundaries.

## Liable account diagnosis and correction

The previous live-queue bridge required an unexpired contract at the current business date. Read-only local evidence found eight active, unterminated NPM registrations starting June 7, 2023, with three-year scheduled expiry June 6, 2026 and no successor. These establish the lapse defect class; they are not proof of which account appeared in an earlier screenshot.

One inspected exact relationship was StallId `4f067350-a380-4484-a772-82d42c2c0080` and ContractId `560df728-b70b-464b-a117-9d492b2e6d16`. Resolution uses those tenant-owned identities, never occupant-name matching, unit-number matching or market-area inference.

The live bridge first requires one valid current registration. If no unexpired candidate exists, it accepts one still-active, unterminated, unreplaced lapsed registration. It rejects competing candidates, stale snapshots before the selected occupancy, replacements, wrong facility/tenant and missing current occupancy. An explicit historical contract must belong to the exact selected stall and tenant. The source account read supplies area, recorded term and Active/Lapsed/Closed presentation.

Ambiguous or unavailable live identity has a compact **Select verified source account** recovery path. It loads actual contracts for that exact stall, displays occupant/area/unit/term/status, and re-verifies the explicitly selected contract on the server. Nothing is selected automatically. This applies to supported NPM/NCC/TCC/BBQ/ICE sources.

**Limits:** recognition of a lapsed identity does not extend its term or billing window. Violation dates must still belong to the recorded occupancy interval. Holdover incidents beyond that interval require an accepted domain contract; no guessed extension was implemented. A lapsed/historical account also remains subject to the existing policy's closed-account authorization. Closed stalls require explicit historical selection rather than an automatic current occupant.

Queue `ContractId = null` retains its rental-payment meaning. Selecting a fine account changes only the fine context. Tests preserve the original rental target and loaded summary; no rent, rate, contract lifecycle or queue identity is rewritten.

## Optional affected-period contract

`CreateFineAssessmentRequest.RelatedPeriod` is optional. `FinePeriodRef` contains a closed `FineAffectedPeriodKind`, start/end dates and an optional real ObligationPeriod ID. `FinePeriodOption` adds the server-owned display label. The exact liable account and violation date remain independently required.

| Source | Verified reference | Evidence source |
| --- | --- | --- |
| Monthly facility rentals | `FacilityMonth`, month start/end, exact contract retained on assessment | Existing outstanding ledger and recorded payment months, filtered through `StallOccupancy.AnsweringForMonth` and original billable window |
| NPM daily | `NpmDay`, one actual chargeable date | Existing server settleable-day query plus recorded paid/non-absent days, exact contract and calendar rules |
| KM / Vegetable-Fruits monthly | `ObligationPeriod`, actual period ID and recorded PeriodStart | Tenant/account-scoped existing assessed periods |
| General contract incident | No related period | Exact account plus incident/violation evidence |

The NPM selector requests an explicit year; it does not fabricate a monthly rental obligation. This increment supports an individual verified day, not a new range settlement contract. Paid historical source periods may be selected where the underlying source evidence exists. Future, unassessed, excused or foreign-account periods are rejected. Source reads do not assess missing periods or calculate client-side rates.

Creation re-reads eligible source periods and validates the entire typed reference. It freezes period kind/dates/ID/label in the assessment and original audit evidence. Read models return the stored relationship; the label is not recomputed from a future occupant. Multiple distinct incidents can concern the same period. Existing incident uniqueness and operation idempotency remain separate from period association.

Historical requests without a period retain the original request-fingerprint format. A changed period with the same operation ID conflicts. A replay race discovered in testing is corrected by re-checking durable replay after duplicate detection, before reporting a business duplicate.

### Additive migration

`20261010120750_FineAssessmentRelatedPeriodEvidence20261010` adds only:

- Five nullable FineAssessments columns: RelatedPeriodKind, RelatedPeriodStart, RelatedPeriodEnd, RelatedObligationPeriodId, RelatedPeriodLabel (160 characters).
- Tenant/period lookup index and a tenant-composite FK to existing ObligationPeriods with restricted deletion.
- A typed-shape CHECK enforcing all-null historical evidence or valid complete evidence; monthly bounds are real month boundaries, day/source-period references use the same recorded date. A null kind cannot evade the CHECK through SQL UNKNOWN.

Application validation additionally enforces exact period-account ownership. Facility month/day references have no invented universal BillingPeriod ID; they retain the existing account plus source-typed dates. Historical rows remain null. No backfill, collection/SRC change or rental-table rewrite occurs. EF reports no pending model changes after the generated snapshot.

Only disposable PostgreSQL received this migration. It is **not applied** to the existing local Cantilan database or any hosted database.

## Workspace and drawer presentation

`/operations/fines` now shows one selected view: **Assessments | Collected | Penalty settings**, with Assessments default. Existing query/account/payment links remain supported; `?view=settings` opens configuration. The actual shared `filter-tab` presentation is used.

Assessments reuse FineAssessmentsRegister, including existing filters, details and payment drawer. Related period is a compact register column and appears in account-context charges. Dates remain in details. Collected preserves legacy and assessment-linked fine reads; each displayed collected amount now uses the same correction-aware effective amount as its existing total, rather than displaying a reversed original amount beside an effective total.

Penalty settings brings the existing category definition form and **Where fines apply** workflow into one administrative view. Their backend identities remain separate. Applicability lists the actual saved category, tenant-derived operation name, amount rule, governing reference and version-date basis. Configuration uses the existing policy writer and an explicit governing-evidence confirmation. Uncapped authority is shown only for an uncapped DirectApprovedAmount definition; exceptional authority is grouped in a disclosure. Successful saves reload authoritative configuration. Admin has no configuration action.

FineAssessmentDrawer remains the existing implementation over **AddNewDrawer → VendorDrawerShell**, with **AddNewSection**, shared field/tab/button styles and the existing account-review focus module. The small optional footer-cancel hook on AddNewDrawer preserves default behavior for other consumers and permits a clean Back/Confirm transition.

The drawer is account-first: exact source summary, fine category, violation date, optional real period, authorized amount, reason and incident reference. Fixed amounts remain locked; ceilings and centavo precision remain enforced by the existing server. Review displays frozen intended facts with Back / Confirm assessment. Back preserves fields. Cancel/header close never creates an assessment. Main source search returns verified accounts independently of policy configuration, so **account unavailable**, **select account**, **no approved policy**, and **API failure** are distinct states.

Applicability uses the same fixed-footer drawer with compact checkboxes, review/confirmation, focus binding and return navigation. Mobile forms stack into one column. No raw GUID or source enum is primary user-facing content; source choices come from actual supported tenant facilities and typed KM/VF adapters. No new FA policy choice was activated.

## Accepted Account Review preserved

The rental summary, year groups, daily details, account information and footer are unchanged. Fine source recovery and Other assessed charges are the only extended area. Verified Head/policy context opens the existing assessment drawer with the exact account preselected through a controlled single-dialog transition.

Save returns to the kept-mounted review and refreshes only the fine component. Tests prove the rental summary, expanded detail and original payment identity remain intact. Saved fines remain visible even when no policy permits new assessments. Related-period labels belong to the fine ledger and never increase that rental period's assessed amount.

## Financial and authorization verification

Assessment creation still produces only the existing governed liability and creation event. PostgreSQL tests prove no Collection, CollectionAllocation, PaymentRecord or remittance entry is generated by creating the new period-linked assessment. Source rent amounts and settlement versions remain unchanged.

The existing canonical fine writer was not rebuilt or changed. Regression coverage exercises standalone full-balance OR, compatible same-account office rent/fine OR, authorized/unauthorized Collector scope, closed-account and retired-definition payment, stale quotes, race/retry/rollback, OR/CT rejection, correction-aware allocation balances and reporting. Original Collection/SRC evidence remains immutable. Fine cash follows the actual collection business date, never the related rental period.

## Validation evidence

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Focused Web: FineAssessment, FinesWorkspace, FollowUpAccountReview, FollowUpQueueComposition | 64 | 0 | 0 |
| Full Web component suite | 935 | 0 | 0 |
| Separate full Unit suite | 2,529 | 0 | 0 |
| Relevant PostgreSQL fine assessment / settlement / migration compatibility suite | 58 | 0 | 0 |

Client Release and API Release final incremental builds both succeeded with 0 errors / 0 emitted warnings. Earlier compilation/test logs retain existing nullable, analyzer, obsolete API and AngleSharp NU1902 warnings; they were not suppressed or attributed to this feature.

The lapsed-account regression test failed before the correction. Intermediate runs exposed obsolete stacked-view test assumptions, a textarea fixture-value assertion, two load-sensitive existing Web waits, and the EF boundary violation from a proposed new Application partial file. The new read methods were kept in the existing assessment workflow; the architecture guard was not relaxed. The queue composition test now waits for its asynchronously rendered review. Final suites passed separately.

169 source/scoped-bundle CSS files were checked with balanced braces. `git diff --check` and staged diff checks passed. EF `has-pending-model-changes` reports none.

### Database and runtime status

Disposable tests use Testcontainers `postgres:16-alpine`, database `stalltrack_tests`, ephemeral localhost mapping and automatic cleanup. They apply the additive migrations and seed isolated test-only authority evidence. No existing tenant data is seeded or modified.

A fresh read-only check against the configured local `localhost:5432 / EEMOCantilanSDS.DB` found the three existing FineAssessment foundation/integrity/settlement migrations, two definitions, **one** assessment policy and zero assessments. This supersedes older zero-policy observations. The recorded NPM policy references `MEEDO`, permits uncapped direct amounts/closed accounts, and links an active definition effective October 9, 2026. Its underlying ordinance/authority has not been independently verified in this task. A short stored reference is not proof of legal sanctioning power.

The five new period-evidence columns are absent locally. Development startup migration is enabled; no API restart was performed against that target. The effective running-process environment and latest authenticated route behavior were not certified. Approval must name the exact tenant/database migration target before activation.

### Visual evidence

48 rendered fixture captures cover 16 existing/new states at 1440, 1024 and 390 pixels: workspace views, empty/policy states, selected details, period-linked confirmation, applicability details/review, existing assessment/payment states. Images were inspected and corrected: missing tab class, textarea styling and mobile grid cascade were caught visually. Final measurements show zero document/control overflow, zero multiple dialogs and zero footer clipping. Fixture textarea/select values were applied as DOM properties to represent Blazor-bound values accurately.

Evidence is under ignored `artifacts/fine-refinement/` (HTML, PNG, layout-facts.json and renderer); logs are ignored `artifacts/fine-refinement-*.log`. Browser inventory returned no available browser/app surface. **Authenticated visual, real keyboard/focus and save/payment acceptance remain pending.** Static fixtures do not prove runtime API hydration, legal evidence or tenant migration.

## Governing authority context and activation gates

Research is guidance only:

- [RA 7160](https://lawphil.net/statutes/repacts/ra1991/ra_7160_1991.html), particularly sections 168–170 and 447, distinguishes legislative revenue/penalty powers and authorized revenue collection. The supplied Official Gazette URL was inaccessible in this pass; the statute text was checked through Lawphil. No statutory ceiling is automatically configured as a Cantilan fine.
- [Ongsuco v. Malones](https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/1/50269) demonstrates the importance of valid ordinance procedure for market charges. Search-indexed official text was available despite intermittent direct-page failures; it does not establish Cantilan authority.
- [Davao e-stall workflow](https://davaocity.gov.ph/economy/e-stall-system-offers-efficient-lease-collection/) supports source/account profiling and collection review as workflow context.
- The supplied [Davao volume](https://davaocity.gov.ph/wp-content/uploads/2026/03/CGD_CC2026_VOLUMEIIE.pdf) is a Citizen's Charter containing market payment services. It is not independent proof of Cantilan market-penalty authority.
- [Naga Ordinance 2024-020](https://www2.naga.gov.ph/wp-content/uploads/2024/03/Ordno-2024-020-Satellite-and-Public-Market-Ordinance.pdf) distinguishes rental surcharge/interest provisions from general violation penalties. Its rates, sanctions and closure provisions were not imported.

Before live activation, MEEDO must provide the actual applicable ordinance/contract/schedule, offense eligibility, notice/evidence requirements and authority to impose each administrative/contractual liability. Head-only software permission does not itself authorize a court-imposed sanction. Ordinary rental debt, arrears management, a contractual assessment, a statutory late surcharge and a court fine remain distinct. No automatic delinquency penalty or retrospective calculation was enabled.

| Capability | Delivery status |
| --- | --- |
| Verified current/lapsed/historical source identity, optional real period association | Implemented and verified in component/PostgreSQL tests |
| Fines views, clean assessment/applicability drawers, preserved Review Account transition | Implemented; fixture inspected; authenticated acceptance pending |
| Existing full-balance canonical fine settlement/reporting | Regression verified; operational authority/runtime acceptance still required |
| Existing local new period migration | Not applied; explicit target approval required |
| Legal authority behind configured penalty/policy | Policy/authority verification blocked |
| Cancellation, reduction, waiver, refund writer, partial fine payment, Mobile settlement, expanded Collector mixed drafts | Not implemented by this task; separate controlled increments |
| Holdover violations beyond recorded occupancy term; NPM daily range association | No new domain semantics implemented |

## Exact implementation inventory and source control

Implementation commit `02bbe636` contains only these 29 files (directory headings plus filenames identify exact repository paths):

- `EEMOCantilanSDS.Api/Controllers/Revenue/`: FineAssessmentsController.cs.
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/`: IPenaltiesApiClient.cs.
- `EEMOCantilanSDS.Application/Common/Revenue/`: FineAssessmentWorkflow.cs.
- `EEMOCantilanSDS.Application/Dtos/Revenue/`: FineAssessmentDtos.cs.
- `EEMOCantilanSDS.Client/Components/Pages/Menus/`: Fines.razor, Fines.razor.css.
- `EEMOCantilanSDS.Client/Components/Shared/`: AccountFineAssessments.razor, AddNewDrawer.razor, FineAssessmentDrawer.razor, FineAssessmentDrawer.razor.css, FineAssessmentsRegister.razor, FinePolicySetup.razor, FinePolicySetup.razor.css, FollowUpAccountReview.razor.
- `EEMOCantilanSDS.ComponentTests/Pages/`: FineAssessmentComponentTests.cs, FineAssessmentComponentTests.Refinement.cs, FinesWorkspaceTests.cs, FollowUpAccountReviewTests.Fines.cs, FollowUpAccountReviewTests.cs, FollowUpQueueCompositionTests.cs.
- `EEMOCantilanSDS.Domain/Entities/Revenue/`: FineAssessment.cs.
- `EEMOCantilanSDS.HttpClients/ApiClients/`: PenaltiesApiClient.cs.
- `EEMOCantilanSDS.Infrastructure/Migrations/`: AppDbContextModelSnapshot.cs.
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/`: FineAssessmentConfiguration.cs.
- `EEMOCantilanSDS.Infrastructure/Persistence/Migrations/`: 20261010120750_FineAssessmentRelatedPeriodEvidence20261010.cs and its .Designer.cs.
- `EEMOCantilanSDS.IntegrationTests/`: FineAssessmentTests.cs, FineAssessmentTests.AccountResolution.cs, FineAssessmentTests.RelatedPeriods.cs.

This handoff is the only task-owned documentation addition. Canonical release/coordination documents were read without rewriting their concurrent edits. Login and CSS, AuditTrail and CSS, Menu, ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE_20261008 remain dirty and excluded. Their baseline totals remain 132 additions / 209 deletions. No unrelated artifact or secret was staged.

No new branch/worktree, push, integration, deployment, production/local tenant migration or publication occurred.

Next increment: confirm the actual governing/source authority and approve the exact migration target; then perform authenticated Head/Admin/assigned-Collector acceptance on safely migrated data. Liability adjustments/refunds remain a separate financial contract.
