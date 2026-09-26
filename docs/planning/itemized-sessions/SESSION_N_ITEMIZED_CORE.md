# Session N — Itemized Collections Core

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
- Preserve existing legacy write paths while introducing the canonical path.
- Do not redesign Web pages.
- Do not own Collection Activity UI, RCD UI, or historical migration UX.
- Leave a clean bridge for later accountable-form inventory/custody.

Required tests should cover multi-line OR, mixed-instrument rejection, one-payor invariant,
partial allocation, over-allocation rejection, stale/double-post protection, and frozen snapshots.

At completion report files changed, contracts, migrations, build/tests, risks,
integration requests for O/P/Q, and the commit hash.
Do not merge into canonical until Clint approves.
