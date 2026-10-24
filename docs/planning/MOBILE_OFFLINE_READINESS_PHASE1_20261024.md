# StallTrack V14 — Mobile Offline Readiness, first implementation checkpoint (2026-10-24)

## Status and worktree
Branch `feat/v14-mobile-offline-readiness-20261024`, isolated worktree `C:\dev\stalltrack\v14-mobile-offline-readiness-20261024`. Started from released `f5acf1bbf94b031949fa9ba4a6d26de3517adc40`. Local-only; not pushed, merged, installed, or deployed. Codex's concurrently active branding/weighing worktree and the 40-file old Fish/Meat worktree were left untouched.

## Purpose and implementation
`Mobile.Core/Services/OfflineReadinessCoordinator.cs` begins after the authenticated Menu loads assigned operation capabilities. A background pass fetches current business-period GETs without opening each page. A per-collector/date/assignment key deduplicates passes; at most two requests run concurrently; a successful pass is throttled for ten minutes, partial failure retries are throttled for one minute; reconnect restarts preparation and cancels stale attempts. Logout/new login stops preparation. It does NOT grant financial eligibility.

Coverage in the coordinator:
- Assigned available NPM daily facility: month roster, arrears and utility read lists.
- Assigned monthly-rental facilities, including custom facility codes: current-month collection read lists.
- Assigned Slaughterhouse: today's approved terms and day collection view.
- Assigned Taboan: current attendance/collection read.
- Assigned collectible WCF: current-month sources and obligations.
- Assigned collectible ECF: current electricity source list.
- Assigned collectible Fish/Meat Vendor Fee or Weight & Measure: direct vendor-fee source list (deduplicated); this does NOT preload every registered weighing source choice.
- Assigned collectible governed services (supported `GovernedServiceCatalog` entries): today's approved terms, including daily mode when required.
- Menu itself already loads operation capabilities and anonymous-source collection-session discovery once; those aren't duplicated by coordinator.

The Menu has a small informational status line: lists preparing/fetched/partially fetched, with explicit notice that review may need internet. It does NOT say every payment is offline-ready. No new large report/history downloads and no concurrency explosion.

`CachingMobileApiClient` now uses a session epoch and serialized cache-write gate; pending GETs or 10-second-budget warm tasks from a previous collector cannot write stale data after identity-changing login/logout. `MobileSessionService` stops readiness and clears the read cache through this boundary on successful login/logout or definitive refresh-token rejection.

`CollectionSessionEditor` now releases the Save button immediately after durable `PendingOperationStore` queue persistence; the existing `MobileSyncService` already starts synchronization in the background. The UI listens to queue updates to distinguish local queued state, Needs Review, and a server-recorded SRC. Save shows progress and the pending page says 'Saved on this device · Waiting to sync'. No collection is represented as server-posted merely by queuing.

## Financial/security boundaries preserved
- No backend, database, migration, permission, rate, receipt/SRC, or settlement algorithm changes.
- Server preflight `QuoteCollectionSessionAsync` and `QuoteFingerprint` remain mandatory; source search, fine balances, corrections, online-only approvals, and special settlement quotations are never synthesized offline.
- `SyncOfflineCollections`, its idempotent ClientOperationIds, owner binding, retry/backoff, and reconciliation states remain authoritative.
- No automatic transfer if Android kills/suspends the app was introduced. Existing sync operates when the app/service is running, and reconnect signals are available.
- `JsonOfflineReadCache` remains best-effort for reference persistence; 'lists fetched' is NOT a guarantee of full durability. The local collection queue has separate durable-storage fault handling.

## Operation coverage — honest limits
| Workflow | Prepared on Menu where assigned | Offline financial capture claim |
|---|---|---|
| NPM Daily, NPM utility/arrears | Current-period reference lists | Existing legacy/issued-document capture may queue; validate exact route and server authority |
| Monthly TCC/NCC/BBQ/ICE and custom monthly facilities | Current-month facility list | Existing route may queue; individual account/balance authority still controls capture |
| WCF | Sources and obligation list | Existing WCF writer queues; requires current, authorized source and sync revalidation |
| ECF | Electricity source list | New Collection's source discovery and quotation remain live |
| Fish/Meat Vendor Fee and Weight & Measure | Direct vendor-fee source list | Registered weighing and source-native quote remain live |
| Slaughterhouse | Today's terms/day list | New itemized quote and account authority remain live |
| Taboan | Current market-day view | Batch quote remains live for batch operation |
| Governed services / parking / transport | Approved daily terms for assigned supported code | Existing governed writer can queue; server must revalidate policies and amounts |
| NPM whole month/outstanding, assessed fines | Some current period references only | Quote, fingerprint and full-balance authority remain live |
| Source search / New Collection canonical checkout | Menu's anonymous-source discovery only | Requires server-specific discovery and quote; NOT cold offline-capable |

## Verification and release gates
Focus tests (run in this worktree): Menu + CollectionSessionEditor 72 passed, 0 failed; unit cache/coordinator 25 passed, 0 failed, including fresh-instance JSON-cache offline read after Menu preparation and cross-collector late-response isolation. Windows MAUI net10.0-windows10.0.19041.0 Release build 0 errors/18 warnings (previous dependency warnings). Broader unit/Mobile component and Android Release builds are separately running and their final results must be recorded before considering merge.

Still required before production: authenticated collector with realistic assigned sources; Android physical/emulator online Menu → offline-first navigation → durable queue → offline app restart → reconnect and server posting/reconciliation, across each operation claimed supported; device storage/battery performance and nonzero read-cache persistence; no production financial test writes without separate authorization. Never claim complete universal offline capture from this checkpoint.

## Why no universal offline New Collection
The canonical `CollectionSessionWorkflow.RecordAsync` recomputes quote and demands a matching server-created fingerprint that encodes authoritative source/rate/balance/approval. An offline device cannot generate that token safely. A future, explicitly approved offline intent protocol is a separate server/business design, not a cache setting. Do not invent receipt numbers, SRCs, rates, financial authority or unsettled balances to make an offline button appear ready.

## Verification follow-up (2026-10-24 09:54 Asia/Manila)
- Complete Mobile component suite executed without rebuilding shared output concurrently: **168 passed / 0 failed / 0 skipped**.
- Complete .NET unit suite executed without rebuilding shared output concurrently: **3,131 passed / 0 failed / 0 skipped**. Earlier parallel full-test attempts failed during project-copy stage due simultaneous .pdb file locks; those were tooling/concurrency failures, not failing assertions. Serial/no-build runs succeeded.
- Targeted tests before final cache-clear hardening: **25 passed**, including actual JSON disk restart; Mobile Menu/Editor **72 passed**.
- Windows MAUI net10.0-windows10.0.19041.0 Release: **build passed with 0 errors, 18 warnings**.
- The cache-clear behavior was subsequently hardened to fail closed on disk errors; rerun its targeted persistence tests with that final patch before declaring release ready. Android release linking/package build was still running at this checkpoint. No signed APK or production rollout was triggered.

## Final local validation and safeguards — 2026-10-24
- Targeted post-hardening Debug cache/collector-isolation regression: **14 passed, 0 failed, 0 skipped** (includes strict cache clearing across restart, in-flight stale GET/warm invalidation and cold Menu-to-governed-terms read from persisted JSON).
- Final Windows MAUI Debug build after strict cache-clear and login failure-handling: **0 errors, 16 warnings**.
- Android net10.0-android Release build reached native assembly optimization [147/147], but had not produced a final exit/result when preparing the commit. Do not claim Android build success until an exit code and final artifact are observed. The Android work was not signed or published.
- Full-unit 3131/3131 and Mobile components 168/168 were run on the Release build prior to strict cache-clear and login exception-handler hardening; the later targeted 14/14 and final Windows Debug build validate the added code. Future release must run full suites again on integrated SHA.
- The cache identity clear now requires a successful disk write before admitting the next collector; failed clear causes the login path to fail closed with device-storage guidance. Read-cache persistence during ordinary prefetch remains best-effort, so readiness text intentionally promises lists fetched rather than durably/offline-collectible all operations.
- Source-search and session-quote/fingerprint are **not offline-enabled**. No changes to sync handler, posted collections, business policies, schema or production configuration. No safe claim of all-operation offline payment capture.

## Final Android build result
- Full Android net10.0-android Release build completed **exit 0, 0 errors, 52 warnings**, after approximately 12 minutes dominated by native assembly optimization. It started before the final strict disk-cache clear/login hardening, so a subsequent compile-only Android check on committed source is required and tracked separately.
- No APK was signed or published by this task. Release installation, physical Android offline acceptance, and production authorization remain pending.
- Final compile-only Android net10.0-android Release check on committed source, after cache/login hardening, completed **exit 0**. It verifies Android-target compilation but not an installed device session or a new signed APK. Build output recorded in `.offline-final-android-compile.log` (untracked/ignored local evidence).
