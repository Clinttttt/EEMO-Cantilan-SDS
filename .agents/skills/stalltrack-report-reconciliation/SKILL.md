---
name: stalltrack-report-reconciliation
description: Reconcile StallTrack financial reports, dashboards, follow-up views, facility screens, print documents, PDFs, CSV exports, or mobile summaries when figures or labels disagree. Use for cross-screen financial consistency work, not ordinary layout-only edits.
---

# StallTrack report reconciliation

Determine whether apparently different figures should agree before changing either one.

## Authority

Read the root `AGENTS.md`, the Reporting and billing sections of
`docs/business/EEMO_BUSINESS_RULES.md`, and the applicable rules under `docs/architecture/`. Explicit current business
rulings govern. If documentation, tests, code and observed production disagree, identify the contradiction and decide
which source is stale; do not make reports agree by silently adopting whichever implementation is easiest to reuse.

## Reconciliation workflow

1. Define each compared figure before comparing values:
   - tenant and facility;
   - row grain and aggregation grain;
   - current-holder, historical-occupancy, active, inactive or mixed scope;
   - month, year, custom period or lifetime window;
   - snapshot/as-of date and effective-rate date;
   - assessment period versus receipt/recorded date;
   - obligation, collection, credit, outstanding balance or revenue;
   - base rent versus utilities, fish fees or other additions.
2. Build a small reconciliation matrix for the affected screens and exports. Record their query or repository source,
   filters, calculation owner, cache and presentation transforms. Treat pagination or a capped visible list separately
   from the total over the full result set.
3. Decide whether the figures are semantically identical. Identical definitions should share the same source or durable
   rule. Legitimately different definitions should remain different and be labelled clearly; do not force period and
   lifetime, assessment and receipt, or active and inactive views into one number.
4. Check the established reporting invariants: current rosters exclude past occupancies; bounded history excludes
   occupancies outside its window; daily-billed rent uses the monthly ledger; historical rent uses the responsible
   occupancy; partial counts as unpaid for the paid/unpaid invariant; heterogeneous total-row rates display no invented
   common rate; and cache invalidation covers every mutation embedded in the snapshot.
5. Verify every affected representation: API DTO, screen, detail view, print route/document, PDF and CSV. Mobile is a
   separate representation when it consumes the same definition.
6. Add the narrowest tests that prove both the calculation and its rendered claim. Use repository or integration tests
   for data semantics and bUnit tests for labels, grouping, print/export selection and conditional sections. Reintroduce
   the defect once to prove the regression test fails.
7. For visual changes, inspect responsive and print behaviour and check scoped CSS brace balance. Do not redesign
   unrelated report sections.

## Result

Report which figures must match, which intentionally differ, the source of each, and the evidence used. Call out any
unverified production-only or historical-data assumption rather than smoothing it over.
