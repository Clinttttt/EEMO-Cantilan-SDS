# Public Market rental models — onboarding to collection (2026-10-18)

**Status:** implemented in the working copy (uncommitted in both repositories). No financial writer was activated, no migration was added, and no retained database was touched.

## 1. The three models and where each is stored

The market keeps its identity (`FacilityCode.NPM`), its sections, stalls, occupancy records and classifications in every model. What changes is the **stored, immutable `Facility.Archetype`** the LGU chose, plus the existing `Facility.MonthBasis` for the daily model. Nothing is inferred from a rate, an amount, a collection frequency or a name.

| Model | Facility record | Obligation | Collection | Source |
|---|---|---|---|---|
| **A. Standard monthly stall rental** | NPM, `Archetype = MonthlyRental` (`Facility.IsMonthlyRentMarket`) | One per occupied stall and month, at the contract/stall monthly rent | Full or partial installments, each with its own SRC | The existing TCC/NCC monthly source: `PaymentRecord`, `MonthlyRentCollectionSourceAdapter`, `CollectionComposerWorkflow` rent receipts, per-row cutover |
| **B. Monthly rent, daily installments** (Cantilan) | NPM, `Archetype = DailyStall`, `MonthBasis = RentGoal` | One fixed monthly obligation | Daily installments | Unchanged: daily collections / the dormant `NpmRentMonth` candidate |
| **C. Daily-based charges** | NPM, `Archetype = DailyStall`, `MonthBasis = PureDays` | Chargeable days × daily fee | Daily | Unchanged |

No new enum, column or migration was needed: `Archetype` has always been stored per facility ("data, not code"). Cantilan's stored value is `DailyStall` (verified on the working copy: NPM `Archetype = 1`), so every existing record keeps its interpretation. A market that states nothing is daily. The archetype has no public setter and `UpdateFacility` only edits name/short name/description, so a live market cannot be switched mid-period.

## 2. Onboarding to activation (Platform ↔ EEMO API contract)

1. **Landing** (`onboarding-workspace`): the Public Market card asks "How stalls are rented" — *Monthly stall rental* or *Daily collection*. The answer is stored on the draft facility as `rentalModel: 'monthly' | 'daily'` (absent = `daily`; drafts saved earlier are unchanged). The draft `config` is opaque JSON to the API, so no API change is required.
2. **Platform Admin** (`activation.mapper.ts`): `catalogKey === 'public_market' && rentalModel === 'monthly'` files the facility as `{ code: 'NPM', archetype: 'MonthlyRental' }`; no daily rate, no `NpmMonthlyStall`, no `monthBasis`; sections, section labels and custom areas are still sent; the per-kilo weighing fee is still filed. Daily markets map byte for byte as before. A rental model on any other facility type is ignored.
3. **EEMO activation** (`ActivateMunicipalityCommandValidator`/handler): NPM must be `DailyStall` or `MonthlyRental`; a `MonthlyRental` market is refused any NPM daily or monthly-stall rate and any non-default month basis (refused, never silently converted); market sections stay available; `SetMonthBasis` is recorded only for a daily market.
4. **After activation** the Head sets stall rent per stall/contract (term rates are preserved because each past month is answered by its own term's contract and frozen `PaymentRecord` amount); `SetFacilityRate` (daily/monthly-stall keys) and `SetNpmMonthBasis` refuse a monthly market.

Compatibility requirement: Landing/Admin builds that send `archetype: 'MonthlyRental'` for NPM require an EEMO API containing this change; an older API accepted the pair but treated the market as daily everywhere. Deploy EEMO first.

## 3. What a monthly market uses (source ownership)

- **Posting/reversal/reporting:** canonical `Collection` + SRC via `CollectionComposerWorkflow.PostOfficeRentAsync` (Head) and the Mobile rent collection; Head reversal via `HeadCollectionCorrectionWorkflow`; remittance on the actual collecting actor; Official Monthly Income row `RENT_NPM` (Permanent Stall Rent); Collection Activity/reconciliation from canonical collections.
- **Facility-level readers** (`FacilityReportsRepository`, collection/month-end/financial report handlers, sidebar summary) treat a monthly market as a monthly-rental facility: no daily coverage, daily rate, daily record counts or streaks. Utilities and weighing remain NPM add-ons and are never mixed into rent.
- **Daily writers refuse a monthly market:** `RecordDailyCollection`, `SettleNpmDays`, `SettleNpmMonth`, Part/Whole payment, daily batch, `NpmRentMonthWorkflow`, native-session daily/whole/part choices and online daily checkout. `RecordPayment`'s NPM guard now only applies to a daily market.
- **Head UI:** `/npm` redirects a monthly market to `/facility/market-rent`, which hosts the shared `FacilityPage` (the TCC/NCC workflow). Cantilan's `/npm` calendar is untouched.
- **Mobile:** the collector menu returns `Archetype = MonthlyRental` for a monthly market (Mobile screens already route by archetype); `GetMobileMonthlyCollection` accepts it.

## 4. Remaining financial activation gates

- The office rent receipt shortcut stays default-off per tenant and facility (`MonthlyRentReceiptOptions`); a monthly market posts only when its tenant and `NPM` are explicitly enabled. `NPM` was added to the facilities the gate can name; Cantilan's configuration does not enable it.
- Per-row cutover (`PendingCutover` → `Canonical`) is still mandatory for each monthly source row, exactly as for TCC/NCC. Legacy partial/paid rows are never converted.
- The dormant `NpmRentMonth` financial candidate (model B exactness) remains uninstalled and default-off.
- Mobile device: the Collector menu returns `Archetype = MonthlyRental` for a monthly market, so the app opens the monthly rent screen; Records/Reports pages read month chips for it; the daily NPM list and arrears endpoints refuse it; the native collection session lists its stalls under Rentals and offers no whole/part daily payment. Verified at API and component level; **not run on a Windows Mobile device** in this environment.
- Also made model-aware: follow-up/delinquency wording (months owed, no daily coverage gap), `/npm/reports` (a monthly market is sent to its monthly workspace; only the utilities view stays), Payor Portal (monthly rent balance and payable item, no daily balance), stallholder and payment-history imports (a monthly market imports like a monthly facility), stall creation (no daily rate, section optional), section rate/closure (refused for a monthly market), the collector report summary.
- Remaining: none known in user-facing workflows; the dormant `NpmRentMonth` candidate (model B exactness) is unchanged by design.

## 5. Synthetic acceptance (monthly-only tenant) — see the handoff

The authenticated procedure is in `REPORT_GOVERNANCE_FAST_COLLECTION_HANDOFF.md` (section "Public Market rental models"). It uses a disposable database only.
