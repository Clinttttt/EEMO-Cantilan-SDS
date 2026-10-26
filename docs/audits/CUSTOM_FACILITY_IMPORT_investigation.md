# Custom Facility stallholder import - investigation (2026-10-26)

## Where the restriction lived
- UI route `/import/{Facility}` (`ImportStallholders.razor`): a static `Configs` dictionary of npm/tcc/ncc/bbq/ice only; any other slug showed "Import not available".
- UI entry (`AddVendorModal.razor`): `ImportFacilities` allow-list of the same five codes hid the "Import list" button.
- Backend `BulkImportStallholdersCommandValidator`: `Supported` set of the same five codes (`POST api/stalls/bulk-import`).
- Handler (`BulkImportStallholdersCommandHandler`) itself had no code switch beyond NPM-only behaviour (section, daily rate) and NCC area location; it is generic over `FacilityCode`.
- `/import-history/{Facility}` (payment history, `ImportPaymentHistory.razor` + `BulkImportPaymentHistory*`) already accepts Custom1..5 (it excludes only NPM-daily/TPM/SLH/TRM). Not changed.

## Storage of a custom facility today
Custom1..Custom5 use the ordinary monthly machinery: `Facility` (per tenant, `IsActive`, `CustomCategory`) -> `Stall` (`Section` null, `CustomSectionName` null) -> `Contract` (term, `MonthlyRentalRate`) -> `PaymentRecord`. `CustomFacility.razor` renders the shared `FacilityPage` for both categories. Custom sections exist only for NPM, so a custom facility imports as one list (like TCC).

## Authority per category
- **Stall Rent**: facility/stall/contract engine -> the existing `BulkImportStallholdersCommand`. SUPPORTED.
- **Space Rental**: `docs/planning/TENANT_TERMINAL_CUSTOM_RENTAL_WORKSTREAM_20261022.md` and `CUSTOM_FACILITY_MONTHLY_CATEGORY_HANDOFF_20261023.md` say the declared category is a label for operation/registry/report identity and that official classification (`RENT_OTHER`) is not yet accepted for Space receipts; a true generic Space Rental would be an `ObligationAccount` source that does not exist. The import is therefore REFUSED for `SpaceRental` facilities (handler + UI) rather than writing stall/contract rows under a Space identity. GAP: enabling it is a one-condition change (`CustomCategory != StallRent` in the handler and `CustomConfigFor` in the page) once Core Brain/the LGU approves classification and reporting for Space-category stalls.

## Safeguards (server is authoritative)
- Tenant: `FacilityRepository.GetByCodeAsync` and `StallRepository` run under the tenant query filter, so another tenant's facility is "not found" (a tenant with no `Custom1` cannot write one into another's).
- Inactive: handler returns a clear failure; the UI resolves slugs from the active-facility catalogue only (inactive/unknown shows "Import not available").
- Rates/terms/dates come from the file rows exactly; custom facilities use no daily rate and no fee-rate defaults.
- Idempotency: the bulk import has no ClientOperationId; a retry is content-idempotent via the existing guards (active occupant, occupied stall, "already recorded from this date"). No migration needed, none added.
- An import creates stalls and contracts only: no collections, payments, allocations or documents.
