# Session O — Collection Composer / Web

**Status: PERMANENTLY PAUSED as an implementation session.** Preserve its partial worktree unchanged as read-only candidate evidence. Do not resume, modify, merge, reset or discard it. MASTER / V2 Planner is the sole primary implementation agent and proceeds sequentially on `interface-v2/clean-adoption`. This guide records O's UX concern boundaries and candidate file provenance only; it is not an assignment or a grant to define backend contracts. MASTER may selectively reuse, adapt, reject, defer or reimplement pieces after review.

## Confirmed Payor decision — IA-038 / Q41

Read [ADR-001](../../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md), the canonical twelve-constraint StallTrack domain decision.

- Consume MASTER's business Payor and eligible-source contracts. Do not substitute portal users, display-name keys, or client-side matching for business identity.
- Payor-first choices derive only from approved explicit source relationships. The source domains continue to calculate obligations.
- Do not require portal registration or permanent Payor creation for operation-permitted anonymous, snapshot-only, or one-off transactions.
- Review uses the payer evidence supplied for the collection; posted views preserve frozen evidence. MASTER defines missing contracts; mocks do not define them.

## Confirmed settlement cutover — IA-039 / Q42

Follow [ADR-002](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md). Use authoritative eligible-source balances and MASTER's canonical payment/allocation contract. Do not directly write cumulative paid/status fields for Canonical sources. Display opening legacy settlement distinctly when needed to explain a balance; never present it as a newly posted collection. This guide remains candidate review material, not an assignment.

## Confirmed reconciliation gate — IA-040 / Q43

Consume [ADR-002's scoped gate](../../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md#controlled-reconciliation-gate--q43). Respect server-owned Pending Cutover/authority status and affected-source availability; unrelated sources remain usable. Do not bypass a cutover guard or silently resubmit a late legacy cumulative value as a new receipt. Preserve submitted operation/document identity and present reconciliation exceptions distinctly from successful posting.

## Confirmed durable posting identity — IA-041 / Q44

Consume [ADR-003](../../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md). Keep the same operation ID and financial intent through retries/lost responses. Treat changed-intent reuse as an explicit conflict, not success. A corrected intent after durable business rejection needs a new ID and normal validation; never silently regenerate keys to bypass an issued-document exception. Display the recorded outcome and current correction disposition returned through authorized contracts. Do not invent client-side canonical normalization rules.

## Confirmed Web draft authority — IA-042 / Q45

Consume [ADR-004](../../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md). Replace preview circuit state as authority with MASTER's durable user-owned draft contracts. Support authenticated recovery, DraftId/ExpectedRevision mutations, stale/review-required responses and review of exact normalized financial content. Never silently overwrite newer content or auto-post materially revised allocations. Posted drafts link their existing Collection; discard remains non-financial. Shared editing/handoff is unapproved; UI-only changes must not manufacture false financial revisions. These are candidate UX acceptance concerns, not work assigned to O.

## Confirmed correction reporting — IA-043 / Q46

Consume [ADR-005](../../decisions/ADR_005_CORRECTION_REPORTING_BASES.md). Preserve original event visibility and current correction disposition; distinguish document replacement from financial reversal/reposting. Show the selected report period/cutoff/basis explicitly wherever those reports are consumed. Do not imply an Office-approved cross-period RCD treatment. Consult the [baseline awaiting approval](../../v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md); implementation remains paused.

Branch: interface-v2/o-collection-composer
Worktree: C:\dev\stalltrack-v2-worktrees\o-collection-composer

Read AGENTS.md, CONTEXT.md, REVENUE_ARCHITECTURE.md,
DECISION_REGISTRY.md, ACTIVE_WORKSTREAMS.md, and current V2 UI pages.

Own the Web Current Collection / Collection Composer experience.
Preserve the accepted navy/gold/white StallTrack V2 design system.
Approved UX:
- Operations-first and Payor-first both enter the same composer.
- Add to Collection creates/updates a draft only; it does not post money.
- Current Collection persists while staff navigate.
- one payer context per collection;
- OR/CT is resolved by policy, not selected by staff;
- incompatible instrument items are separated;
- explicit allocations are visible before posting;
- arbitrary free-text financial lines are not allowed;
- Review is required before final posting.
Any selected UI work must consume MASTER's shared financial contracts.
Do not invent API/domain contracts. Missing contracts are resolved by MASTER;
this candidate guide does not authorize isolated parallel UI implementation.

Target surfaces:
- persistent Current Collection drawer/workspace;
- review screen with itemized lines and total;
- clean allocation presentation;
- Payors & Accounts collect entry;
- operation-level Add to Collection entry pattern.

Do not independently rewrite ECF/WCF/Rent business logic.
MASTER may inspect these candidate UI patterns read-only and selectively reimplement
them in a sequential phase. Do not resume or merge this candidate worktree.
