# Mobile V3 - Backend Gaps (2026-09-30)

Gaps found while wiring the Collector Mobile to the governed contracts. Mobile does not fake any of these.

## MOBILE BACKEND GAP - Collector position (Collected / Remitted / Unremitted)
- Current behavior: the position is served by `api/remittances/position`, which is Admin-only. A collector token cannot read it.
- Required behavior: a collector-scoped read returning only the caller's own collected, remitted and unremitted pesos and their own form counts (assigned, issued, spoiled, returned, on hand, needs review).
- Why the UI cannot truthfully implement it: Mobile would have to add up its local queue and cached records, which double-counts synced rows and cannot know remittance coverage.
- Exact frontend contract needed: `GET api/mobile/position?from&to` returning the caller's slice of `CollectorPositionDto` (same collector-total algorithm as remittance), read-only.

## MOBILE BACKEND GAP - Fish/Meat Vendor Fee
- Current behavior: no Mobile writer; the collector-facing operation code list has no Vendor Fee code.
- Required behavior: a governed or dedicated writer with server-owned amount and instrument, kept separate from Fish/Meat weighing and Stall Rental.
- Why the UI cannot truthfully implement it: the instrument and revenue classification are not defined for a Mobile collection.
- Exact frontend contract needed: an operation code in `CollectorOperationCodes` with terms (amount, allowed instrument) from `GetGovernedServiceTerms`.

## MOBILE BACKEND GAP - Kanmanggay
- Current behavior: no Mobile writer or operation code.
- Required behavior: an approved writer (or an explicit statement that it is Web-only) with server terms.
- Why the UI cannot truthfully implement it: classification and cadence are not exposed to Mobile.
- Exact frontend contract needed: as above.

## MOBILE BACKEND GAP - Fiesta / Araw
- Current behavior: no Mobile writer or operation code.
- Required behavior: an approved writer with server terms, or Web-only.
- Why the UI cannot truthfully implement it: same as Kanmanggay.
- Exact frontend contract needed: as above.

## MOBILE BACKEND GAP - Accountable-form position for the collector
- Current behavior: Mobile lists available documents per operation, but no collector-scoped count of assigned / spoiled / returned / needs-review forms.
- Required behavior: included in the collector position read above.
- Why the UI cannot truthfully implement it: remaining tickets must come from the ledger, and the ledger read is Admin-only.
- Exact frontend contract needed: the form counts in the position contract above.

## Not a gap (already served)
Transportation vehicle classes and rates (governed terms), approved slaughter animals (now on `MobileSlaughterCollectionDto.ApprovedAnimals`), governed sync payload with `VehicleClassCode`.
