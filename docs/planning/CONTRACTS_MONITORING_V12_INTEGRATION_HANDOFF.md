# Contracts Monitoring V12 — integration handoff

Branch `integration/contracts-monitoring-v12` (worktree `C:\dev\stalltrack-v2-worktrees\contracts-monitoring-integration-v12`).
Built from backend `184170c5`, with frontend `f93f774d` cherry-picked (`417d849d`), then the API binding. Local only: nothing pushed,
merged or deployed. The two agent worktrees and the main checkout (and its eight protected files) were not touched.

## What was bound

* `ReportsContractsMonitoringSource` (Client/Services) implements `IContractsMonitoringSource` over the existing
  `IReportsApiClient.GetContractsMonitoringAsync` (`GET /api/monitoring/contracts`). No second client, no duplicated DTOs. DI now
  registers it; the "unavailable" stub is gone, the abstraction stays for fictional test fixtures.
* Request: search, operation (`NPM`…`Custom5`, `KM`, `VF`), status, expiryWindow (7 / 30 only), occupancyBasis, page, pageSize. `asOf`
  is never sent. The **Expired** chip sends `status=Expired`; there is no "Today" chip because the API has no such window.
* Response: summary (9 counts) and rows are copied; status words, expiry, remaining days and counts are the server's. Nothing is
  recomputed. Null evidence reads "Not recorded"; space-only/extension shows "No contract term" / "No expiry". Approved charge and the
  contract-stated amount are separate rows (the latter only when different or the only figure). Row identity is
  `sourceType:sourceRecordId`.
* Operation filter: built from the tenant's facility catalogue (without SLH/TRM/TPM) plus KM/VF named like the API prints them, so it
  does not change while paging.
* Source links (only where a direct page exists): facility stall → `/profile/{facility}/{stallId}`; Kanmanggay →
  `/operations/kanmanggay/profile/{accountId}`; Vegetable/Fruit → `/operations/vegetable-fruit/profile/{accountId}`. Otherwise the
  drawer says "Source record unavailable." and shows no link.
* Page: first unfiltered page is passed prerender → circuit with `[PersistentState]` (one API call on open); in-flight requests are
  cancelled when superseded or when the page is left.

## Verification

| Check | Result |
|---|---|
| Whole-solution Release build | succeeds |
| Head component tests | 1,194 passed (1,161 baseline + 33), run 5× clean after the last change |
| Backend unit (Monitoring) / integration (ContractsMonitoring, real PostgreSQL via Testcontainers) | 19 / 22 passed |
| Browser, real API + disposable PostgreSQL (fictional data) | below |

Browser run: API `:7597`, Head `:7567`, container `stalltrack-contracts-v12-qa` (removed afterwards); 47 monitored rows (stalls in
NCC/TCC/NPM plus Kanmanggay and Vegetable/Fruit accounts; the Fiesta account was correctly excluded). Verified: summary 37/5/1/3/47;
7-day chip → 3 rows incl. same-day expiry; Expired chip → 1; **summary unchanged by status/window, changed by the operation filter**
(KM: 1/1/0/1/4); search; empty state; paging 1–25 of 47; View details → stall profile and Kanmanggay space profile open on the
intended record; Follow-up Queue and its tabs intact; API stopped → "Contracts could not be loaded. Retry", then Retry loads;
no page-level horizontal scroll at 1440 / 1024 / 768 / 390 (table scrolls inside its region). Screenshots:
`docs/evidence/contracts-monitoring-v12/`.

## Not exercised / remaining

* Unauthorized state is covered by tests (adapter maps 401/403; backend rejects wrong roles) but not shown in a browser.
* Mapped source links exist for facility stalls and KM/VF only; other account kinds have no direct page.
* "Today" expiry choice needs an API window of 0.
* The intermittent failures seen earlier in the full suite (three different unrelated tests, each passing when re-run) did not
  recur in five clean runs after the final change; no cause was found in this work.

## October 14 Follow-up clarity refinement (CORE BRAIN V6)

* Follow-up navigation includes an optional Past-period view link rendered by the existing MonitoringTabs component, with the same unboxed tab styling as Follow-ups / Contracts. The existing historical route and semantics remain unchanged.
* The Follow-up search is transparent and underlined, not a boxed field. Compact breakpoints keep the tabs and filters within their own scrolling/wrapping regions rather than creating page-level horizontal overflow.
* The account drawer distinguishes the selected **queue follow-up amount** (from the queue DTO, with its reported coverage) from **total outstanding balance across returned periods** (from the existing authoritative account ledger). No client-side reconciliation, amount modification, or billing-policy changes were introduced.
* Outstanding-year disclosures label years relative to Philippine local today as Current year, Past year, or Future year. Source periods and annual sums remain unchanged.
* Validation: focused FollowUpQueueCompositionTests **8 passed / 0 failed**; complete Head Portal component suite **1,198 passed / 0 failed / 0 skipped** (Debug); Client Release build succeeded (pre-existing warnings); git diff --check clean.
* Restarted only the verified isolated V12 Head Portal preview on :7567 with ApiBaseUrl pointing to the unchanged V12 API on :7597. Both Monitoring routes returned 302 for unauthenticated HTTP probes, as expected.
* Authenticated browser layout and actual account-position visual acceptance after this refinement are still pending with the owner. No main-checkout merge, database migration, production deployment, or push.

## October 14 Whole-time History reason shortcut simplification

* Only the **Missing OR** and **NPM Daily** reason shortcut buttons were removed from `PastFollowUpQueue.razor`'s `ReasonFilters` presentation list. The retained shortcuts are All, Delinquent, Current-period, Contract, Excused, Miscellaneous, and Closed.
* The source DTO, API, query composition, All list, search, facility selection, section counts, and underlying follow-up records are unchanged. Missing OR and NPM daily items remain visible under All and discoverable via search.
* A new rendered-component test verifies both removed shortcut buttons are absent while example records from both categories are still present under All, including its item count.
* Validation: focused Follow-up tests **9 passed / 0 failed**; full Head Portal component suite **1,199 passed / 0 failed / 0 skipped** (Debug); Client Release build **succeeded with 82 pre-existing warnings, 0 errors**.
* Only the identified V12 preview Head Portal process was restarted on :7567; the V12 API on :7597 and the local PostgreSQL database were left running. Authenticated manual visual acceptance remains with the owner. No main integration, migration, deployment, or push.
