---
name: stalltrack-backend-engineering
description: Workflow for implementing a StallTrack backend change (domain, application, API, persistence, canonical collections, tenancy, Mobile server contracts). Use at the start of any backend slice, before editing code.
---

# StallTrack backend engineering workflow

Work one bounded gap at a time. Stop and document instead of guessing whenever a step cannot be answered from
authority.

1. **Business authority.** Name the rule and where it is written (rulebook section, IA-###, ADR, explicit
   Clint/Core Brain direction). If the only evidence is a UI label, candidate branch, old code or another LGU → stop,
   record as BLOCKED BUSINESS RULE.
2. **Source owner.** Which specialized domain owns the fact (NPM `DailyCollection`/month ledger, `PaymentRecord`,
   `UtilityBill` part, TPM, TRM, slaughter, governed configurable service)? Do not create a second owner or registry.
3. **Inspect existing code.** Entity, workflow/handler, EF configuration, controller, tests, export/backup
   registration. Find the nearest existing pattern and copy its shape.
4. **Invariant.** State what must never happen (double cash, reused document, cross-tenant read, rewritten history).
5. **Tenant boundary.** Where is the tenant resolved? Is every query filtered by it? Are FKs same-tenant composite?
6. **Concurrency / idempotency boundary.** What does a retry with the same `ClientOperationId` do? A changed intent?
   Two concurrent requests? A different key against the same document/source?
7. **Accountable-document impact.** Does this issue, consume, quarantine or read an OR/CT? A physically issued
   document never returns to stock.
8. **Smallest additive change.** Prefer a read model or an inactive foundation over a writer. No activation, no
   cutover, no backfill.
9. **Tests.** Unit tests for rules/handlers; a failing-before-fix test for money/tenancy/auth/report changes
   (reintroduce the defect once). Cover the dangerous path, not just the happy path.
10. **Migration if required.** Additive only; never edit old migrations. Run
    `dotnet ef migrations has-pending-model-changes --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api`.
11. **PostgreSQL proof** when persistence, filters, constraints or transactions changed
    (`EEMOCantilanSDS.IntegrationTests`, Docker required).
12. **Inspect the diff.** `git diff --stat`, `git diff`, `git diff --check`; confirm no UI files changed.
13. **Update the handoff.** Append a checkpoint to the backend handoff and update the gap audit row.
14. **Bounded commit.** Run `stalltrack-financial-safety` and `stalltrack-backend-review`, stage by explicit path,
    one gap per commit, verify clean status afterwards. Never push/merge/deploy.
