# Session Q — Collection Activity / RCD Reporting

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
Work only within read models, reporting/query surfaces and Client presentation that can
remain compatible with N's future canonical contracts.

Do not create another financial write path.
Do not fabricate accountable-form authority.
If N must expose a query contract, document an INTEGRATION REQUEST FOR SESSION N.

Preserve official EEMO report structure where evidence exists.
Do not merge until Clint approves. Report files, review routes, build/tests,
N integration requests, and commit hash.
