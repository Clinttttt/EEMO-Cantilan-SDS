# Task 2B: specialized NPM monthly settlement architecture

**Status: proposed architecture for owner review; NOT implemented or operationally approved.**

## 1. Checkpoint, scope and authority

Primary checkout: `C:\dev\stalltrack\eemo`; branch `codex/local-unified-v8-v10-20261013`; verified HEAD `17b9d100`. Task 2A's three uncommitted paths were present at entry and are preserved without edits:

- `EEMOCantilanSDS.Testing/Application/Payments/NpmMonthlyBillingCharacterizationTests.cs`
- `EEMOCantilanSDS.Testing/Application/Payments/NpmExactMonthlySettlementAcceptanceTests.cs`
- [Task 2A design and appended owner clarification](NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md)

Platform inspected read-only: `C:\dev\stalltrack\platform`, branch `feat/onboarding-tenant-operations-v2`, HEAD `665cc5a`, clean at entry. Worktree inventory inspected; none created or changed. No production financial file, API, configuration or database was changed. No second API/database, Testcontainers provisioning, migration generation/application, cash posting, push or deployment.

Read with the [Task 1 audit](NPM_MONTHLY_BILLING_STANDARD_REVIEW_20261016.md), current release/coordination notes, root AGENTS, documentation authority map, business rulebook/rules/revenue architecture, architecture/application patterns and decision registry. This document is a scoped planning recommendation, not an accepted ADR or a wholesale replacement of those sources.

**Confirmed future requirement:** for Cantilan's stated PHP 900 monthly stall rent / PHP 30 installments, thirty valid installments exhaust that month's rent. Day 31 cannot create another PHP 30 stall-rent debt or collection. Legitimate credits reduce liability; separate weighing, utilities, fines and other approved fees retain their own authority. Historical excess cash remains factual and needs separately authorized reconciliation, never automatic deletion/refund/reclassification. Current instructions and the appended owner clarification supersede the older extra-day assumption for this case. Deployment still needs office evidence and release approval.

**Other future direction:** permanent public-market rent across consulted LGUs is monthly; collection cadence is separate. PHP 1,900 monthly with no daily rate must eventually be supported without synthetic days. The precise monthly-only partial-month/credit/collection permissions are not yet approved. PureDays history remains supported; no historical conversion is authorized.

## 2. Recommendation: Option C, one specialized NPM month source

Use a **source-owned NPM rent-month settlement anchor**, with the existing specialized NPM assessment/credit rules and existing canonical Collections, allocations, corrections, posting coordinator and remittance. Daily installments and Whole/monthly payments become two ways of paying **the same month**, not two engines or two assessments. Keep DailyCollection for truthful existing daily activity/evidence and historical compatibility; stop treating each unpaid day as an independently collectible rent receivable after monthly liability is exhausted.

Option A is technically capable of recording PHP 25/20 against a genuine PHP 35 day. It is not the primary design: it needs almost the same cross-path month-cap coordination as C, still leaves partly paid daily evidence after month completion, and cannot serve a daily-rate-free municipality without another representation. C provides the common identity/lock and exact allocation target the longer-term requirement needs. Do not ship A as an interim engine alongside C.

Option C is an **architecture change requiring subsequent approval**, not a schema-free two-line F-01 correction. The smallest recommended persistence is one specialized month-anchor table plus typed allocation identity support, reusing existing event tables. No generic receivables engine or duplicated cash ledger is proposed. The need follows from the existing source inventory below, not from assuming every new feature needs a table.

## 3. Current behavior and F-01

`NpmMonthSettlementService.ResolveMonthAsync` computes the correct monthly obligation, credits and remaining amount (`:310–318`), using `StallOccupancy.AnsweringForMonth`, month windows and approved effective rates. `ComputeWholeMonthPayableAsync:102` and `SettleCoreAsync:163` stop before the next **complete** daily fee exceeds the remaining ceiling. The planner in `.PartPayment.cs:44–49` also excludes the final full daily balance before considering a fractional allocation. Result: an exact PHP 25/20 monthly balance can become impossible to collect through either planner.

The Whole workflow deliberately verifies allocation sum equals received amount (`NpmWholePaymentWorkflow:72`). Changing only the quote breaks that invariant. `NpmDailyCanonicalPoster:137` allocates only to real DailyCollection IDs/DailyFee parts; its partial and adjustment branches are distinct. `MonthEndAdjustment` currently means a calendar/eligible-installment shortfall, such as February PHP 840 + PHP 60. It is not permission to disguise rounding residues or invent an extra full day.

**Additional cap defect verified:** `GetPayableDaysAsync:84–89` returns uncapped uncollected days; daily batch uses that list. `RecordDailyCollectionCommandHandler` does not check monthly remaining rent. With thirty prior PHP 30 days, current discovery includes August 31, and the ordinary legacy handler accepts PHP 30, bringing actual mock receipts to PHP 930. New owner policy rejects this behavior. Whole Payment returning zero does not protect the daily route.

**Option A counterexample:** PHP 875 + a real partial PHP 25 = monthly PHP 900, but the PHP 35 day remains partial with PHP 10 remaining. The PHP 1,000 counterpart leaves PHP 15. `StallRepository.Arrears:127–130` separately sums those residuals; `NpmOperatingScheduleRepository:48` likewise reports partial-day remaining amounts in day impact. The monthly ledger can correctly read zero while operational readers still ask for money. Reversal of the partial correctly restores the original PHP 35 base and the true PHP 25/20 monthly balance; it does not cure inconsistent collection eligibility.

## 4. Dependency map: daily state, monthly state and cash

Paths below are repository-relative. Function names are search anchors; line references describe this checkpoint. All must be reviewed when implementing the approved architecture, even when a consumer needs only a contract adaptation.

| Path / function | Current responsibility and input | Required boundary |
| --- | --- | --- |
| `EEMOCantilanSDS.Domain/Common/Billing/DailyBilledMonthRule.cs`; `Domain/Constants/DomainRules.cs` | Monthly assessment/credits/outstanding and separate coverage; RentGoal versus PureDays | Reuse assessment math. Do not repair F-02/F-03 or infer historical basis. |
| `Domain/Entities/Facilities/Stall.cs`, `StallOccupancy.cs:AnsweringForMonth`, `Contract.cs` | Occupancy windows, signed term end, monthly owner, stall rate overrides | Bind month to stable Contract ID and approved owner rule, never names/current holder. |
| `Application/Common/Fees/NpmDailyFee.cs`, `IFeeRateResolver.cs`; `Infrastructure/Fees/FeeRateResolver.cs` | Daily rate by tenant/section/stall/date; monthly rate resolution | Daily installment rate remains optional in future monthly-only setup; approved monthly amount cannot be invented from it. Capture actual policy/rate provenance. |
| `Application/Common/Payments/NpmMonthSettlementService.cs:ResolveMonthAsync`, `ComputePayableAsync`, `ComputePayableForDaysAsync`, `ComputeWholeMonthPayableAsync`, `GetPayableDaysAsync`, `SettleCoreAsync` | BOTH month debt and daily selection; Whole permits future days, elapsed paths do not | One common month position and exact-money plan; retain explicit as-of/accrued versus full-month scope. |
| Same file, `QuoteFishDayAsync`, `SettleFishDayAsync` | Daily base rent plus separately calculated weighing; no full monthly cap check | Cap only rent through the shared month authority; do not block/settle weighing by pretending rent remains owed. |
| `Application/Common/Payments/NpmMonthSettlementService.PartPayment.cs` | BOTH partial daily amounts and monthly ceiling | Remove terminal money dependence on a day under C; keep general partial-payment permission separate. |
| `Application/Common/Revenue/NpmWholePaymentWorkflow.cs` | Quote, hash, replay first, serializable re-quote, exact charge sum, one SRC | Use source-month version and exact allocation evidence; no relaxed assertions. |
| `Application/Common/Revenue/NpmPartPaymentWorkflow.cs`, `NpmPartPaymentOptions` | Explicit feature gate; partial-day plans and canonical posting | Do not enable general partial payments implicitly. Whole exact remainder needs its own approved capability within the same engine. |
| `Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommandHandler.cs` | DAILY writer for Head/Mobile; frozen day fee, weighing preservation, canonical or legacy money | All new rent, including direct calls, must check/consume month remaining transactionally. UI checks alone insufficient. |
| `Application/Command/DailyCollections/SettleNpmDays/SettleNpmDaysCommandHandler.cs`; `SettleNpmMonth/SettleNpmMonthCommandHandler.cs` | DAILY/BOTH; specific days and separate Head elapsed-month writer; latter counts full-paid rows and skips partials | Same position/lock as Whole; retain request scope and permission, correct partial receipt accounting. |
| `Application/Queries/DailyCollections/GetSettleableNpmDays/…Handler.cs`, `QuoteNpmAmount/…Handler.cs` | Daily eligibility and day-count amount; amount endpoint limitation F-05 documented in Task 1 | Expose exact server eligibility; do not promise identical current APIs when one route is unavailable. |
| `Application/Common/Revenue/NpmDailyBatchWorkflow.cs:PreviewCore/RecordAsync` | Today's eligible day list, amount, tenant assignments, atomic checked batch | Get true capped rent from common position; per-stall SRC remains; same-month serialization. |
| `Application/Common/Revenue/NpmDailyCanonicalPoster.cs` | CASH: daily ID allocations, intent/replay, source projections and NPM void | Reuse coordinator; introduce month allocation support explicitly, preserve old daily-source correction dispatch. |
| `Domain/Entities/Payments/DailyCollection.cs` | DAILY operational record, frozen base/adjustment, partial projection, canonical IDs, independent weighing | Keep evidence; uncollected/partial day is not separate debt once month rent is zero. No fabricated fully paid PHP 35 day. |
| `Application/Common/Payments/OnlinePaymentSettlementService.cs:SettleNpmAsync/SettleNpmFishDayAsync/SettleNpmFishDaysAsync`; `Command/OnlinePayments/Initiate/…Handler.cs` | Provider cash evidence plus day/month settlement; mismatched capture may need reconciliation | Route rent eligibility to same month engine; preserve captured cash facts and explicit reconciliation/refund authority. No provider-event duplication. |
| `Infrastructure/Repositories/Payments/PaymentRepository.Ledger.cs:GetPaymentHistoryAsync`, outstanding-month and ledger readers | BOTH: monthly rules, day-paid/partial cash, closure/occupancy windows; outstanding filter at `:313` | Include new month allocations once; do not derive collectible debt from floor quote. Preserve period/occupancy ownership. |
| `Infrastructure/Repositories/Facilities/StallRepository.Arrears.cs` | BOTH: month-payable quotes, missed-day list, separately added partial-day balances | Read exact monthly remaining and as-of collectible amount; never add raw day residual again. |
| `Infrastructure/Repositories/Facilities/StallRepository.Mobile.cs` | DAILY round, missed/paid days, amounts that can include weighing | Add server rent eligibility/month position; separate attendance, actual cash and independently collectible fees. |
| `Infrastructure/Repositories/Facilities/StallRepository.ClosedAccounts.cs` | MONTHLY obligations minus daily cash/credits per historical contract (`:228–238`) | Read unified month settlement including new allocations; closure does not transfer debt or erase cash. |
| `Infrastructure/Repositories/Payments/NpmOperatingScheduleRepository.cs:GetDayImpactAsync` | DAILY closure-preview unpaid/partial amounts | Show rent impact capped by month authority; actual attendance remains distinct. |
| `Application/Command/Payments/SetMarketClosure/`, `ClearMarketClosure/`, `RestoreNpmOperatingDay/`; daily absence commands; operating schedule paths | Change credit/chargeability evidence | Update month version/audit under the same concurrency boundary; no stale quote survives altered credits. |
| `Application/Queries/Stalls/GetStallReassignmentPreview/…Handler.cs`; `Command/Stalls/CreateStall/…Handler.cs`, historical assignment/renewal/import paths | Registration/rate/occupancy state, not permission to move old liabilities | Validate stable month ownership; block ambiguous already-settled owner changes pending reconciliation. |
| `Infrastructure/Repositories/Reports/FacilityReportsRepository.cs`, `.Revenue.cs`, `.Rates.cs`, `.Compliance.cs`, `.Breakdowns.cs`; `Infrastructure/Repositories/Auth/CollectorRepository.Mobile.cs`, `PayorRepository.cs` | BOTH/CASH: obligations, cash, coverage, collector/payor views | Consume common settlement position for balances; actual cash remains source-authority-based. Do not fix unrelated annual/coverage math here. |
| `Application/Queries/Reports/GetFinancialReport/…Handler.cs`, `GetCollectionReport/…Handler.cs`; `Queries/Facilities/GetMonthEndReport/…Handler.cs` | MONTHLY/DAILY operational reference and reports; distinct coverage definitions | Adapt input for new month source without redefining F-02/F-03. Reconcile like-for-like periods. |
| `Application/Queries/Reports/GetFollowUpQueue/FollowUpComposer.cs` and query handler | Uses debt/history summaries and attention evidence | Exact remaining month feeds follow-up, not unpaid day count alone. |
| `Domain/Entities/Revenue/Collection.cs`, `CollectionAllocation.cs`, `CollectionCorrectionAllocation.cs`; `Application/Common/Revenue/CanonicalCollectionPostingCoordinator.cs` | CASH/ALLOCATION: immutable lines, exact positive cent allocations, signed corrections, posting identity/SRC | Reuse, not a new receipt ledger. New source must pass domain/model constraints and have tenant-owned existence checks. |
| `Application/Common/Revenue/MobileCollectionCorrectionWorkflow.cs`; existing source correction handlers | Source-dispatched void/replacement; only accepted source kinds; closed remittance restrictions | Add C explicitly; retain legacy DailyCollection correction logic; correct original allocation, not a guessed current day. |
| `Application/Common/Revenue/RemittanceWorkflow.cs` | CASH: posted Collections and correction disposition, once per remittance | New anchor/assessment/opening position is never cash or remittable. Reuse current safeguards, do not recalculate remittance from month debt. |
| `Domain/Constants/CollectionSourceAuthorityMap.cs` | Authoritative legacy-versus-canonical report routing | Decide new month source canonical always; never count a daily projection again as legacy cash. |
| `Application/Common/Revenue/CollectionSessionWorkflow.cs`, `CollectionSessionChoiceProjection.cs`; discovery and quote providers | Source-owned selection, same-stall/month duplicate guard in a session, server quote and commit | Keep one NPM month conflict key across Daily/Whole/Partial; do not rely on session duplicate guard for cross-session concurrency. |
| `Api/Controllers/Collections/DailyCollectionsController.cs`; `Api/Controllers/Revenue/NpmDailyCollectionsController.cs`; `Api/Controllers/Mobile/MobileController.cs`, `NpmDailyBatchController.cs` | Authorized Head/Collector endpoints and server commands; office canonical partial/void route | Retain endpoint guards and assigned-facility checks, typed source IDs; no arbitrary tenant parameters as authorization. |
| `HttpClients/ApiClients/DailyCollectionApiClient.cs`, `MobileApiClient.cs`; `Client/Components/Shared/DailyCollectionCalendar.razor` and settlement callers | Head daily/month commands and server quote contracts | Shared server balance, no client rent division. Preserve existing unavailable-route caveat. |
| `Mobile/Components/Pages/Menus/NpmWholePayment.razor`, daily/arrears UI, `Components/Shared/CollectionSessionEditor.razor`; offline sync | Quote display, persisted operation ID/token, pending state | Server adjudicates replay/stale quote; paid month disables only rent. Pending offline money is not posted revenue. |
| Platform `apps/landing/…/onboarding-workspace.ts/.html`, `apps/admin/src/app/core/activation.mapper.ts`; EEMO `ActivateMunicipality` validators/handler | Setup needs daily rate today; current basis/amount activation contracts | Future arrangement-specific completeness and real monthly effective date; no Platform change in Task 2B. |

Prefix abbreviations in this table: `Domain/` = `EEMOCantilanSDS.Domain/`; `Application/` = `EEMOCantilanSDS.Application/`; `Infrastructure/`, `Api/`, `HttpClients/`, `Client/`, `Mobile/` use their corresponding `EEMOCantilanSDS.*` roots. Ellipsis handler paths use the named directory's `{UseCase}QueryHandler.cs` or `{UseCase}CommandHandler.cs`. The exact proposed modification paths appear in section 11.

## 5. Options and existing-entity compatibility proof

| Candidate | F-01 and cap | Monthly-only PHP 1,900 | Compatibility cost / decision |
| --- | --- | --- | --- |
| A: real-day terminal partial | Records real PHP 25/20 but leaves PHP 10/15 daily residual; needs all writers/readers to defer to month. Exact reverse supported at daily projection level. | No: requires a meaningful real daily rate/day. Mock low-level monthly-only service creates 31 zero-base daily rows and attaches PHP 1,900 as adjustment. This is unsupported, not a workaround. | No immediate migration presumed, but nearly same cross-path cap work and gated partial semantics; another redesign for monthly-only. Not recommended as primary. |
| C: explicit NPM month allocation | Exact month remainder, common lock/eligibility; no artificial final day necessary | Yes at architecture level, using one approved month source and canonical allocation; current endpoint/setup does not implement it | Requires explicit source/persistence extension and controlled compatibility bridge. Recommended single design. |
| B: distinct adjustment | Can represent a separately authorized residual only with a proper meaning/identity | Does not solve an entire ordinary monthly-only rent as an adjustment | No evidence that rounding residue is a new adjustment event. Keep contingency; do not misuse existing MonthEndAdjustment. |

**Can existing entities represent C unchanged?**

- `PaymentRecord` has a stable ID, stall/month uniqueness, BaseRentalAmount, partial/status projection and settlement-version/cutover support. Its canonical adapter sums allocations plus signed corrections. This is valuable reusable design. But `MonthlyRentalFacilities` excludes NPM, `RecordPaymentCommandHandler:28–33` explicitly rejects NPM, and generic assessment comes from contract/stall monthly rent rather than NPM basis/rate/credit rules. The entity lacks a stable responsible Contract ID and NPM basis/rate/credit provenance, and contains mixed utility/fish fields. Merely adding NPM to the generic allow-list would be a second engine and may change classification/history. A specialized adapter plus a durable NPM evidence extension could reuse its ID, but would still require schema/constraints/cutover changes and affect a shared entity. It is an alternative physical implementation of C, not a free existing capability.
- `ObligationPeriod` already has amount, rate ID and concurrency, but requires `ObligationAccount/Rate`. Current accounts explicitly reject a StallId except the retired vendor-fee context, and cannot describe NPM permanent tenancy/history. Creating a second account would duplicate tenancy/rate assessment authority. Reuse allocation/correction patterns, not a synthetic space account for each NPM stall.
- `CollectionAllocation.SourceSnapshot` and `CollectionLine.CalculationSnapshot` preserve receipt evidence, not an authoritative unpaid month identity. An unallocated month has no allocation; snapshots cannot enforce one owner, lock month-wide concurrent writes, provide tenant FK identity or preserve assessment revisions before cash exists. A deterministic GUID alone supplies none of those guarantees.
- `CollectionSettlementCutover` records a reconciled opening position for an **existing** source. It does not create the NPM month source or hold its owner/rate/credit lifecycle. Its `OpeningLegacySettledAmount` must not silently include old canonical cash under a misleading name. Reuse the reconciliation pattern with explicit evidence.
- In-process proofs show existing `Collection.Post` accepts exact PHP 1,900 allocations to existing PaymentRecord/ObligationPeriod source shapes without DailyCollection allocations. That proves the cash aggregate is suitable, **not** that NPM posting, source validation, SRC persistence or remittance supports C today. Relational source-shape checks and correction allow-lists require deliberate additions for a new typed NPM month source.

**Minimum persistence recommendation:** one additive specialized `NpmRentMonths` table, not an NPM account plus a second rate ledger. Extending PaymentRecord with equivalent durable NPM evidence is possible but offers no demonstrated smaller compatibility surface. Recommend the dedicated anchor to keep NPM assessment specialized and ordinary monthly rentals unchanged. This is a design conclusion; no migration was generated.

## 6. Proposed source identity, assessment and allocation

### Month identity and ownership

Proposed `NpmRentMonth`: stable `Id`, `MunicipalityId`, `FacilityId`, `StallId`, responsible `ContractId`, first-of-month `PeriodStart`; reviewed basis/policy and rate/occupancy/credit evidence; assessed amount; a settlement/concurrency version; reconciliation/activation state and timestamps/actor. Tenant-scoped composite FKs must prove stall, facility and contract belong to the same tenant and that the contract belongs to the stall (or enforce via an appropriate composite principal key and application validation).

Use **unique `(MunicipalityId, StallId, PeriodStart)`** for the current one-month-owner rule; do not make ContractId part of a uniqueness key that permits two full monthly bills after handover. ContractId is immutable responsible-party evidence once money is attached. A later owner conflict must be rejected/reconciled, not answered by creating another source for the same stall/month. Existing daily-receipt attribution within handover months must be inspected before converting a month; do not transplant all daily cash to the latest occupant. Ambiguous historical months remain blocked from C activation until reviewed.

### Amount and provenance

The anchor does not introduce a new rate calculator. It records the assessment/effective-date/basis and legitimate credit evidence produced by the existing specialized rule, with exact rate row IDs/versions, amount/effective date snapshots, section/custom-stall evidence, occupancy window/term and policy evidence. Monthly-only months later need an approved policy that works without a daily amount; do not run the daily partial-month/credit formula with zero as a substitute policy.

Keep rate amounts, assessed amount, credits and actual allocated cash separate. Do not store another independently editable outstanding or collected total. Compute `Outstanding = max(0, ApprovedAssessment - ValidCredits - NetRentSettled)` with existing cent precision. Expose historical excess separately as `max(0, NetRentSettled + ValidCredits - ApprovedAssessment)` for reconciliation; flooring debt at zero must not erase excess cash. New posts must not create that excess. Balance and due/collectible-now are separate values: preserve approved earned/as-of timing, February shortfall due timing and Whole advance permission explicitly.

Compare excess with the full approved obligation for the same scope, **not** an elapsed-day earned figure: a properly authorized Whole payment in advance is not historical overcollection simply because only part of the month has elapsed. The month source owns full approved liability; the same specialized policy supplies its earned/due/as-of view. Do not create another assessment for each view. Legacy PureDays full liability remains its actual approved day-derived amount (for example 930 for 31 days at 30), not a universal 900 cap.

Reviewed assessment/credit changes need old/new evidence, actor, reason and version, in the same transaction as the source update using existing tenant AuditLog snapshots. Freeze accepted assessment evidence; no silent re-price from a changed Facility basis/rate row. Capture hashes/versions even where rates currently permit in-place edits. Do not decide now that all old unpaid months must freeze at today's current rate: historical reconstruction and effective policy require owner review. If existing audit retention cannot guarantee immutable assessment-revision evidence, add a narrowly scoped append-only revision table in a separately reviewed schema; that is conditional, not a reason to create a generic policy engine now.

### Money and daily activity

Add an explicit stable `CollectionSourceKind.NpmRentMonth` value after checking all persisted enum/check constraints; null source part for rent-month allocation. The kind is canonical-always for new receipts. Existing `CollectionAllocation` carries exact positive cash against this anchor. Prefer a nullable typed `NpmRentMonthId` matching SourceId with a tenant composite FK/check (the existing FineAssessment typed-FK pattern), rather than relying only on a JSON identifier. Include draft/source-shape configurations in the additive migration review. Source lookup still validates tenant, month, facility, occupancy, permission and state.

Daily PHP 30/35 receipts also allocate their cash **once to this same month source** after cutover. Actual daily visit/rate evidence can remain in immutable line calculation snapshots and truthful existing operational rows/projections. Do not create a second monetary allocation to a daily row for the same amount. Day projections are not money authority, must be excluded from legacy cash totals, and their IsPaid/RemainingBaseFee cannot authorize another rent payment. A partial final real visit may retain partial operational evidence if useful; a Whole terminal PHP 25/20 requires **no invented day or partial daily receivable**. Display its actual month settlement receipt in history.

For PHP 875 already received, allocate new PHP 25 to the same approved PHP 900 month; for PHP 980, allocate PHP 20 to the PHP 1,000 month. The allocation and original Collection/SRC are immutable. Reverse via existing signed correction allocation targeting that exact original allocation: month net decreases by PHP 25/20, correct outstanding reopens by that amount (assuming unchanged approved credits/assessment), and other cash/day base fees remain unchanged. If an entire multi-allocation receipt is voided, reopen the actual rent allocations it contained, not always only PHP 25. Report received cash by its Collection business date and balance settlement by assessment month; do not confuse the two.

## 7. Exact eligibility, day 31, concurrency and correction

1. **Quote:** authenticate/tenant-scope, choose source-owned month and answerable occupancy, validate requested collection arrangement/date and approved current scope. Return assessment, credits, net settled, remaining rent, collectible-now, payment amount and source/assessment/settlement versions. Whole amount equals exact eligible remaining; daily suggested rent is at most the remaining eligible amount, never an extra full fee. If remainder is less than a daily installment, offer the explicit final month-balance amount rather than label it a fully paid day.
2. **After thirty PHP 30 installments:** assessment 900, credits 0, net rent settled 900, remaining/collectible rent 0. Daily/Whole/specific-day/batch/online discovery all state `MonthSettled` for rent; server refuses any new positive rent allocation for this source. UI may show day attendance without a rent debt. Other fee sources remain independently eligible. No synthetic day31 payment/absence/credit is needed.
3. **Commit:** bind operation identity to actor, tenant, source-month, amount, dates, versioned intent and approved policy. Return matching successful replay **before** current eligibility evaluation, including after a later reversal; never reuse an original successful key as a new collection. For a new key, within one serializable transaction lock/update the unique anchor and re-evaluate month net/credits/occupancy/rates; compare quote token/version and validate amount <= collectible remaining. Increment settlement version and post Collection, allocation, projection and PostingOperation atomically via existing coordinator. Anchor creation races converge through unique index/retry/re-quote; no second month is created.
4. **All writers:** Head and Mobile must use this boundary. Two different daily dates, a Whole operation, a provider callback and an offline retry must contend on the same month version, not merely on separate day rows. Session duplicate guards are useful but not a DB concurrency substitute. Acquire multi-month/multi-stall locks in stable order. On serialization/version conflict, rollback and return the existing reconciliation/review result; do not silently take a different amount or another month after cash was asserted.
5. **Corrections:** tenant/role/source-specific authorization, original allocation existence and correction limits, closed remittance protections and correction idempotency remain authoritative. Append signed correction against original allocation, increment month version and refresh projections atomically. Source month stays its original occupancy; no guessed current holder or payment date moves the debt. Legacy daily-source voids still dispatch to the legacy handler, but must update the activated month position/version consistently.
6. **Remittance/SRC:** keep existing canonical posting/SRC sequence and cash/disposition rules. An anchor, assessed amount, credit or opening balance creates no Collection/SRC/remittance. One received event posts once; its allocation is not another cash event. Reversal does not silently alter historical remitted cash or physical form evidence.

### Minimum API compatibility proposal

Retain the established NPM endpoint ownership/authorization. Introduce a shared typed month-position/quote payload (proposed `NpmRentMonthPositionDto` / `NpmSettlementQuoteDto`) containing source-month ID and Contract ID, assessment month, business date and as-of/scope, assessment/credits/net settlement, outstanding/collectible amount, requested and quoted amount, eligibility reason, assessment/settlement version and opaque quote token. Money is decimal; nullable daily rate remains null when not configured. Existing Whole payloads can be extended compatibly, but posting must require the new reviewed token once a month is activated. No client-supplied municipality or amount becomes authority.

Ordinary `RecordDailyCollectionCommand` currently carries a paid flag, not a reviewed amount/token. Old callers may continue only where an approved complete daily installment fits remaining rent and all server checks pass. If the final balance is 25 rather than the client's expected 35, reject with a review-required response until the caller confirms the exact amount through the new quote contract. Never silently post 25 after the collector asserts receiving 35, discard 10, or move it to another month. A zero balance is refused even for old clients; successful old-operation replay still returns its original outcome. Offline queues preserve original intent and require office reconciliation for rejected/stale received-money assertions.

Future monthly-only requests use the same month quote/post contract without day count or daily rate. General partial amounts remain permission-controlled. This contract is proposed, not published as an implemented endpoint or binding frontend API.

## 8. Legacy bridge, activation and historical overcollection

No backfilled Collections, reallocated old source IDs or re-issued SRCs. Establish reviewed opening evidence from authoritative legacy daily rent and existing canonical daily rent allocations **net of corrections**, with weighing/utilities excluded and occupancy/month attribution verified. Preserve the provenance and exact original IDs.

Prefer: freeze the actual **legacy-only** opening cash component/evidence; continue reading pre-anchor canonical daily allocations and their signed corrections by verified original daily-source IDs; add post-cutover NpmRentMonth allocations net of their corrections. Thus `NetRentSettled = ReconciledLegacyOpening + PreAnchorCanonicalDailyNet + NewMonthAllocationNet`. A canonical daily projection must never be added again as legacy opening, and old canonical cash must not be both snapshotted into opening and summed again. Source evidence can identify the bounded set of original daily IDs and cutoff; original allocation/correction tables retain their full histories. Any later authorized legacy-opening correction needs a separate audited opening reconciliation, not an undocumented edit of that amount.

Before activation, quiesce all rent writers for the chosen tenant/source-month scope, resolve ambiguous owner/basis/credit/excess evidence and verify opening position. A shared version/lock must cover later correction of linked old canonical allocations too. Legacy month readers use current compatibility rules until activation; activated month readers use only the unified position. Never run old daily writer and new month writer simultaneously against an activated month. Unknown/missing month-basis history cannot be repaired by assuming RentGoal.

Historical PHP 930 cash on PHP 900 assessment remains PHP 930 in cash reports with zero monthly debt and PHP 30 identified for audited reconciliation. Do not auto-refund, turn it into advance credit, reclassify it or delete a day. The owner must approve any financial correction separately. This conflicts intentionally with the older comment approving an extra daily rent: the new cap governs future authorization; it does not rewrite factual history.

## 9. Example matrix and proof limits

All illustrated months below are fully held in 2025 with no credits and approved rates. Current examples use the real service and mock repositories. `Received before` is actual seeded cash; `Current Whole available` is the currently quoted sum; residual is after the currently quoted amount is settled. No municipal Collection was posted.

| Monthly / daily | Month | Received before | Current Whole available | Current residual | Required future rent availability |
| --- | --- | ---: | ---: | ---: | ---: |
| 900 / 30 | February, 28 days | 0 | 900 (840 + 60 adjustment) | 0 | 900 under existing Whole scope |
| 900 / 30 | April, 30 days | 0 | 900 | 0 | 900 |
| 900 / 30 | August, 31 days | 0 | 900 | 0 | 900 |
| 900 / 35 | February | 0 | 875 | 25 | 900 |
| 900 / 35 | August | 0 | 875 | 25 | 900 |
| 1,000 / 35 | February | 0 | 1,000 (980 + 20 adjustment) | 0 | 1,000 |
| 1,000 / 35 | August | 0 | 980 | 20 | 1,000 |
| 900 / 30 | August | 90 | 810 | 0 | 810 |
| 900 / 35 | August | 875 | 0 | 25 | 25 |
| 1,000 / 35 | August | 980 | 0 | 20 | 20 |
| 900 / 30 | August | 900 | 0 Whole; ordinary day31 accepts 30 | 0 debt, 30 historical excess if posted | 0 additional rent through every writer |
| 900 / 35, Option A proof | August | 875 + genuine partial 25 | 0 | 0 month; 10 day residual | 0; no extra daily debt |
| 1,000 / 35, Option A proof | August | 980 + genuine partial 20 | 0 | 0 month; 15 day residual | 0; no extra daily debt |
| Reverse terminal 25/20 | August | Returns to 875/980 | Current quote still 0 | True month 25/20; original base 35 restored | 25/20 via corrected month source |
| 1,900 / no daily rate | August | 0 | Low-level service quotes 1,900 adjustment against 31 zero-base days | Unsupported workflow, not a valid deployment result | Pending: one month source, 1,900 received, 0 remaining, **zero fabricated days** |

Credit/partial-month/rate scenarios: existing RentGoal charges a full held month by approved rent, caps part-month daily-derived liability and uses approved closure/absence credits; no change here. PureDays remains day-based. Current-month earned debt versus Whole advance remains explicit. For a new monthly-only LGU, first/last month proration, absence eligibility, credit amount, due date, effective rate and reassignment policy must be approved without deriving them from an absent daily rate. C accommodates those approved inputs without changing allocation mechanics; A depends on day amounts to represent both debt and settlement. Neither architecture gives permission to guess policy.

## 10. Tests and remaining verification

Task 2A's eight pending F-01 inline cases retain their Skip marker unchanged. Task 2A's earlier red run (8 failed / 8 passed) remains historical evidence; it was not rerun unskipped in Task 2B. A new pending day31 test asserts discovery excludes the date, writer refuses and cash remains 900. The adjacent active characterization proves the opposite current behavior; passing it does not approve that behavior.

New `EEMOCantilanSDS.Testing/Application/Payments/NpmMonthlySettlementArchitectureProofTests.cs`:

- 11 quote/actual in-memory settlement cases cover the matrix, prior cash preservation and stranded balances.
- 2 real partial-domain/reversal cases verify PHP 25/20 monthly settlement, PHP 10/15 daily residuals and exact reopening.
- 1 real ordinary legacy-handler/discovery case proves day31 still accepts additional rent. It does **not** exercise an actual canonical post or API authorization.
- 1 canonical day-projection void case restores exactly 30 and preserves other days; 1 historical 930-cash case preserves the cash and zero monthly debt. Neither persists a financial correction.
- 2 Collection-domain shape cases show exact 1,900 allocations need no daily allocation, but not an implemented NPM source lookup/post.
- 1 source-model boundary characterization records generic adapter exclusion and absent NPM-specific ownership/provenance fields.
- 1 mock-only low-level monthly-without-daily case exposes 31 zero-base records and an adjustment; explicitly unsupported future behavior.
- 1 pending day31 acceptance test, no changes to production code or old test assertions.

Focused command (Release build included):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

Initial focused actual result before adding the final two historical/reversal proofs: **36 passed, 0 failed, 2 skipped, total 38**. One skip is the Task 2A theory containing eight inline requirements; the other is the new day31 requirement. The monthly-only scenario remains a documented pending future specification, not a passing monthly-only acceptance. Final focused/full unit results are recorded below. Logs: `%TEMP%\stalltrack-npm-task2b-focused.log`, `%TEMP%\stalltrack-npm-task2b-unit.log`.

No new architecture was installed, so future C integration acceptance is **unverified**: real PostgreSQL uniqueness and serialization, cross-tenant source FK/filter behavior, lost response/replay/stale quotes, concurrent Head/Mobile and provider/legacy correction, source-authority reporting, remittance/closed-remittance, exact posted correction and same-scope Head/Mobile equality. Existing integration tests are source evidence only; no Testcontainers or shared database writes were authorized. A later implementation needs separate explicit approval for isolated PostgreSQL test infrastructure; do not test cash against the shared office database. Browser/MAUI checks are not financial proof and were not run for this design-only task.

## 11. Smallest controlled implementation plan and files

Do not implement any phase under Task 2B. Proposed sequence after owner accepts C:

1. **Approve source/compatibility contract and policy boundary.** Exact month identity, owner attribution, opening reconciliation, fixed assessment/change approval, current-month earned versus Whole scope, general partial permission and separately approved non-rent fees. Define blocked historical ambiguity/excess behavior. Add PostgreSQL acceptance plans before coding.
2. **Minimal model and one position reader.** Add proposed `Domain/Entities/Payments/NpmRentMonth.cs`, `Infrastructure/Persistence/Configuration/NpmRentMonthConfiguration.cs`; update `Application/Common/Interface/Persistence/IAppDbContext.cs`, `Infrastructure/Persistence/AppDbContext.cs` and tenant/save conventions. Add source enum, allocation shape/FK and reporting authority. Approve/generate/review additive migration separately; **do not apply** until authorized. Reuse AuditLog and existing financial event tables; no new generic balance/cash table.
3. **Common specialized quote/plan/post boundary, initially disabled.** Extend `Application/Common/Payments/INpmMonthSettlementService.cs`, `NpmMonthSettlementService.cs`, `.PartPayment.cs`; introduce proposed `Application/Common/Payments/NpmRentMonthPosition.cs` and source adapter if needed. Existing month rules remain assessment authority. Adapt `Application/Common/Revenue/NpmWholePaymentWorkflow.cs`, `NpmPartPaymentWorkflow.cs`, `NpmDailyCanonicalPoster.cs` (or extract its shared coordinator call, not a second cash writer). Implement exact source amounts, re-quote/version checks and atomic month consumption; keep general part gate unchanged.
4. **Every rent ingress and correction together.** Update ordinary daily, specific days, Head month, Daily batch, fish rent portion, online initiation/settlement, source discovery/session dispatch and offline sync contracts. Add new-source correction routing in `MobileCollectionCorrectionWorkflow.cs` and corresponding canonical source correction paths; retain old-source reversal. No cutover while any old writer can bypass month cap.
5. **All balance readers and truthful presentation together.** Adapt the repositories/reports in section 4, Head calendar/settlement consumers, Mobile daily/Whole/arrears and typed DTO/client contracts. Reports read rent-month settlement once; actual legacy/C Collections keep original authority. F-02/F-03 arithmetic remains separate. Add server reason `MonthSettled`; preserve weighing/utilities independent capabilities.
6. **Compatibility rehearsal and controlled tenant/month activation.** On separately authorized test infrastructure prove opening cash, corrections, owner history, daily and monthly balance reconciliation, concurrency and day31 cap before deployment. Define rollback: disable new writes and preserve immutable posted C evidence; never fall back to an old writer that ignores new allocations. No dropping/reinterpreting old data.
7. **Later monthly-only configuration.** Platform/activation/Head/mobile contract changes accept an approved monthly amount/effective date without daily rate. Reuse exactly the same position/posting model. This phase is a future task, not permission to change onboarding here.

Exact shared financial source files needing review if C is approved:

- `EEMOCantilanSDS.Domain/Enums/CollectionSourceKind.cs`
- `EEMOCantilanSDS.Domain/Constants/CollectionSourceAuthorityMap.cs`
- `EEMOCantilanSDS.Domain/Entities/Revenue/CollectionAllocation.cs`, `CollectionLine.cs`
- `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/CollectionAllocationConfiguration.cs`, `CollectionLineConfiguration.cs`, `WebCollectionDraftAllocationConfiguration.cs`, `WebCollectionDraftLineConfiguration.cs`
- Existing `CollectionCorrectionAllocation`, `PostingOperation`, `CollectionRemittance` and `CanonicalCollectionPostingCoordinator` remain authoritative event models; change only required source dispatch/validation, not financial meaning.
- `EEMOCantilanSDS.Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommandHandler.cs`
- `EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmDays/SettleNpmDaysCommandHandler.cs`
- `EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmMonth/SettleNpmMonthCommandHandler.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/NpmDailyBatchWorkflow.cs`, `MobileCollectionCorrectionWorkflow.cs`, collection-session partial files/discovery
- `EEMOCantilanSDS.Application/Common/Payments/OnlinePaymentSettlementService.cs`, `Command/OnlinePayments/Initiate/InitiateOnlinePaymentCommandHandler.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Payments/PaymentRepository.Ledger.cs`, `NpmOperatingScheduleRepository.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.Arrears.cs`, `.Mobile.cs`, `.ClosedAccounts.cs`
- Report, registration/credit/rate mutation, API/client and UI integration files from section 4; final names/scope must follow the approved contract rather than inventing unrelated refactors.

Reusing a posting coordinator and specialized assessment rules is **required**. Renaming DailyCollection, a generic billing abstraction, broad repository refactor, new global payor identity or changes to remittance/receipt policies are **not required**.

## 12. Decisions and verification required before implementation/release

1. Owner accept C and its specialized month source/additive schema direction. Day31 cap is already decided; do not re-ask it.
2. Approve historical opening/owner/basis ambiguity rules, excess-cash reconciliation ownership, assessment/credit revision authority and freezing boundary. Never auto-rewrite an earlier tenant's basis or amount.
3. Approve exact final month-balance collection through daily/Whole surfaces without broadly enabling general partial payments. Preserve normal collector assignment and authorization.
4. For later monthly-only LGUs, obtain approved monthly amount/effective date and partial-month, holiday/absence credit, due/advance and reversal policy; no Cantilan tariff defaults.
5. Authorize separate schema implementation/review, then separately authorize migration application and PostgreSQL test infrastructure. No schema/database work is implied by this design recommendation.
6. Verify office ordinance/evidence and actual local historical 31st-day/excess records read-only before operational rollout; this task did not audit or modify the database. Actual feature switch values, shared running API behavior and live concurrency were not verified here.

Documentation cleanup candidates (do not rewrite history now): `EEMO_BUSINESS_RULES.md` calendar ledger paragraph allowing a 31st installment beyond rent; service `GetPayableDaysAsync`/`SettleCoreAsync` comments permitting extra day rent; Task 2A option A text preserving an old extra-day assumption before the appended clarification; Task 1 historical descriptions. Add targeted supersession in the approved implementation task; retain historical facts and PureDays distinctions. Other towns' approved monthly cap must follow their policy evidence, not a hardcoded Cantilan amount.

## 13. Final verification and Git

Task 2B changes only this new document and `NpmMonthlySettlementArchitectureProofTests.cs`; Task 2A files remain intact. No canonical business document or index was edited. No source behavior, migration or Platform change. No commit is made merely to clean Task 2A's pending work; architecture and financial compatibility still await review.

Final executed verification:

| Check | Result |
| --- | --- |
| New Task 2B active proof cases | 20 passing within the combined focused run; 1 additional pending day31 Fact |
| Combined Task 1 / 2A / 2B focused run | **38 passed, 0 failed, 2 skipped; total 40** |
| Full unit suite, Release | **2,664 passed, 0 failed, 2 skipped; total 2,666** |
| Release compilation through focused test command | Succeeded; existing repository warnings remain. No separate Client/Mobile build claimed. |
| `git diff --check` and new-file trailing whitespace | Passed; no whitespace errors. Git emits an existing LF/CRLF notice for Task 2A's test fixture. |
| Task 2A protected-path SHA-256 comparison | All three unchanged from Task 2B entry; hashes below |
| Platform final status | Clean, same branch/HEAD; no changes |
| Git HEAD/staging | Still `17b9d100`; nothing staged; no new commit |

The two skips represent **nine pending cases**: eight InlineData cases inside Task 2A's one skipped theory and the new day31 Fact. The monthly-only case is a documented future specification, not an additional executed passing acceptance test. No Task 2B active test failed; the eight Task 2A pre-skip failures remain previously executed evidence rather than a new run.

Preserved Task 2A SHA-256 values:

```text
NpmMonthlyBillingCharacterizationTests.cs
A84ABCB9A9E9765F739A49AA0C0AD09D2D3803F2E513EA6AED8C4EBFD71240BA
NpmExactMonthlySettlementAcceptanceTests.cs
5F7850A07799BEA4B5A4388286B077924B07BE8996975AA81F920EC3136F08CA
NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md
826DE5678B54D504D5A468DD5632C0308C895D5AD00218B38473E5B4AAB2881B
```

Final EEMO status: one preexisting modified Task 2A test fixture; four untracked files (the other two Task 2A files and the two new Task 2B files). No unrelated/protected Login, Audit Trail, planning-index or release document changed. Stop after this design; no production implementation proceeds automatically.
