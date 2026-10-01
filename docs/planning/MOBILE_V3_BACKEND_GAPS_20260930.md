# Mobile V3 - Backend Gaps (2026-09-30, completion pass)

Mobile does not fake any of these.

## Resolved in the completion pass

- Collector-scoped position: `GET api/Mobile/position?from&to` returns the signed-in collector's Collected / Remitted / Unremitted,
  needs-review count, legacy amount outside remittance coverage and own form counts (`CollectorPositionDto`). Collector-only; identity from the token.
- Collector-scoped accountable-form counts: the `Forms` list of the same contract (on hand, issued, spoiled, returned, needs review per instrument).
  No office-wide books or ranges are exposed.
- Collector-scoped canonical collections: `GET api/Mobile/records/collections?from&to` (own collections only).

## MOBILE BACKEND GAP - Fish/Meat Vendor Fee
- Current behavior: no Mobile writer or operation code.
- Required behavior: an approved writer with server-owned amount and instrument, kept apart from Fish/Meat weighing and Stall Rental.
- Why the UI cannot truthfully implement it: instrument and revenue classification are not defined for Mobile.
- Exact frontend contract needed: an operation code with terms from `GetGovernedServiceTerms`.

## MOBILE BACKEND GAP - Kanmanggay
- Current behavior: no Mobile writer or operation code.
- Required behavior: an approved writer, or a statement that it is Web-only.
- Why the UI cannot truthfully implement it: classification and cadence are not exposed to Mobile.
- Exact frontend contract needed: as above.

## MOBILE BACKEND GAP - Fiesta / Araw
- Current behavior: no Mobile writer or operation code (Web is authoritative).
- Required behavior and contract: as Kanmanggay.

## RESOLVED 2026-10-01 - Collector Reports: legacy plus canonical, once, with a classification breakdown
- Found at runtime: a posted Landing/Berthing CT (₱100, Oct 1 2026) showed ₱0 on By Month, Per Payee and Summary, because
  `GET api/Mobile/report` read only the legacy facility sources of the collector's assigned facilities.
- Now: `GetCollectorReportQueryHandler` composes the legacy facility report with `ICollectorCollectionFacts` — the signed-in
  collector's posted canonical Collections, net of corrections, from the same derivation the Position tab sums
  (`RemittanceWorkflow`). `CollectorReportComposer` adds them once; the legacy reader skips any PaymentRecord whose settlement
  authority is canonical (`CollectionSourceAuthorityMap.LegacyMoneyCounts`), so converted rent is never counted twice.
- Breakdown: canonical money by CollectionLine classification; legacy money by the facility that recorded it, labelled as a
  facility source (a facility is never presented as a classification).
- Date basis: canonical money by `Collection.BusinessDate` over the whole selected month, inclusive (DateOnly, no UTC instant).
- Payor: only an explicit `Collection.PayorId` makes a named payor. A walk-up CT is in every total and stated once as
  "Walk-up · no registered payor"; it never adds to "Payor accounts".
- Pending sync: device-queue money is shown beside the report as "Waiting to sync" and is never added to server totals.
- Tests: `CollectorReportCompositionTests` (unit), `PendingSyncSummaryTests`, `CollectorRecordsTests` cut-over row, and
  PostgreSQL `RemittanceWorkflowTests` (report handler end to end, retry, remittance, tenant/collector scope).

## MOBILE BACKEND GAP - Records feed is two server reads
- Current behavior: Records lists the legacy facility feed and the canonical operation register as two server reads, each
  authoritative for its own rows; nothing is merged or summed on the device.
- Remaining: a converted rent/utility row's canonical Collection is not yet listed as a facility record line in the legacy feed.
- Exact frontend contract needed: one server read returning both, with the authority per row.
- Also open: a cut-over rent row recorded at the office (canonical Collection with no CollectorId) no longer appears as
  "recorded at the office" in the collector report; it is not the collector's accountability, but the split line is lost for it.

## Not a gap
Transportation vehicle classes and rates (governed terms), approved slaughter animals (`MobileSlaughterCollectionDto.ApprovedAnimals`),
governed sync payload with `VehicleClassCode`. Mobile cannot record or void a remittance; that stays with the office.
