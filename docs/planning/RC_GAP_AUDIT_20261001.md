# Release-candidate gap audit — 2026-10-01 (Claude UI lane, cloud continuation)

Scope: the "Known remaining backend / product gaps to audit" (A–G) in `docs/handoff/CLOUD_CONTINUATION.md`, checked against
the code on `interface-v3/claude-ui`. This lane owns the Web presentation only (root `CLAUDE.md`); nothing under Domain,
Application, Api, Infrastructure, HttpClients, Mobile or migrations was changed. Where a gap needs the server, it is stated
as a BACKEND GAP for the Completion Backend lane; where it needs a ruling, it is stated as a decision for Clint / Core Brain.

## A. Legacy NPM utility writer (`Components/Modals/UtilityBillModal.razor`)

**What the code does now**

- Used from `Menus/Facilities/NPM.razor` and `Reports/FollowUpQueue.razor`.
- A **new** month opens as a direct approved amount ("Approved amount, no meter reading" ticked; any carried per-kWh /
  per-cu.m rate is cleared, not offered as an amount). An **existing** month keeps the basis it was recorded with, so a
  recorded charge is never repriced; its readings stay visible as evidence and are locked once settled.
- The clerk can still untick the direct toggle on a new month and enter previous/current readings × rate. The component
  tests protect that path deliberately (`UtilityBillModalLockTests.UseReadings`, `ANewMonth_DoesNotRepeatLastMonthsReadingAsThisMonths`,
  `AReadingBelowTheLastOne_IsRefusedWithTheFigureItMustClear`).
- Payments go through `RecordUtilityPaymentCommand`. For Water, the handler first runs
  `WcfCollectionWorkflow.ReconcileLegacyMobileWaterAsync(..., fromWeb: true)` and refuses with `RECONCILIATION_REQUIRED`
  when the Water part is canonical, so this dialog cannot become a second Water financial authority after cutover.
  Electricity has no such canonical counterpart yet and is still settled here (OR, legacy).

**Assessment.** Consistent with IA-053/IA-054 as written: meter readings are *not required* financial evidence and the
default is the direct amount. Neither ruling *forbids* a metered basis for a new month.

**Decision for Clint / Core Brain.** Should a new month be allowed to use a metered basis at all? If not:

```
BACKEND GAP:
- current behavior: RecordUtilityReadingCommand accepts a Metered basis for a month with no bill on record.
- required behavior: refuse Metered for a new month (direct approved amount only); keep Metered only on bills already
  recorded with it, as historical evidence.
- why the UI cannot truthfully implement it: hiding the toggle would only hide the path; Mobile/legacy sync and any other
  caller could still create metered assessments, and the existing tests encode the opposite rule.
- exact frontend contract needed: the entry seed (GetBillForEntryAsync) returns AllowedBases (e.g. ["DirectApproved"] for a
  new month); the dialog then shows the toggle only when more than one basis is allowed.
```

## B. Collection Activity (`/collections/activity`, `Menus/Transactions.razor`)

**What the code does now.** `TransactionsApi.GetRecentAsync` → `GetRecentTransactionsQueryHandler` →
`TransactionFeedRepository`, which reads the legacy facility tables only (stall payments, daily collections, terminal
trips, slaughterhouse transactions, market attendance). It reads no canonical `Collections` and applies no source-authority split. The page says so
("facility records only", commit 4f2c145). Because post-cutover money is not written to the legacy tables, the feed does
not double count; it omits canonical collections.

```
BACKEND GAP:
- current behavior: the activity feed is legacy facility records only.
- required behavior: one feed of authoritative legacy (pre-cutover) plus canonical posted collections (post-cutover), each
  real collection exactly once, using the same source-authority mechanism as the official Monthly Income and the
  collector report.
- why the UI cannot truthfully implement it: concatenating the legacy feed with the Collections register in the browser
  would decide authority client-side and double count any source whose shadow/compatibility rows exist.
- exact frontend contract needed: GET api/transactions/recent (or a new api/collections/activity) returning rows with
  { occurredAt, documentNumber?, instrument?, payorName?, collectorName?, sourceLabel, classificationName?, amount,
    authority: "Legacy" | "Canonical", collectionId? (canonical), legacyReference? (legacy) }, already de-duplicated.
```

## C. Mobile writers — Fish/Meat Vendor Fee, Kanmanggay, Fiesta/Araw

Confirmed absent. `EEMOCantilanSDS.Mobile*` has no page, operation kind or sync payload for any of the three (the
`VendorFee` on `Taboan.razor` is the Tabo fee, a different source). They remain Web-only and must not be described as
Mobile-ready. Already recorded in `docs/planning/MOBILE_V3_BACKEND_GAPS_20260930.md` (sections "MOBILE BACKEND GAP").

## D. ECF on Mobile

No canonical ECF Mobile writer exists. Mobile **does** still settle Electricity through the legacy-compatible NPM utility
path (`Mobile/Components/Pages/Menus/Market.razor`, "Electricity remains a focused OR legacy-compatible part"), which writes
the same `UtilityBill` payment fields as the Web dialog in A. Intended release workflow for current ECF is the Web/Current
Collection with an OR (IA-053); nothing was invented here. Open question for Clint: keep the legacy Electricity part on
Mobile for this release, or withdraw it until ECF has a canonical writer.

## E. Slaughterhouse packages / add-ons

No package or add-on concept exists in Domain or Application; approved animal/service definitions only. Remains an explicit
gap; nothing was invented in the UI.

## F. Annual targets

No approved target governance or source is configured (`OfficialMonthlyIncomeDto.TargetsConfigured` is false and row
targets are null). The official Monthly Income prints "—" for Annual Target and Percentage and says why; no value is
invented. Needs a target source (who approves, per year, per line) before any figure can appear.

## G. Integration-test skips

The integration project has two skip gates, both explicit and intentional:

1. **`PostgresFixture`** — every PostgreSQL test skips when no Docker-compatible runtime can start the throwaway
   `postgres:16-alpine` container. In this cloud session Docker was started and these tests ran (results in the
   checkpoint).
2. **`SnapshotDatabase`** (`STALLTRACK_SNAPSHOT_DB`) — `RegisterAndFollowUpAgreementTests` and
   `OccupancyHistorySnapshotTests` read a **restored production snapshot** and skip unless that variable names a local
   database whose name contains "snapshot", "restore" or "test". They are read-only by construction and refuse any
   non-local host. These seven tests (2 + 5) are the recurring skips; they are deliberately opt-in so no developer machine or CI runner is
   ever pointed at production data. Release implication: the agreement between the register and the follow-up queue,
   and occupancy history, are verified against real data only when someone restores a snapshot and runs them. Do not
   unskip; run them against a restored snapshot before release sign-off.

## Doc drift

- `DECISION_REGISTRY.md`, `EEMO_OPERATIONAL_RULEBOOK.md`, `REVENUE_ARCHITECTURE.md`, `MOBILE_V3_FUNCTIONAL_AUDIT_20260930.md`
  and `MOBILE_V3_BACKEND_GAPS_20260930.md` already carry IA-054 and mark the superseded IA-053 WCF wording explicitly
  (registry lines "SUPERSEDED for WCF by IA-054"). No further change needed.
- `docs/interface/OFFICIAL_MONTHLY_INCOME_REPORT_V3.md` now points at the in-repo reference image and records the
  letterhead and print geometry. `docs/interface/WEB_ADMIN_V3_VISUAL_SYSTEM.md` lists this pass's pages.
