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

## RESOLVED 2026-10-01 - WCF end-to-end readiness (operation-only collector)
- Runtime evidence: Bobby Mercado held the WCF assignment, no NPM facility assignment and 4,999 assigned Cash Tickets;
  Mobile showed "Authorization inactive". The local V3 database had no UtilityBill at all.
- Root cause 1 (authorization): the WCF capability added `NPM_FACILITY_REQUIRED`, and — contrary to its
  `requireNpmAuthorityForCollector: false` argument — `WcfCollectionWorkflow.PostMobileAsync` still rejected a collector
  without NPM, as did the WCF obligation and Cash Ticket reads. Per IA-053 the WCF operation assignment alone now
  authorizes the collector in the capability, the reads and the writer; the NPM-bound UtilityBill remains the SOURCE
  context (checked on the source), and the legacy NPM utility sync path keeps its own NPM gate.
- Root cause 2 (no obligation path): Water obligations could only be written through the NPM utility dialog with both
  utilities, and no route could make one Canonical. Added, Head/Admin only:
  - `GET/POST api/wcf-collections/setup-sources|obligations` — the direct approved Water amount for one stall and billing
    month on the ONE stall/month UtilityBill (created only if none exists); Electricity is passed through unchanged; a
    settled, partly settled or cutover Water part is frozen; no future period; no reading/cubic metre/rate input.
  - `POST api/wcf-collections/activation-readiness|activations` — "Activate for Mobile collection" for one unsettled direct
    approved Water obligation through the existing `SettlementCutoverWorkflow` (dry-run, attested checklist, freeze,
    activate). Nothing is written while a real blocker remains. Clint chose this scoped exposure on 2026-10-01; there is
    still no bulk or generic cutover route. Water cutover collector evidence now covers NPM-facility collectors (legacy
    writers) and every WCF-assigned collector.
- Mobile: a Ready WCF now opens its own page (`/wcf`) — obligation list, approved/settled/outstanding, amount up to the
  outstanding (partial settlement is what the writer already supports), next assigned CT, durable enqueue before report.
- No obligation: capability stays Ready when collectible sources exist but nothing is owed, and the page says
  "No outstanding WCF obligations"; with no active source at all it is "Pending activation", never an authorization fault.
- Tests: `WcfEndToEndReadinessTests` (PostgreSQL), capability/Today's Work unit tests, WCF Accounts component tests.

## RESOLVED 2026-10-01 - WCF direct Mobile entry and one-time enablement (IA-054)
- Routine WCF no longer needs per-row Save → Activate → attestation. Head/Admin enables WCF Mobile collection once (server-
  derived readiness, idempotent, audited); collectors then collect office-prepared amounts or enter the amount directly.
- Endpoints: `GET api/wcf-collections/mobile-sources`, `GET api/wcf-collections/mobile-status`,
  `POST api/wcf-collections/mobile-status/enable`; `POST api/wcf-collections/mobile-collections` accepts `StallId` +
  `BillingYear` + `BillingMonth` for a direct amount (queued offline with the same fields).
- Remaining: the legacy NPM utility dialog still writes Water readings as before; after enablement, a Water amount it sets
  with no legacy settlement is simply collected (and converted) on Mobile. Device app version is not tracked server-side, so
  it is not part of readiness.
