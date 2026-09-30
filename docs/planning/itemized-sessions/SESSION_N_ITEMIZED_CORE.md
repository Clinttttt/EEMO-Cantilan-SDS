# Session N — Itemized Collections Core

**Status: PERMANENTLY PAUSED as an implementation session.** Preserve its partial worktree unchanged as read-only candidate evidence. Do not resume, modify, merge, reset or discard it. MASTER / V2 Planner is the sole primary implementation agent and proceeds sequentially on `interface-v2/clean-adoption`. This guide records N's architectural concern boundaries and candidate file provenance only; it is not an assignment or a grant of contract authority. MASTER may selectively reuse, adapt, reject, defer or reimplement pieces after review.

## Confirmed Payor decision — IA-038 / Q41

Read [ADR-001](../../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md), the canonical twelve-constraint decision. This is a StallTrack engineering/domain decision, not an additional EEMO accounting policy.

- Own the small tenant-scoped business Payor contract independently of `PayorUser` and portal activation. Any access link is explicit and optional.
- Use minimal explicit relationships appropriate to existing source domains. Never link from name, spelling, phone alone, or OR/CT similarity, and never across tenants.
- Payor identifies who; source domains retain assessment/settlement authority. Do not create a Payor balance or generic receivable subsystem.
- Validate approved source associations for payor-first collection; preserve operation-permitted anonymous/one-off contexts.
- Freeze posted payer identity/name evidence. Coordinate conservative linkage with P and expose authoritative contracts for O/Q.

## Confirmed settlement cutover — IA-039 / Q42

Follow [ADR-002](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md). The canonical implementation must provide an explicit per-source authority marker, posting/correction protocol and atomic compatibility projections. Retain opening evidence separately from new collections. Every settlement writer for a Canonical source must join this protocol; do not introduce independently mutable duplicate outstanding balances. MASTER owns source adapters and cutover evidence sequentially. This guide remains candidate review material, not an assignment.

## Confirmed reconciliation gate — IA-040 / Q43

Enforce [ADR-002's scoped gate](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md#controlled-reconciliation-gate--q43): explicit Pending Cutover scope, quiesced new legacy writes, drained/reconciled in-flight operations, evidenced opening freeze, then Canonical activation. Coordinate physical document and Mobile evidence with P. Late old submissions retain operation/document identity as reconciliation exceptions; they cannot silently mutate opening evidence or settlement. Unready scopes remain Legacy and unrelated scopes continue normally.

## Confirmed durable posting identity — IA-041 / Q44

Own [ADR-003](../../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md): tenant-scoped durable operation registry, deterministic semantic intent normalization, immutable binding, authorized outcome replay and explicit changed-intent conflicts. Coordinate concurrent identical requests and commit success with all financial/document/projection effects atomically. Preserve bindings through correction; distinguish durable business rejection from infrastructure failure. Replace mutable source-row keys as authority and reconcile the existing global Collection key index with the approved tenant scope. A new operation key cannot bypass document/source/reconciliation checks.

## Confirmed Web draft authority — IA-042 / Q45

Own [ADR-004](../../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md): server-persisted tenant/user-owned DraftId, monotonic revision and expected-revision mutation guards, normalized financial review binding, revalidation and atomic Posted/CollectionId linkage. A new operation key cannot repost a posted draft. Draft intent has no financial effect; proposed allocations allocate no money and consume no document. Material changes require renewed review. Shared ownership/handoff is outside initial approval.

## Confirmed correction evidence — IA-043 / Q46

Own [ADR-005](../../decisions/ADR_005_CORRECTION_REPORTING_BASES.md)'s immutable original/correction relationships, separate BusinessDate/CorrectionEffectiveDate/RecordedAt and explicit financial effects. A document-only replacement creates no new revenue. Expose enough committed evidence for Q's AsOf and LatestCorrected queries; backdated corrections cannot enter an earlier recorded-knowledge cutoff. Official cross-period RCD policy remains open. Consult the [baseline awaiting approval](../../v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md); implementation remains paused.

Branch: interface-v2/n-itemized-core
Worktree: C:\dev\stalltrack-v2-worktrees\n-itemized-core

Read first:
- AGENTS.md
- CONTEXT.md
- docs/business/REVENUE_ARCHITECTURE.md
- docs/business/EEMO_BUSINESS_RULES.md
- docs/decisions/DECISION_REGISTRY.md
- docs/planning/ACTIVE_WORKSTREAMS.md

Own the shared financial core for itemized collections.
Implement the approved model:
- one Collection has one payor context and one instrument family;
- one Collection may have multiple CollectionLines;
- allocations explicitly apply money to source obligations;
- OR/CT comes from revenue policy;
- OR-compatible and CT-compatible lines cannot mix;
- the document number exists once at collection/document level;
- partial payments are supported;
- obligation-backed amounts cannot exceed outstanding;
- historical calculation/source details are frozen;
- posting revalidates balances and totals atomically.

Weight & Measure = OR.
WCF = CT.
ECF = OR.
Scope:
- Domain, Application, Infrastructure, API, persistence and focused tests.
- Preserve legacy settlement authority only for sources still Legacy. For Canonical sources, route all settlement paths through the canonical protocol and maintain old paid/status fields only as atomic projections (ADR-002).
- Do not redesign Web pages.
- Do not own Collection Activity UI, RCD UI, or historical migration UX.
- Leave a clean bridge for later accountable-form inventory/custody.

Required tests should cover multi-line OR, mixed-instrument rejection, one-payor invariant,
partial allocation, over-allocation rejection, stale/double-post protection, and frozen snapshots.

For any candidate evidence MASTER elects to reuse, report its files, contracts,
migrations, validation and risks. No candidate branch is resumed or merged;
MASTER integrates approved implementation directly into the canonical branch.
