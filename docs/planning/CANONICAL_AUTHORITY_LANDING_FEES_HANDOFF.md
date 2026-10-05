# Canonical authority and Landing/Berthing fee definitions

Backend-only follow-up from `8e2c0dcc`, on `codex/canonical-authority-landing-fees`.
Local validation only; this does not change the verified production release. No UI or migration change.

## Canonical collection contract

Canonical collectors submit stable client intent IDs and source-specific facts, not physical OR/CT serials.
Quotes/discovery have no SRC. Only a successful server posting returns the persisted Collection ID and SRC.
Instrument is resolved from the approved policy, not selected by the collector. SRC is not a physical form serial.

| Current path | Authority / result |
|---|---|
| TCC/NCC/BBQ/ICE `PostMobileRentAsync(MobileRentPostRequest)` | Canonical PaymentRecord only; approved occupancy rent and remaining balance; explicit payer/occupancy; OR. Returns `EcfPostOutcomeDto` with CollectionId/ReferenceCode. |
| NPM Daily | `NpmDailyCanonicalPoster` after business-date cutover; handler discards typed OR on the canonical branch; sync acknowledgement returns SRC. |
| NPM Whole Payment | `NpmWholePaymentWorkflow` monthly quote/settlement; no serial; server monthly basis and balance unchanged. |
| Slaughterhouse | `FeeScheduleCollectionWorkflow` with `FeeSchedulePostRequest`; existing SlaughterTransaction calculation, approved animal/head/rate rules, per-transaction posting; OR. |
| Governed services | `GovernedServiceWorkflow.PostMobileAsync`; configured classification/instrument/rule; optional obsolete AccountableDocumentId/DocumentNumber/IssuedAtUtc are ignored. |
| Office monthly space obligations | Existing composer and obligation allocation; SRC, no mandatory physical serial. |

Older `/api/Mobile/monthly/collections/record`, NPM settle-days/settle-month, slaughter record/update,
and utility compatibility routes retain older request/Boolean response shapes. They are not substitutes for current
canonical SRC contracts. Monthly rows without approved cutover remain legacy-authoritative and are refused by the
canonical rent writer. NPM business-date authority, legacy receipt evidence and specialized operational readers remain
intact. Transportation service collections and legacy terminal-trip records are distinct paths.

Rent, direct Fish/Meat Vendor Fee and Weight & Measure remain separate classifications and effects.
Remittance and remittance void only change remitted/unremitted position; neither creates revenue or SRC.

## Landing/Berthing model and office setup

Reuse `GovernedServiceFeeOption` / immutable `GovernedServiceFeeOptionRate` under the existing governed
Landing/Berthing service. This is the same abstraction used for Market Fees and Transfer Large Cattle.
No additional revenue classification or table exists. The current official Landing/Berthing income row and CT policy
remain unchanged. Tenant ownership and restrictive history relationships are retained.

To use selectable fee types, Head explicitly configures `ApprovedFeeOption` and adds approved definitions, initially
Landing (`LANDING`) and Berthing (`BERTHING`), through the management API. Each receives its own server ID.
There is no automatic rate seeding or conversion of existing fixed/direct configuration. Office supplies either a
positive fixed approved amount or a permitted direct-amount rule with the existing optional ceiling. Missing fixed
amount is rejected. Test fixture amounts are not ordinance rates. Future definitions use the same API.

## Typed frontend contract

Existing authenticated `IGovernedServicesApiClient` is the shared client; no extra writer is required.

| Endpoint / method | Contract |
|---|---|
| GET `api/governed-services` | Existing definitions and AllowedBases; Landing/Berthing now allows ApprovedFeeOption. |
| PUT `api/governed-services/{operationCode}` | `ConfigureGovernedServiceRequest`, Head only; append effective service configuration. |
| GET `.../{operationCode}/fee-options` | All definitions including retirement/history; existing client method unchanged. |
| GET `.../{operationCode}/fee-options?activeOnly=true` | Offered definitions with a current rule; disabled service can still have active definitions, so inspect CanCollect. Client overload `(operationCode, activeOnly)`. |
| POST `.../{operationCode}/fee-options` | `AddFeeOptionRequest`: DisplayName, optional Code/Location/Description, EffectiveDate, Basis, FixedAmount/MaximumAmount. Head only. |
| POST `.../{operationCode}/fee-options/{id}/rates` | `ScheduleFeeOptionRateRequest`; append effective rule. |
| POST `.../{operationCode}/fee-options/{id}/retire` | `RetireFeeOptionRequest`; no deletion. |
| GET `.../{operationCode}/terms` | Collector-only assigned/enabled terms; only currently collectable fee options, frozen on posting. |
| GET `.../{operationCode}/activity` / `fee-option-totals` | Existing historical activity and option drill-down; no presentation row cap. |

`GovernedServiceFeeOptionDto` now adds `Availability`, `CanCollect`, `ReasonCode`, `RateId`, `Instrument`.
`Availability`: Active=1, Scheduled=2, Retiring=3, Retired=4, NeedsApprovedRule=5.
`ReasonCode`: FeeTypeRetired, FeeTypeRuleNotEffective, FeeTypeModeNotActive, SourceNotAvailable,
InstrumentPolicyNotEffective; null when usable. Existing Status text remains for backwards compatibility, not logic.
Each `History` version also has `RateId`. Retiring means a future retirement date; already posted history remains visible.

`FeeOptionTermDto`: Id (stable selection identity), Name, Location, Basis, Amount, MaximumAmount,
plus Code, RateId, EffectiveDate. FixedAmount needs no arbitrary amount entry; DirectApprovedAmount needs AmountReceived.
Posting always resolves the rule again using business date; display metadata is not financial authority.

Standalone queue sends `GovernedServicePostRequest.FeeOptionId` plus the existing operation, business date,
client operation ID, amount and required facts. No physical serial fields are needed.
`GovernedServiceOutcomeDto` returns CollectionId, ReferenceCode, Amount, Instrument, ReturnedExistingOutcome,
and the frozen FeeOptionId/FeeOptionName/FeeOptionRateId. Replays retain original facts even after retirement/rate change.
Reusing a current canonical operation ID with another fee choice conflicts.
Historical document-bound replay now compares the complete original normalized intent after removing only
accountableDocumentId/documentNumber/issuedAtUtc. Previously it compared only the amount after a fingerprint mismatch.
The regression failed with Conflict expected / Ok actual before the fix. Source, choice, payer, date, mode and money
cannot be changed under the old ID. Historical intent rows and Collections are not rewritten.

## Itemized integration

`GET api/mobile/collection-session/eligible` continues to return `Operations[].Choices`, EligibleChoiceCount,
CanAutoSelect. Fee choices carry stable Identity.FeeOptionId, display/context, Instrument, AmountRule,
ServerAmount/MaximumAmount, RequiredInputs, and now RateId/RateEffectiveDate.
Auto-select only when CanAutoSelect; otherwise render choices. Never identify a fee by name or code alone.
Item intent uses `Service.FeeOptionId`; existing session quote/record endpoints, quote fingerprint and stale review remain.
The session calls the same governed writer, preserves V1 one item per Collection/SRC, and returns every child result.
OR/CT items remain separate. No generic fallback writer or new basket classification is introduced.

## Preserved contracts

Transportation vehicle-class identities, effective-rate history and retired-state rules are unchanged.
Space/payor Add New/import Preview/Save, blank server numbering, tenant/event/date scopes and concurrent allocation
remain as documented in [space/payor handoff](SPACE_PAYOR_DOMAIN_BACKEND_HANDOFF.md).
No frontend file, physical form, historical source, accounting amount or production record was edited.

## Regression evidence

The new Landing/Berthing PostgreSQL test was run with the old catalog allowance restored and failed at configuration
with “This service does not support the chosen amount basis.” Restoring ApprovedFeeOption enables the same existing writer.
Coverage includes stable distinct types, missing-rule refusal, effective rule versions, selected-type snapshots,
retirement/history, standalone and mixed-instrument itemized posting/replay, changed-choice conflict,
register/activity/income exactly once, remittance and remittance void with unchanged income.
Rent posting and reporting tests now cover TCC/NCC/BBQ/ICE rather than only TCC. NPM, Slaughterhouse,
governed definitions, utilities, session, remittance and space regressions are run separately from unit validation.

Validation completed: 160 focused PostgreSQL tests passed (no skips), followed by 40 affected session/definition/
governed tests after the final replay and rate-version assertions (no skips). All 2,511 unit tests passed.
API, HttpClients and Mobile.Core Release builds passed. EF reports no pending model changes; `git diff --check` passed.
No Android build, migration, deployment, production mutation or APK publication was performed.
