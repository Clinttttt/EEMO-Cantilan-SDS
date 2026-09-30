# Mobile V3 Functional Audit (2026-09-30)

Baseline HEAD 282c6d0e. Verified by reading code and building `net10.0-android` Debug. No device or emulator review was performed.

| Area | State | Notes |
|---|---|---|
| Login | Works; not restyled | Server-error copy no longer says "EEMO". Seal fallback logic left as is. |
| Menu | Works; not redesigned | Reads capabilities; "Today's Work" grouping not built. |
| NPM / TCC / NCC / BBQ / Ice | Works; presentation unchanged | Fish/Meat weighing, Vendor Fee and Stall Rental not re-audited visually. |
| WCF | Obligation-based CT, server outstanding amount, capability-gated | Meter/cubic-meter/rate line removed. Amount is validated against the server outstanding; no hard-coded peso. |
| ECF | Unchanged | Not audited for context wording. |
| Slaughterhouse | Approved-animal selection | Typed animal name and rate removed for new and edited activity; historical custom lines still display with their recorded rate. Server enforces the approved list. |
| Transportation | Vehicle-class CT | `VehicleClassCode` queued; server rate read-only; store refuses issue without a class. |
| Market Fees / Landing / Transfer Large Cattle / Vegetable-Fruit | Governed operation screen | Existing capability, terms and document checks kept. |
| Tabo / TPM | Works | Vendor fee shown from server collection data. |
| Fish/Meat Vendor Fee, Kanmanggay, Fiesta/Araw | No Mobile writer | Reported as backend gaps. |
| Records | Unchanged | Not re-audited for duplicate local/server rows. |
| Reports / collector position | Not built | Blocked by backend gap (Admin-only position read). |
| Profile | Tenant-branded header | Assignment/operations/version/notification sections not added. |
| Offline / sync | Preserved | Store, sync, ClientOperationId, OwnerKey and issued-document safety untouched apart from the `VehicleClassCode` field. |
| App update / FCM | Untouched | |

## Tests
`PendingOperationStoreTests` covers the Transportation vehicle-class rule and the wire DTO. Slaughter and Profile screens have no automated test: Mobile has no UI test harness.
