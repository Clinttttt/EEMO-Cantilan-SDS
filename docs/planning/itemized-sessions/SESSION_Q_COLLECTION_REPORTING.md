# Session Q — Collection Activity / RCD Reporting

**Status: PERMANENTLY PAUSED as an implementation session.** Preserve its partial worktree unchanged as read-only candidate evidence. Do not resume, modify, merge, reset or discard it. MASTER / V2 Planner is the sole primary implementation agent and proceeds sequentially on `interface-v2/clean-adoption`. This guide records Q's read/reporting concern boundaries and candidate file provenance only; it is not an assignment or authority to add financial writes. MASTER may selectively reuse, adapt, reject, defer or reimplement pieces after review.

## Confirmed Payor decision — IA-038 / Q41

Read [ADR-001](../../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md), the canonical twelve-constraint StallTrack domain decision.

- Consume MASTER's optional business Payor reference and frozen posted payer evidence separately from authentication identity.
- Render historical payer evidence from the posted collection/document, not mutable Payor master names. A current master lookup must not rewrite historical display.
- Use explicit tenant-scoped identity for linked payor history. Never infer associations or combine records from equal names, phone numbers, or OR/CT text.
- Preserve permitted anonymous/one-off events and unresolved legacy identity in results. Reporting remains derived, with no independent balance or financial write path.

## Confirmed settlement cutover — IA-039 / Q42

Follow [ADR-002](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md). Canonical post-cutover cash reports/RCD/Activity derive from actual canonical events and attributable corrections. Opening legacy settlement is historical balance evidence, never new revenue. Do not double count compatibility paid/status projections as additional receipts. Preserve separately attributable legacy history and distinguish cumulative settlement/outstanding from cash events. This guide remains candidate review material, not an assignment.

## Confirmed reconciliation gate — IA-040 / Q43

Consume [ADR-002's evidenced boundary](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md#controlled-reconciliation-gate--q43). Distinguish reconciled opening evidence, canonical posted events and unresolved late-submission exceptions. An exception is not automatically a posted canonical receipt; retain visibility of its original operation/document evidence, including an already-issued physical OR/CT, without double counting or hiding unresolved physical collections.

## Confirmed durable posting identity — IA-041 / Q44

Consume [ADR-003](../../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md). Preserve the original operation-to-Collection/document relationship and show current reversal/void/replacement disposition. Retries and conflict/rejection records are not additional collected revenue. Apply normal tenant/authentication/authorization boundaries to outcome queries; an operation ID grants no access. Derive financial totals from canonical financial events rather than the operation registry.

## Confirmed Web draft authority — IA-042 / Q45

Follow [ADR-004](../../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md). Exclude persisted drafts, discarded drafts and proposed allocations from Collection Activity, RCD and revenue. Successful posting exposes the resulting Collection once; draft revisions, review actions and retry attempts are not separate financial events. Draft recovery belongs to the operational draft surface, not the posted financial feed.

## Confirmed reporting bases — IA-043 / Q46

Own [ADR-005](../../decisions/ADR_005_CORRECTION_REPORTING_BASES.md)'s AsOf/LatestCorrected queries, with explicit period, cutoff and basis. Keep original Collection Activity rows and expandable linked corrections. Follow cross-period correction relationships without inventing collections or double counting replacement documents. RecordedAt bounds historical knowledge even for backdated effective corrections. Official cross-period RCD presentation/accounting remains Office-gated. Consult the [baseline awaiting approval](../../v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md); implementation remains paused.

Branch: interface-v2/q-collection-reporting
Worktree: C:\dev\stalltrack-v2-worktrees\q-collection-reporting

Read AGENTS.md, CONTEXT.md, REVENUE_ARCHITECTURE.md,
DECISION_REGISTRY.md, ACTIVE_WORKSTREAMS.md, and existing report/activity code.

Own the read-side presentation for itemized posted collections.
Approved reporting model:
- one top-level Collection Activity row = one posted collection/document event;
- itemized CollectionLines expand beneath it;
- filters may include date, payor, collector, instrument, revenue classification and facility;
- RCD/category totals derive from posted lines and document usage;
- staff do not re-enter the same RCD financial totals manually;
- document view is clean;
- audit/detail view preserves source, period, allocation, quantity/rate and snapshots.
The canonical reporting implementation remains derived from posted events and must
remain compatible with MASTER's sequential core contracts.

Do not create another financial write path.
Do not fabricate accountable-form authority.
MASTER owns missing query contracts; this guide does not authorize parallel implementation.

Preserve official EEMO report structure where evidence exists.
MASTER may inspect these candidate reporting patterns read-only and selectively
reimplement them in a sequential phase. Do not resume or merge this candidate worktree.
