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

---

# Final-closure checkpoint — 2026-10-04 (local, `release/v3-final-closure`, baseline `cdcd3701`)

Re-audit of the old Cloud TODO list against current master. Git/code is authoritative; nothing below was rebuilt from older notes.

## Old TODO list: status

| Item | Status |
|---|---|
| PR #28 (backend RC closure) | Merged. Done. |
| PR #27 | Already MERGED (2026-10-02); no open PRs remain. Stale administrative item only. |
| Mobile Collect by Payor | **Not built, deliberately** - see below. |
| Mobile Change Ticket recovery | Already correct: next Cash Ticket is automatic; "Change ticket" is a collapsed secondary control, shown only when more than one assigned ticket exists (Market, WaterCollection, OperationCollection). No change made. |
| Cash Ticket denomination (IA-056) | Still **OPEN - awaiting office confirmation**. Not encoded anywhere; unchanged. |
| Monthly Income duplicate protection | Done this session (below). |
| Tabo report body | Done this session (below). |
| Collect-by-payor tests | N/A (feature not built). |
| Production smoke test | Done, read-only: `GET https://api.stalltrack.site/health` -> 200 `{"status":"ok"}`. No authenticated reads (no credentials used), nothing posted. |
| MEEDO tenant stored office name | Code defaults/seeds already say MEEDO. The deployed tenant's stored name could not be read (needs an authenticated Office Profile read) and was not touched. If it still holds an older name, the remaining action is an **Office Profile administrative update** by the Head - no migration or code change. |
| Seven snapshot-gated integration tests | `STALLTRACK_SNAPSHOT_DB` is not set; they remain skipped (7). Run against a restored snapshot before sign-off. |
| Android builds | Mobile Debug and Release (`net10.0-android`) build with 0 errors. |
| Android runtime | **Not performed.** The only AVD (`Medium_Phone`) references an Android 37 system image that is not installed; a throwaway 36.1 AVD would not stay running. Per Clint, runtime review is done on the Windows build, not an emulator. The throwaway AVD was deleted; `Medium_Phone` was not modified. |
| Stale Git branches | Not deleted (remote deletions are outside this pass). |

## Collect by Payor - why it was not built

The only Mobile writers that are keyed to a payor today are WCF (one source per stall per billing month, already searchable by payor/stall at `/wcf`). Governed services (Market Fees, Landing/Berthing, Transportation, ...) are walk-up collections that carry optional payer text only; IA-056 and the Payor rule forbid creating or matching a Payor from typed text. Rent, ECF and penalties are legacy-written on Mobile and a cutover was explicitly out of scope. Aggregating "everything for a payor" would therefore either (a) match governed-service activity to a payor by name, which is forbidden, or (b) reduce to the existing single WCF screen. No hollow screen was added.

```
BACKEND GAP:
- current behavior: governed-service collections have no payor identity; Mobile has one payor-keyed canonical writer (WCF).
- required behavior: a payor-keyed list of every currently collectible canonical obligation, each posted by its own writer.
- why the UI cannot truthfully implement it: it would need either name matching (forbidden) or a Rent/ECF/penalty canonical cutover (not decided).
- exact contract needed: GET api/mobile/payors/{payorId}/collectible-items returning items { sourceKind, sourceId, label, outstanding, writer } for canonical-authoritative sources only; each item posted through its existing writer with its own ClientOperationId and Cash Ticket/OR.
```

Decision for Clint: authorise the payor-linked item read above, or a Rent/ECF cutover, before this workflow is built.

## Monthly Income duplicate protection (done)

Real path: `GetOfficialMonthlyIncomeQueryHandler` counted every canonical line, while the Collection Activity feed already honoured `CollectionSourceAuthorityMap.CanonicalMoneyCounts`. A canonical line (and its corrections) against a stall-rent/utility source row whose legacy money still counts would have been reported beside the legacy figure. No production writer creates such a line today (posting cuts the row over first), so this was latent. The handler now applies the same authority rule on the line's and its allocations' **source identity** - no Distinct/amount/payor/date grouping. Test: a shadow line plus a partial correction is not counted (fails without the guard: 800 vs 0), and two identical Market Fees collections (same day/amount/no payor, different CTs) are both counted. Future invariant: any new writer that posts against a legacy-authoritative row must first cut that row over.

## Tabo report (done)

`/tpm/reports` body moved to the V3 report language: one summary strip, the shared flat trend chart, and two `v3-report-table` registers (Vendors by goods; attendance log / monthly summary). Removed the icon-tile KPIs, gradient/drop-shadow donut, duplicate goods tally (inline-styled meters), card mosaic, redundant eyebrow and dead 3-D/donut CSS. Lifecycle, `[PersistentState]` cache, API calls, figures, and the Status Report/History print documents are unchanged. **Not visually reviewed in a browser this session** - Clint to review at `/tpm/reports` (Monthly, Weekly, Yearly, 400px width).

## Visual review - what was and was not done

No rendered review was performed this session (no local API/seed database was started, and no emulator was permitted). The pages listed in the brief (Revenue Setup, vendor drawer, Follow-up Queue, WCF/ECF statements, remittance screens, Mobile) were **not** visually reviewed; they remain for Clint's localhost pass.

## Results

- Unit 2478/2478 · Component 681/681 · PostgreSQL integration 194 passed / 7 skipped (snapshot-gated) · solution Release build 0 errors · Mobile Android Debug and Release 0 errors.
- No Domain persistence change, so no EF model/migration check was needed.

## Production effects

DID: one unauthenticated `GET /health` against the production API. DID NOT: any write, post, migration, backfill, tenant/user/policy change, deploy, APK publish, version bump, merge or push.

## Remaining release blockers / open decisions

1. IA-056 Cash Ticket denomination policy (office).
2. Collect by Payor: needs a payor-linked item contract or a Rent/ECF cutover decision (above).
3. Run the 7 snapshot tests on a restored snapshot.
4. Office Profile name check/update for the deployed tenant (MEEDO).
5. Rendered UI + Windows-Mobile review by Clint; Android device runtime check.
6. Earlier open items unchanged: Collection Activity page still reads the legacy `api/transactions/recent` (FRONTEND CONTRACT FOLLOW-UP in the backend handoff), annual targets (IA-027), Mobile Electricity legacy path decision.

## SRC-first pivot addendum (IA-062, 2026-10-04)

BACKEND GAP / follow-ups recorded while converting collection identity to SRC (none are faked in the UI):

- **Cross-day SRC lookup.** Collection Activity filters the loaded day only. Required contract: an optional `search` (case-insensitive SRC, legacy document, payer) on `GET api/collections/activity` that is applied server-side over the requested range.
- **Legacy Mobile writers.** NPM daily/utility Electricity (typed OR number), TPM/TRM/NCC/ICE legacy payloads and slaughter/Tabo legacy paths do not produce a canonical Collection and therefore have no SRC. They need their own controlled source cutover; this pivot deliberately did not perform one.
- **Cutover prerequisite wording.** `SettlementCutoverWorkflow` still treats accountable-document inventory reconciliation as evidence for converting a legacy source. That is a source-cutover control and was left unchanged; Core Brain should decide whether to relax it now that collection no longer consumes documents.
- **Document trace.** `GET api/official-reports/documents/{number}` now finds physical custody only for forms the office recorded as issued; a Collection is traced by its SRC (`GET api/official-reports/collections/{id}` returns `ReferenceCode`).
