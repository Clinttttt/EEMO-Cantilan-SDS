# Session P — Itemization Migration / Legacy Adapters

**Status: PERMANENTLY PAUSED as an implementation session.** Preserve its partial worktree unchanged as read-only candidate evidence. Do not resume, modify, merge, reset or discard it. MASTER / V2 Planner is the sole primary implementation agent and proceeds sequentially on `interface-v2/clean-adoption`. This guide records P's legacy-evidence and reconciliation concern boundaries and candidate file provenance only; it is not an assignment. MASTER may selectively reuse, adapt, reject, defer or reimplement pieces after review.

## Confirmed Payor decision — IA-038 / Q41

Read [ADR-001](../../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md), the canonical twelve-constraint StallTrack domain decision.

- Propose linkage only from explicit authoritative existing relationships within one tenant. Document its evidence and limits; authentication/stall access alone does not establish every historical operational relationship.
- Never merge/backfill business identity solely from names, spelling, phone alone, document numbers, or other textual similarity.
- Preserve source identity/name evidence and unresolved canonical Payor linkage when authority is insufficient. Do not require mass Payor creation or invent historical composition.
- Use MASTER's minimal relationship contracts. Introducing identity must not move balances or create a new assessment/settlement authority.

## Confirmed settlement cutover — IA-039 / Q42

Follow [ADR-002](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md). The canonical implementation needs frozen evidenced opening capture, cutover metadata, complete writer inventory and reconciliation using an explicit per-source Legacy/Canonical marker. Preserve original assessment, opening settlement, post-cutover allocations and corrections distinctly. Never manufacture collections for opening settlement or permit an independent legacy writer after conversion. MASTER owns implementation sequentially; this guide remains candidate review material.

## Confirmed reconciliation gate — IA-040 / Q43

Review evidence for [ADR-002's scoped gate](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md#controlled-reconciliation-gate--q43): explicit auditable scope, reconciled Mobile queues and issued physical documents, retries, in-flight Web/online settlement and server idempotent operations. Freeze only after reconciliation; retain source/boundary, assessment, settled/outstanding and evidence/status metadata. MASTER owns writer guards and activation contracts. Unready scopes remain Legacy. Preserve late submissions and consumed documents as controlled-review exceptions without automatically changing the opening snapshot or inventing new receipts. This describes a concern boundary, not work assigned to P.

## Confirmed durable posting identity — IA-041 / Q44

Follow [ADR-003](../../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md). Inventory existing operation/source evidence and migration gaps without fabricating missing historical posting intents. Preserve original operation/document identity through Q43 reconciliation. Mutable PaymentRecord/UtilityBill keys may remain for compatibility but cannot be canonical retry history once the durable registry is active. Coordinate registry cutover and in-flight outcome recovery with N; new operation IDs cannot release issued documents or bypass reconciliation.

## Confirmed Web draft authority — IA-042 / Q45

Follow [ADR-004](../../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md). Saved drafts and proposed allocations are not legacy settlement, opening-position or collected-revenue evidence. Source adapters expose current authority/balance changes so resumed drafts can revalidate and require renewed review. Preserve the distinction between a resumable working DraftId and a durable posting ClientOperationId; do not turn draft migration into invented financial history.

## Confirmed correction reporting — IA-043 / Q46

Follow [ADR-005](../../decisions/ADR_005_CORRECTION_REPORTING_BASES.md). Preserve actual original/correction dates and relationships where known; do not reconstruct absent legacy event history or promise unsupported AsOf results from current cumulative rows. Keep opening evidence, late reconciliation exceptions and posted corrections distinct. Consult the [baseline awaiting approval](../../v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md); implementation remains paused.

Branch: interface-v2/p-itemization-migration
Worktree: C:\dev\stalltrack-v2-worktrees\p-itemization-migration

Read AGENTS.md, CONTEXT.md, REVENUE_ARCHITECTURE.md,
EEMO_BUSINESS_RULES.md, DECISION_REGISTRY.md, and existing legacy payment models.

Own historical compatibility, cutover and legacy-source mapping.
Non-negotiable rule:
Never invent old receipt groupings, itemization, allocations, CT numbers,
vehicle classes, or source detail that the legacy database did not store.

Map known facts truthfully from PaymentRecord, DailyCollection, UtilityBill,
TPM/TRM/Slaughter and other existing sources.
Mark unknown historical composition as legacy/detail unavailable.

Design adapters so new canonical itemized collections can coexist with old history
during the transition without changing historical money.
Focus on:
- source-by-source legacy inventory;
- mapping matrix: known / derivable / unknowable;
- cutover strategy;
- reconciliation checks;
- compatibility read adapters;
- migration tests where safe.

The shared Collection contract is owned by MASTER; this session guide and worktree remain candidate evidence only.
MASTER owns all shared contracts and canonical integration. The candidate worktree
remains read-only and is not merged. MASTER may reuse or reimplement selected
evidence after review in the appropriate sequential phase.
