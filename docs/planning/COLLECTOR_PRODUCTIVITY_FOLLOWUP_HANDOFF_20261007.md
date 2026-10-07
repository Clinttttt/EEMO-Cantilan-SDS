# Collector Productivity Backend Handoff — 2026-10-07

Implementation follow-up to [the office clarification backend](OFFICE_CLARIFICATION_BACKEND_HANDOFF_20261006.md), based on accepted integration `4f2efad0`. This document records the explicitly approved 2026-10-07 collector self-correction policy and typed integration contracts. It does not claim a production release. No frontend screens/styles were changed. The dirty authority-document checkout was read only.

## NPM source-native New Collection

Existing `POST /api/mobile/collection-session/source-eligible` accepts `CollectionSourceIdentity(Occupancy=1, occupancyId)`. An authorized NPM occupancy can now return both:

| Item | Exact contract | Financial authority |
|---|---|---|
| Daily stall payment | `CollectionSessionItemKind.NpmDaily=11`, operation `NPM_DAILY`, choice `Identity.StallId`, `OccupancyId`, `PeriodStart` (server business date); `FixedAmount=3`, `ServerAmount`, server instrument, no required entry fields | Existing daily canonical handler and NPM month/rate rules |
| Whole payment | Existing `NpmWholePayment=8`, `NPM_WHOLE_PAYMENT`, stall/occupancy/year/month identities and monthly remaining amount | Existing NPM Whole Payment workflow |

Daily intent: `CollectionSessionItemIntent(ClientItemId, NpmDaily, ConfirmedAmount, NpmDaily: SessionNpmDailyIntent(StallId))`. Session `SourceIdentity` is the selected occupancy; no Business Payor is required. Use the returned server amount. `AmountChanged` refuses an altered charge. Discovery omits Daily after today's payment; Whole remains only where a monthly balance is collectible. Choosing Daily and Whole for the same stall/current month in one checkout returns `DuplicateBusinessEvent`; they are alternative settlement actions, not two charges against the same rent. Other eligible items may accompany either choice.

The existing quote/record/reconcile routes and durable itemized intent serialization remain in use. One item posts one canonical financial boundary/SRC; child IDs use the existing tenant/session/item derivation. No device-generated references or tariffs.

## NPM Collect All readiness

`GET /api/mobile/npm-daily-batch/readiness` (Collector) returns `NpmDailyBatchReadiness`:

- `BusinessDate`: current server date.
- `Sources`: existing `NpmDailyBatchSource[]` with stall/occupancy identities, occupant, stall number, `EffectiveCharge`, `CanCollect`, nullable `ReasonCode`.
- `EligibleCount`, `AlreadyCollectedCount`, `CanCollectAll` (`EligibleCount > 0`), `EligibleTotal`.

`AlreadyCollected` identifies paid days; other blockers retain their existing machine codes (`SourceStillLegacy`, `PolicyNotEffective`, `InvalidSource`, `AlreadySettledOrUnavailable`, `RateNotEffective`). Existing `/sources` array is preserved for compatibility. `IMobileApiClient.GetNpmDailyBatchReadinessAsync` is available through HttpClients and Mobile.Core.

Four paid stalls plus one pending stall means `CanCollectAll=true`. Send only checked eligible stalls to existing `/quote` and `/record`. The server revalidates the selected set and fingerprint. Do not submit previously paid rows, infer a monthly price, or create absence records for unchecked stalls. Discovery internally handles more than 500 stalls in bounded chunks; a posting batch still has its existing maximum of 500.

## Terminal optional Name

Existing `SourceNativeCollectionRequest.PayerSnapshot` and session `PayerSnapshot` accept an optional trimmed name/reference for Terminal, including by-vehicle entry. Maximum 200 characters; `InvalidPayerSnapshot` rejects longer input. This is a frozen Collection snapshot, never a transporter registration, Business Payor, or name-based link.

`SourceNativeActivityDto.PayerSnapshot`, `VehicleClassName`, `VehicleClassId`, `Section`, and `CashTicketCount` retain the posted facts. Count remains supporting evidence only. The recent query below includes this same native context. No Terminal rate/calculation policy was changed.

## Collector recent activity and correction

All routes require authenticated active `Collector`; tenant and collector IDs come from authentication. These are online review/post contracts, with stable retry IDs for unknown responses. They do not introduce a new offline correction queue.

| Verb / route | Request | Response / typed client |
|---|---|---|
| GET `/api/mobile/collections/recent` | None; current server date, current collector | `MobileRecentCollections`; `GetRecentCollectionsAsync()` |
| POST `/api/mobile/collections/edit/quote` | `EditMobileCollectionIntent` | Existing `CollectionSessionQuote`; `QuoteCollectionEditAsync()` |
| POST `/api/mobile/collections/edit` | `RecordMobileCollectionEditRequest(Intent, QuoteFingerprint)` | `MobileCollectionCorrectionResult`; `EditCollectionAsync()` |
| POST `/api/mobile/collections/remove` | `RemoveMobileCollectionRequest` | `MobileCollectionCorrectionResult`; `RemoveCollectionAsync()` |

`MobileRecentCollections` has `BusinessDate`, newest-first `Rows` (maximum 100), and `Truncated`. Each `MobileRecentCollection` contains:

- `Collection`: the existing canonical `CollectionActivityEventDto`, with CollectionId, SRC (`ReferenceCode`), date/time, payer snapshot, collector, friendly source/classification and line context, original/net amount, instrument, disposition and corrections.
- `CanEdit`, `CanRemove`, nullable `BlockReasonCode`, nullable friendly `BlockReason`.
- nullable `SourceNativeContext`: existing frozen `SourceNativeActivityDto` for Terminal/vendor/weighing details.
- nullable `EditSource`: `MobileCollectionEditSource(SourceIdentity?, Kind, OperationCode, Identity, Native?, Slaughter?, PayerSnapshot?, Reference?)`. This resolves the original's recorded source/allocations into stable occupancy/account/vendor and choice identities in bounded tenant-scoped lookups. Use these typed facts to initialize the owning form; do not reconstruct ownership from a display name. Utility/rent source versions are current display/preflight facts, still revalidated on quote/post. Native/slaughter input facts are frozen snapshots, not rates to trust on-device.

Legacy records are excluded. Reversed originals remain traceable rows with zero effective amount and blocked actions. Multi-boundary records may be removable but have `CanEdit=false` / `MultipleSourceBoundaries`; one-item replacement must never discard extra original financial boundaries.

Tabo self-correction is deliberately unavailable (`SourceCorrectionUnavailable`): its vendor/day duplicate protection is not reversal-aware, and it has no safe itemized replacement adapter. Its established standalone/batch posting path is unchanged. Missing/malformed edit metadata blocks that row's Edit capability without hiding unrelated rows. Recent locks and source addresses are loaded in bounded batches, rather than re-reading the municipality for each row.

Reasons: `MobileCorrectionReason` is `EnteredByMistake=1`, `WrongPayerOrSource=2`, `WrongAmount=3`, `Duplicate=4`, `Other=5`. `MobileCorrectionReasonIntent(Code, Note?)`: note is trimmed, at most 300 characters; Other requires a note. Reason codes do not calculate money.

Remove request: `ClientOperationId`, original `CollectionId`, `Reason`. No amount or physical serial is accepted. Result: `CorrectionId`, `OriginalCollectionId`, `OriginalSRC`, nullable `Replacement`, `ExistingOutcome`.

Edit intent: `ClientOperationId`, original `CollectionId`, `Reason`, `Replacement` (one-item existing `CollectionSessionIntent`). Replacement's `ClientCollectionSessionId` **must equal** the correction `ClientOperationId`, and its business date must be the current server date. Keep correction ID, replacement item ID and intent stable across quote, confirm and retry. Use a fresh identity for a changed proposed correction. Replacement returns the standard child CollectionId/new SRC, amount, instrument and disposition.

Edit preview temporarily releases the original settlement inside a Serializable transaction, quotes through the existing source dispatcher, then rolls back. It allocates no SRC and commits no correction. Record repeats the reversal, re-quotes and compares the fingerprint, posts through the owning writer, and links the new Collection to the reversal, all in one Serializable transaction. A failure rolls back both reversal and replacement. Original Collection, lines, amount, SRC and audit history are never overwritten/deleted. Daily rows are restored through the established canonical void projection; obligation/rent/utility balances use negative allocation effects.

Direct sources may change permitted amount/snapshot/context facts. Rate/obligation replacements use the same source-specific intent and quote rules as normal posting; arbitrary NPM daily or weighing amounts are refused. Replacement cannot cross financial source-kind/allocation-kind/classification boundaries; Terminal may change between its explicitly approved sections. A multi-source original cannot be edited through a single-item replacement.

Relevant correction codes (consume codes, not messages):

| Code | Meaning |
|---|---|
| `DifferentCollector` | Original is owned by another collector |
| `OutsideCurrentBusinessDate` | Original is outside current server day |
| `Remitted` | Active canonical remittance coverage locks it |
| `AlreadyCorrected` | Original has a later correction |
| `LaterSettlementExists` | Later month-end adjustment depends on original NPM installment |
| `SourceCorrectionUnavailable` | Source has no safe supported correction representation |
| `MultipleSourceBoundaries` | Original cannot be replaced by one item |
| `CollectionNotFound` | No original in authenticated tenant |
| `InvalidReason` / `InvalidClientOperationId` / `InvalidReplacementIntent` | Fix request facts |
| `QuoteStale` | Refresh/review replacement; original remains intact |
| `ReplacementSourceMismatch` | Replacement crossed the original financial boundary |
| `CorrectionIntentConflict` | Operation/child identity is already bound to different activity |
| `ConcurrentCorrection` | Retry the same ID; a concurrent change may already have won |

Successful replay is checked before today's eligibility is reconsidered and returns the saved correction/new SRC. Changed intent conflicts. Correction results are stored in the existing tenant-scoped PostingOperation ledger; no new money authority or schema is introduced. Remittance void deactivates coverage and restores ordinary current-day correction eligibility; it creates no Collection/SRC and changes no income. This does not grant Head/Admin Mobile self-correction authorization or bypass office correction paths.

## Fish / Meat registry import and summary

| Verb / route | Contract | Authorization |
|---|---|---|
| POST `/api/office-sources/fish-meat/import/preview` | `VendorRegistryImportRequest` → `VendorRegistryImportPreview` | Head (`SuperAdmin`) |
| POST `/api/office-sources/fish-meat/import/save` | Same request → `VendorRegistryImportResult` | Head (`SuperAdmin`) |
| GET `/api/office-sources/fish-meat/summary?year=` | `VendorRegistrySummary` | Existing Head/Admin/assigned Collector registry access |

`VendorRegistryImportRequest.Rows` (1–500) contains `VendorRegistryImportRow(RowNumber, Registration, ConfirmSeparateRegistration=false)`. Registration is the existing `RegisterFishMeatVendorRequest`: stable `ClientOperationId`, `TaxYear` (2000–2200), `VendorType` (`Fish=1`, `Meat=2`), `RegistrationKind` (`New=1`, `Renew=2`), required `DisplayName` (max 200), optional `BusinessName` (200), `Address` (300), `Reference` (200). Each row number is positive and unique, each operation ID unique in the import. Blank optional fields normalize to null. No columns for money are accepted.

Preview returns rows with normalized `Registration`, `State`, structured `Problems[{Code, Message}]`, `PossibleDuplicateIds`. `VendorRegistryImportState`: `New=1`, `PossibleDuplicateRequiresReview=2`, `Invalid=3`; `CanSave` is true only when every row is New. Codes: `InvalidImport`, `InvalidRegistration`, `DuplicateRowIdentity`, `RegistrationIntentConflict`, `PossibleDuplicate`. A same-name/same-year record or another row triggers review only; it never links, merges or overwrites. Explicit `ConfirmSeparateRegistration=true` permits a distinct registration. An exact same-operation replay is safe without duplicate confirmation.

Preview persists/reserves nothing. Save revalidates tenant, facts, duplicates and confirmation inside one Serializable transaction; all rows must be ready or the entire request returns `ImportNeedsReview`. Each created row returns its actual registration ID via `VendorRegistryImportSavedRow(RowNumber, Registration)`. Exact retries return those IDs. Reused operation IDs with different details conflict; `ConcurrentImport` requests a same-ID retry after concurrent registry changes. No Collection, SRC, monthly balance or financial totals are imported.

HttpClients provides `IOfficeSourcesApiClient.PreviewImportAsync`, `SaveImportAsync`, `RegistrySummaryAsync`. Frontend may parse upload/template/manual data into these rows; it must not invent backend CSV money import semantics.

`VendorRegistrySummary` contains selected `TaxYear`, server `CurrentTaxYear`, server current-month `ActivityFrom`/`ActivityTo`, `TotalRegistered`, `FishCount`, `MeatCount`, `Registrations`, and `CurrentMonthCollections` (canonical Vendor Fee and Weight & Measure activity). These are registry/transaction facts, not space/facility metrics. No names are turned into identities.

## Reports and operation metadata

No replacement performance classifications or report DTOs were added. Existing official Monthly Income and RevenueSourcePerformance share report governance actuals/targets. Approved groups remain `MARKET`, `RENT`, `SPACE`, `TERMINAL`, `SLAUGHTERHOUSE` (plus truthful `PENDING` for unresolved facts). Terminal row keys remain `TERMINAL_COMFORT_ROOM`, `TERMINAL_PULL_PUL_VANS_CARGO_VANS`, `TERMINAL_TRICYCAD`; BBQ is `RENT_BBQ` in `RENT`; Slaughterhouse is standalone. Official sections remain A/B/C and overall total. Contribution sums leaf rows once, not leaf rows plus group subtotals. Targets/signatories continue using the established tenant-scoped contracts in the prior handoff; Overview does not gain target mutation authority. Operations metadata already supports Terminal as a distinct normal catalog operation/family.

No schema migration, historical reclassification, technical namespace change, UI redesign, deployment or production-data edit is part of this pass.

## Verification

Validation results are recorded in the final backend checkpoint. PostgreSQL tests cover NPM discovery and 4-of-5 readiness, canonical writer replay, correction negative ledger and replacement SRCs, source balance restoration, remittance locks/void restoration, registered weighing recalculation, walk-up correction, rollback, concurrent removals, registry import without Collections, and Terminal official/performance roll-ups. Unit, build, full integration and EF results must be reported separately; skips are not passes.

Final validation: focused PostgreSQL suite 102 passed / 0 failed / 0 skipped; full integration 349 passed / 0 failed / 7 skipped (356 total); unit suite 2,514 passed / 0 failed / 0 skipped. The seven integration skips require a restored production-snapshot database (`STALLTRACK_SNAPSHOT_DB`); Testcontainers financial tests ran against throwaway PostgreSQL. API, HttpClients and Mobile.Core Release builds passed. EF reported no pending model changes; no migration was added. `git diff --check` passed. Existing repository compiler/analyzer warnings remain; Android and frontend component/visual validation were not run because this pass changes no UI.

Defect sensitivity was verified by temporarily suppressing NPM Daily discovery: the focused source-native test failed at the missing Daily choice. Restoring the exact fix returned that test to 1 passed / 0 failed / 0 skipped. The temporary defect was not committed.
