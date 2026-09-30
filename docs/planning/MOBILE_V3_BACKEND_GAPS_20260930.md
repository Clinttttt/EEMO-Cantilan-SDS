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

## MOBILE BACKEND GAP - Legacy-source records and reports by classification
- Current behavior: the legacy Records feed and facility report are unchanged; canonical collections appear in the new register only after the source is canonical.
- Required behavior: a single collector activity read that combines legacy and canonical rows once, and a per-classification breakdown for Reports.
- Why the UI cannot truthfully implement it: merging the two on the device would double count around cutover.
- Exact frontend contract needed: one server read returning both, with the authority per row.

## Not a gap
Transportation vehicle classes and rates (governed terms), approved slaughter animals (`MobileSlaughterCollectionDto.ApprovedAnimals`),
governed sync payload with `VehicleClassCode`. Mobile cannot record or void a remittance; that stays with the office.
