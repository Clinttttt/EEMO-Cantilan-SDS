# Public Market monthly billing: architecture and compatibility review

**Date:** 2026-10-16. **Status:** Task 1 investigation and characterization only; Task 2 is NOT authorized. No runtime billing, configuration, financial records, migrations, API contracts or UI behavior were changed.

## 1. Requirement, authority and checkpoint

The project owner confirms that permanent Public Market stall rent is a **monthly obligation across the consulted LGUs**. Collection may be daily installments or one monthly payment. The illustrative Cantilan ₱900 monthly / ₱30 installment is not a cross-tenant tariff. Retiring `PureDays` as a selectable new setup is the owner's proposed direction, not permission to recalculate historical debt or delete its implementation.

Current approved documentation and explicit office decisions govern meaning; source/tests establish current behavior. IA-035 already distinguishes obligation from collection cadence. Older descriptions permitting a days-derived obligation conflict prospectively with this new requirement, but remain implementation/compatibility evidence. No national ordinance or legal conclusion is inferred from code comments. No external LGU practice was substituted for an approved local rule.

| Repository | Branch | Initial HEAD | Initial state |
| --- | --- | --- | --- |
| `C:\dev\stalltrack\eemo` | `codex/local-unified-v8-v10-20261013` | `6114e9c2` | Clean: no staged, unstaged or untracked changes |
| `C:\dev\stalltrack\platform` | `feat/onboarding-tenant-operations-v2` | `665cc5a` | Clean; local `main` also points to this commit |

Root/ancestor agent guidance, current release/coordination guidance, business rulebook/rules/revenue architecture, October 9/14 contract clarification, architecture/onboarding documents, decision registry, ADR-007, October 6 office evidence, and V2/V3/Landing onboarding handoffs were reviewed. Current Git supersedes their older branch and deployment claims. There are many retained linked worktrees; none was created, switched, merged, reset, cleaned or edited for this task. The formerly protected Login/AuditTrail/planning changes are already committed at this baseline; those files remain untouched.

The one running API was verified as `C:\dev\stalltrack\eemo\EEMOCantilanSDS.Api\bin\Debug\net9.0\EEMOCantilanSDS.Api.exe`, PID 10012, with both 5117 and 7097 listeners. It was not restarted or replaced. The read-only audit used the configured main local PostgreSQL database, not a fixture backend or production. Tests below use existing in-process mocks/EF InMemory and frontend test runners only. No additional API or database was introduced.

## 2. How the current debt and installments work (questions A–C)

### Authoritative monthly and daily amounts

`Facility.MonthBasis` selects `FeeRateSnapshot.MonthRule`; it is currently one mutable facility setting. `RentGoal` is default value 1; `PureDays` is value 2. `MonthBasisStatedAt` distinguishes an explicit statement from the default; it is not an effective-date history.

For standard NPM sections, `Stall.ResolveMonthlyRent(daily, configuredMonthly)` uses positive effective `FeeRateKey.NpmMonthlyStall` (9), otherwise the resolved daily fee × 30. `Stall.MonthlyRate`, `Contract.MonthlyRentalRate`, and descriptive contract amounts are **not** the general standard-section NPM assessment source. A custom-section stall with its own positive `DailyRate` instead owes 30 × that stored daily rate, even when the facility has a different monthly amount. Thus agreement-specific and section-specific *monthly* approved rent are not comprehensively modeled by today's NPM calculation. Do not assume entering a monthly figure in a stall form establishes its financial authority.

`NpmDailyFee.ForStallOrNull` resolves, in order: positive custom-stall daily rate; custom-section effective daily rate; positive canonical-area effective daily key; market-wide effective daily key; null if absent. `FeeRateSnapshot.ResolveOrNull` returns no unstated rate; `Resolve` presents that as zero. **Current production resolver does not silently return Cantilan's constants for an unconfigured LGU.** Many older comments and documents still say it does. This differs from the surviving monthly fallback of 30 × the tenant's actual resolved daily fee. A zero daily market rate and an absent rate are also not identical input evidence; zero does not provide an approved monthly-only collection arrangement.

### Calendar-month position

The shared rule and `DomainRules` establish:

- Complete held `RentGoal` month: approved monthly rent, independent of 28/29/30/31 calendar days.
- Partial held `RentGoal` month: lesser of daily fee × held days and monthly rent. A positive monthly amount with zero daily fee prices a complete month but currently prices a partial month at zero.
- `PureDays`: daily fee × held days, capped to calendar length; monthly rent ignored; no month-end top-up.
- Excused days/closures: one daily installment per forgiven day, capped at the obligation; if all held days are forgiven, the obligation is fully credited.
- Outstanding: `max(0, obligation − collected − credits)`.

For ordinary live NPM ledgers, `EarnedThrough` limits the current month to today; future days are not earned debt. A current month's earned position is therefore not always the whole monthly face amount. Whole Payment deliberately quotes the full eligible month including advance days. **The new monthly-obligation requirement does not by itself decide a different due date or authorize changing Cantilan's existing earned/as-of behavior.** The future projection must distinguish monthly amount from what is due/collectible at a stated date.

### Collection examples and safeguards

| Existing scope | Obligation / received / consequence |
| --- | --- |
| Complete February, ₱900 / ₱30 | Obligation ₱900; 28 ordinary installments total ₱840; ₱60 month-end adjustment is carried on the last paid installment. Ordinary elapsed-day settlement requires month closure; Whole Payment may include it when accepting the entire month in advance. |
| Complete 31-day month, ₱900 / ₱30 | Obligation ₱900; whole-month settlement takes 30 installments. A further ordinary daily collection can be revenue beyond the obligation; it never creates negative debt or automatic future credit. |
| Three valid ₱30 installments | Actual collected ₱90; remaining full-month position ₱810. The same service subsequently settles to ₱900 and a repeated settlement adds nothing. Canonical operation replay is a separate posting safeguard. |
| Explicit ₱1,000 / ₱35 | Assessment can correctly be ₱1,000, independently of 30 × ₱35. Whole Payment nevertheless currently quotes only ₱980 in a 31-day month: see F-01. |
| Historical `PureDays`, ₱30 | February ₱840; a 31-day month ₱930; no top-up. Preserve this calculation for genuine historical dependencies. |

`DailyCollection` retains actual received amount, partial settlement and separate month-end adjustment. `NpmPartPaymentWorkflow` / `PlanPartPaymentAsync` allocate positive cent amounts oldest-first to payable installments, refuse an amount above current payable, and do not keep an advance; month-end adjustment is not a part-paid installment. Normal daily, partial and whole actions have distinct eligibility and timing. Same-day/day-row guards and canonical `ClientOperationId`/intent matching prevent double posting; stale quotes require reconciliation. Reversal/correction/classification/remittance remain their existing source-owned operations.

**Monthly-only support is not end-to-end today.** A low-level whole-month rule can return a positive monthly amount without a daily fee, and some Whole Payment code has no explicit missing-daily guard. That is not proof of supported setup: Landing requires a daily area rate; the activation mapper blocks its absence; NPM registration requires positive `DailyRate`; import refuses absent daily rates; ordinary Head settle-month requires a stated daily rate; proration/credits/allocation still use days. Never work around this by supplying a fake daily rate or zero-fee daily records. `RecordPayment` explicitly refuses NPM; the generic monthly-rental route is not an authorized shortcut.

The backend activation validator checks nonnegative amounts, key ownership, duplicate rows and the PureDays/monthly contradiction, but does not require a complete positive NPM monthly/daily configuration. Operator/stage preflight is not a pricing-completeness check. Thus frontend mapper blocking and backend acceptance are not equivalent guarantees. This is a static contract finding, not a live activation attempt; Task 2 needs server-side arrangement-specific completeness validation as well as new UX.

## 3. Occupancy, exemptions and rate history (questions D–E)

`Stall.Occupancies` reconstructs retained term windows, supersession, closure and exact term end. `DomainRules.TermLastDay` ends a signed term the day before its anniversary. An occupancy's `End` and `BillableEnd` are distinct: money actually received after lapse can still belong to that occupant even when new days are not billable. `StallOccupancy.AnsweringForMonth` selects the latest-starting overlapping occupancy as the single monthly owner. NPM day windows are clamped to the relevant occupancy; explicit named historical terms matter in Head settlement. Renewal/handover does not transfer old liability by occupant name.

NPM credits consider recorded absences, explicit market closures, effective operating schedules and restored-day exceptions. A national holiday is not automatically exempt merely because it is a holiday; applicable office calendar evidence is required. Reopening has existing closure-excusal behavior. None of these is permission to infer a new monthly-only LGU's proration, holiday entitlement, first-month charge or expiry/holdover policy.

Rate rows are tenant-owned and effective-dated; the snapshot resolves the latest applicable row on/before the requested date, checking the correct owning facility/key. Ordinary daily installments resolve each collection day; monthly position readers commonly resolve at that month's clamped end. A midmonth monthly-rate change therefore does not imply automatic blended-rate proration. Custom stored daily overrides are not an effective-dated term-rate chain and can override later section prices. Same-effective-date edits may update a row in place. The data model does not guarantee an immutable published assessment merely because the resolver is date-aware.

Past monthly-rental occupancies elsewhere use their recorded contract rate; **do not extrapolate that guarantee to NPM** where the effective tenant/custom daily/monthly rate resolver determines the assessment. Yearly report reuse of one rate is a confirmed exception to the intended per-month history protection (F-02). Activation currently seeds all rate rows effective **2020-01-01**, not a date supplied by the LGU. Future onboarding must capture actual approved effective-date evidence without retroactively altering accepted tenants.

## 4. Dependency map and `PureDays` reach

Paths are relative to the EEMO root unless marked Platform. These are the important runtime reader/writer groups; generated migration designers/snapshot also retain the enum/columns and must not be removed.

| Layer / exact path or directory | Responsibility and compatibility risk |
| --- | --- |
| `EEMOCantilanSDS.Domain/Enums/NpmMonthBasis.cs` | Persisted values 1/2; defaults; surviving compatibility decoder |
| `EEMOCantilanSDS.Domain/Common/Billing/DailyBilledMonthRule.cs` | `RentGoalMonthRule`, `PureDaysMonthRule`, `DailyBilledMonthRules.For`; unknown enum falls back to RentGoal |
| `EEMOCantilanSDS.Domain/Constants/FeeRates.cs` | `DomainRules` obligation, credit, outstanding, coverage, `EarnedThrough`, term end; 30 is compatibility installment coverage, not a universal future tariff |
| `EEMOCantilanSDS.Domain/Entities/Facilities/Facility.cs`, `Stall.cs`, `Contract.cs`, `StallOccupancy.cs` | Current setting, amount precedence, occupancy/expiry and historical ownership |
| `EEMOCantilanSDS.Application/Common/Fees/IFeeRateResolver.cs`, `NpmDailyFee.cs`; `EEMOCantilanSDS.Infrastructure/Fees/FeeRateResolver.cs` | Tenant snapshot, effective rates, section/stall precedence and current basis loaded together |
| `EEMOCantilanSDS.Application/Command/Facilities/SetNpmMonthBasis/SetNpmMonthBasisCommandHandler.cs` | Mutates current basis and invalidates payment/reference caches; no effective history or debt-preservation migration |
| `EEMOCantilanSDS.Application/Queries/Stalls/GetNpmRates/GetNpmRatesQueryHandler.cs`; `Application/Queries/Settings/GetSystemSettings/GetSystemSettingsQueryHandler.cs` | Current rate/basis DTOs, reference-tenant confirmation reminder, `PureDays` hides monthly confirmation |
| `EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.cs`, `.PartPayment.cs` | Elapsed/whole quote, canonical month position, oldest daily allocation, adjustment, partial-day handling |
| `EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmMonth/SettleNpmMonthCommandHandler.cs`, `SettleNpmDays/`, `RecordDailyCollection/` | Head/legacy day and month writers; distinct eligibility; adjustment and canonical poster wiring |
| `EEMOCantilanSDS.Application/Common/Revenue/NpmWholePaymentWorkflow.cs`, `NpmPartPaymentWorkflow.cs`, `NpmDailyBatchWorkflow.cs`, `NpmDailyCanonicalPoster.cs` | Current source-owned Mobile financial quote/post, one collection/SRC, replay/conflict/transaction/correction authority |
| `EEMOCantilanSDS.Infrastructure/Repositories/Payments/PaymentRepository.Ledger.cs` | Profile/history/outstanding months; month-specific effective rate, as-of position, actual daily money/credits |
| `EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.cs`, `.Revenue.cs`, `.Compliance.cs`, `.Breakdowns.cs` | Operational debt/revenue/compliance; period-end rate caching; daily money versus coverage; F-02 |
| `EEMOCantilanSDS.Infrastructure/Repositories/Auth/CollectorRepository.Mobile.cs` | Collector reports; per-source and period attribution, daily-billed monthly position |
| `EEMOCantilanSDS.Application/Queries/Reports/GetFinancialReport/GetFinancialReportQueryHandler.cs`, `GetCollectionReport/GetCollectionReportQueryHandler.cs`; `Application/Queries/Facilities/GetMonthEndReport/GetMonthEndReportQueryHandler.cs` | Shared coverage rule/reference columns; use report period and actual per-stall daily rate; references are not identical to as-of debt |
| `EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.ClosedAccounts.cs`, `.Register.cs`, `.Arrears.cs`, `.Attention.cs`, `.Collectable.cs`, `.Mobile.cs` | Closed/lapsed/past occupancy balances, register monthly/annual presentation, arrears and follow-up/day eligibility; current basis still affects old unpaid windows |
| `EEMOCantilanSDS.Application/Queries/Stalls/GetStallReassignmentPreview/`; `Command/Stalls/AssignPastOccupantStall/`, `CreateStall/`, `UpdateStall/`, `BulkImportStallholders/`, `RenewStallContract/` | Preview hides monthly on PureDays; positive daily requirement; contract/occupancy evidence and no silent transfer of old liability |
| `EEMOCantilanSDS.Application/Command/Payments/RecordPayment/RecordPaymentCommandHandler.cs`; existing online payment settlement adapters | Generic RecordPayment refuses NPM; online NPM routes reuse specialized day/month service, not generic monthly collection |
| `EEMOCantilanSDS.Api/Controllers/Facilities/FacilitiesController.cs`; `Collections/DailyCollectionsController.cs`; `Revenue/NpmDailyCollectionsController.cs`; `Mobile/MobileController.cs`, `NpmDailyBatchController.cs`, `CollectionSessionController.cs` | Authorized configuration, settlement and Mobile HTTP boundaries; Head amount-quote endpoint finding F-05 |
| `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.Native.cs`, `.NpmPart.cs`, `.LinkedSources.cs`, `.cs` | Actual source-native occupancy discovery, explicit NPM Daily/Whole/Part capabilities and separate verified links; no free-text rent eligibility |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FacilityConfiguration.razor` | Head selectable current basis; suppresses monthly rate on PureDays; historical rate display affected |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/NPM.razor`, `ImportStallholders.razor`; `Pages/Shared/AddVendorModal.razor`; `Client/Services/DailyFeeFromMonthlyRent.cs` | Register/import, conditional monthly form, custom daily/month conversion/rounding; no source-of-truth change from presentation |
| `EEMOCantilanSDS.Client/Components/Shared/MarketRentReminder.razor`; `Pages/Reports/NpmReports.razor`, `CollectionExceptions.razor` | Confirmation, report/closed-account and amount/day settlement presentation; typed client must match actual endpoint |
| `EEMOCantilanSDS.Mobile/Components/Pages/Menus/NpmWholePayment.razor`; `Components/Shared/CollectionSessionEditor.razor`; existing Market/Reports pages | Consume server quotes/capabilities; Whole, Daily and Part have distinct scopes; no client monthly arithmetic substitute |
| `EEMOCantilanSDS.Application/Command/Onboarding/ActivateMunicipality/ActivateMunicipalityCommand.cs`, `...Validator.cs`, `...Handler.cs` | Optional legacy basis defaults RentGoal; rejects PureDays + positive monthly rate; seeds facility setting and effective 2020 rates atomically; creates no stall groups |
| `Application/Command/Onboarding/UpdateOnboardingConfig/`, `SubmitOnboarding/`, `ReturnOnboardingToDraft/`; `Domain/Entities/Onboarding/OnboardingDraft.cs` | Saved opaque JSON and stage/token workflow; operation declarations validated, not a complete NPM money schema |
| **Platform:** `apps/landing/src/app/shared/onboarding-workspace/onboarding-workspace.ts`, `.html` | Explicit RentGoal/PureDays choices, optional monthly amount, required daily area rate, default RentGoal; draft preservation and normalization |
| **Platform:** `apps/admin/src/app/core/activation.mapper.ts`, `activation.api.ts`, `pages/activation/activation.ts`; current draft/review types | Mapper normalizes missing/unknown basis to RentGoal, blocks missing daily input, files market/per-area daily keys and optional monthly key; no collection-arrangement contract |

Scope searches included `MonthBasis`, `PureDays`, `RentGoal`, `MonthRule`, `ResolveMonthlyRent`, `NpmDailyFee`, settlement/quote writers and callers. A DTO conditional, migration column or archived draft is a dependency even if it never writes cash. `BillingArchetype.DailyStall` is a technical compatibility discriminator and must not be globally renamed to `MonthlyRental` to implement the requirement.

## 5. Local read-only compatibility findings

Audit on 2026-10-16 used the existing configured loopback connection to **EEMOCantilanSDS.DB**, port 5432. Connection options enforced `default_transaction_read_only=on` and a 15-second statement timeout; queries ran within `BEGIN READ ONLY` / `ROLLBACK`. `current_database()` and `transaction_read_only` returned the expected database and `on`. No credentials, names of payors/users, hashes, tokens, full JSON documents or transaction rows were exported.

| Aggregate | Observed local result |
| --- | --- |
| All NPM facility rows, including soft-deleted | 2 rows, both basis 1 (`RentGoal`), neither deleted; 0 `PureDays` |
| Cantilan | Active/default; RentGoal; statement timestamp null (default basis, not explicit statement) |
| Carrascal | Active/non-default; RentGoal; explicit statement timestamp present |
| Current effective Cantilan NPM rates | Daily key 1 = ₱30, effective 2020-01-01; no explicit monthly key 9, so current monthly reference falls back to ₱900 |
| Current effective Carrascal NPM rates | Market/three canonical-area daily keys = ₱35; explicit monthly key 9 = ₱900; all effective 2020-01-01 |
| NPM source row counts, Cantilan | 34 stalls; 0 physically Closed stalls; 122 DailyCollections; 1 PaymentRecord; counts include existing historical source rows, not a posted-revenue total |
| NPM source row counts, Carrascal | 0 stalls / DailyCollections / PaymentRecords |
| Saved onboarding drafts | 1; contains RentGoal; 0 contains PureDays |
| Available audit OldValues/NewValues references | 0 containing MonthBasis; 0 matching PureDays or numeric MonthBasis 2 |
| Relational basis columns | Only Facilities.MonthBasis and Facilities.MonthBasisStatedAt found; no per-period basis column found |

Both activated NPM facilities have a positive daily amount. Cantilan lacks an explicit monthly approval row but its compatibility ₱900 is the established example; do not “repair” it under Task 1. Carrascal has explicit ₱900/₱35, which exposes F-01 if occupants are registered later. That observation is configuration evidence, not approval to change either amount. No other active NPM facility was found in this local database.

There is no current local PureDays facility to attribute current local source rows to. **Zero matches do not prove that a historical basis was never changed, that archived reports/backups contain no dependency, or that production has no PureDays.** Audit coverage is incomplete as a historical policy timeline. Production, external LGU databases and archived exported reports were not accessed. A broad production retirement needs a separately authorized read-only audit, including saved drafts, soft-deleted facilities, backups and source snapshots; do not infer absence globally from these local counts.

The following read-only query pattern reproduces the central counts without personal information; use only the verified existing local connection and keep read-only options enabled:

```sql
BEGIN READ ONLY;
SELECT current_database(), current_setting('transaction_read_only');
SELECT m."Code", f."MonthBasis", f."IsDeleted", count(*)
FROM "Facilities" f JOIN "Municipalities" m ON m."Id" = f."MunicipalityId"
WHERE f."Code" = 1 GROUP BY 1,2,3;
SELECT count(*), count(*) FILTER (WHERE "ConfigJson"::text ILIKE '%PureDays%')
FROM "OnboardingDrafts";
SELECT count(*) FROM "AuditLogs"
WHERE (coalesce("OldValues", '') || coalesce("NewValues", '')) ILIKE '%MonthBasis%';
ROLLBACK;
```

Audit export remains aggregate-only. No INSERT, UPDATE, DELETE, DDL, migrations, database restoration or seed operation was executed against the main database.

## 6. Confirmed gaps, contradictions and reporting consistency (question F)

These are current observations, **not accepted future financial rules**. They must not be repaired as part of Task 1.

| ID | Evidence / current result | Consequence / future acceptance |
| --- | --- | --- |
| F-01 | Executed service characterization: monthly ₱900 + daily ₱35 → Whole quote ₱875; monthly ₱1,000 + daily ₱35 → ₱980. The loop stops before another whole daily installment exceeds remaining balance. | Exact whole-month balance cannot always be settled through the day-count quote; require exact ₱900/₱1,000 without inventing a rate, extra day or unapproved charge. Local Carrascal configuration is exposed prospectively. |
| F-02 | Executed repository characterization: January monthly rate ₱900, February–December ₱1,200. Monthly reports sum ₱14,100; annual report says ₱14,400. `CalculateNpmDailyObligation` obtains fee/rent outside its month loop from the report period-end snapshot. | Same-scope historical annual obligation is not protected for changing rates; repair requires separate approval, per-month rate resolution and financial reconciliation. Stable-rate Cantilan examples still agree. |
| F-03 | Executed domain characterization: wholly excused 28-day February has net obligation zero, but RentGoal coverage reference computes ₱900 − 28 × ₱30 = ₱60. | Distinguish reference coverage from actual debt. Do not collect ₱60 from this result. Confirm the intended coverage metric and reconcile all three report handlers before changing it. |
| F-04 | Executed old-period characterization: changing current facility PureDays → RentGoal changes a past February balance from ₱810 to ₱870 after the same recorded ₱30 payment; cash/reference unchanged. | Current setting is not safe historical retirement/versioning. Posted money survives, but dynamically calculated historical unpaid debt changes. Preserve basis and old calculation paths. |
| F-05 | Static HTTP source finding: the entire amount-quote action/attributes are on a `//` comment line in `Collections/DailyCollectionsController.cs`. `DailyCollectionApiClient.QuoteNpmAmountAsync` and `CollectionExceptions.razor` still call it. | Source has no executable Head `GET .../amount-quote` action at this point. Running-binary route behavior was not authenticated/tested. Treat client/helper existence as insufficient proof; a separate repair must restore/test the intended endpoint without changing collection authority. |
| F-06 | Static source finding: custom-stall obligation can be 30 × own daily (e.g. ₱50 → ₱1,500) while report handler coverage receives the market's stated monthly amount (e.g. ₱1,000). Existing custom-rate test also records ₱800 form rent deriving ₱27/day → ₱810 owed. | Monthly agreement authority is not uniform across sections. Future design must resolve approved monthly amount once, not treat a daily derivation as the contractual amount. This custom coverage path has not been newly exercised end-to-end here. |

Under stable rates and matched scope, existing tests compare profile ledger, history grid, outstanding-month dialog, Head operational report and collector report. Those checks remain valuable but do not establish universal equality under rate changes or different as-of/full-month scopes. Closed Accounts uses occupancy windows and month-specific rates, but still consumes the current facility basis. Mobile arrears uses the settlement quote, which can inherit F-01, whereas a report's raw obligation can retain the full rent. Official Monthly Income is posted classified cash, not a debt recomputation; Task 1 does not reclassify historical cash or change that report's authority.

## 7. Authorization/accountability (question G)

Facilities rate/custom-section/basis mutations are guarded by SuperAdmin endpoints. The rate/basis handlers use tenant-scoped repositories and cache invalidation; the basis-setting handler itself is not a substitute for its endpoint authorization. Head ordinary month/day settlement endpoints allow SuperAdmin/Admin. Collector settlement additionally requires NPM facility assignment through `NpmSettlementAccess`; canonical Mobile paths also check source activation, business date, effective classification/instrument policy, authenticated source and retry identity. Non-Collector allowance in that helper relies on the endpoint's role guard; do not expose it through a new anonymous/general route.

Activation uses the dedicated platform-operator/preflight boundary rather than arbitrary municipality IDs or tenant-Head promotion. Tenant reads use global query filters; rate snapshots/cache regions are tenant scoped. The cross-tenant SQL aggregates above were explicitly authorized local audit reads, not an application permission change. Existing isolation and rate ownership tests were run in the unit suites.

Canonical posting retains Collection/SRC identity, source allocations, independent optional physical receipt evidence, immutable classified cash, correction/replay and remittance boundaries. A monthly obligation is not collected cash, and a report adjustment is not a collection. No OR/CT policy, cutover, collector assignment, accountable-form, audit or remittance code was changed.

## 8. Recommended target and minimum contract (proposal only)

Keep NPM's specialized source and canonical posting architecture. **Monthly rent is the standard obligation; collection arrangement is separate configuration.** Preserve the current RentGoal/Daily adapter for existing tenants and introduce a carefully bounded monthly-balance settlement path for newly confirmed monthly-only setup. Do not reclassify NPM as a generic monthly-rental facility, create a global billing engine, or synthesize DailyCollections just to reuse a writer.

Minimum conceptual changes for Task 2 design review:

1. Retain `NpmMonthBasis` and existing stored values as compatibility evidence. New supported setup chooses monthly-obligation behavior; PureDays remains readable/calculable for old records until retirement proof. Do not add a second general-purpose billing-basis enum.
2. Add one typed NPM collection-arrangement field: `DailyInstallments` or `MonthlyPayment`. Partial payment is a supported writer capability/approved policy, not a third obligation model or an implicit permission. Existing activated tenants with no field retain the current path. Old drafts with no field need explicit review, not silently fabricated installment configuration.
3. Reuse approved effective-dated monthly amount data where its scope is genuinely authoritative (`NpmMonthlyStall` for market-wide rent). Capture approved effective date and evidence. For agreement-specific monthly rent, decide whether existing recorded approved occupancy amounts/term dates suffice; do not automatically promote contract-stated rent or stored `Stall.MonthlyRate` into assessed rent. If versioned approved occupancy rates are required, design the smallest source-owned addition after approval, not a parallel receivables engine. Per-section *monthly* pricing likewise needs explicit rate scope, not reuse of a daily-only section amount.
4. `DailyInstallments` requires the actual approved daily amount and keeps Cantilan's current day/credit/top-up/as-of semantics. `MonthlyPayment` must accept a monthly rate without a daily input and must never derive a fake daily fee. Monthly-only partial-month/credit/due-date policy remains an approval gate.
5. Provide one server month-position projection with stable tenant, stall, occupancy, period, rate/effective evidence, gross monthly amount, applicable obligation/as-of due, credits, allocated money, outstanding, policy provenance and quote/concurrency token. Reuse existing rule holders; clients consume this projection rather than calculate it. Existing DTO consumers need additive compatibility/defaults, not an immediate contract break.
6. Extend source-owned quote/post capabilities so exact remaining monthly balance, optionally approved partial amounts, can be recorded through the canonical coordinator with the existing idempotency, source eligibility, transaction and SRC guarantees. The current poster allocates to DailyCollection IDs, not an occupancy/month anchor. **That is a real design seam, not solved by hiding a daily input.** Evaluate an explicit NPM occupancy/month allocation anchor, or carefully reuse an existing PaymentRecord only if its NPM legacy meaning/uniqueness is proven compatible. Do not merely enable the generic `RecordPayment` route or duplicate its assessment calculator. Schema/anchor choice needs a separately reviewed Task 2 design; no zero-migration claim is made.
7. Historic policy/rate selection must be period-aware. Do not use current mutable basis or latest approved rent to reinterpret prior months. A future policy transition needs explicit start date, approval and source/provenance; missing historical evidence remains unknown, not reconstructed from today.

### Future onboarding UX

Public Market card: **Monthly rent**; concise **Rate basis** (approved market rate / approved occupancy agreement, only where supported); **Effective from**; **Collection** (Daily installments / Monthly payment). Show **Daily installment** only for approved Daily installments. For monthly-only, do not require a section daily rate. Keep area labels/sections as source organization, separate from price ownership. No ₱30/₱900 defaults or placeholder implying an approved tariff for another LGU.

The existing `monthlyRent` draft field can be retained; add arrangement/effective evidence additively. Preserve unknown fields on save/reload. Current hydrate spreads each facility; missing/unknown basis normalizes to RentGoal in the mapper, while explicit PureDays is retained and suppresses the monthly amount at activation. A future UI must keep legacy draft evidence visible/read-only for review, not silently normalize PureDays to a different financial assertion. Require explicit resubmission/confirmation before activating contradictory or incomplete drafts. Backend validation must enforce the final approved shape, not trust frontend visibility.

Current activation `Rates` lacks a rate effective date; handler seeds 2020-01-01. Propose an additive approved effective-date field (or compatible per-rate date where necessary), validated for the declared rate scope. Existing activated rate rows and older requests retain their original interpretation; no backfill. New monthly-only activation rejects missing approved monthly amount; daily amount is required only under the approved Daily installments arrangement.

## 9. Compatibility alternatives and recommendation

| Alternative | Migration / code impact | Financial risk and evidence required |
| --- | --- | --- |
| **A — Stop new selection, retain legacy engine** (recommended first) | Remove prospective PureDays choice/acceptance from new setup after explicit legacy-draft review; preserve enum, migrations, persisted values, calculators, DTO decoding and historical tests. Arrangement/monthly-only features may need additive persistence independently. | Lowest retirement risk. Must not update existing facilities as a side effect; old clients/drafts get clear reviewed compatibility responses. Local zero PureDays alone does not authorize deletion. |
| **B — Fully retire calculation after proven absence** | Full dependency audit across authorized environments, soft-deleted/history/backups/drafts/replay/report exports; staged deprecation and only then remove unused calculation paths. Historical migration definitions remain immutable. | Higher risk: this local audit cannot reconstruct historic switches or prove production absence. Removing the engine now could silently reprice unpaid old months. Not recommended now. |
| **C — Controlled prospective transition for genuine legacy tenants** | Approved effective-dated policy transition and retained legacy reader by period, with signed reconciliation baseline and any additive migration separately reviewed. | Appropriate if PureDays data is found. Never reinterpret old months; missing policy history needs office evidence. More code/verification than A, but honest compatibility. |

Recommend A now, with C only for identified legacy cases. Keep B conditional on evidence that does not yet exist. Cantilan's stored rates, basis, installment writers, closed-month adjustment, day exemptions, historical amounts and source classifications stay unchanged. New behavior must be opt-in by confirmed policy/effective date; “all monthly” is not permission to convert every accepted tenant's due timing or monthly amount. Previously activated LGUs require a read-only readiness review; missing rates or arrangement must block new unsupported collection capability rather than borrow Cantilan's tariff.

## 10. Regression evidence and pending acceptance cases

New files only:

- `EEMOCantilanSDS.Testing/Application/Payments/NpmMonthlyBillingCharacterizationTests.cs`: service basis/February/31-day quotes, unchanged contract evidence, three installments and repeated settlement, non-divisible quote limitation, missing-daily partial-occupancy limitation, and fully excused February coverage discrepancy.
- `EEMOCantilanSDS.Testing/Infrastructure/Repositories/NpmMonthlyBillingCompatibilityTests.cs`: current-basis historical debt recalculation without rewriting cash/reference, and effective-dated monthly sum versus annual report discrepancy.

These are **characterization assertions**, including explicitly named current limitations. They are not approved desired semantics and must be revisited only with an approved correction. Existing tests were not weakened or removed. No production rule was edited to make them pass.

Existing wider protection includes `NpmMonthlyObligationLedgerTests`, `NpmMonthSettlementServiceTests`, `DailyBilledMonthRuleTests`, `DailyBilledMonthCoverageTests`, `NpmSectionRateTests`, `NpmDailyFeeTests`, `FeeRateResolverTests`, snapshot tests, `ActivationMonthBasisTests`, `NpmMonthBasisHandlerTests`, settlement access/command tests, `ClosedStallAccountsTests`, `MobileNpmArrearsTests`, `StallOccupancyTests`/related occupancy cases, `NpmOperatingCalendarTests` and report handler tests. These cover per-area/custom rates, effective-date selection, no unstated-rate borrowing, tenant filtering, absences, partial periods, expiry, duplicate/settlement guards and stable-scope Head/collector comparison.

**Pending future acceptance — not implemented/passing specifications:**

| Case | Required reviewed result |
| --- | --- |
| Monthly-only LGU, approved ₱1,900, no daily rate | Save/reload/submit/activate/register/quote/post exactly the approved monthly obligation with no synthetic daily amount or records. |
| Exact full settlement, ₱900/₱35 or ₱1,000/₱35 | Remaining full monthly balance is quoted/allocated exactly; no ₱25/₱20 stranded balance, overcollection or invented day. |
| Rate changes across a year | Same-scope annual obligation equals sum of the period-effective months; posted cash and classifications unchanged. |
| Genuine legacy PureDays then monthly policy | Old February stays ₱840, old August ₱930; new policy starts only at its approved date; repeated posting returns original SRC. |
| Legacy drafts | Explicit PureDays/absent arrangement survives loading and save; clear review blocks silent conversion; unsupported clients receive truthful validation. |
| Agreement-/section-specific monthly rent | Recorded approved amount, scope and effective evidence win according to approved policy; contract description alone never silently prices debt. |
| Monthly-only first/last month and absences | Exact approved due/proration/credit policy, including fully excused short February; no hidden daily-rate dependency. |

### Executed checks

Final run totals and commit gate are recorded below; logs are in `%TEMP%\stalltrack-npm-monthly-task1-*.log`, outside Git. Test suites were run separately. The full unit suite exposes a **pre-existing** tenant-label safety failure: `SectionNamesComeFromTheOfficeTests.EveryScreenThatNamesAMarketAreaAsksTheOfficeWhatToCallIt`, naming unchanged `PendingNpmRequests.razor` (its SectionName switch at lines 215–219 renders literal Fish/Meat/Vegetable labels). The new test files do not touch that page or the architecture guard. Do not bypass the guard or fold an unrelated UI repair into this billing-planning task.

| Executed check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| New characterization/compatibility cases | 12 | 0 | 0 |
| Relevant NPM/month rules/rates/occupancy/closure subset, final source | 392 | 0 | 0 |
| Full unit suite, final source | 2,635 | **1** | 0 |
| Full Head components | 1,253 | 0 | 0 |
| Full Mobile components | 143 | 0 | 0 |
| Platform Admin, 15 test files | 134 | 0 | 0 |
| Platform Landing, 12 test files | 78 | 0 | 0 |

Commands (EEMO and Platform run from their separate primary roots):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmMonthlyBillingCharacterizationTests|FullyQualifiedName~NpmMonthlyBillingCompatibilityTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Npm|FullyQualifiedName~DailyBilledMonth|FullyQualifiedName~FeeRate|FullyQualifiedName~StallOccupanc|FullyQualifiedName~MarketClosure'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore -m:1
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --no-restore -m:1

# Platform primary repository; no frontend development server/API started:
$env:NX_DAEMON='false'
npx nx test admin --watch=false --skip-nx-cache
npx nx test landing --watch=false --skip-nx-cache
```

Builds required by the test commands succeeded; existing compiler/analyzer/advisory warnings were not altered. This is not a new full-solution/native Windows build claim. No runtime UI change required visual acceptance, and no new authenticated browser workflow or financial post was executed. The baseline safety failure is independent of the executed billing characterizations. Do not describe the full unit suite or overall verification gate as green.

PostgreSQL/Testcontainers integration suites were intentionally not run: they would provision another database under the current Task 1 one-backend/no-disposable-database restriction. InMemory tests do not prove PostgreSQL SQL translation, actual posting concurrency or receipt allocation. No live cash posts, UI saves, migrations, native app rebuild/release or production checks were used as tests.

## 11. Proposed implementation phases and affected files (NOT executed)

| Phase | Bounded work after approval | Existing files/modules to review/change |
| --- | --- | --- |
| 1 — Agree semantics / baseline | Resolve due timing, monthly amount authority, exemptions, non-divisible remainder and transition dates; approve reconciliation cases and inventory real legacy basis evidence. | This review; business rulebook/rules/revenue architecture, decision registry and contract clarification **only after owner acceptance**. |
| 2 — Domain / application seam | Define typed arrangement, effective policy/rate evidence, shared NPM month position and exact-balance allocation anchor. Keep legacy adapter and Cantilan behavior. | `Domain/Enums/NpmMonthBasis.cs` (compatibility retained), `Common/Billing/DailyBilledMonthRule.cs`, `Constants/FeeRates.cs`, `Entities/Facilities/{Facility,Stall,Contract,StallOccupancy}.cs`; `Application/Common/Fees/{IFeeRateResolver,NpmDailyFee}.cs`, `Common/Payments/NpmMonthSettlementService*.cs`; proposed narrowly scoped `NpmMonthlyRentResolver` / source adapter if required, not approved file/schema names yet. |
| 3 — API / compatibility first | Add validated arrangement/effective-date DTOs, exact balance quote/post capability, stale/replay checks; reject new contradictory setup while decoding old DTOs/drafts. Any persistence migration needs a separate approval. | `Application/Command/Onboarding/ActivateMunicipality/ActivateMunicipalityCommand{,Validator,Handler}.cs`; preflight, `UpdateOnboardingConfig`, submit/resubmit validation; `Dtos/Stalls/NpmRatesDto.cs`; existing NPM workflow/DTO/HTTP clients/controllers and `Infrastructure/Repositories/Revenue/CollectionSessionSources*.cs`; persistence only after anchor review. |
| 4 — Platform onboarding / activation | Monthly-first Public Market UX, conditional genuine daily installments, rate scope/date, no fixed tariff placeholder; legacy draft review and mapper blocking. | Platform `onboarding-workspace.ts/.html`, draft/review API types, `core/activation.mapper.ts`, `activation.api.ts`, `pages/activation/activation.ts/.html`, their specs. |
| 5 — Head configuration / registration | Hide prospective PureDays selection only with compatible legacy display; approve configuration; correct term/amount presentation; monthly-only registration/import without fake daily data. | `FacilityConfiguration.razor`, `MarketRentReminder.razor`, `NPM.razor`, `AddVendorModal.razor`, `ImportStallholders.razor`, `DailyFeeFromMonthlyRent.cs`, create/update/import/reassignment validators and handlers. |
| 6 — Collector Mobile | Expose only server-authorized daily/monthly capabilities; support exact monthly balance; keep separate source context, offline intent/retry and existing daily batch scope. | `NpmWholePayment.razor`, `CollectionSessionEditor.razor`, mobile API DTO/client/core queue contracts as needed; `NpmWholePaymentWorkflow`, `NpmPartPaymentWorkflow`, existing canonical coordinator/poster and source discovery. No new writer without approved allocation seam. |
| 7 — Reports / historic views | One period-aware projection for earned debt/face amount/coverage; resolve F-02/F-03/F-06 under explicit scopes; keep official cash and remittance untouched. | `PaymentRepository.Ledger.cs`, `FacilityReportsRepository` partials, `CollectorRepository.Mobile.cs`, `StallRepository` closed/register/arrears/attention partials, `GetFinancialReport`, `GetCollectionReport`, `GetMonthEndReport`, NPM report UI/export readers. |
| 8 — Release validation | Cross-tenant and true concurrency/replay tests in separately authorized infrastructure; before/after Cantilan reconciliations; old-draft and old-Mobile compatibility; reviewed docs; manual visual gate. | Existing unit/component/integration/Platform suites; financial comparison fixtures; approved release handoff. No deployment, cutover or APK publication implied. |

EEMO paths in this table abbreviate the already listed `EEMOCantilanSDS.*` project prefix. Future changes must be split into reviewable slices; a new arrangement must not be rolled out before its quote/post/report contract is safe. Platform and EEMO remain separate repositories and one shared local API.

## 12. Owner decisions required, in priority order

1. Approve handling F-01/F-02 as separate financial corrections, including whether any historical discrepancy requires an audited reconciliation rather than automatic reassessment. Task 1 leaves them unchanged.
2. Confirm monthly amount scope per LGU: market-wide, section-specific, approved per occupancy/agreement, and which recorded amount is approved assessment versus descriptive contract evidence. Supply effective dates/evidence; do not infer a hierarchy from field names.
3. Confirm due timing and first/last partial-month policy for monthly-only LGUs. Preserve Cantilan's current earned/day, exemption and top-up behavior unless explicitly changed separately.
4. Confirm credit/exemption and coverage-reference treatment, including a fully excused February, operating closures and holidays. Confirm whether monthly-only partial payments are authorized and when.
5. Approve prospective PureDays deprecation with legacy read/calculation preservation (recommended A). If a real legacy tenant appears, require an effective-date/history plan (C), not an automatic conversion.
6. Accept an explicit review/resubmission process for preexisting incomplete/PureDays onboarding drafts and already activated LGUs. Rate effective dates must not default blindly to 2020 for new approved configuration.

Separate engineering blocker: the existing tenant-label safety failure must be resolved by its owning UI workstream before this task can meet the requested all-checks-green local commit gate. This is not permission to weaken the safety test.

## 13. Documentation cleanup candidates and boundaries

- `EEMO_BUSINESS_RULES.md`, architecture rules and source comments describing selectable PureDays as a current prospective product choice need a clearly dated supersession after Task 2 acceptance; preserve their historical calculation evidence.
- `DailyBilledMonthRuleTests` / `ActivationMonthBasisTests` comments calling both choices verified ordinances are implementation history, not current evidence that consulted LGUs bill permanent stalls by days. Preserve tests as compatibility protection and later relabel intent, not delete them.
- `FeeRateResolverTests`, GetNpmRates/report/settlement comments, and older rules claiming an absent rate falls back to Cantilan constants are stale against the current zero/null resolver. The tenant-owned daily × 30 **monthly** fallback remains current and distinct.
- `SetNpmMonthBasisCommandHandler` commentary suggesting periods keep original figures is contradicted by the executed F-04 old-period test. A basis statement timestamp is not period history.
- V2/V3/Landing handoffs contain historical isolated stacks, branch statuses and test counts. Current one-shared-API guidance supersedes those operational instructions, not their preserved evidence.
- Platform `docs/LOCAL_DEVELOPMENT_SHARED_API.md` previously observed only Cantilan Active; this audit also sees Carrascal Active. That past observation is stale, not a reason to change status or data. `CURRENT_RELEASE_STATE.md` / `ACTIVE_WORKSTREAMS.md` also contain older branch snapshots under their newer overlays; current Git remains the checkpoint.
- `MEEDO_CONTRACT_CLARIFICATION_20261009.md` retains a historical three-year-only Kanmanggay matrix superseded by its October 14 flexible-term overlay. Contract defaults do not establish monthly amount or collection cadence.

No historical document was deleted, reorganized or rewritten. A single planning-index link points here; canonical business rules await owner acceptance. Only this focused review, two new characterization files and that index link belong to Task 1. No other workstream's changes are included.

## 14. Final Git / safety checkpoint

No commit was made because the user's Task 1 commit condition requires passing safety checks; the unchanged tenant-label guard currently fails. HEAD remains `6114e9c2` in EEMO and `665cc5a` in Platform. Nothing is staged. EEMO has exactly four scoped pending paths: modified `docs/README.md`, this new planning review, and the two new test files listed above. Platform remains clean. Preserve these changes for review rather than bypassing the gate or committing unrelated fixes.

No financial production source, migration/model snapshot, database connection, launch setting, protected Login/AuditTrail/planning file or Platform source was modified. No main-database writes, account changes, alternative backend/database, production endpoint access, Git merge/cherry-pick/push, deployment or mobile publication occurred. Task 1 stops here; the proposed Task 2 rollout is not executed.

## 15. Task 1.1 closure checkpoint (2026-10-16, later than the initial snapshot above)

The initial findings, including F-01 through F-06 and the explicit **no financial implementation authorized** boundary, remain unchanged. The one unit-suite failure in section 10 was resolved in an **independent Head Portal display correction**, commit `52e542d2` (`PendingNpmRequests.razor` now loads `FacilityState` and resolves section names through `SectionLabelOf` rather than hardcoding another LGU's market-area wording). The architecture guard was not modified or bypassed.

Verification after that correction: five focused tenant-name architecture checks passed; the entire EEMO unit suite passed **2,636/2,636** (including these 12 new NPM characterization cases); and the full Head component suite passed **1,253/1,253**. The previous 2,635-pass/1-fail row remains as historical *pre-fix* evidence, not the final green result. Platform Admin/Landing and Mobile suites were previously executed in Task 1 (section 10) with no changes to those codebases in this closure. The user-visible running Head Portal was not restarted or visually reaccepted by this test-only closure.

The Task 1 review, its two characterization test files and the `docs/README.md` index entry belong in their own **documentation/test-only** commit, distinct from the Head fix above. No F-01/F-02 correction, monthly-only NPM billing, `PureDays` removal, data migration, or database/receipt changes were implemented. Owner approval remains required before proceeding beyond the characterization baseline.
