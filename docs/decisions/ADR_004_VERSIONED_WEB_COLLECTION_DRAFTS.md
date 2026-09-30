# ADR-004 — Server-persisted, user-owned Web collection drafts

**Status:** Accepted

**Date:** 2026-09-26

**Owners:** Clint; MASTER / V2 Planner (sole implementation owner). N/O/P/Q are paused candidate workstreams whose concern boundaries and partial work remain review evidence only.

**Related decisions/issues:** [IA-042 / Q45](DECISION_REGISTRY.md#ia-042--server-persisted-versioned-web-collection-drafts); [IA-041 posting identity](ADR_003_DURABLE_POSTING_OPERATIONS.md)

## Context

**CURRENT IMPLEMENTATION:** Canonical CollectionLineDraft is a domain posting input, not a persisted Web working draft. O's paused CurrentCollectionState preview stores lines and a review boolean in circuit memory. Its integration request proposes durable versioned drafts but is candidate evidence, not architecture authority.

**CONFIRMED STALLTRACK DECISION:** Clint approved Q45 with the ten requirements below. The server is authoritative for Web draft state. This is approved target architecture; it does not implement drafts or restart paused N/O/P/Q work.

## Decision

### 1. Draft identity and ownership

Each Web draft has a stable DraftId and belongs to a TenantId, owning UserId, payor context and applicable instrument family/policy context resolved by the draft. Initial V2 drafts are user-owned. Collaborative editing, handoff and shared staff ownership require a separately approved workflow.

### 2. Persisted but non-financial

Server persistence supports recovery across navigation, browser refresh, temporary connection loss and later authenticated Web sessions. A draft is not a Collection or revenue, belongs in neither RCD nor Collection Activity, consumes no OR/CT and allocates no money. Draft allocation entries express proposed allocation intent only. Successful canonical posting alone creates financial history.

### 3. Revision and mutation concurrency

Each draft has a monotonically changing Revision. Every mutation sends DraftId and ExpectedRevision. Reject stale expected revisions with a concurrency conflict; never silently overwrite newer content. The caller must reload/reconcile before continuing.

### 4. Review binds an exact revision

Review confirmation belongs to the exact reviewed draft revision/content. Financially meaningful changes invalidate it and require renewed review. A generic IsReviewed boolean without the reviewed revision/content binding is insufficient.

### 5. Meaningful review content

Bind review to normalized financial state, including applicable payor, lines, classifications, amounts, allocations, source obligations, instrument policy, business date and accountable-document intent. Transport/UI-only changes must not manufacture false financial revisions.

### 6. Final revalidation

Review does not bypass posting validation. Revalidate current authoritative outstanding, allocations, payor context, classification/policy, instrument compatibility, document availability, authorization, totals and concurrency state. If material financial state changed, return revalidation/review-required. Do not silently change the reviewed draft and post. Show the changed state and require review again.

For example, if another valid payment reduces a source obligation by PHP 200 after a PHP 1,000 draft was reviewed, the original allocation must not silently post. Refresh/recalculate the affected draft and obtain renewed review.

### 7. At most one successful Collection per draft

On success, atomically create Collection, lines and allocations; consume the applicable accountable document; apply compatibility projections; persist the durable posting operation; mark the draft Posted; and store its CollectionId. A DraftId can never produce another successfully posted Collection afterward.

### 8. DraftId differs from ClientOperationId

DraftId identifies working collection state; ClientOperationId identifies one posting attempt/intent under IA-041. A different operation key cannot make an already-posted draft post again. Return/link its existing Collection with an already-posted result, subject to normal authorization. Do not claim that a different financial intent was newly posted. Genuine transport retries reuse the original operation key under IA-041; changing an already-bound intent still conflicts.

### 9. Multi-tab behavior

If tab A reviewed revision 10 for PHP 1,000 and tab B adds a PHP 50 penalty creating revision 11, tab A's attempt against revision 10 returns stale/review-required. It must reload and review PHP 1,050. Once revision 11 posts, every tab sees the draft's Posted disposition and CollectionId; none can post that DraftId again.

### 10. Discard and abandoned drafts

Discard is non-financial. A draft may be marked Discarded; this is not Collection deletion. Retention/cleanup duration for abandoned drafts is deferred and does not block the core architecture.

## Consequences

### Positive

- Work is recoverable across Web sessions without creating premature financial history.
- Concurrent tabs cannot overwrite newer work or post an obsolete review.
- Draft uniqueness prevents duplicate posting even when distinct operation IDs are submitted.

### Trade-offs / risks

- Server draft persistence, owner checks, revision comparisons and review metadata are required.
- Revalidation can return the user to review after outside changes; this must not silently alter the confirmed financial intent.
- Posted-draft identity and operation idempotency are separate guards and must commit consistently.

## Compatibility and ownership

MASTER owns draft domain/persistence, owner/tenant guards, revision/review contracts, Web recovery and atomic posting linkage. Drafts remain excluded from financial activity, RCD and collected revenue. Mobile retains its separately approved focused/offline workflow; this Web draft decision does not require a giant Mobile composer. N/O/P/Q remain permanently paused and provide candidate evidence only.

## Verification

Future acceptance checks cover recovery across refresh/reconnect/later authentication, tenant/owner isolation, stale mutation rejection, meaningful review invalidation, transport/UI-only changes, materially changed source facts, two-tab posting, different-operation-key attempts on one draft, atomic rollback including draft disposition, and non-financial discard. One DraftId yields at most one successful Collection; persisted drafts have zero financial effect.

Current evidence: canonical `EEMOCantilanSDS.Domain/Entities/Revenue/CollectionLineDraft.cs` and AppDbContext; paused O `EEMOCantilanSDS.Client/Services/CurrentCollectionState.cs` and `docs/planning/itemized-sessions/SESSION_O_INTEGRATION_REQUESTS.md`. No implementation or runtime verification is claimed.

## Supersedes / superseded by

Refines the prior user/session-context draft wording: Web authority is durable tenant/user-owned server state. Supersedes target reliance on circuit-only draft state or an unversioned review boolean. Does not approve collaborative draft ownership or determine abandoned-draft retention duration.
