# Session P — Itemization Migration / Legacy Adapters

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

Do not own the shared Collection domain contract; Session N does.
If a shared contract change is needed, issue a precise request to N.
Do not merge until Clint approves. Report artifacts, reconciliation findings,
tests/build results, N integration requests, and commit hash.
