# MEEDO Office Clarification Backend Handoff — 2026-10-06

This handoff records the backend contracts introduced by the office clarification pass. All APIs are tenant-scoped from the authenticated context. Collection amounts and classifications are server-authoritative; a successful post returns the server-generated `SRC`.

## Source-native discovery and checkout

| Method and route | Contract | Authorization |
|---|---|---|
| `GET /api/mobile/collection-session/sources?search=` | `CollectionSourceSearchResult[]`: `Identity { Kind, Id }`, `DisplayName`, `Context`, `OperationCode`, optional `TaxYear`, optional `VendorType`. Empty and partial terms are supported; result limit is 50. | Collector |
| `POST /api/mobile/collection-session/source-eligible` | Body `CollectionSourceIdentity?`; returns `CollectionSessionDiscovery` with typed `Operations`, `Family`, `Choices`, stable selection identities and source-specific facts. | Collector |
| `POST /api/mobile/collection-session/quote` | `CollectionSessionIntent`; returns normalized item quotes, per-instrument totals, grand total and item problems. No SRC is allocated. | Collector |
| `POST /api/mobile/collection-session/record` | `RecordCollectionSessionRequest(Intent, QuoteFingerprint)`; returns one `CollectionSessionCollection` per child (`CollectionId`, `ReferenceCode`/SRC, instrument, amount, item IDs, disposition). | Collector |
| `GET /api/mobile/collection-session/{clientSessionId}` | Reconcile/retrieve recorded session result. | Collector, owner checked |

Source identity values are `Occupancy=1`, `SpaceAccount=2`, and `FishMeatVendorRegistration=3`. `CollectionFamily` values are `Market=1`, `Rent=2`, `Space=3`, `Terminal=4`, `Slaughterhouse=5`. A `CollectionSessionSourceChoice` includes `SelectionKey`, `Kind`, `OperationCode`, display/context, typed `Identity`, `Instrument`, `AmountRule`, optional `ServerAmount`, optional `Rate`, effective date/rate ID, and required input names. `CanAutoSelect` is derived only when one eligible choice exists. Stable source identity never comes from display-name matching.

Supported source-native intent kinds are `TERMINAL`, `FISH_MEAT_VENDOR_FEE`, and `WEIGHT_AND_MEASURE`. `SourceNativeChargeIntent` carries optional `VendorRegistrationId`, `TerminalSection`, `VehicleClassId`, `CashTicketCount`, or `Kilograms` as appropriate. `SourceNativeCollectionRequest` carries `ClientOperationId`, server-validated `BusinessDate`, `AmountReceived`, the typed charge, optional `PayerSnapshot`, and optional `ExpectedSourceVersion`. For weighing, quote determines `amount = kilograms × effective rate`; the submitted amount must equal the server quote. Terminal count is optional evidence and never changes the amount. Current `CollectionSessionItemKind` values are defined in the shared DTO assembly; native entries use `SourceNative`.

Structured relevant problems include `InvalidIntent`, `InvalidBusinessDate`, `CollectorNotAssigned`, `InvalidTerminalSection`, `VehicleClassNotAvailable`, `InvalidSource`, `RequiresPayerSnapshot`, `RequiresRegisteredVendor`, `InvalidQuantity`, `RateNotEffective`, `PolicyNotEffective`, `InvalidAmount`, `NotSupportedYet`, `QuoteStale`, `AmountChanged`, `CollectionIntentConflict`, `PayerMismatch`, and session-level `SessionIntentConflict`. UI should branch on codes and keep the server's concise user message for display.

BusinessPayor master/search routes remain compatibility APIs. The target source-native route uses occupancy, space-account, or vendor-registration IDs; new native vendor-fee walk-up collections persist the supplied `PayerName` snapshot with no source ID and do not create a Payor or registration. Existing legacy Payor links may continue to supply a display snapshot. Historical unregistered weighing is not inferred or repriced.

## Terminal and Transportation

`TerminalSection` values are `ComfortRoom=1`, `PullPulVansCargoVans=2`, `Tricycad=3`; the shared labels are exactly `COMFORT ROOM`, `PULL PUL VANS, CARGO VANS`, and `TRICYCAD`. Terminal report row keys are `TERMINAL_COMFORT_ROOM`, `TERMINAL_PULL_PUL_VANS_CARGO_VANS`, `TERMINAL_TRICYCAD`; report group key is `TERMINAL` / `Income From Terminal`. Terminal uses Cash Ticket. `POST /api/office-sources/quote` and `/record` accept aggregate amount, optional cash-ticket count, and optional explicitly mapped `VehicleClassId`. Vehicle rate is an informational frozen aid; received amount controls the posted Collection. `GET /api/office-sources/terminal/vehicle-choices?date=YYYY-MM-DD` returns currently effective choices. `PUT /api/office-sources/terminal/vehicle-section` is Head-only and maps a class explicitly. Known Tricycle/Jeepney/Multicab/Van/PUB/PUBB codes are constrained to the approved section mapping; historic classes are not automatically mapped.

From the 2026-10-06 cutover, Transportation/Parking remains a separate Cash Ticket source and permits a direct amount without vehicle class/rate. Old saved class settings and pre-cutover Collections remain historical evidence. New posts do not create `TrmTrip` records. `GET /api/office-sources/activity?from=&to=&operationCode=` reads canonical source-native office activity only; it returns Collection/SRC, date/time, operation, optional section/vendor/source snapshot, collector, amount/net amount/state, count, vehicle/rate evidence, and kilograms. It does not fabricate a TRM trip number or include legacy trip history.

## Fish/Meat registration and posting

`FishMeatVendorType` is exactly `Fish=1` or `Meat=2`; `VendorRegistrationKind` is `New=1` or `Renew=2`. `POST /api/office-sources/fish-meat/registrations` takes `RegisterFishMeatVendorRequest(ClientOperationId, TaxYear, VendorType, RegistrationKind, DisplayName, BusinessName?, Address?, Reference?)`, Head-only. `GET /api/office-sources/fish-meat/registrations?year=` is tenant-scoped for Head/Admin/Collector. Response identity is registration `Id`, year, type, kind and supplied register facts. Registration posts are idempotent by client operation ID; changed intent conflicts.

Fish/Meat Vendor Fee is direct `AmountReceived`, Official Receipt, classification `FISH_MEAT_VENDOR_FEE`. It may reference a same-year registration or use a required payer snapshot. Weight & Measure requires a same-year Fish or Meat registration and positive kilograms; rate and effective evidence are server-resolved and frozen. It uses Official Receipt and `WEIGHT_AND_MEASURE`. Neither source changes NPM rent, and the sources remain separate Collections/SRCs.

## NPM Daily Collect All

| Method and route | Contract |
|---|---|
| `GET /api/mobile/npm-daily-batch/sources` | Today's eligible stall rows with `StallId`, `OccupancyId`, stall number, occupant, server `EffectiveCharge`, `CanCollect`, optional `ReasonCode`. |
| `POST /api/mobile/npm-daily-batch/quote` | `NpmDailyBatchIntent(ClientCollectionSessionId, BusinessDate, Items[{ClientItemId, StallId}])`; response includes normalized sources, selected item count, server total, quote fingerprint and blocked problems. |
| `POST /api/mobile/npm-daily-batch/record` | `RecordNpmDailyBatchRequest(Intent, QuoteFingerprint)`; returns the ordinary session result, one Collection/SRC per selected stall. |

Only checked items are sent. Omitted/unchecked stalls receive no row or absence state. The server revalidates today, assignment, occupancy, daily rate, canonical authority and settlement. Posting is one serializable transaction for the batch: any preflight/posting failure rolls back every child. Child operation IDs are deterministic from tenant, session and client item IDs. Exact replay returns prior SRCs; altered items conflict. This endpoint requires a current online quote and record; it is not the durable offline queue. For offline use, retain a local draft until the server can quote; never queue an unquoted price.

## Monthly Income, targets, adjustments and signatories

`GET /api/official-reports/monthly-income?year=&month=` is Head/Admin and returns the official Monthly Income DTO. Stable report groups include `MARKET`, `RENT`, `SPACE`, `TERMINAL`, `SLAUGHTERHOUSE`, and `PENDING`. BBQ remains `RENT_BBQ` under Rent Income; the three Terminal rows form section `B`; Slaughterhouse is its own section `C`. Unknown classifications remain pending. Canonical collections and linked corrections are counted once.

`POST /api/official-reports/targets` accepts `SetAnnualTargetRequest(ClientOperationId, RowKey, Year, Amount, ApprovedSource, Reference?, Note?, ExpectedRevisionId?)`; Head (`SuperAdmin`) only. `POST /api/official-reports/monthly-income/adjustments` accepts `SetMonthlyIncomeAdjustmentRequest(ClientOperationId, RowKey, Year, Month, ExpectedSystemAmount, OfficialAmount, Reason, Reference?, ExpectedRevisionId?)`; Head (`SuperAdmin`) only, required reason. Both return `ReportRevisionDto` with revision/supersession, actor and timestamp. `GET /api/official-reports/governance?year=` returns revision history. Adjustment modifies only official presentation. Monthly cell facts expose `SystemAmount`, `AdjustmentAmount`, `OfficialAmount`, and `IsAdjusted`; target fields are derived from report actuals, and zero/unconfigured target attainment stays null.

`PUT /api/municipality-profile/signatories` accepts `SetReportSignatoriesCommand(Signatories, Align?)`, SuperAdmin/Head only. Each `ReportSignatoryDto` has `Caption`, `Name`, optional `Title`. Values are stored in the existing tenant profile JSON and returned on newly generated report DTOs; no person/title is hard-coded.

## Current compatibility notes

- Existing BusinessPayor records, links, search APIs and old master-linked display fallback remain for historical compatibility; native discovery and new direct vendor fee do not require them.
- Existing NPM `DailyCollection` weighing facts without frozen amount/rate remain visible as unresolved history and are no longer multiplied by today's rate in Monthly Income, Collection Activity, or Collector Report.
- Existing `TrmTrip` and old vehicle classes remain untouched; current Terminal and Transportation writers use canonical Collections.
- Existing report target/adjustment revision entities and Municipality signatory JSON were reused; migrations add the independent vendor-registration table, Terminal class mapping, and source-owned space occupant support.
