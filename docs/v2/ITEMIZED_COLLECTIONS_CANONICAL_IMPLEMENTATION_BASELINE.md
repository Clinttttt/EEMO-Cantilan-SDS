# STALLTRACK V2 — ITEMIZED COLLECTIONS CANONICAL IMPLEMENTATION BASELINE

**Status: APPROVED BY CLINT — documentation checkpoint first; MASTER then proceeds sequentially from Phase 1.**

**Date:** 2026-09-26

**Architecture authority:** MASTER / V2 Planner

**Canonical destination:** `C:\dev\stalltrack-v2-clean`, `interface-v2/clean-adoption`

**Inspected code baseline:** `db2abac32fb48035959424a1e21abad89a8b67af`, plus uncommitted canonical decision documentation

**Evidence method:** Static repository/worktree review. No build, test, production verification, merge or deployment was performed for this architecture review.

## 1. Authority, scope and approval boundary

This document consolidates the locked MASTER rulings and accepted Q41–Q46 decisions into the approved implementation baseline. MASTER / V2 Planner is the sole primary implementation agent and proceeds sequentially against `interface-v2/clean-adoption`, one bounded phase at a time. Sessions N/O/P/Q are permanently paused as implementation agents. Their worktrees and guidance remain available as candidate evidence, concern boundaries and file-history provenance; they are not assignments or parallel implementation lanes. MASTER may inspect them read-only and selectively reuse, adapt, reject, defer or reimplement pieces after review. Do not merge, reset, discard or modify those candidate worktrees during this baseline checkpoint.

- **CONFIRMED OFFICE RULE:** Itemized documents retain independently classified revenue; one document has one payer context; classification determines OR/CT. Cantilan ECF = OR, WCF = CT, Weight & Measure = OR. Physical documents and specialized assessment meanings remain distinct.
- **CONFIRMED STALLTRACK DECISION:** The locked draft/posting/allocation/authority/offline/correction rules and [Q41 business identity](../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md), [Q42/Q43 settlement/cutover](../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md), [Q44 operation identity](../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md), [Q45 Web drafts](../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md), [Q46 reporting bases](../decisions/ADR_005_CORRECTION_REPORTING_BASES.md). Engineering decisions do not create additional Office accounting policy.
- **CURRENT IMPLEMENTATION:** Statements about existing code below are evidence, not target approval or verified production observations.
- **TARGET DESIGN:** The domain/API/persistence/ownership/phase choices below are the implementation proposal submitted for baseline approval. Exact class/route names may follow repository conventions without changing these semantics.
- **OPEN QUESTION / ROLLOUT GATE:** Official cross-period RCD treatment and operation-specific unconfirmed policy remain explicitly gated in section 18. They do not justify inventing financial rules.

Goal: implement one itemized financial posting path around specialized operations, preserving Cantilan balances, historical truth, offline events and accountable-document identity. No unrestricted financial lines, customer-credit subsystem, replacement assessment engine, fabricated legacy itemization, shared Web draft editing, or production deployment is included.

## 2. Current state versus target state

| Area | Current evidence | Target / transition |
|---|---|---|
| Financial foundation | Dormant immutable Collection/CollectionLine, effective classification policies, tenant constraints and audit/export integration exist. No universal operational writer/report cutover. | Extend the existing foundation; do not create a competing ledger. |
| Identity | PayorUser is login identity; PayorStallLink authorizes stall access. Collection currently carries PayorUserId/name. | Small business Payor, explicit source relationships and frozen posted payer evidence. |
| Settlement | PaymentRecord/UtilityBill store cumulative mutable payment state; NPM uses specialized daily/month settlement rules. | Per-source Legacy/Canonical marker, evidenced opening position, canonical allocations and atomic compatibility projections. |
| Drafts | CollectionLineDraft is a trusted posting input. O's candidate draft state is circuit-local and uses a review boolean. | Separate persisted, versioned, user-owned Web drafts with at most one posted Collection. |
| Documents | Legacy number fields, cross-source OR checks and OrSeriesConfig suggestions exist; authoritative document/custody lifecycle does not. | AccountableDocument and physical unit/custody bridge participate in posting and corrections. |
| Operations UI | Several accepted ECF/WCF/landing and other V2 surfaces contain sample/local state. Existing specialized backend paths still carry financial authority. | MASTER connects accepted surfaces to canonical contracts without treating sample data as a business source. |
| Mobile | Durable local queue and operation IDs exist, but payloads replay cumulative legacy commands; rejected entries can be discarded. | Canonical protocol for converted sources, durable semantic idempotency, permanent issued-document evidence and reconciliation exceptions. |
| Reporting | Current feed/report queries assemble legacy source rows, sometimes grouping number/name fields. Q's candidate reads canonical events but lacks complete document/allocation/correction detail. | One parent Collection event, classified line totals, explicit report basis and supported legacy evidence shown separately. |

Primary evidence: [Collection](../../EEMOCantilanSDS.Domain/Entities/Revenue/Collection.cs), [CollectionLine](../../EEMOCantilanSDS.Domain/Entities/Revenue/CollectionLine.cs), [UtilityBill](../../EEMOCantilanSDS.Domain/Entities/Payments/UtilityBill.cs), [PaymentRecord](../../EEMOCantilanSDS.Domain/Entities/Payments/PaymentRecord.cs), [NPM settlement contract](../../EEMOCantilanSDS.Application/Common/Payments/INpmMonthSettlementService.cs), [OR registry](../../EEMOCantilanSDS.Infrastructure/Repositories/OrNumberRegistry.cs), [Mobile sync](../../EEMOCantilanSDS.Application/Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandHandler.cs), [legacy report queries](../../EEMOCantilanSDS.Infrastructure/Repositories/CollectorReportQueries.cs).

Conflicts resolved by the accepted target: prior lane instructions allowing immediate implementation are superseded by the MASTER pause; login identity is insufficient business identity; independently mutable candidate receivable balances conflict with Q42; source-row/global-only operation keys conflict with Q44; client-only draft review conflicts with Q45. Older documentation calling custody future describes implementation status and does not revoke the newly approved Head/Admin authority. None of these decisions claims the target is already deployed.

## 3. Target domain and aggregate boundaries

| Concept | Ownership and invariants |
|---|---|
| Payor | Tenant-owned person/organization identity, independent of authentication. Minimal explicit relationships to existing source entities; no balance or assessment ownership. |
| WebCollectionDraft | Separate non-financial aggregate: tenant, owner, DraftId, revision, payer context, policy-resolved instrument, business date, proposed lines/allocations, review binding, disposition and resulting CollectionId. |
| Collection | Immutable posted received-money event, with attributed actor/channel, business date, server recorded timestamp, frozen payer evidence, total and durable operation relationship. A business Payor reference is optional where allowed. |
| CollectionLine | One independently reportable classified amount. References exact applicable classification/policy and charge basis, with historical calculation/source snapshots. OR/CT number is not repeated per line. |
| CollectionAllocation | Explicit amount linking one line to an authoritative source obligation anchor. Multiple allocations may target different periods/obligations of one compatible line. Reversal evidence remains attributable. |
| Source obligation anchor | Stable tenant/source identity used for allocation, not an independent assessment/balance engine. Existing source and period/occupancy rules remain authoritative. Snapshot assessed facts without creating a separately editable outstanding amount. |
| Settlement transition/opening evidence | Explicit affected scope and Legacy/Pending Cutover/Canonical transition, reconciled frozen opening facts, version/time/evidence, and original assessment/settlement/outstanding distinctions. Pending Cutover has not yet activated Canonical authority. |
| PostingOperation | Tenant + ClientOperationId, immutable normalized semantic intent and durable outcome; supports authorized retries, conflicts and correction disposition. |
| AccountableDocument | Single physical document identity/number, instrument/series context, owning Collection, immutable issue facts and linked correction history. One Collection can have multiple historical documents but at most one current document. |
| Form batch/unit/assignment | Minimal authoritative received range, individual control/serial identity, custody assignment and permanently consumed state history needed by supported posting. Not a second financial ledger. |
| Correction event | Immutable original-event/document relationship, explicit financial effect, reason/actor and distinct effective/recorded dates. Document-only replacement adds no revenue. |
| Reconciliation exception | Preserved original submission, operation/document identity and evidence requiring controlled review; not automatically a successful Collection or a released physical form. |

Keep Collection lines and allocations within the posting consistency boundary, but do not pull all source assessments, Payor records or form inventory into one giant domain aggregate. The application posting transaction coordinates those boundaries in the existing database. A user-facing visit may contain separate compatible drafts; it is not an all-or-nothing financial transaction.

Conservative CT implementation: one CT-compatible revenue line per physical CT-backed Collection unless later Office confirmation permits more. Calculation details may have several components without producing extra lines. OR supports multiple compatible lines. Fish/Meat and Weight & Measure remain separately classified where officially distinct; slaughter package components remain detail unless a reporting ruling says otherwise.

## 4. Source identity, allocations and snapshots

- Existing persisted obligations use tenant + source kind + source ID + applicable source part as their stable reference. Utility electricity and water parts remain distinct; a bill-row-wide switch must not accidentally convert an unready component.
- Period/occupancy-derived obligations use a deterministic identity from authoritative existing relationships, with an adapter-defined anchor mapping where needed. For NPM, preserve the actual term/occupancy, month basis and shared ledger rules; do not manufacture monthly PaymentRecord rows or infer owner from the current stall holder.
- A rent line of PHP 1,500 may allocate PHP 900 to July and PHP 600 to August. Its origin metadata must not force every allocation to share one source-row ID. A repeated source reference across legitimate installments is not itself a duplicate.
- Obligation-backed lines allocate their full received amount explicitly, without exceeding current outstanding. Supported immediate activity charges have approved source/charge evidence; they need not fabricate receivables solely for uniformity.
- Suggested oldest-first allocation is visible and confirmed. Client text, names and suggested ordering do not establish settlement authority.
- Freeze payer evidence, source identities, occupancy/facility/stall/period, classification/policy, applicable charge/rate/configuration version, quantity, rate, readings and calculation detail where relevant. Snapshot values come from trusted adapters and approved inputs, not arbitrary client-authored JSON.
- ChargeDefinition/basis approval is required even when a classification is active. Manual Approved is a permitted basis on that approved definition for Head/Admin, not arbitrary description/amount entry or a rate override.

## 5. Command, query and API boundaries

Use the existing CQRS/application/repository/controller/API-client conventions. N publishes the shared contract before consumers bind to it; thin controllers delegate to application handlers. Suggested operation names below specify responsibilities, not mandatory route spelling.

| Boundary | Required behavior |
|---|---|
| Business Payor lookup/linking | Explicit tenant/source associations; authorized office maintenance; optional auth link; no inferred merging. Link administration must not rewrite posted history. |
| EligibleSources / QuoteCandidate | Query approved source relationships or operation context; return authoritative identity, class/charge/policy, amount constraints, allocation targets, source versions and evidence quality. |
| Create/Get/Mutate/DiscardDraft | Tenant/user ownership, stable identity and expected revision. No financial writes. Recover across authenticated sessions. |
| ReviewDraft | Authoritative normalized financial content, proposed allocations and document intent, bound to revision/content and acknowledged by the user. |
| PostDraft | DraftId + expected revision/review binding + ClientOperationId + permitted document/confirmation inputs; atomic canonical posting, never a caller-computed replacement balance. |
| PostFocusedCollection / Sync | Channel-specific validated intent entering the same posting coordinator. Mobile does not need a Web draft or the large Composer. Online remains a separate channel with its existing legitimate Awaiting OR handling. |
| GetPostingOutcome | Authorized tenant/caller outcome, recorded Collection/document identity and current disposition; not a bearer-key lookup. |
| CorrectCollection / CorrectDocument | Head/Admin, explicit reason/type/target/effective date, own operation identity, linked immutable result; explicit distinction between financial effects and document-only change. |
| ManageForms / CutoverScope | Head/Admin controls approved form/custody and cutover workflow; state/evidence transitions are auditable, never client flags that bypass checks. |
| Activity / PayorHistory / ClassifiedTotals / DetailStatement | Read-only projections from canonical posted events/corrections plus separately labelled supported legacy evidence. |

Common responses distinguish stale draft, review required, source balance changed, policy/instrument conflict, document unavailable/custody failure, idempotency conflict, already posted, cutover pending, reconciliation required, forbidden and retryable infrastructure failure. Never return success for changed unposted intent. No error path deletes physically issued evidence.

## 6. Atomic posting and concurrency

Within one database transaction for a supported scope:

1. Enforce authenticated tenant/actor/source authority, including outcome replay access. Resolve the durable operation identity and normalized intent; equivalent completed retries return the authorized original outcome.
2. For Web, validate draft ownership, unposted disposition, expected revision and exact review binding. A different operation key cannot produce another Collection from a posted draft.
3. Coordinate the source's cutover marker/version and lock or concurrency-check every affected settlement anchor. Re-read current authoritative source facts through its adapter.
4. Resolve active approved charge/classification policy at the applicable business date; check payer relationships, amounts, instrument compatibility, explicit allocation sums/outstanding and source snapshots.
5. Validate document identity, tenant uniqueness, physical-unit state and custody/assignment. A new operation key does not make a consumed unit reusable.
6. Persist Collection, lines, allocations, immutable snapshots, document issue/consumption, compatibility projections, audit evidence, successful operation outcome and Web draft Posted/CollectionId atomically.
7. Return the committed outcome. A timeout/unknown transport result is resolved using the same operation identity; do not issue another receipt to guess whether commit succeeded.

For existing PostgreSQL persistence, use serializable coordination and/or explicit shared source-row/anchor locking with database uniqueness constraints. The implementation must demonstrate the common protocol across every writer; isolation on new canonical tables alone cannot protect against legacy writers. Lock multiple anchors in a consistent order. A concurrent stale settlement fails cleanly with refresh/review required. Only an identical operation retry may resolve to the winner's outcome without a new review. Do not silently retry changed financial intent.

A successful operation cannot exist without its financial effects, and financial effects cannot commit without their successful operation identity. A durable terminal rejection preserves its bound intent; a changed corrected intent needs a new operation. Infrastructure failures remain retryable according to IA-041. Financial corrections and compatibility projections share the same atomicity requirement.

## 7. Persistence changes and data protection

MASTER owns additive migrations and the EF model snapshot. Proposed new storage supports business Payor and minimal domain-specific links; Web drafts/lines/proposed allocations/review metadata; source allocation anchors and settlement cutover evidence; canonical allocations/corrections; durable operations; accountable documents/form units/assignments; and reconciliation evidence.

- Preserve existing source tables and legacy OR fields. New nullable relations and explicit coverage markers distinguish pre-cutover rows from complete new events.
- Enforce tenant-consistent foreign keys and uniqueness for operation identity, draft-to-posted-Collection linkage and physical document/unit use. Keep used/voided numbers protected even if legacy soft deletion exists.
- Reconcile the current global Collection.ClientOperationId index with tenant-scoped operation registry authority. Do not drop protections without a compatible replacement and evidence inventory.
- Preserve the exact printed document identity. Applicable series/instrument uniqueness follows approved document policy; series labels must not become a workaround for reusing a physical number. Ambiguous legacy collisions require reconciliation rather than inferred composition.
- Compatibility paid/status fields may change only through canonical projection writes after source conversion. Never persist another independently editable balance beside them.
- Add every new tenant-owned entity to established query filters, audit, backup/export/restore dependency ordering and retention protection. Preserve financial and issued-document evidence permanently according to existing audit rules.
- Migration startup cannot initiate automatic financial cutover, source backfill, Payor name merging or document inventory fabrication. Schema readiness and runtime activation are distinct.

## 8. Operation adapter contract and Payor integration

MASTER defines and implements the contract and source adapters sequentially, reviewing each adapter's transaction participation before activation. Each adapter must resolve authoritative source identity/ownership, quote an approved candidate, expose snapshot/version and outstanding, validate proposed allocations inside posting, and update compatibility projections without committing separately. It must not recalculate historic receipts using current settings.

| Source | Preserve during adaptation |
|---|---|
| Monthly PaymentRecord rent | Existing term/rate/period ownership and current cumulative settlement evidence; distinguish rent from any other stored components. |
| ECF / WCF UtilityBill | Electricity/water source parts, meter/rate assessment snapshots, independent partial settlement and instrument policy. No merging of OR ECF and CT WCF into one Collection. |
| NPM DailyCollection / month settlement | RentGoal/PureDays, chargeable days, closures, occupancy and month-end adjustment semantics; reuse the shared month ledger/service. Do not relabel all NPM daily receipts as monthly rent. |
| TPM / TRM / landing / other activity | Actual activity/rate/source authority; no inferred historical vehicle class, ticket number or receipt grouping. A mock UI does not create a persisted source. |
| Slaughter / Fish-Meat / Weight & Measure | Approved calculation/classification boundary; distinct reported fees without counting the same money twice; package breakdown as detail. |
| Online settlement | Existing provider lifecycle and real captured money evidence; canonical settlement for converted targets; no duplicate revenue from provider and Collection rows. |

Payor-first discovery follows explicit approved associations only. Optional PayorUser linkage does not widen portal access; retain established authentication/access checks. Anonymous and named-snapshot-only contexts remain available where approved. A later Payor rename or link change cannot rewrite historical payer evidence. If an association cannot be established from authoritative evidence, omit that obligation from payor-first discovery and preserve the unresolved source evidence.

## 9. Collection Composer contract

O preserves accepted visual patterns and connects both operation-first and payor-first entry to the same server draft contract. Current Collection is a navigation/recovery surface, not a second financial store. Policy resolves the instrument; incompatible items are directed to separate compatible drafts. A visit can show several drafts with separate post results; posting one must not imply that its siblings succeeded.

Review shows payer/document context, line classifications and amounts, explicit allocations, source periods and calculation detail, total and any changed facts. Required document input identifies an available authorized physical unit; draft entry consumes nothing. Use one posting intent/key across transport retries. Stale revisions and material revalidation changes return to review. A posted draft links its existing Collection. Discard affects only unposted working state; it cannot void issued money/documents.

Do not reuse O's client-only PayorKey, arbitrary sample candidates, instrument-only CT grouping or review boolean as shared contracts. Keep previews clearly non-posting until integrated with authoritative source/charge/document contracts.

## 10. Accountable Document bridge and corrections

Build the minimal authoritative book/range, unit and custody capability required for the supported rollout. Head/Admin manage office custody and assignments; collectors consume only their assigned units. Normal Web posting consumes a valid physical document atomically. OrSeriesConfig remains operational suggestion context, not proof that a form exists or permission to generate/reuse a physical number.

One Collection can have multiple linked historical documents and at most one current document. Do not edit a number in place. A document-only correction appends linked document history and consumes its replacement unit without a second receipt of money. A financial reversal/reposting explicitly changes allocation/report effects through immutable correction evidence and Q42 projections. Original operation bindings, snapshots and used units remain traceable. A replacement financial posting has its own operation ID and correction relationship; a document-only command has its own audited operation outcome without manufacturing a Collection.

Controlled online Awaiting OR remains a separately authorized channel state. It does not relax required physical-document validation for normal Web collection. Broader remittance, stock procurement and accounting workflows are not prerequisites to a minimal trustworthy document bridge, but no unsupported custody/issuance claim may be made.

## 11. Migration, cutover and legacy compatibility

P creates a source/part writer inventory covering specialized Web commands, Composer, Mobile sync, online callbacks/settlement, corrections, retries and maintenance paths that affect settlement. N enforces the same protocol once the selected scope converts. A live legacy method cannot bypass authority because it uses an older route or payload.

For each explicit tenant/source/part/facility/workflow scope: mark Pending Cutover; quiesce new legacy settlement; drain/reconcile in-flight work and issued physical forms; record the evidence; freeze opening assessment/settled/outstanding and boundary metadata; activate Canonical authority. Unrelated scopes remain operational. If device/physical evidence or any writer is unready, do not activate that scope.

Opening settled amounts never generate artificial Collection/Line rows. For assessment PHP 800, reconciled opening PHP 200 and new allocation PHP 300, the cumulative projection is PHP 500 and outstanding PHP 300. Reversing that new allocation restores projection PHP 200 and outstanding PHP 600 without changing the opening evidence. Offline issued cash discovered before freeze belongs in the reconciled opening position. Unexpected old submissions after activation become preserved reconciliation exceptions, never automatic updates to the frozen opening snapshot or new receipts inferred from cumulative values.

Compatibility reads preserve known PaymentRecord, DailyCollection, UtilityBill, TPM, TRM, slaughter and online facts with explicit evidence quality. Report coverage is by source/part and cutover/event contribution, not by deleting an entire legacy source whenever any canonical allocation exists. A single source may legitimately have opening legacy settlement and several new installments. Do not group historical sources into a receipt solely by matching OR/name text. Use Legacy record / Itemized detail unavailable where composition is not known.

Historical identity backfill uses only explicit authoritative relationships. Source labels, names and phone numbers alone never merge Payors. No absent event timeline, CT unit, historical rate, vehicle class or allocation is reconstructed to make reports look complete. Existing endpoints/readers remain until their replacement reconciles like for like; label coverage and basis rather than claiming universal cutover.

## 12. Mobile and offline transition

Keep focused field workflows and collector assignment checks. The initial Mobile work is a versioned payload/queue/document bridge into the canonical core, not the Web Composer. MASTER defines and integrates this contract sequentially; the paused P worktree is read-only candidate evidence for migration/replay and queue compatibility.

- Canonical payloads represent a received amount and explicit approved source/allocations rather than an ambiguous cumulative paid value. Include operation identity and immutable input evidence required by IA-041; retain legacy payload interpretation only for appropriate Legacy scopes/reconciliation.
- Preserve the existing owner-scoped queue and server-issued business-date behavior. Never let a different collector silently acquire another collector's queued work.
- If a physical assigned CT is issued offline, atomically retain its local consumed identity with the original queued event. Failed sync, app retry, rejection, restart or new operation key cannot make the unit available again.
- Queue storage/recovery must not silently treat lost/corrupt issued-event evidence as a valid empty queue. Preserve recoverable data and expose a reconciliation failure; never infer successful synchronization from absence alone.
- Match server outcomes to original operation identity. Infrastructure/transport failure retries the same intent/key; durable changed-intent corrections follow IA-041. Terminal issued-document problems become reconciliation exceptions rather than discardable transactions.
- Prevent old clients from silently writing settlement for converted scopes. A capability/version guard cannot delete their unsynced events; accept evidence into controlled reconciliation where ordinary posting is no longer valid.
- Enforce current authority and document assignment on canonical sync; preserve physically issued facts even when validation now rejects normal posting. No automatic overpayment/credit is created to hide a collision.

Collector deployment is a separate release assignment and requires the repository's signed APK/version/publication process. A source's Q43 gate includes safe collector behavior before conversion; a Web feature completion never grants permission to ignore pending devices or deploy production.

## 13. Activity, RCD, monthly income and report transition

Q provides a read-only parent-per-Collection feed, expandable classified lines/allocations/source evidence and attributable correction/document history. Current payer master edits do not alter historical displayed evidence. A document-only replacement changes document history/disposition without another revenue row. Original reversed/voided Collections remain visible with their explicit financial effect/history.

Query parameters include tenant, facility/source/occupancy scope where relevant, period, cutoff/AsOf, correction basis and money basis. AsOf uses only durably recorded knowledge by the cutoff, irrespective of backdated effective dates. LatestCorrected follows applicable corrections to original period events even when corrections fall outside that period. Keep BusinessDate, CorrectionEffectiveDate and immutable server RecordedAt distinct. Capture a consistent query cutoff/basis for pagination, totals and exports.

Distinguish filters that select whole events containing a classification from aggregation of only matching lines. A Collection containing rent PHP 500 + ECF PHP 500 has a full-event total of PHP 1,000 but ECF classified revenue of PHP 500. Label these explicitly; do not use Q's candidate whole-event filtered total as classified RCD revenue. Group by stable classification identity with explicit policy/display handling across versions.

RCD/category and monthly-income actual totals derive from canonical financial/correction events for converted coverage, with separately identified legacy contributions where supported. No re-entry of amounts already captured; drill down to contributing events. Drafts, opening settlement, projected paid fields, provider lifecycle rows and operation retries are not additional revenue. Existing report sources remain available until reconciled; canonical availability in one scope does not authorize replacing every report globally.

An Itemized Collection Details / Collection Detail Statement may show the physical document reference, payer, line amounts and total with optional audit detail. It supports the physical OR/CT and must not claim to be a replacement government accountable form.

Official cross-period RCD presentation/accounting remains gated by IA-043 and Office confirmation. Implement the two technical query bases in their sequential phase; do not hard-code automatic official restatement or later-period adjustment. Legacy rows without event history cannot promise full historical AsOf reconstruction; disclose the limit. Refund processing and revenue-target governance remain outside this implementation baseline unless separately authorized.

## 14. Authorization matrix

| Action | Head | Admin | Collector | Payor portal user |
|---|---|---|---|---|
| Normal Web draft/review/post | Own tenant/user drafts | Own tenant/user drafts | No office Composer initially | No office Composer |
| Manual Approved charge basis | Only when approved definition permits | Same | No newly granted override | No |
| Form books/ranges/custody/assignment | Yes, tenant-scoped | Yes, tenant-scoped | Consume assigned units only | No |
| Financial/document correction | Audited authorized workflow | Audited authorized workflow | No silent rewrite/delete of issued/synced events | No office correction authority |
| Focused field collection/sync | Not implied by office role alone | Not implied by office role alone | Assigned source/facility/document authority | No |
| Existing online channel | Existing channel guards | Existing channel guards | No implied portal authority | Existing authorized account access and provider flow |
| Business Payor/source association setup (proposed implementation permission) | Audited tenant/source maintenance | Same | No new identity-merge authority | No broad business-identity merge authority |
| Cutover activation (proposed implementation permission) | Audited ready-scope action | Same | Supply assigned event/form evidence | No |
| Activity/report/outcome reads | Existing authorized scope | Existing authorized scope | Existing assigned/own scope only | Existing portal access only; optional Payor link grants nothing automatically |

The last two maintenance permissions are target workflow choices submitted with this baseline, not newly asserted Office policy. Existing Head-only collector administration/audit destinations are not broadened by this matrix. Use established authorization guards; tenant filters fail closed. Draft ownership and operation-outcome authorization are enforced server-side even for otherwise privileged roles; no shared editing/handoff is introduced.

## 15. Single-MASTER ownership and candidate provenance

| Role | Meaning for the approved implementation |
|---|---|
| MASTER / V2 Planner | Sole primary implementation owner and architecture authority. Executes all phases sequentially on `interface-v2/clean-adoption`, owns shared and integrated changes, reviews candidate work, validates each bounded phase, and decides selective reuse/adaptation/reimplementation. |
| N — candidate provenance | Shared core, posting, persistence and document concern boundary; its partial branch is read-only evidence, not an implementation assignment or contract authority. |
| O — candidate provenance | Composer/current-collection UX and entry concern boundary; its partial branch is read-only evidence and cannot define backend contracts. |
| P — candidate provenance | Legacy evidence, source adapter and cutover concern boundary; its partial branch is read-only evidence and cannot infer missing history. |
| Q — candidate provenance | Collection Activity and derived reporting concern boundary; its partial branch is read-only evidence and cannot introduce a financial write path. |

Shared-file locks:

- **MASTER owns all canonical edits:** shared model/context/configuration/migrations, application contracts and handlers, writer/API/mobile integration, read models/reporting, Client integration, tests, export/backup/restore and canonical documentation. Shared files are changed only by MASTER as part of the current bounded phase.
- Candidate N/O/P/Q file lists and edits are inspection evidence only. A useful piece may be reimplemented or selectively ported into the canonical branch after review; no candidate worktree is modified or merged as part of the sequential plan.
- `CONTEXT.md`, `docs/decisions/*`, this baseline and canonical lane guidance remain under MASTER's authority. Branch-local handoff notes do not create competing implementation ownership.

No paused session or candidate branch has edit permission from these historical concern boundaries. MASTER keeps each phase file-scoped and integrates its own changes directly into the canonical target.

## 16. Existing candidate work disposition

Classifications apply to the identified behavior/pieces, not wholesale file or branch approval. KEEP means the inspected invariant/pattern is reusable as-is; it does not certify that its containing file compiles or that its surrounding implementation is approved. All four dirty worktrees remain preserved and paused.

### SESSION N

- **KEEP:** Exact positive-money/line-total checks; mixed-instrument rejection invariant; explicit tenant relationship constraints; existing audit/export participation patterns.
- **ADAPT:** Collection/line/allocation schema; serializable posting coordinator; document bridge; snapshots; durable operation replay; source adapters and genuine shared-writer concurrency tests; migration ordering and null/legacy handling. Use optional business Payor rather than login/name identity.
- **REJECT:** Independently authoritative ReceivableObligation.OutstandingAmount for existing sources; forcing every allocation on one line to share one source-row ID; arbitrary client snapshots/amounts as authority; accepting an active classification as unrestricted manual-charge permission; raw document string as proof of valid custody; changed-intent retries silently returning old success.
- **DEFER:** Generic receivable/assessment authority for genuinely new domains, customer credits/deposits and broad all-operation activation. Any new domain authority requires separate design; customer advances remain excluded from this phase.

### SESSION O

- **KEEP:** Non-financial Add to Collection messaging; accepted drawer/review layout patterns; invalidating review on line edits; operation-first/payor-first convergence intent and integration-request discipline.
- **ADAPT:** Circuit state into a server draft client; business Payor/source lookup; exact reviewed revision; real allocations/document readiness; source-backed entry surfaces and recovery/conflict states. Retain accepted page appearance.
- **REJECT:** Client mock/name keys defining business identity; grouping every CT-compatible item into one CT document merely by instrument; client-only state/review boolean as posting authority; sample calculations or data becoming backend contracts.
- **DEFER:** Shared editing/handoff, broad activation of all operation entry buttons before authoritative adapters exist, and any large Mobile Composer.

### SESSION P

- **KEEP:** Pure evidence adapters that preserve known facts; explicit unknown historical itemization/detail; separation of supporting online-provider evidence from received-money totals; refusal to invent absent historical CT/rate/class facts.
- **ADAPT:** Source/part/tenant coverage and opening boundary; compatibility reconciliation for multiple installments and allocations; source authority markers; cutover writer inventory; conservative Payor linkage; durable operation/queue transition.
- **REJECT:** Repeated canonical source reference automatically classified as duplicate; excluding an entire cumulative legacy source because one new canonical contribution exists; inferred receipt composition, payer merging or historical snapshots unsupported by evidence.
- **DEFER:** Automatic bulk backfill/production cutover, global historical reconstruction and financial resolution of late exceptions without an approved controlled workflow.

### SESSION Q

- **KEEP:** Read-only parent Collection with expandable lines; separation from financial writers; full-result totals before display limiting where basis is consistent.
- **ADAPT:** Document/instrument/allocations/source/snapshot detail; AsOf/LatestCorrected; correction disposition; stable class grouping across policies; separate matched-event and matched-line totals; explicit legacy coverage and cutoff.
- **REJECT:** Treating whole matched-event totals as classification-only RCD totals; a derived read model acting as another balance writer; placeholder null document/allocation fields presented as complete evidence.
- **DEFER:** Global report replacement, official cross-period RCD treatment, target-attainment governance and unsupported historical AsOf reconstruction.

## 17. Sequential implementation phases and canonical integration

Clint approved the sequence. MASTER executes one bounded phase at a time on `interface-v2/clean-adoption`. Existing candidate work remains preserved and read-only unless MASTER later selects a piece for canonical reuse. Each phase must build and run its relevant tests before the next phase begins; no phase activates Canonical settlement before every writer and the controlled reconciliation gate are ready.

| Phase | Sequential deliverable and exit gate |
|---|---|---|
| 0 — Documentation checkpoint | Incorporate this single-MASTER clarification, validate documentation links/diff and commit one focused canonical docs checkpoint. **Approved and performed before Phase 1.** |
| 1 — Shared contracts and dormant additive persistence | Implement minimal Payor/source relationship contracts, persisted Web draft, allocation/source anchors, durable posting-operation identity, accountable-document and correction/report evidence contracts with additive tenant-safe persistence and audit/export/restore participation. No active financial writer or settlement-authority switch. Build and run relevant tests before proceeding. |
| 2 — Bounded ECF / OR vertical path | Implement authoritative ECF source quoting, durable draft/review, OR document bridge, idempotent atomic posting and basic Activity/detail for a bounded scope. Keep ECF Legacy until all touching writers are ready; prove the posting path before any scoped activation. Build and run relevant tests. |
| 3 — Monthly-rental allocation and compatible multi-line OR | Add explicit Payor/source relationships and the existing monthly-rental adapter. Prove rent + ECF only where actual approved relationships support one payer context, plus multi-period/partial allocations. Preserve NPM's own month/occupancy rules. Build and run relevant tests. |
| 4 — CT / WCF and focused Mobile readiness | Add WCF source-part adapter, supported CT custody/issue evidence and focused Mobile queue/retry/reconciliation protocol. Keep CT conservative; do not assume OR-style multi-line physical CT behavior. Build and run relevant tests. |
| 5 — Controlled source-by-source reconciliation and cutover | For each explicitly selected source scope, quiesce writers, drain/reconcile in-flight submissions and issued physical documents, freeze evidenced opening position, then activate Canonical authority only when every writer/projection is ready. Unready scopes remain Legacy. Official cross-period RCD treatment remains gated on Office confirmation. |
| 6 — Additional operations | Extend the same core to NPM, TPM, TRM, landing, slaughter, Fish/Meat, Weight & Measure and other approved sources only after source-specific policy and proof. Build and run relevant tests for each bounded source phase. |

This order refines the bounded OR-pilot direction. It does not promise ECF/rent share an occupancy or manufacture missing relationships. NPM is not routed through monthly PaymentRecord merely to satisfy the example. Every Web, specialized operation, Mobile/offline and online writer touching a source must use the canonical protocol before that source activates.

Integration path: documentation checkpoint -> Phase 1 shared foundation -> each bounded vertical phase -> integrated canonical validation -> per-source readiness/reconciliation -> activation. MASTER owns each change and integrates it directly into the canonical branch; N/O/P/Q worktrees are not resumed and there is no parallel lane merge graph.

After each bounded phase passes its build and relevant tests, record a focused canonical commit before proceeding. Preserve all paused candidate worktrees unchanged; do not synchronize or merge them. Use the single `https://localhost:7167` preview for visual review when a UI phase requires it. No production deployment occurs without a separate authorization.

## 18. Deferred decisions and activation gates

| Gate | Owner / resolution | What it blocks |
|---|---|---|
| Official cross-period RCD correction treatment | Office confirmation, recorded by MASTER under IA-043 | Automatic official earlier-period restatement/later-period adjustment; not immutable evidence or technical query bases. |
| Unconfirmed operation classification/charge/form policy | Office/canonical decision source; MASTER implements only approved cases | Posting/official reports for that unsupported operation. Never invent a class, rate, denomination rule or multi-line physical CT behavior. |
| Late reconciliation exception's financial resolution | Head/Admin controlled review with evidence; a distinct approved correction path before applying financial effects | Automatic mutation of frozen opening evidence or successful posting of ambiguous old submissions. Preservation/intake can be implemented first. |
| Actual selected cutover scope/device/physical inventory readiness | P evidence + N enforcement + Head/Admin activation | Conversion of that scope. A server snapshot or code build alone is insufficient. |
| Shared draft handoff/collaboration | Separate workflow approval | Shared ownership/editing; not initial user-owned drafts. |
| Abandoned-draft retention duration | Operational retention decision before cleanup jobs | Destructive cleanup automation only. |
| Broader refund, remittance, target governance and new obligation domains | Existing decision gates and explicit future assignment | Those independent capabilities; no inferred expansion from itemization. |

These are deliberate bounded deferrals, not hidden assumptions. Any newly discovered contradiction that changes money, source authority, tenancy or physical document policy returns to MASTER. Local naming, DTO factoring and database locking syntax remain N implementation decisions constrained by this baseline and validation.

## 19. Test and verification strategy for future implementation

No implementation tests were added or run during this review. After explicit assignment, financial/security/report changes follow repository failing-before-fix requirements and run suites separately to avoid the documented bUnit flake.

- **Domain/unit:** Money precision and sums; approved charge basis; OR/CT separation; conservative CT line limit; exact allocations/partial limits; immutable snapshots; Payor identity versus login; document-only versus financial corrections; semantic operation normalization preserving meaningful distinctions.
- **PostgreSQL integration:** Cross-tenant foreign keys/queries; same/different operation intent; concurrent identical operations using independent contexts; competing settlements with distinct keys; multi-anchor locking; document/custody uniqueness; two keys on one draft; stale review/source version; cutover-writer races; transaction fault injection proving no partial posting/projection/document/draft/success outcome.
- **Migration/compatibility:** Populated legacy databases; existing global key constraints; no invented itemization/identity/operations; opening amounts and multi-installment coverage; partial UtilityBill source conversion; accurate NPM term/month calculations; no double counting provider/projection rows; tenant export/restore with all new relationships.
- **Mobile:** Lost response; transient retry; durable rejection; same-key changed content; restart; corrupt/recoverable queue; owner switching; already-issued ticket rejection; new-key attempted reuse; old payload after cutover; version guard and retained evidence. Prove physical consumption cannot disappear on discard/sync failure.
- **Web components:** Server draft recovery; two tabs and stale revision; review invalidation; refreshed material source facts; no hidden client amount/instrument authority; no posting from mocks; separate incompatible draft results; already-posted and non-financial discard.
- **Reporting:** Period/cutoff/basis explicit; late/backdated correction exclusion; latest cross-period links; original history retained; no added revenue for document replacement; class-matched versus full-event totals; scope/occupancy/as-of/money-basis reconciliation; immutable displayed payer facts; no unsupported legacy AsOf claim.
- **Security/audit:** Head/Admin matrix, collector assignments, source/link maintenance, draft ownership, tenant-safe authorized outcome replay, immutability/attribution, audit/export/restore and physical-unit protection after void/soft delete.
- **Integrated acceptance:** Combined canonical Release build; relevant unit, component and Docker/Testcontainers integration suites separately; scoped CSS structural check where edited; Clint's localhost visual gate. Collector rollout additionally requires signed release validation under the collector-release runbook.

## 20. Acceptance criteria and handoff

The implementation baseline is satisfied only when the applicable phase/scope proves:

1. Lisa's explicitly associated rent PHP 500 + ECF PHP 500 + approved penalty PHP 50 can form one OR-backed Collection with correct independent classifications, when those approved sources exist; the same visit's WCF is a separate CT-compatible collection.
2. No draft creates money, allocation, document consumption or financial-feed rows. Recoverable owner-scoped drafts enforce exact revision/review and one successful Collection despite distinct posting keys.
3. One rent line can explicitly allocate July PHP 900 + August PHP 600; allowed partial utility settlement works; no hidden allocation or unapplied customer-credit balance is introduced.
4. Business Payor identity works without portal activation, cannot cross tenants or be inferred from names, and does not replace specialized assessment authority. Posted payer evidence survives later master changes.
5. Every converted source has frozen evidenced opening facts, an explicit authority marker and one settlement protocol across all writers. Projections and financial/document/operation/draft effects commit together.
6. Identical authorized retries resolve once; changed intent conflicts; concurrency cannot double-settle, double-post a draft or reuse a consumed unit. Reversed originals remain bound to their original operation.
7. Offline issued forms/events survive every failure and remain consumed. Cutover proves reconciliation before activation; late old submissions preserve original evidence as exceptions.
8. Corrections retain original events/dates/relationships, separate document replacement from money, update settlement correctly, and support reproducible AsOf plus LatestCorrected queries without inventing official RCD policy.
9. Activity has one Collection parent with expandable details; classified reports and drill-down reconcile; legacy evidence and opening/projection/provider data do not inflate new revenue.
10. Approved rates/period/occupancy/NPM rules remain intact; client mocks or new tables never become a parallel assessment or balance source. Cantilan like-for-like figures reconcile for the converted scope.
11. New persistence is tenant-safe, additive, audited and included in backup/restore; integrated canonical validation and relevant visual/release gates pass before rollout.

Each phase handoff lists exact files, candidate pieces reused/adapted/rejected/deferred, source coverage, tests actually run with results, migrations, data/cutover risks, open gates and commit hash. Report unsupported scope explicitly. MASTER reviews the canonical integrated result.

## Approval requested

Clint approved this baseline and the single-MASTER sequential workflow. Q1–Q46 decisions remain in force, including the unresolved Office confirmation for official cross-period RCD treatment. The documentation checkpoint is committed before Phase 1 begins. This approval is not a production deployment instruction or permission to discard, reset or modify the paused candidate worktrees.
