# Session O — Collection Composer / Web

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
Build only Client-side work that is isolated from N's shared financial contracts.
Do not invent API/domain contracts. If a needed contract is missing, record an
INTEGRATION REQUEST FOR SESSION N and continue with isolated UI work or mocks.

Target surfaces:
- persistent Current Collection drawer/workspace;
- review screen with itemized lines and total;
- clean allocation presentation;
- Payors & Accounts collect entry;
- operation-level Add to Collection entry pattern.

Do not independently rewrite ECF/WCF/Rent business logic.
Do not merge until Clint approves. Report files, screenshots/review route, build/tests,
N integration requests, and commit hash.
