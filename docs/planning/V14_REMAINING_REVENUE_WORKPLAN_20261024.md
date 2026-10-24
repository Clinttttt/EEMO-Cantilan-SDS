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

## Implementation checkpoint — grouped collection-session read presentation
- API/Client and Mobile read from existing tenant-scoped `MobileCollectionSessions.ResultJson` and resolve only persisted `Recorded` checkouts with >1 distinct child Collection IDs. Each child must appear in the existing canonical read feed with the matching collector and business date; incomplete/filter-truncated sessions render as individual records. Malformed/overlapping session correlations do not replace any collection.
- New optional additive `Sessions` member on `CollectionActivityFeedDto` and `CollectionsRegisterDto`, referencing session ID + exact child collection IDs, leaves all `Events`, `Rows`, summary, totals, SRCs, documents, corrections, remittance, and financial authorities unchanged. No schema change/migration.
- Web Collection Activity renders a combined row only for complete visible sessions and opens a modal listing each child SRC, classification line, individual receipt/OR/CT, gross amount and correction-aware net. Each child has its own Details action using the existing correction workflow. Filtered or searched subsets revert to individual rows; hero collection count and totals remain authoritative child counts.
- Mobile Records renders one combined card and a detail sheet with each actual child SRC, OR/CT evidence, operation, amount and status. Search that finds only one child remains a child card; legacy source rows remain separate. The displayed total uses the canonical record's correction-aware net, not checkout JSON. Entire 2,000-line-truncated register responses now explicitly disable combined cards because a limit can split the lines of a child Collection; this is a read-only safety guard, not a new limit or monetary change.
- Verified on synthetic PostgreSQL mixed OR/CT session: 1 targeted integration test passed after added collector/date/tenant exclusion assertions; 3 children retained, ₱1,130 preserved; partial/foreign projection rejected; exact SRC lookup stays one child. Full Mobile component suite: 177/177 passed, including a cross-Philippine-midnight display guard. Full Web component suite: 1453/1453 passed after a prior concurrent-run receipt-test timeout; grouped-row/receipt focused test set: 13/13 passed, including gross/net with a corrected CT child. Group lookups use one keyed visible-record map instead of re-scanning records per child; Web groups follow chronological activity order.
- The collection-session group view is read-only. No new collection, receipt, correction or payment operation was written by this implementation. Do not claim deployed, actual-device verified, cross-tenant production audited or fully regression-gated until the final combined release process. Android Debug build subsequently completed with 0 errors and 58 warnings (including existing AndroidX dependency version constraints and Android API compatibility warnings); physical device installation/smoke and signed release build are still not verified.

## Implementation checkpoint — assessed-fine partial payment in Mobile Collector
- Reused the authoritative fine assessment with unchanged original principal and governing evidence. The Mobile fine source choice is now `PreparedBalance`, prefilled with the correction-aware outstanding amount and bounded by that amount.
- The Mobile amount editor permits a positive partial OR amount with at most two decimal places; zero, negative, excess and fractional-cent amounts cannot be added. Existing NPM part-payment handling was restored unchanged after an initial regression detection.
- Server `QuoteFineAsync` checks exact linked liability, tenant/collector policy, original assessment version and `0 < ConfirmedAmount <= remaining` with currency precision. Quoted amount is the collector-confirmed amount, not automatically the full outstanding balance.
- Server `PostFineAsync` repeats the same checks inside the already serializable checkout transaction, writes one canonical OR collection line/allocation for precisely the confirmed amount, and increments the concurrency-token assessment version. Session intent fingerprint, source quote token and child posting identity remain unchanged for prior issued receipts and replay compatibility.
- Full payments remain available. Multiple later partial payments each get distinct SRC/OR boundaries; the remaining balance derives from existing correction-aware canonical allocations. No mutable paid-balance column, schema change, migration or authoritative assessment rewrite.
- Verified focused PostgreSQL: original assessed 125, first payment 45, remaining 80, stale previous assessment version blocked, zero/negative/excess/too-many-decimals rejected, replay idempotent, final 80 posted separately, final balance zero, two child Collection/SRCs and allocations 45 + 80. Test passed.
- Existing financial PostgreSQL regression set: 6/6 passed (concurrent fine drafts, direct fine concurrent retry, stale offline session quote, web standalone full-payment rule, partial invalid values, and new source-bound partial/final scenario).
- Complete Mobile component suite: 178/178 passed; explicitly tests initial prefill, partial editing, invalid bounds, preservation of original OR and liability identity, and existing NPM partial behavior.
- Operational limits: the separate Head Portal Composer fine-draft path still enforces full balance, by design of this scoped change. Android Debug build completed successfully (58 warnings, 0 errors); the generated signed Debug APK is local to the isolated worktree and is not a production release. Physical handset/offline device verification, production tenant verification and final cross-system end-to-end acceptance remain release gates.
- No production writes were performed; all monetary integration tests use isolated database fixtures.

## Implementation checkpoint — E. Optional OR metadata versus online receipt issuance
- Separated operational cash/field records whose OR-number metadata is blank from ONLINE payments already received but still awaiting actual office receipt issuance.
- Additive optional `FollowUpItemDto.ReceiptActionRequired` is true only for the online issuance branch. Existing historical DTO properties, row identities, money amounts, periods, links, and underlying source records remain unchanged. Legacy clients default false until matched API and Web are deployed.
- The Web live Follow-up Queue no longer hides the entire `missingor` family: it suppresses only optional rows. Actual online receipt issuance is visible and retains `Immediate / High` and `Encode OR`.
- The shared composer changes fully-paid legacy monthly blank-OR metadata from `Immediate / High / Missing OR` to `Operational / Normal / OR not recorded`. NPM daily, slaughterhouse, transport trips and market-day blank-OR items were already operational and remain historical source rows; online issuance stays urgent. No physical OR is invented or automatically assigned.
- Whole-time History retains all optional records and the historical source amounts and per-record periods, including past years. Its immediate-section subtitle now distinguishes online receipt issuance from optional receipt-number records; no Missing OR shortcut was reintroduced.
- Targeted report unit suite: 37/37 passed, including a historical old monthly blank-OR source and positive online issuance. Focused Web component set: 13/13 passed, including the live positive online/negative optional test and Whole-time section placement.
- First full Web component run: 1,453 passed, one unrelated Stall/Space holder test timed out (1,454 total). The specific timeout test then passed twice in isolation; a full Web re-run passed 1,454/1,454 (no failures). Follow-up unit suite 37/37 passed after adding the old monthly blank-OR history regression.
- Follow-up history cache TTL is 2 minutes. Existing cached snapshots may retain old urgency assignments until TTL expiry; the release must ship API + Client together and verify a fresh follow-up query.
- No changes to posting, receipt issuance, accountable forms, canonical collections, amounts, financial aggregates, database schema or production data.

## Implementation checkpoint — F. Offline readiness failure diagnostics
- Current source: `OfflineReadinessCoordinator` deduplicates source fetches and performs up to two background reads concurrently using existing read-through API caching. The old 16/18 menu status reflected 18 scheduled fetches and 2 non-successful results, but the original bool-only implementation discarded the failed source identities and errors. The screenshot cannot identify the actual two failing sources retrospectively; do not claim otherwise.
- Added local-only, bounded (32 entries) `FailedSteps` snapshot: each failure records only its deterministic preload task ID (up to 96 characters) and `OfflinePreloadFailureKind` (UnsuccessfulResponse, Network, Timeout, ConnectivityLost, Unexpected). No response body, HTTP error string, payor, amount, receipt or exception text enters the diagnostic state. No new network endpoint, persistent storage, UI row, analytics or telemetry.
- Concurrent work and duplicate-step suppression remain unchanged; all unsuccessful/exception/offline-skipped steps increment the relevant count. Starting a new pass clears the previous failure snapshot; reconnect forces a new pass, including any partial/interrupted prior pass. Logout/collector switch/dispose clears the diagnostic state, while stale generations cannot publish results. Offline collection acceptance, caching and durable sync queues remain independent.
- Collector menu still hides optional preload progress and failure counters; it does not falsely disable source operation tiles or conceal genuine queue/storage errors. No UI redesign.
- Deterministic unit fixture reproduces 18/18 success and 16/18 (named `ecf-sources` and `wcf-obligations`) with sanitized response data; reconnect recovery returns to 18/18. Also verifies transport/timeout reason classes and in-flight disposal. Those named failures are TEST FIXTURES, not observed diagnoses of the earlier production screenshot.
- Focused offline readiness + MobileSyncService suite: 40/40 passed. Core Release build succeeded with 0 errors. An initial full Mobile component suite passed 178/178; after adding the explicit menu no-label assertion, two full Mobile runs returned 177/178 (one Fiesta/Araw immediate stale-option assertion failed each time at VegetableFruitModeTests.cs:158). That unrelated search test passed in 5/5 isolated reruns (two parameterized cases per run), but must not be dismissed until broad-suite timing/isolation is resolved. Focused menu/editor tests passed 73/73 after the no-label assertion. Do not claim a green full-suite release gate from this checkpoint.
- Android packaging, real-phone offline-first usability, actual production-tenant failure identification, and end-to-end synchronization with the deployed API remain release verification gates. No production access or writes.

## Baseline findings (before local fixes; production account diagnosis pending)
- Baseline Fiesta/Araw browse excluded registered future-event lots; the local change now displays them as non-selectable. Exact production account exclusion reason remains unverified without authorized tenant-specific evidence.
- Baseline NPM duplicate-review override could bypass exact active association; local server fix and integration tests now reject it. Cross-process DB uniqueness/data audit remains a separate safeguard.
- Baseline session grouping was confined to the checkout success sheet; the local read projection and both Records/Collection Activity parent views now address this without altering the financial ledger.
- Baseline assessed-fine source choices and quote/post enforced the full outstanding amount. The local Mobile implementation now supports positive partial settlements with unchanged authoritative correction-aware allocation and assessment version safeguards; the separate Head Portal fine draft path intentionally retains full-payment rules.
- Missing OR has mandatory and optional subtypes and cannot be safely hidden wholesale.
