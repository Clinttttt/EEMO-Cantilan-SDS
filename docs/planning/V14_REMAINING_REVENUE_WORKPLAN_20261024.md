# V14 remaining revenue and collector work — implementation workplan
Date: 2026-10-24 (Asia/Manila)
Integration base: Phase A `feat/v14-collector-consistency-20261024`, SHA `2f3843342d42308ba39113ef9e9b7378e31a61f4` (PR #57; merge/CI is independent).
New isolated integration branch: `feat/v14-revenue-integrity-20261024`
Deployment status: NOT DEPLOYED. No production writes authorized.

## Guiding invariants
- One posted Collection and SRC per authoritative financial child; a Mobile checkout session groups these for presentation only.
- Never turn CT into OR, merge receipt numbers, net independent collections together, change monetary reporting totals, or reinterpret remittance.
- Municipality + authorized collector scope applies to every lookup and result; never leak other tenants' vendors or sessions.
- Existing collections, fine assessments, and accountable documents remain immutable. Correct through supported event/correction flows.
- Offline queue remains durable/idempotent; no locally queued item claims to be successfully posted.
- Legacy collection events and pre-session collections remain visible individually.

## Implementation order and acceptance

### B1. Fiesta/Araw lot rental source mismatch (critical)
Verified source: `CollectionSessionSources.Native.cs/SearchSourcesAsync` queries `ObligationAccounts` with `Kind == FiestaArawLotRental`, `ActiveFrom <= PhilippineToday <= ActiveTo`, and a `CollectorOperationAssignments` operation-code requirement. The Head Portal vendor listing is NOT the same as authorized space obligation accounts.
1. Read-only inspect an authorized fixture/tenant's vendor/lot mapping, ObligationAccounts, event start/end dates, payor labels, office collector assignment, and actual mobile query results. Diagnose the missing list at the earliest exclusion stage.
2. Confirm legitimate pre-event payment date policy from authoritative source before altering date logic.
3. Align search/discovery/quote/post authority using the same eligible-source predicate. Do not solve by exposing arbitrary Payors or allowing direct unaffiliated payments.
4. Tests: valid authorized event lot; future event if policy authorizes advance payment; expired/closed lot; unassigned collector; other tenant; search by occupant/payor/lot; offline cached eligible source; stale search results.

### B2. Strict NPM/Fish-Meat duplicate prevention
Verified source: `OfficeCollectionWorkflow.NpmAssociations.cs` includes a `DuplicateReviewConfirmed` override that can be applied to an already active link; `NpmOccupancy.cs` also accepts reviewed creation. Current unique index only covers `(municipality,vendorRegistration,contract)` for active rows and does not globally enforce one active link per contract.
1. Define exact conflict identity as verified active contract/occupancy or linked registration; same-name PossibleMatches remain a warning/review, not an identity match.
2. Reject exact existing active association BEFORE the review override, for both register-from-NPM and link-to-NPM. Preserve idempotent operation replay, correction of old links, and legitimate distinct occupancies/vendors.
3. Review create-from-vendor vacancy/contract uniqueness and concurrent-create paths. Determine whether a new tenant-scoped filtered DB index is compatible with existing data; do not migrate production without separate authorization and a collision report.
4. Present clear Already Registered / open existing association UX. Do not remove ambiguous-identity verification.
5. Tests: same vendor+contract, different vendor+same contract, reviewed flag cannot override exact conflict, same-name different person, corrected link replacement, two simultaneous creates, cross-tenant isolation, API conflict messaging.

### C. Group multi-item session in Mobile Records + Web Collection Activity
Verified source: `MobileCollectionSessions` stores tenant-scoped `ClientCollectionSessionId` and result JSON with child CollectionIds/SRCs. `GetCollectionActivityQueryHandler` currently returns one authoritative collection event each, and must keep that for ledger totals.
1. Add a read-only, tenant-scoped session association projection using persisted successful result child CollectionIds. Resolve each child to the existing canonical reader and validate collector, date and scope. Keep each collection event in underlying feed.
2. Add group DTO: session ID, payer, business date, collector, per-child SRC/instrument/amount/status/corrections and computed display subtotal. Never aggregate two payment documents into one.
3. Mobile Records and Web Collection Activity render one visual parent for a real matching session; legacy and ungrouped rows remain separate. Date/collector/source filters and pagination must not silently double-count or hide out-of-range children.
4. Parent modal shows itemized operations and child SRCs, OR/CT and receipt evidence, corrections/reconciliation and individual amounts; existing export/financial analytics remain unchanged unless explicitly scoped.
5. Tests: four-item mixed OR/CT session, single-item session, same payer separate sessions, children with corrections, canceled/rejected checkout, missing children, old records, cross-tenant, mixed date/filter and pagination, no change to authoritative collected totals.

### D. Assessed fine/penalty partial settlement (highest financial risk)
Verified source: `CollectionSessionSources.Fines.cs` exposes full remaining as `FixedAmount` and compares entered amount to remaining at both quote/post. `FineAssessment.ClaimSettlement` increments version on every settlement and existing canonical allocations represent cash.
1. Extend source choice to allow positive partial amount defaulting to remaining, with maximum remaining. Keep OR policy and source identity.
2. Server quote and post validate requested amount > 0 and <= quote remaining using same assessment version and quote fingerprint. Post only requested amount as its own collection line and allocation; do not claim whole assessment paid.
3. Keep settlement in serializable transaction; increment assessment version to invalidate concurrent stale quotes, and recompute remaining from correction-aware posted allocations. Do not add a mutable paid balance that can diverge.
4. Review full correction/reversal, idempotent replay, stale version, retries, offline/online mixed session and reporting/receipt consequences.
5. Tests: full, partial, second partial, final settlement, zero/negative/overdue amount, rejected stale quote, two collectors race, failed sibling rollback, retry idempotency, correction, date boundary, other tenant, mixed checkout; use ephemeral PostgreSQL integration database.

### E. Missing OR follow-up policy
Verified source: `FollowUpComposer` emits multiple Missing OR categories, including online payments (immediate/high) and service/field records. `PastFollowUpQueue.razor` displays the urgent section.
1. Classify which source-policy cases truly require an accountable official receipt versus optional OR metadata. Do NOT globally hide unreceipted online payments by mistake.
2. Drop optional Missing OR from immediate action; preserve genuine mandatory receipt/online-payment exceptions and the raw historical trail. Update Whole-time History section text/labels only after policy classification is verified.
3. Tests: optional field OR not urgent, mandatory receipt still visible, online payment obligation still visible, legacy correct, historical immutable.

### F. Offline readiness failed-step diagnostics
`OfflineReadinessCoordinator` displays fetch counts (16/18 screenshot) but lacks source-specific diagnostic reason. Phase A hides the optional UI progress string.
- Record minimal failed task identifiers and reason classes inside bounded local diagnostic state, with no payer/financial data. Preserve retries/concurrency and keep actionable queue/storage failures visible.
- Tests: 18/18, 16/18 with exact failed task identifiers, retry recovers, offline-first queue works, disposed coordinator cancels correctly.

## Verification and release gates
- During each phase: targeted unit, authorization, component and PostgreSQL integration tests for new behavior; do not defer all validation until the end. Independently review diffs and migration effects.
- Once all phases are complete: run unit, component, integration, tenant isolation, offline queue/sync, multi-operation checkout, reporting totals, historical cases, new signed Android APK build, API and Client release build.
- End-to-end fixture workflow on a nonproduction environment: vendor account -> mobile source lookup -> partial fine -> mixed checkout -> sync -> one grouped Mobile Records and Head Collection Activity card -> underlying distinct child SRC/OR/CT -> corrected balance/remittance checks.
- Compare unchanged financial totals and child event counts with the baseline before promoting. No production data writes or migration until authorized.
- Merge/release only after protected CI and review; deploy matched API/Client images by exact commit; deliver separately signed Android APK. Smoke test on Android against live authorized accounts; check rollback.
- Do not call the milestone complete unless all scoped acceptance cases pass; document failures or unavailable real-device checks explicitly.

## Implementation checkpoint — NPM exact association conflict
- Server now returns `AssociationAlreadyExists` for any active link on the same exact contract, before considering `DuplicateReviewConfirmed`. This applies to registration-from-NPM and linking an independently registered vendor.
- Name-only PossibleMatches still allow explicit office review; multiple distinct stalls per vendor remain supported.
- New test `Npm_active_contract_link_is_not_overridable_even_after_duplicate_review` verifies cross-vendor duplicate rejection, register-from-NPM rejection, correction/replacement, zero cash side effects.
- Focused PostgreSQL integration test: 1 passed, 0 failed, 0 skipped. Related NPM integration regression subset: 5 passed, 0 failed, 0 skipped, including the new test.
- No database migration or production data change; do not claim fully protected against cross-process concurrency until the relevant unique-index/data audit and concurrent tests pass.

## Implementation checkpoint — Fiesta/Araw source visibility
- Verified structural mismatch: the Head Portal roster reads registered event lots, while Mobile source browse/search required ActiveFrom <= today. Event lot ActiveFrom is its EventDate; a future registered lot was invisible.
- Registered, unclosed future event lots now appear in assigned collectors' source browsing/search with exact SpaceAccount identity, payor name and event date. DTO `CanSelect=false` and `AvailableOn` communicate that settlement remains unavailable until the event date; the Mobile selector disables such rows.
- Linked Business Payor names are searchable and displayed even when no event quote exists yet; payor lookup remains tenant-scoped.
- No change to event account date logic, quote, posting, allocation, receipt, rates, authorization, or financial ledger. The server still refuses creating event liability before its date.
- Existing current/due accounts remain selectable. Closed past accounts, unassigned collectors and other tenants remain excluded.
- A source-search generation guard and immediate row invalidation prevent older asynchronous responses / debounce rows remaining selectable after search text changes.
- Focused PostgreSQL future-event scenario passed. Expanded browser tests: 8/8 passed, including Fiesta and Araw, closed event, date-of-event eligibility, cross-tenant and unassigned collector conditions.
- Full Mobile component suite after additional stale-row guard: 175/175 passed. Rapid-search Fiesta/Araw regression repeated 5/5 runs without failure. Android Debug build passed (58 warnings, 0 errors), and a signed Debug APK was generated locally. No APK installation, on-device smoke test, or production deployment occurred.
- Production account-level diagnosis is not yet verified: actual vendor dates, tenant configurations, account status, and collector assignment require permitted read-only production evidence. Do not claim all missing real-world vendors fixed solely from test fixtures.

## Baseline findings (before local fixes; production account diagnosis pending)
- Baseline Fiesta/Araw browse excluded registered future-event lots; the local change now displays them as non-selectable. Exact production account exclusion reason remains unverified without authorized tenant-specific evidence.
- Baseline NPM duplicate-review override could bypass exact active association; local server fix and integration tests now reject it. Cross-process DB uniqueness/data audit remains a separate safeguard.
- Session grouping requires a new read model; checkout success sheet alone does not satisfy grouped Records/Collection Activity.
- Fine partial input alone cannot work until quote/post amount invariants are updated.
- Missing OR has mandatory and optional subtypes and cannot be safely hidden wholesale.
