# ADR-005 — As-of and latest-corrected reporting

**Status:** Accepted; official cross-period RCD treatment remains pending Office confirmation

**Date:** 2026-09-26

**Owners:** Clint; MASTER / V2 Planner (sole implementation owner). N/O/P/Q are paused candidate workstreams whose concern boundaries and partial work remain review evidence only.

**Related decisions/issues:** [IA-043 / Q46](DECISION_REGISTRY.md#ia-043--as-of-and-latest-corrected-reporting); [settlement cutover](ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md); [durable operations](ADR_003_DURABLE_POSTING_OPERATIONS.md)

## Context

**CURRENT IMPLEMENTATION:** CollectorReportQueries reads current specialized payment statuses and cumulative amounts, with source-specific date filters. The dormant Collection ledger does not yet implement the correction/query model described here. Legacy history cannot be assumed to contain the events necessary to reconstruct an earlier state.

**CONFIRMED STALLTRACK DECISION:** Clint approved Q46 with the eleven requirements below. Preserve immutable original events and immutable linked corrections, supporting explicit AsOf and LatestCorrected query interpretations. This is target architecture; it does not establish the Office's official cross-period RCD accounting/presentation policy or approve implementation.

## Decision

### 1. As-of view

Answer what StallTrack would have reported using information durably recorded and known by the specified cutoff. Only events/corrections durably recorded on or before that cutoff participate. For an original PHP 300 collection recorded September 26 at 10:00 and a reversal recorded September 27 at 09:00, the September 26 23:59 cutoff still includes the original PHP 300.

### 2. Latest corrected view

Answer the currently valid corrected interpretation of the reporting period after all applicable corrections now known. For the same September 26 collection, show original +PHP 300 and linked September 27 reversal -PHP 300, net PHP 0. Retain the original event visibly and traceably; do not invent an edited replacement for its history.

### 3. Distinct dates

Retain BusinessDate for the original event's business/accounting date, CorrectionEffectiveDate for the correction's intended effect under the approved workflow, and immutable server RecordedAt for when StallTrack actually recorded the event. Do not collapse these into one timestamp.

### 4. Recorded-knowledge cutoff

As-of reconstruction is bounded by what the system knew. A correction recorded September 27 must not enter an As-known-September-26 result merely because its effective date was backdated. RecordedAt protects the historical audit boundary.

### 5. Explicit correction relationship

Every correction affecting a posted Collection/document retains its relationship to the original event. Preserve the applicable OriginalCollectionId, CorrectionType, CorrectionId, EffectiveDate and RecordedAt. Void, Reversal and Replacement must have explicit financial effects; a label alone does not determine money movement.

### 6. Document correction is not new revenue

Distinguish document correction from financial reversal/reposting. If OR #001 correctly records PHP 500 but its physical document needs replacement by OR #002, and no money was recollected, the new document must not create another PHP 500 revenue event. Preserve the linked document history and consumed physical units.

### 7. Collection Activity

Retain the original event and expose its current correction state. A reversed PHP 300 collection remains a top-level event, with expandable original +PHP 300 on September 26 and reversal -PHP 300 on September 27. Do not delete the original row.

### 8. Explicit reporting parameters

Reporting/query contracts identify period, cutoff/AsOf timestamp and correction basis (AsOf or LatestCorrected). Avoid hidden semantics under a generic label such as September report. Two users must be able to identify why results use different correction knowledge/bases.

### 9. Financial event integrity

Reports derive from posted financial/correction events and their actual relationships. Do not manufacture another Collection merely to make a corrected report balance. Reports do not become another financial source of truth.

### 10. Official cross-period RCD treatment remains open

The Office must confirm whether a later-period correction requires a revised earlier RCD/report, a later-period adjustment referencing the earlier transaction, or another official treatment. Preserve enough evidence to support these alternatives. Do not hard-code a treatment or present either technical query basis as Office approval of that policy.

### 11. Historical reproducibility

A report run later with the earlier cutoff must reproduce what was known at that cutoff, despite later reversals, voids, replacements, reconciliation or corrections. Preserve both event history and the report's declared basis. Never silently reinterpret the original event.

## Consequences

### Positive

- Original and corrected figures remain explainable without overwriting history.
- Later/backdated corrections cannot leak into earlier recorded-knowledge results.
- Document replacements cannot inflate revenue merely by issuing another number.

### Trade-offs / risks

- Query contracts must expose the reporting period, cutoff and correction basis, with immutable dated correction relationships underneath.
- LatestCorrected queries must follow relevant corrections to period events even when the correction was recorded/effective outside the original period.
- Legacy current-state rows do not prove historical as-of truth. Label unavailable reconstruction honestly rather than promising unsupported reproducibility.

## Compatibility and ownership

MASTER owns immutable event/correction dates, relationships, explicit financial effects and the two derived query interpretations/drill-down, while documenting which legacy facts/cutoffs can actually be supported. Source settlement projections are not historical report events. Official cross-period RCD treatment remains gated on Office confirmation. N/O/P/Q remain permanently paused as implementation sessions.

## Verification

Future acceptance checks cover later reversal, backdated-effective correction excluded before RecordedAt, document-only replacement with zero new revenue, original-row visibility, cross-period linked corrections, explicit report parameters, consistent totals/drill-down, historical as-of reproduction and unsupported legacy-history labels. No tests or implementation are claimed by this decision.

Current evidence: `EEMOCantilanSDS.Infrastructure/Repositories/CollectorReportQueries.cs`, `EEMOCantilanSDS.Application/Queries/Collectors/GetReportOfCollections/GetReportOfCollectionsQueryHandler.cs`, and `EEMOCantilanSDS.Domain/Entities/Revenue/Collection.cs`.

## Supersedes / superseded by

Supersedes any target assumption that a current mutable payment row alone can reproduce an earlier recorded-knowledge report, or that document replacement automatically creates revenue. Does not alter the Office's currently approved official reporting treatment; that cross-period policy remains explicitly open.
