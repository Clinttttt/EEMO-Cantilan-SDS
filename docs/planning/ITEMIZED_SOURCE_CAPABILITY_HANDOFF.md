# Itemized source capability handoff

Local backend follow-up from `integration/v3-payor-space` at `b0db26e2`, on
`codex/itemized-source-capabilities-final`. Not deployed. No UI, financial rule, migration or queue-intent change.
IA-063/IA-064 and the [full-stack baseline](ITEMIZED_COLLECTION_FULL_STACK_FOLLOWUP.md) remain authoritative.

## Discovery and selection

`GET /api/mobile/collection-session/eligible?payorId={PayorId}` retains all existing source lists and adds
`Operations[].Choices`, `EligibleChoiceCount`, and `CanAutoSelect`. Use `CanAdd` first. A supported but unavailable
operation has no choices and a structured `ReasonCode`; unsupported adapters remain `NotSupportedYet`.
If `CanAutoSelect` is true, use the sole choice. Otherwise render the returned rows. Count choices within the selected
operation; do not count unrelated sources or match display names. Vegetable/Fruit modes and weighing types are distinct
choices, even when their underlying occupancy is the same.

Each `CollectionSessionSourceChoice` carries:

- `SelectionKey`: opaque stable selection key, not a receipt or posting identity.
- `Kind` and `OperationCode`: machine identities; `DisplayName` and `Context`: display text.
- `Identity`: typed facts needed to build the existing item intent (see table).
- `Instrument`: policy metadata, never a serial.
- `AmountRule`, `CanEnterAmount`, nullable `ServerAmount` and `MaximumAmount`.
- Weighing: `Rate`, `RateId`, `RateEffectiveDate` from persisted effective rate evidence. Governed fee-option choices
  also carry `RateId` / `RateEffectiveDate`; see the local [canonical authority and Landing/Berthing follow-up](CANONICAL_AUTHORITY_LANDING_FEES_HANDOFF.md).
- `RequiredInputs`: source-specific remaining inputs, not a generic arbitrary form schema.

`CollectionSessionAmountRule`: DirectAmount=1, PreparedBalance=2, FixedAmount=3, QuantityRate=4,
MonthlyRemaining=5. PreparedBalance allows a partial amount up to the authoritative remaining balance; it does not
permit changing the assessment. A null direct ServerAmount means no fixed assessment, not zero due.
Choices are display/selection facts, not reviewed quotes. Posting always revalidates policy, payer, amount and authority.

## Supported-source frontend contract

All rows are scoped to the authenticated tenant/collector and selected Business Payor, where required.

| Source / Kind | Selection identity (`Identity`) | Display/context | Amount rule / server fact | Required input → existing intent | Structured quote problems to handle |
| --- | --- | --- | --- | --- | --- |
| WCF / Water | StallId, UtilityBillId (nullable), SourceVersion, Year, Month | Payer; section/stall | DirectAmount or PreparedBalance; remaining and ceiling when prepared | AmountReceived → ConfirmedAmount + SessionWaterIntent | RequiresPayor, PayerMismatch, BalanceChanged, SourceStillLegacy, InvalidPeriod |
| ECF / Electricity | StallId, UtilityBillId (empty for provisional direct), SourceVersion, Year, Month | Payer; facility/stall | DirectAmount or PreparedBalance; prepared remaining | AmountReceived → ConfirmedAmount + SessionElectricityIntent | RequiresPayor, PayerMismatch, BalanceChanged, SourceStillLegacy, InvalidSource |
| Landing/Berthing / GovernedService | Mode (null normally) | Operation/context | FixedAmount or configured DirectAmount; fixed value/ceiling | Fixed or received amount; Reference if required → SessionGovernedIntent | SourceNotAvailable, AmountChanged, AmountAboveCeiling, InvalidIntent |
| Market Fees / GovernedService | FeeOptionId when configured, Mode | Configured fee name/location | Each option's fixed/direct rule and ceiling | AmountReceived only when permitted; Reference if required → SessionGovernedIntent | InvalidSource, AmountChanged, AmountAboveCeiling |
| Transportation / GovernedService | VehicleClassCode | Vehicle class / operation | FixedAmount; approved vehicle-class amount | No editable rate; Reference if required → SessionGovernedIntent | InvalidSource, AmountChanged, SourceNotAvailable |
| Transfer Large Cattle / GovernedService | FeeOptionId when configured, Mode | Approved option/context | All configured fixed/direct options or configured base rule | Received amount only when permitted; Reference if required → SessionGovernedIntent | InvalidSource, AmountChanged, AmountAboveCeiling |
| Vegetable/Fruit / GovernedService | Mode: WholePayment or DailyTransaction | Operation; friendly mode label | Configured DirectAmount and contextual OR/CT | AmountReceived; Reference if required → SessionGovernedIntent | InvalidIntent, SourceNotAvailable, AmountAboveCeiling |
| Fish/Meat Vendor Fee / VendorFee | StallId, OccupancyId | Linked vendor; section/stall | DirectAmount, OR; no monthly assessment | AmountReceived → ConfirmedAmount + SessionVendorFeeIntent | PayerMismatch, InvalidSource, SourceNotAvailable |
| Weight & Measure / Weighing | StallId, OccupancyId, WeighingType; RateId evidence separate | Vendor; NPM/stall/type | QuantityRate, OR; rate per kg; quote calculates amount | Kilograms → SessionWeighingIntent; ConfirmedAmount may be 0 for initial quote | SourceNotAvailable, AmountChanged |
| NPM Whole Payment / NpmWholePayment | StallId, OccupancyId, Year, Month | Payer; NPM/stall | MonthlyRemaining; existing monthly quote's remaining and instrument | SessionNpmWholeIntent; ConfirmedAmount may be 0 for initial quote | PayerMismatch, InvalidIntent, SourceNotAvailable, AmountChanged |
| Slaughterhouse / Slaughter | Animal, CustomAnimalName for existing custom definitions | Approved animal / transaction | QuantityRate; existing writer calculates amount; instrument from its policy | Heads, OwnerName only if anonymous → SessionSlaughterIntent; initial ConfirmedAmount may be 0 | SourceNotAvailable, AmountChanged |

Do not replace configured options with labels as financial identity. CustomAnimalName is the existing Slaughterhouse
writer's approved-definition selector, not a new free-text animal or registry. No new rate calculation exists in this
contract. For Slaughterhouse, obtain the transaction amount through quote; no universal per-head formula is advertised.

`NpmWholeSources` additionally supplies PayorId, OccupancyId, Year/Month, RemainingAmount, MonthlyObligation,
CollectedAmount, Credits and Instrument. It uses `NpmWholePaymentWorkflow.QuoteAsync`; unavailable/fully settled
months are not selectable. Discovery defaults to the server business month. The existing quote accepts another
permitted Year/Month and checks its authoritative occupancy; do not infer period ownership from the current occupant.

Vendor Fee source lists preserve typed `Status`, `ReasonCode`, `RequiredAction`, `OccupancyId`,
`HasExistingPayorLink`. Unlinked occupants require explicit office linkage; no payer is manufactured from names.
Weighing and Vendor Fee never settle rent or each other.

## Quote, record and queue

Routes and request shapes are unchanged: POST `quote` with CollectionSessionIntent, POST `record` with
RecordCollectionSessionRequest, GET `{clientSessionId}` for reconcile. Quotes supply normalized item Kind/OperationCode,
DisplayName/Context, Amount, Instrument, GroupId, SourceVersion, instrument totals, GrandTotal, QuoteFingerprint and
typed Problems. SourceVersion is opaque stale-detection evidence, not UI copy or a string for the frontend to parse.
Do not sum display rows as financial authority; use server totals. Retain stable ClientItemId and client session ID.
Use item problem ClientItemId and Code, not English Message parsing. Record may return QuoteStale, SourceChanged,
DuplicateBusinessEvent, InvalidPayor or SessionIntentConflict in addition to item problems.

One item still produces one source-authoritative Collection/SRC. All returned results must be shown; no basket receipt
or generic income classification is created. Queue only reviewed intent and QuoteFingerprint through the existing
durable ItemizedCollectionSession contract. Unquoted offline drafts need connection to review. NeedsReview is not a
recorded SRC and must not be retried indefinitely without review. Unknown-response retry returns the recorded outcome.

## Verified baseline gaps and fixes

The starting branch had a Slaughterhouse quote/post adapter but discovery looked for a non-facility operation
capability that never contains SLH. Discovery now consumes the existing facility-assigned canonical Today's Work
menu entry and filters approved animal options through the existing read-only writer quote. Assignment semantics
were not expanded. Thus the earlier full-stack support list overstated discoverability, not posting support.

The starting branch could not post two initial direct utility items for the same absent stall/month bill: whichever
child created it first made the other child's provisional identity stale. The dispatcher now recognizes a bill only
through the other utility child's deterministic Collection posting identity in the same active Serializable transaction.
It maps the provisional version to that newly materialized part's initial version and retains the writer's direct-mode
path. An unrelated newly created bill is never substituted. Existing bill versions remain unchanged and stale checks
still apply. Source-specific writers, quote rules, financial boundaries and session fingerprints are unchanged.

Tabo, NPM Daily, Kanmanggay, Fiesta/Araw and generic monthly facility rent remain deferred as stated in the baseline.
No fallback writer, new assignment rule, classification, schema or source activation was introduced.

Payer browse remains empty/one-character/partial/case-insensitive, deterministic, maximum 50, tenant-scoped.
Ana Reyes remains absent until explicitly associated with a Business Payor; raw occupant text is not searched as identity.

## Validation

Validation passed: 2,511 unit tests; 155 focused PostgreSQL tests; and a final 23-test CollectionSession rerun after
the structured unavailable-policy metadata check. API Release and Mobile.Core Release build successfully. API retains
three existing compiler/analyzer warnings. EF reports no pending model changes; `git diff --check` passes.
Regressions exercise all eleven supported operations through real discovery,
typed choice selection, quote/post and replay; a six-source checkout covers rent, Vendor Fee, weighing, WCF, ECF and
Landing/Berthing in both utility orders. Its injected failure rolls back all money, and reporting/remittance checks
keep classifications separate and restore eligibility after remittance void without new Collection/SRC or income changes.
The initial regression run failed under the starting dispatcher behavior before the two source seams were corrected.
No frontend files, production data, Android build, APK, version, push, merge or deployment are part of this work.
