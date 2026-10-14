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
