# Cross-workspace financial UX and source-authority refinement

Date: October 11, 2026. Checkout: `C:\dev\stalltrack\eemo`.
Branch: `codex/vegetable-fruit-space-workspace`. Starting HEAD: `e1a5a5cc`.

## Delivered scope and authority

This pass changes presentation/orchestration over existing financial services. It adds no financial writer, source discriminator, migration, penalty policy, automatic fine or historical reclassification.

| Workstream | Status |
| --- | --- |
| A: compact account fine list, details, quote-eligible actions and controlled payment return | Implemented and verified; authenticated acceptance pending |
| B: existing fine selection and new assessment then payment from Fines | Implemented and verified; authenticated acceptance pending |
| B: walk-in fine without a verified source account | Requires office/financial authority approval; not implemented |
| C: delinquency/Arrears source audit and regression verification | Implemented and verified as an audit; no new qualification rule or writer |
| C: automatic period qualification and dedicated Arrears recovery | Requires office/financial authority approval; not implemented |
| D: NPM receipt evidence outside primary row; optional Mobile monthly receipt disclosure | Implemented and verified; authenticated/device acceptance pending |
| D: removing genuine legacy receipt requirements | Not implemented; source cutover/authority must be verified first |
| E: Income From Terminal card from canonical Terminal activity | Implemented and verified; authenticated acceptance pending |

References reviewed: AGENTS, CONTEXT, V2 master/current release/documentation map; operational rulebook/business rules, IA-019/020/062/067/068 and ADR-007; architecture/application patterns, interface architecture/design/V3/migration guidance; latest fine refinement, settlement and in-context account handoffs; NPM arrears readiness handoff and testing strategy. Historical handoffs are implementation evidence, not new financial authority. Canonical/protected documents were not rewritten.

## A. Other assessed charges

The root cause was a six-column desktop table inside a narrow drawer. `AccountFineAssessments` now renders bounded list rows: penalty/status, assessment date/related period, aligned Assessed/Collected/Remaining, then separate details/payment actions. Full reason, violation date, incident and governing reference are disclosed inline, with actual allocation/SRC history on demand. History errors remain distinct from no history.

An existing fine is visible even when no current creation policy exists. New assessment eligibility continues to require a verified account, applicable policy and active definition. Payment actions require a successful existing server quote with `CanSettle` and matching assessment/account IDs. Quote failure does not erase balances or show a fabricated zero. Settled rows do not request a new payment quote. Generation checks discard obsolete account-load results.

`FollowUpAccountReview` keeps its rental drawer mounted while the existing `FinePaymentDrawer` is visible. Successful payment refreshes only fine data; closing restores Review Account. Rental API targeting, summary, period disclosures and footer remain unchanged. The existing focus module owns one visible dialog; standalone fine payment now also uses that module. Fine history displays each allocation's actual effective amount, not a whole mixed Collection total.

## B. Direct fine collection entry

The existing Assessments view has Head-only **Record fine payment**. It selects the Outstanding filter and asks staff to select the exact saved assessment in the existing searchable register. Each selected ID enters the existing quote/review/post drawer; no new assessment is created for an existing fine.

**Assess then collect** opens the existing account-first assessment drawer. Its new typed `OnCreated` callback passes the actual saved DTO/ID directly into the existing payment drawer. Assessment confirmation and receipt-of-money confirmation remain separate. Cancelling/failing the payment leaves the saved liability intact and offers the normal existing register payment path. No request is retried as a new assessment. Existing `OnSaved` consumers remain compatible.

Full remaining-balance OR, compatible office rental/fine drafts, assigned-Collector standalone authorization, quote/revision/idempotency checks, transactional allocations, correction-aware balances, dedicated Fines classification and remittance remain the existing backend contracts. The accepted modal/rental ledger and source ownership were not redesigned.

Walk-in investigation: the new assessment requires an actual typed source account. The definition-based Composer penalty path remains a different immediate-charge operation and requires a compatible allocated source item. It does not establish a walk-in assessment identity. No dummy contract/payer/source was introduced. Office approval is needed for eligible walk-in offenses, permissible payer evidence and source/receipt authority before a dedicated direct-payer capability is activated.

## C. Classification audit

| Situation | Verified current source/behavior | Unresolved boundary |
| --- | --- | --- |
| Current incomplete month | Shared source ledger/as-of clamp; not one fully elapsed delinquent month | None; retain current-period follow-up separately |
| Active account with one elapsed month carrying a balance | `DomainRules.DelinquentThresholdMonths = 1`; report/compliance reader | None; delinquency already applies |
| Active account with three or more elapsed months | `FollowUpComposer.ImmediateSeverityAgeMonths = 3`; Critical severity | Does not redefine delinquency or Arrears |
| Lapsed/ended occupancy | Original contract/account attention and historical balances; exact source identity | Which specific old/lapsed obligations qualify for dedicated Arrears recovery |
| Prior-calendar-year or 12-month-old active debt | Aging information; not automatically reclassified | MEEDO must distinguish old-year debt from active old installments |
| NPM daily | Actual eligible days/shared month basis, excuses/absence excluded; historical occupancy/rate retained | No invented monthly installment or automatic fine |
| Current and old debt on one account | Existing period ledger; no second debt | A governed period-level recovery classification is required before splitting cash classifications |
| Saved fine | Independent fine projection/allocations | Never added to rental delinquency/Arrears totals |

`ArrearsReads` uses financial-report delinquency totals and queue delinquent rows; it reads actual ARREARS cash from the official statement. `GetFinancialReportQueryHandler` deliberately returns null qualifying-Arrears totals, not zero. `StallRepository.Arrears` has a 12-month **phone-screen lookback** explicitly documented as a display bound, not expiry/qualification. FollowUpComposer preserves explicit historical contract IDs, distinguishes contract attention and prevents a debt from being counted twice. Duplicate stall numbers across areas do not establish account identity.

Important recovery gap: `NpmDailyCanonicalPoster` resolves Permanent Stall Rent; the monthly adapter resolves `ForMonthlyRental(facility)`. Neither classifies a period as dedicated Arrears simply because it is old. The official ARREARS report/classification exists, but that is not proof that qualifying source-period recovery is implemented. Required office decisions: eligibility of prior-year active debt versus lapsed terms, holdover liability, paid-period correction/replacement classification, and exact recovery attribution/effective date. No posted cash was reclassified in this pass.

## D. Receipt authority matrix

| Web/Mobile path | Current authority and receipt entry | Change/result |
| --- | --- | --- |
| Web NPM operations table | Daily cash exists before later OR attachment | Primary Add OR icon removed; **Record receipt evidence** remains in collection details for a paid day without evidence |
| Web NPM evidence maintenance | Existing `SaveDailyCollectionOrNumberCommand` targets exact paid day; no second collection | Writer and audited source maintenance retained |
| Mobile Market/NPM Daily | Canonical branch receives server SRC; legacy paid-day branch retains actual OR rules | Canonical already omits manual OR; unchanged |
| Mobile NPM Whole/eligible-day settlement | Server quote, canonical SRC/idempotency; no typed receipt required | Unchanged; existing readiness/cutover boundaries retained |
| Mobile monthly rental | Canonical branch has no OR field; existing legacy normal field is optional | Optional field moved into native **Receipt details (optional entry)** disclosure; request/validation unchanged |
| Monthly online-payment OR issuance | Money already received; physical receipt issuance is distinct | Existing required issuance/evidence flow retained |
| Slaughterhouse | `!Canonical` controls OR field and legacy required validation | Canonical unchanged; legacy requirement retained |
| Tabo-an | Canonical capture omits OR; legacy field optional; historical editing separate | Unchanged |
| New Collection/CollectionSessionEditor | Source discovery/capability, server quote and SRC, durable queued intent | No manual OR field added; offline queue does not fabricate SRC |
| OperationCollection: Terminal, Fish/Meat, Weight & Measure, supported spaces/utilities | Existing typed canonical charge/assignment/instrument controls | No manual OR needed; existing source eligibility retained |
| Legacy Terminal/trips | Separate historical trip route/writer still requires actual OR and trip facts | Not weakened or reclassified as new Terminal |
| Market utilities/legacy ECF vs canonical WCF | Existing authority-specific branch (ECF legacy evidence; canonical eligible WCF CT quote) | No global nullable rewrite or OR/CT mixing |
| Historical imports/correction flows | Existing documentary/authority contracts | Unchanged |

An SRC still does not prove that a legally required physical receipt was issued. No serial was generated, guessed, copied or consumed by these UI changes. OR uniqueness/correction policy was not weakened; the relocated legacy evidence handler's broader registry validation remains an existing contract, not a newly verified universal reconciliation engine.

## E. Income From Terminal

The old card read `TrmTrips`, so renaming its Trips/Drivers figures would misrepresent data. The narrowly scoped `TerminalOverviewCard` calls the **same** `IOfficeSourcesApiClient.ActivityAsync(..., TERMINAL)` used by the Terminal workspace. That read uses actual canonical office Collections and correction effects; it excludes Transportation/Parking and historical trips.

The card shows TRM / Income From Terminal / Terminal collections, selected-month net money, today's effective collection count, and `/operations/terminal`. Reversed zero-effect collections are excluded from today's effective count. No collection percentage or fake denominator exists. Zero and API failure differ. Loading uses neutral Skeleton primitives without old labels. The existing main Dashboard hero/projection and legacy historical routes remain unchanged; this is a Terminal-specific card read, not a broad dashboard cash aggregation rewrite.

Only the five-line Terminal rendering hunk in `Menu.razor` was staged. Its concurrent Operations overview-before-Revenue Source Performance change remains exactly unstaged.

## Validation and runtime safety

- Focused fine/account/Terminal Web run: 61 passed, 0 failed, 0 skipped before the final payment-return regression; final full Web includes that regression and all 944 tests pass, 0 failed/skipped.
- Full Unit, separately from Web: 2,530 passed, 0 failed, 0 skipped. Mobile-related/Core subset: 195 passed, 0 failed/skipped (part of the same Unit project).
- Full Mobile components: 80 passed, 0 failed, 0 skipped.
- PostgreSQL filter `FineAssessment|ArrearsCounting|RegisterAndFollowUpAgreement|CollectionSessionTests`: 155 passed, 0 failed, 2 skipped. Snapshot agreement tests require a safe isolated `STALLTRACK_SNAPSHOT_DB`; it was not configured. Disposable tests use Testcontainers `postgres:16-alpine`, database `stalltrack_tests`, ephemeral host port, automatic cleanup; all additive migrations are applied there, including related-period evidence. No existing tenant/hosted database was migrated.
- Client/API Release: both passed, 0 emitted warnings, 0 errors in standalone incremental builds. Earlier recompilation emitted existing nullable, obsolete API, analyzer and AngleSharp NU1902 warnings; they were not suppressed or classified as new defects.
- Android Release (`net10.0-android`, `--no-restore`): passed, 51 warnings, 0 errors, 8m58s. Existing AndroidX NU1608 dependency constraints and existing compiler/analyzer warnings remain; no version/package change was made. Build is verification only, not publication/device acceptance.
- Source/scoped CSS plus generated Client bundle: 261 files checked, 0 unbalanced. `git diff --check` passed.
- File-only rendered fixtures use the actual generated scoped bundle. Focused fine/card checks at 1440/1024/390: 12 states, 0 document/fine-row overflow or clipped footer. Screenshots inspected include the narrow compact list, full Review Account scrolled to fine actions, and Terminal card. Fixtures also cover existing assessment/review/payment/workspace states. Authenticated save/post/keyboard/browser and physical-device acceptance remain pending; configured computer-use inventory returned no browser surfaces.

Development `Database:ApplyMigrationsAtStartup` remains true. No API was launched/restarted against the existing tenant. The prior handoff's local-tenant migration/policy evidence remains historical read-only evidence; disposable verification does not prove that tenant is migrated or that its governing reference is legally sufficient. No new policy/demo fines were seeded.

## Commits and exact inventory

- `b42a04ae`: Terminal-only `Menu.razor` hunk; Shared/TerminalOverviewCard.razor and .razor.css; ComponentTests/Pages/TerminalOverviewCardTests.cs.
- `acc1a19a`: Shared/AccountFineAssessments.razor and .razor.css, FineAssessmentDrawer.razor, FineAssessmentsRegister.razor, FinePaymentDrawer.razor, FollowUpAccountReview.razor; ComponentTests/Pages/FineAssessmentComponentTests.FinancialUx.cs and FollowUpAccountReviewTests.Fines.cs.
- `79d1f5a8`: Client/Pages/Menus/Facilities/NPM.razor, Pages/Shared/Actions/CollectionReceiptModal.razor, Pages/Shared/FacilityStallsTable.razor; Mobile/Pages/Menus/MonthlyCollection.razor; Testing/Architecture/MobileLegacyWorkflowPresentationTests.cs (all under their Components/project prefixes).
- This focused handoff is committed separately. Final HEAD is the documentation commit reported in delivery.

Eight original concurrent files remain unstaged: Login and CSS, AuditTrail and CSS, Menu's section order, ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE, VEGETABLE_FRUIT_WORKSPACE_20261008. Their original line counts remain 132 additions / 209 deletions. No artifacts/secrets were staged. No new worktree/branch, push, merge, deployment, Mobile publication or local/hosted migration occurred.

## Next controlled increment

Verify real Cantilan penalty/source authority and approve the intended migration/runtime target before authenticated Head/Admin/assigned-Collector acceptance. Separately decide period-level Arrears qualification/recovery and walk-in fine identity/evidence. Cancellation/reduction/waiver/refunds, partial fine payment, Mobile fine settlement and expanded Collector mixed-draft permission remain **not implemented**. This pass does not imply release or production readiness.
