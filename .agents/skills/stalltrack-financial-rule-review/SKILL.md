---
name: stalltrack-financial-rule-review
description: Review or change StallTrack billing, rates, rent, arrears, settlements, occupancies, utilities, OR handling, or other financial rules. Use when money semantics or effective-date behaviour could change; do not use for purely visual financial-page edits.
---

# StallTrack financial rule review

Protect the intended financial behaviour while making the requested change reviewable across every affected layer.

## Authority

Read the root `AGENTS.md`, then the relevant parts of:

- `.kiro/knowledge/EEMO_Complete_Documentation.md` for accepted business semantics;
- `.kiro/knowledge/arch-rules.md` for implementation boundaries;
- `.kiro/knowledge/patterns.md` for code shapes.

An explicit current task or business ruling governs the requested behaviour. Code and tests are evidence of current
behaviour, not automatic proof of intended behaviour. If a ruling, documentation, tests and implementation disagree,
surface the contradiction and determine which source is stale before changing financial behaviour.

## Review workflow

1. State the rule being reviewed in business terms. Identify the tenant, facility and billing model; the obligation or
   revenue type; the business period; the effective-rate date; and the occupancy responsible for it.
2. Trace the rule end to end. Find the domain calculation, application handler or service, repository projection, API
   contract, presentation paths, exports, mobile path when applicable, cache invalidation and audit entry.
3. Reuse the established rule holders. In particular, inspect `IFeeRateResolver`, `RatePeriod.AsOf`,
   `Stall.ResolveDailyFee`, `Stall.ResolveMonthlyRent`, the daily-billed monthly ledger in `DomainRules`,
   `DomainRules.TermLastDay`, and `StallOccupancy.AnsweringForMonth` when their concepts apply. Do not create a parallel
   calculation in a handler, repository or UI.
4. Check boundary cases that can change liability: mid-period rate changes, partial months, month-end adjustment,
   excused or closed days, handovers, expired or terminated terms, past occupancies, partial payments, utilities,
   over-collection, and a tenant with no configured override. Test only the applicable cases, not a ritual list.
5. Confirm tenant isolation and attribution: Cantilan remains unchanged unless the current ruling changes it; another
   municipality uses its own data; collector identity comes from authentication; and OR uniqueness remains per tenant.
6. Add or update a focused regression test that fails under the old behaviour. For tenant-specific behaviour, place a
   Cantilan-unchanged case beside it. Use an integration test when correctness depends on PostgreSQL translation,
   migrations, query filters or database constraints.
7. Run the affected build and each relevant test project separately. Report unrun checks and their prerequisites.

## Review output

Explain the business rule, affected paths, contradictions resolved, tests proving the rule, and any remaining cross-screen
or historical-data risk. Do not silently broaden the requested ruling or rewrite durable documentation unless the ruling
actually changed.
