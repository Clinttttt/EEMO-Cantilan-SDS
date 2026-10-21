# NPM monthly settlement consolidated implementation — engineering checkpoint

This is an implementation/evidence handoff under the owner's consolidated sprint authorization. It is **not
financial activation approval and not a claim that every writer/reader has been integrated**. Continue the
authorized engineering work; the outstanding integration items below are not completed by the foundation.

The latest continuation is recorded under **Financial integration continuation** at the end of this handoff.
Earlier test counts and path inventories below are retained as prior checkpoints, not final continuation results.

## Repository and permanent foundation

Primary checkout `C:\dev\stalltrack\eemo`, branch `codex/local-unified-v8-v10-20261013`, HEAD
`17b9d100e8ddffdd747f5688a8e49818693d078a`. Entry: 69 pending files, empty index. No reset, stash, branch/worktree
change, commit, push, deployment or API restart. Existing task artifacts were retained.

The exact Task 2D.3 migration, designer and snapshot were promoted; hashes match
[the accepted manifest](../evidence/task2d3/verification-20261017034328278.json). The reviewed readiness-test patch
was applied. The older model-diff test was also updated to assert the promoted migration's seven operations and
zero active snapshot/model drift. No financial assertion was removed to accommodate promotion.

The approved **seven operations were permanently installed** through `IMigrator.MigrateAsync` in a standalone
console, without the application host or seeding. Only the approved migration was pending. No execution-strategy
retry, external side effect or compensating DDL was used. Intent was fsynced before invocation.

Fresh independent read-only postflight verified:

- **144 applied entries**, including all original 143 with unchanged product versions; **131 compiled migrations**.
- `NpmRentMonths` present and empty; 26 installed columns, nine checks, three restrictive composite FKs, PK/AK,
  four explicit indexes and the two prescribed parent alternate keys.
- Every original non-history table's row count and data fingerprint unchanged, including all 13 retained tables.
- Every original schema entry, index definition/validity, object owner/ACL, view, trigger/rule state and sequence
  unchanged; the retired kind-10 index remains absent. Lookup and kind-8 indexes preserved.
- `CollectionReferenceNumberSeq` remains **17, is_called=true**. No Collections, allocations, remittances or
  historical financial rows were changed by installation.

Evidence: [durable intent](../evidence/npm-sprint/2026-10-17T04-11-01-639Z-intent.json),
[initial result](../evidence/npm-sprint/2026-10-17T04-11-01-639Z-result.json),
[independent reconciled result](../evidence/npm-sprint/2026-10-17T04-12-29-205Z-reconciled.json),
[post-install backup](../evidence/npm-sprint/2026-10-17T04-12-29-205Z-reconciled.json.backup.json).

An initial verifier defect expected 50 schema metadata additions; PostgreSQL 18 also exposes 18 NOT NULL
constraints, producing 68. Writes stopped after the failed postflight. The accepted Task 2D.2 evidence already
contained those exact 18 constraints. The verifier was corrected offline to compare their exact names and
definitions, then a **fresh read-only connection** reconciled the state. The migration was **not retried**.
The original failed result is preserved. This was a verifier error, not an additional database change.

### Verified full backups, outside Git

Both custom archives include the entire database, all retained historical tables and migration history. Each
passed `pg_restore --list` and complete archive decoding with `pg_restore --file NUL`; neither was restored.

| Archive | Bytes | SHA-256 |
| --- | ---: | --- |
| `C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_npm_install_before_2026-10-17T04-11-01-641Z.dump` | 1,711,539 | `57c5b1158276c6c33747aaef4608000711fcfdb679fee467ec94e1aba67b4700` |
| `C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_npm_install_after_2026-10-17T04-12-29-211Z.dump` | 1,720,931 | `2c9017ce10fb703f85ef4de66e06ebe4244c9544e031dd5cb00f6f95f0a6e02d` |

## Financial source engineering — inactive

Implemented candidate source contracts and executable in-process workflow:

- `NpmRentMonthPosition`: one projection of approved assessment, credits, reviewed legacy-only cash, canonical
  daily allocations and month allocations, with signed corrections tied to original allocation IDs. No second
  cash/balance table. Duplicate evidence and mixed tenant/stall/occupancy/month are rejected. Historical excess
  is visible separately and never converted into an advance.
- `NpmRentMonthWorkflow`: tenant/auth/assignment validation, explicit reviewed opening manifest, historical
  occupancy checking, exact whole/terminal daily quotes, effective classification policy, stable client intent,
  re-quote/token checks, canonical coordinator posting, audited settlement-version advancement and Head reversal.
  No fabricated daily rows or contract data. Original collections and allocations remain immutable.
- Default-denied `NpmRentMonthRuntime`: enabled, schema verified, coordinated paths ready AND an explicitly
  accepted municipality are all required. The workflow additionally checks the actual EF model for all four financial
  properties. The production model does not contain them, so options or an enum alone cannot enable this workflow.
- Dormant `CollectionSourceKind.NpmRentMonth = 17`, canonical reporting authority and typed allocation identity.
  The canonical coordinator requires an internal specialized month-posting capability; generic writers cannot
  accidentally post the new kind.
- Candidate `NpmRentMonthFinancialSchema.Configure` is deliberately **not** an `IEntityTypeConfiguration` and is
  **not called by AppDbContext**. It supplies a nullable typed allocation FK, source-shape checks and an immutable
  assessment seal and explicit financial activation evidence for isolated preview tests. The candidate adds four
  nullable columns: `CollectionAllocations.NpmRentMonthId` and the month seal/activation time/activation actor.
  State 3 (`FinanciallyActivated`) is allowed only by this uninstalled candidate; foundation review states 1/2
  do not authorize collections. No financial schema was applied to the municipal database. Normal
  disabled-mode queries ignore the uninstalled properties and retain the installed foundation model.
- Whole/Part workflows, daily batch, native collection session and ordinary daily command have candidate adapters.
  DI injects the engine but registers a literal default-off runtime, **without configuration binding**. Ordinary
  behavior remains the legacy implementation. Part payment retains its existing opt-in and additionally requires
  reviewed source policy. Monthly-only part payments require explicit approved due-date evidence; missing/malformed
  evidence is refused. Whole advance payment requires explicit approved source evidence.
- `NpmMonthSettlementBridge` preserves unactivated months and projects activated month balances through existing
  month-reader interfaces. It refuses day-row mutation/online combined fish-day settlement for an activated source.
  Disabling runtime cannot restore legacy financial authority for an already activated month. Ordinary Head month/day
  commands fail closed and direct users to a reviewed month-source payment instead of silently changing cash amounts.
- Ordinary daily requests require the frozen reviewed amount, quote token and stable client operation ID for an
  activated source. Optional new fields are omitted from legacy JSON when null. Batch/session routes use the same
  authoritative quote/post engine; actual installment date and approved daily fee remain separate from a terminal
  applied amount. No final remainder is disguised as a full daily fee.
- Payment history, outstanding-month and ledger-summary readers now overlay the authoritative activated source;
  arrears excludes activated daily residuals and caps available days. Closed-account outstanding has the same overlay
  and verifies responsible contract. These are current-position readers, not a proof of historical as-of reporting.
- Head quote/post API and typed HTTP-client methods reuse the same Whole workflow as Mobile. The Head payment modal
  has **not** yet been connected to these new methods. Legacy modal actions remain unchanged while the feature is off.
- Explicit Head credit revision is versioned/audited and changes only approved credit evidence. Original assessment,
  collections and allocations are preserved. A credit exceeding received liability exposes historical excess for
  reconciliation, never an automatic refund or advance. Existing absence/market-closure writers are not yet coordinated.
- Correction hooks in the existing daily poster and mobile correction workflow coordinate month versions when
  the engine is available. A month collection is refused by mobile correction if that routing is unavailable.
  The candidate Head month reversal enforces remittance coverage and once-only correction identity.

The opening review is JSON `NpmRentMonthOpeningEvidence` version 1: explicit disjoint `legacyDailyIds`,
`canonicalDailyIds`, and a SHA-256 `legacyFingerprint` over the ordered legacy ID/date/collected-fee evidence
using the workflow's web JSON serialization. Missing or changed original evidence fails closed; existing plain
text reviews do not silently become financial authorization. Canonical corrections remain read dynamically
against original allocations. Cross-occupancy handover evidence requires explicit reconciliation.

Reviewed rate evidence states applicable collection permissions (`dailyInstallmentsApproved`,
`advanceWholeMonthApproved`, `partPaymentsApproved`). These are provenance checks, not onboarding defaults or
permission to invent tariffs. Monthly-only whole settlement needs no daily fee. Historical PureDays amounts
remain supplied by their approved specialized assessment; the position projection never invents a fixed goal.

## Executed checks so far

Commands/logs are under `%TEMP%\stalltrack-sprint-*.log`; historical checkpoint rows are retained below.

| Check | Actual checkpoint result |
| --- | --- |
| Installer Release build | Passed, 0 warnings/errors |
| Promoted offline migration/model/SQL verification | Passed, 131 migrations, seven operations, exact reviewed SQL |
| Foundation focused tests | Initial run: 130 passed / 1 failed (old preparation diff assertion). After explicit promotion update: 131 passed / 0 failed |
| Offline installer postflight safety tests | 11 passed / 0 failed; reject data/history/sequence/index/constraint differences |
| New position tests | 20 passed / 0 failed |
| Deliberately reintroduced installment-rounding defect | 3 failed / 17 passed; mutation restored immediately |
| Full unit suite before later workflow additions | 2,791 passed / 0 failed / 2 pending skips, total 2,793 |
| New application workflow/correction tests | 17 passed / 0 failed, EF InMemory with explicit transaction-port stand-in |
| Later source/model/architecture focused suite | 147 passed / 0 failed / 1 pending day-31 skip, total 148 |
| API Release build before later workflow additions | Passed, 0 warnings/errors |
| Client Release build before later workflow additions | Passed, 75 existing warnings / 0 errors |
| New PostgreSQL financial tests | Compiled; 0 passed / 0 failed / 5 skipped: Docker daemon unavailable |

### Earlier consolidated sprint regression checkpoint

| Command | Actual result |
| --- | --- |
| `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore` | **2,831 passed, 0 failed, 0 skipped** |
| `dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore` | **1,253 passed, 0 failed, 0 skipped** |
| Focused exact/bridge/architecture/workflow suite | **64 passed, 0 failed, 0 skipped** before the later credit/ledger additions; those additions are included in the full unit result |
| Focused credit/workflow/bridge checkpoint | **28 passed, 0 failed, 0 skipped** |
| `dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~NpmRentMonthFinancialTests` | **0 passed, 0 failed, 5 skipped**; unavailable Docker Desktop Linux engine |
| `dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore` | Passed, **20 warnings, 0 errors** |
| `dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore` | Passed, **76 warnings, 0 errors** |
| `node --test docs/evidence/npm-sprint/install-foundation.test.cjs` | **11 passed, 0 failed**; repeated after final engineering changes |
| `dotnet run --project tools/npm-foundation-install/Install.csproj -c Release --no-restore -- verify` | `offline_verified`, **131 compiled, 7 operations, snapshotMatches=true**; no database connection |
| `git diff --check` | Passed; line-ending notices only |

The eight original F-01 rows now run against the explicitly activated isolated month workflow through the shared
reader bridge and actually post the exact remainder. Original legacy-off characterization remains intact. The
previously skipped day-31 acceptance now calls the ordinary `RecordDailyCollectionCommandHandler` with the month
dependency, a reviewed quote and a stable operation ID; it asserts no day-31 offer, refusal, unchanged 900 cash,
no Collection/posting operation and no legacy repository/save calls. These are no longer skipped specifications.
This is **not** a claim that every other ingress/reader is ready or that runtime activation occurred.

Additional tests cover credit revision/stale quote rejection, unchanged cash after a credit, account history and
outstanding disappearing after exact payment then reopening once on reversal. `KIRO_PG_RESTORE_TEST` was absent.
No reset-oriented municipal database test was enabled.

The InMemory tests do not prove PostgreSQL atomicity, FKs, concurrency, SRC issuance or
network behavior. The disposable PostgreSQL tests are isolated through their own `PostgresFixture`; no test
connection override accepts the municipal database, and its fixed test database/random port are checked.

## Earlier outstanding implementation and activation gates

Continue engineering without a new routine task number. Remaining work includes:

1. Complete specific-day/multi-period ingress, offline frozen amount/token propagation and Head modal reviewed
   quote/post integration. Preserve historical replay across the source boundary and independent weighing fees.
2. Complete Mobile legacy round projections, closed-account collected totals, period/as-of reports and statement
   receipt evidence. Current ledger/arrears overlays do not complete these readers. Do not fabricate paid days or
   daily receipts to make month cash visible. Earned/as-of versus Whole scope must remain explicit.
3. Connect legitimate absence/closure/exemption decisions to the explicit audited credit operation. Add controlled
   source preparation/activation handling with reviewed historical evidence; no automatic legacy conversion.
4. Complete online ingress and correction/replacement discovery, transaction/lock coverage and remittance/report
   reconciliation. Activated-source legacy writers are refused rather than allowed to create inconsistent money.
5. Prepare/review the separate financial schema candidate outside active migrations. Foundation approval did
   not install the later typed-allocation/seal columns. Any permanent financial schema installation and runtime
   activation still need separate approval. Do not restart the API to pick up unfinished source changes.
6. Run PostgreSQL financial/concurrency/FK/SRC tests when Docker is available. The shared database must not be used
   as a substitute for these committing payment tests. This is a material financial-verification blocker, not green
   integration evidence. Full units/components passed; subsequent changes must rerun affected checks.

## Git preservation and completion classification

### Exact sprint path inventory

This list is the 55 paths newly changed/created during the sprint plus the seven entry paths deliberately extended.
It is **not** a staging list and does not include 62 preserved earlier-task paths. Installer JSON files contain
schema/aggregate integrity evidence, not exported tenant financial records. Backups are outside the repository.

```text
EEMOCantilanSDS.Api/Controllers/Collections/DailyCollectionsController.cs
EEMOCantilanSDS.Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommand.cs
EEMOCantilanSDS.Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommandHandler.cs
EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmDays/SettleNpmDaysCommandHandler.cs
EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmMonth/SettleNpmMonthCommandHandler.cs
EEMOCantilanSDS.Application/Common/Interface/ApiClients/IDailyCollectionApiClient.cs
EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementBridge.cs
EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthRuntime.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Corrections.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/CanonicalCollectionPostingCoordinator.cs
EEMOCantilanSDS.Application/Common/Revenue/CollectionSessionWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/MobileCollectionCorrectionWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmDailyBatchWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmDailyCanonicalPoster.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmPartPaymentWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmWholePaymentWorkflow.cs
EEMOCantilanSDS.Application/DependencyInjection.cs
EEMOCantilanSDS.Application/Dtos/DailyCollections/NpmPartPaymentDtos.cs
EEMOCantilanSDS.Application/Dtos/Mobile/NpmDailyBatchDtos.cs
EEMOCantilanSDS.Application/Dtos/Mobile/NpmWholePaymentDtos.cs
EEMOCantilanSDS.Domain/Common/Billing/NpmRentMonthPosition.cs
EEMOCantilanSDS.Domain/Constants/CollectionSourceAuthorityMap.cs
EEMOCantilanSDS.Domain/Entities/Payments/NpmRentMonth.cs
EEMOCantilanSDS.Domain/Entities/Revenue/CollectionAllocation.cs
EEMOCantilanSDS.Domain/Enums/CollectionSourceKind.cs
EEMOCantilanSDS.HttpClients/ApiClients/DailyCollectionApiClient.cs
EEMOCantilanSDS.Infrastructure/DependencyInjection.cs
EEMOCantilanSDS.Infrastructure/Migrations/20261017040000_AddNpmRentMonthFoundation.Designer.cs
EEMOCantilanSDS.Infrastructure/Migrations/20261017040000_AddNpmRentMonthFoundation.cs
EEMOCantilanSDS.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
EEMOCantilanSDS.Infrastructure/Persistence/Configuration/CollectionAllocationConfiguration.cs
EEMOCantilanSDS.Infrastructure/Persistence/Configuration/NpmRentMonthConfiguration.cs
EEMOCantilanSDS.Infrastructure/Persistence/Configuration/NpmRentMonthFinancialSchema.cs
EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.Arrears.cs
EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.ClosedAccounts.cs
EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.cs
EEMOCantilanSDS.Infrastructure/Repositories/Payments/PaymentRepository.Ledger.cs
EEMOCantilanSDS.Infrastructure/Repositories/Payments/PaymentRepository.cs
EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.Native.cs
EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmExactMonthlySettlementAcceptanceTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthSettlementBridgeTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthlySettlementArchitectureProofTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmRentMonthWorkflowTests.cs
EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs
EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthPositionTests.cs
EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthTests.cs
EEMOCantilanSDS.Testing/Infrastructure/Persistence/NpmRentMonthModelTests.cs
EEMOCantilanSDS.Testing/Infrastructure/Persistence/NpmRentMonthSchemaReadinessTests.cs
docs/evidence/npm-sprint/2026-10-17T04-10-19-159Z-preflight.json
docs/evidence/npm-sprint/2026-10-17T04-11-01-639Z-intent.json
docs/evidence/npm-sprint/2026-10-17T04-11-01-639Z-result.json
docs/evidence/npm-sprint/2026-10-17T04-12-29-205Z-reconciled.json
docs/evidence/npm-sprint/2026-10-17T04-12-29-205Z-reconciled.json.backup.json
docs/evidence/npm-sprint/install-foundation.cjs
docs/evidence/npm-sprint/install-foundation.test.cjs
docs/planning/NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md
tools/npm-foundation-install/Install.csproj
tools/npm-foundation-install/Program.cs
```

All 69 entry paths remain present. 62 remain byte-identical to entry. Seven earlier task paths were deliberately
extended within the sprint: domain month, its base configuration, exact acceptance, architecture proof, domain
month tests, model tests and schema-readiness tests. No unrelated/protected file was changed, staged or committed.
At the latest count there are 124 pending paths, including all prior work; the index remains empty and HEAD unchanged.
The full pending tree must not be committed wholesale merely to clear status.

**Milestone 1 complete and independently verified. Financial engineering is substantial but incomplete; milestones
3/4 and financial activation readiness are not claimed.** Docker absence blocks the required real financial
transaction/concurrency proof. The remaining engineering items above also remain required before a cutover review.
The installed foundation by itself does not authorize any new payment source.

**No claim that the cap is live.** No live financial posting, backfill, reclassification, remittance/receipt
change, financial migration, API restart or deployment occurred. The existing restore-v2 fail-closed safeguards
and retained-table restore guard are preserved.

## Financial integration continuation — 2026-10-17

### Scope, preservation and runtime state

Continuation entry was 124 pending files on the same branch and HEAD above. All 124 remain present.
The current continuation extends 17 entry paths (including this handoff) and adds changes in 31 other paths:
155 pending files in total, empty index, no commit. This count includes earlier tasks; it is not a staging list.
No reset, stash, worktree/branch change, protected-file edit, database access/write, migration application,
API start/restart, financial activation, deployment or push was performed during this continuation.

The previous foundation installation evidence remains authoritative; it was not repeated. The active assembly
still has 131 migrations and the exact installed seven-operation foundation snapshot. The candidate financial
mapping remains outside the active model/snapshot/migration assembly. Runtime registration is still a literal
default-off NpmRentMonthRuntime, not configuration-bound; schema verification, coordination and explicit tenant
acceptance remain required. A prepared/reconciled month has no collection authority.

### Implemented ingress and actual refusal boundaries

| Path | Implemented behavior |
| --- | --- |
| Head PayBillModal | Quotes exact server monthly balance and responsible contract; freezes amount/token/business date/operation per period. Multi-period attempts capture their request list before awaiting posts. Late loads from a previous stall cannot replace the selected source. Failed or lost HTTP responses retain the original operations; confirmed-attempt count is reported without claiming rollback. |
| Head Collection Manager day action | New authenticated Head/Admin day-review endpoint returns an explicit IsActivated/Quote envelope. Avoids ambiguous null/204 responses. An activated day displays reviewed rent and monthly balance; posts the frozen amount/token/cash date/operation through the existing command. Stale drawer requests are ignored. |
| Specific-day/multi-day legacy writers | Activated month refuses legacy day-row settlement. Collection Manager supports one reviewed installment; selecting multiple days in Mobile requires Whole/office review. No fake full day is created for a terminal remainder. |
| Mobile Market round | Server supplies the authoritative daily quote and actual current-month cash. Market queues exact frozen amount/token/business date/installment date/stable operation. Whole/Part cash never creates paid-day counts. Independent weighing must use its own approved source. |
| Offline sync | Separates installment date from actual cash business date. Reviewed month operations post once and return actual Collection/SRC; stale operations require reconciliation. Authentic pre-cutover canonical daily replay uses original intent/payment-date evidence. Unreviewed activated legacy payloads are refused without legacy dispatch or cash. |
| Mobile direct daily API | Optional frozen fields and operation identity propagate to the same daily command/month workflow. |
| Whole/Part | Existing reviewed source workflow remains; Part now preserves the existing February month-end eligibility timing while permitting exact cents/remainders. Last-day February earned collection limit is 840 for a 900/30 month; after month end it is 900. Whole remains 900. |
| Online initiation | Activated rent refuses legacy checkout before gateway creation. Utilities remain independent. |
| Existing online callback | Already-settled callbacks retain replay behavior. A legacy NPM checkout targeting an activated month fails reconciliation before MarkPaid/save/day-row settlement. No new online month-source financial writer or automatic capture approval is claimed. |
| Historical imports and lifecycle | Daily-history import refuses activated periods. Ordinary stall/section closure or reopening and weekly schedule changes affecting activated sources are refused pending coordinated source review. Raw operating-day-override removal cannot bypass the coordinated reopening workflow. |

Transport failure is explicitly an **unknown payment outcome**, not a confirmed failed database transaction.
Retrying an unchanged operation does not authorize a second payment. Closing/reopening a modal is not a substitute
for reconciliation after an unresolved payment response.

### Implemented readers, credits and accountability

- Source cash events retain actual Collection ID, SRC, cash business date, recorded timestamp, collector,
  original received amount and separately linked correction effect. Only genuine installment operations carry
  an installment date. Whole payments do not fabricate daily receipts.
- Collection Activity now understands typed month periods/facilities and prefers frozen space/facility evidence
  over a subsequently renamed live stall. Original money and collection dates remain unchanged.
- Official Monthly Income includes typed month rent through canonical allocations, with tenant-safe facility lookup.
  This does not rewrite older report correction-date definitions or repair F-02.
- Closed-account collected totals add month-source net cash once under the pinned responsible contract. Existing
  outstanding overlays remain; ambiguous ownership still requires reconciliation.
- Mobile per-payee reports now carry an optional stable StallId. The application handler overlays month balances
  using that ID, never equal names; totals/facility outstanding and status counts follow the overlaid payees.
  Collector cash totals still come from the existing canonical facts composer, so office cash is not added to
  a collector's total merely because it settled a payee's month.
- Historical month positions select allocations by actual collection business date and corrections by their effective
  date. Credit-revision audit history prevents a later credit being projected backward. Before activation, the
  source reader returns no month overlay and retains historical authority.
- Dormant Head preparation/review code validates identity, approved assessment/basis, complete reviewed opening
  evidence and version before activation. It has no public activation route and was exercised only in fixtures.
- Market-day closure/reopening coordinates approved operating credits inside the owning serializable transaction.
  Existing explicit credits from another authority are not silently overwritten. Monthly-only absence/closure credit
  without an approved amount is refused; no daily proration is invented.
- Explicit reversal still refuses actively remitted cash and preserves the original collection/SRC. Original received
  cash and signed correction effects remain separate. No automatic refund or reclassification is introduced.

### Regression evidence and infrastructure limits

The unit/application tests use EF InMemory with an explicit transaction-port stand-in; they do not establish
PostgreSQL rollback, locking or concurrent-post correctness.

| Check | Executed result |
| --- | --- |
| Full unit suite, Release/no-restore | 2,847 passed, 0 failed, 0 skipped |
| Full Head component suite, final continuation pass | 1,259 passed, 0 failed, 0 skipped, including transport-failure retries and a late response after switching stalls |
| Full Mobile component suite, Release/no-restore | 143 passed, 0 failed, 0 skipped; includes the linked Market Razor component |
| NpmRentMonthFinancialTests, Release/no-restore | 0 passed, 0 failed, 7 skipped |
| Windows MAUI Debug build, net10.0-windows10.0.19041.0/no-restore | Passed, 15 warnings, 0 errors |
| API Release/no-restore final build | Passed, 0 warnings, 0 errors on incremental build |
| Client Release/no-restore final build | Passed, 0 warnings/errors on final incremental build; preceding full compilation had 75 warnings, 0 errors |
| Offline foundation verify | offline_verified; 131 compiled, seven operations, snapshotMatches=true |
| Node installer safety tests | 11 passed, 0 failed |
| Diff whitespace check | Passed; Git line-ending notices only |

New executable cases cover frozen offline terminal payment/replay/staleness, rejected unreviewed offline payload,
online fail-closed settlement, as-of payment/credit separation, original cash versus reversal, remittance refusal,
stable-source Mobile payee balance, round-versus-Head exact quote, no invented Whole-paid days, dormant preparation,
operating credit provenance, disabled preparation, schedule/override guard, and February earned-versus-Whole timing.
Head component cases additionally exercise failed responses, actual HttpRequestException retries with unchanged IDs,
and a late outstanding-response from a previous stall without replacing the current stall's reviewed periods.

Mutation proof: temporarily bypassing the Mobile report overlay made its new stable-source regression fail
(1 failed, 0 passed: expected 900, actual 875). The mutation was immediately restored; the full unit suite
then passed again with 2,847 passed, 0 failed, 0 skipped. This is deliberate red/green evidence, not an unresolved failure.

PostgreSQL tests now include transaction rollback and optimistic credit-version conflicts in addition to the
three exact settlement cases, concurrent Whole posting and cross-tenant typed FK rejection. All seven were skipped:
docker info failed because //./pipe/dockerDesktopLinuxEngine does not exist. The supported fixture uses Docker
Testcontainers, a private stalltrack_tests database and a random port with explicit rejection of localhost:5432.
No alternative database/role/server was created and the municipal database was not substituted.

The Windows application was built, not launched for authenticated visual acceptance. No real-browser or Windows
interactive payment smoke test is claimed. Existing package/compiler warnings remain; no dependency upgrade was
bundled into this financial change.

### Remaining engineering and cutover gates — not activation ready

1. FacilityReportsRepository compliance, trends, fee breakdown, broader financial/month-end reports and follow-up
   projections still need coordinated period/earned/as-of month-source integration and like-for-like tests.
   Collection Activity, Official Monthly Income, the account ledger and Mobile payee overlays do not complete those
   other readers. Mobile daily open-item counts also require historical source-position coordination.
2. Activated absence/exemption actions use explicit audited credit authority; the ordinary Collection Manager
   status-overwrite action remains refused. A complete user-facing credit/correction/replacement path is not claimed.
3. Online month rent remains explicitly unavailable; legacy checkout callbacks require office reconciliation.
   No provider cash is represented as newly posted NPM revenue merely because a callback arrived.
4. Mixed operating credits need explicit reviewed reconciliation; monthly-only credit/proration and due-date policies
   cannot be inferred. Future onboarding remains outside this sprint. Month handovers with ambiguous responsibility
   require reviewed source evidence, not automatic reassignment or historical cash conversion.
5. Execute the isolated PostgreSQL financial tests once Docker is usable, add concurrent month-creation/replay/
   remittance integration coverage, and verify full source/report/SRC reconciliation before financial activation.
   InMemory success is insufficient for this gate.
6. Prepare/review the separate typed financial migration and activation plan after integration is complete. Foundation
   installation approval does not authorize that schema or cutover. The active snapshot/model remain foundation-only.
7. Perform authenticated Head/Windows UI smoke review and coordinated final acceptance. No ordinary configuration
   setting should turn on an incomplete source.

No new general cash-policy decision was silently made. Existing nonfinancial registration-request policy and
separate fee authority are untouched. This continuation is implementation progress, **not** a completed financial
cutover or a claim that the 900 cap is live.

### Exact continuation file inventory

The following 48 paths were added to this continuation's scope or deliberately extended from its 124-file entry.
Earlier task content remains in place. This list is review evidence, not permission to commit the entire dirty tree.

```text
EEMOCantilanSDS.Api/Controllers/Collections/DailyCollectionsController.cs
EEMOCantilanSDS.Api/Controllers/Mobile/MobileController.cs
EEMOCantilanSDS.Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommand.cs
EEMOCantilanSDS.Application/Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommandHandler.cs
EEMOCantilanSDS.Application/Command/Facilities/SetNpmSectionClosed/SetNpmSectionClosedCommandHandler.cs
EEMOCantilanSDS.Application/Command/OnlinePayments/Initiate/InitiateOnlinePaymentCommandHandler.cs
EEMOCantilanSDS.Application/Command/Payments/BulkImportDailyHistory/BulkImportDailyHistoryCommandHandler.cs
EEMOCantilanSDS.Application/Command/Payments/ClearMarketClosure/ClearNpmMarketClosureCommandHandler.cs
EEMOCantilanSDS.Application/Command/Payments/RestoreNpmOperatingDay/RestoreNpmOperatingDayCommandHandler.cs
EEMOCantilanSDS.Application/Command/Payments/SetMarketClosure/SetNpmMarketClosureCommandHandler.cs
EEMOCantilanSDS.Application/Command/Payments/SetNpmOperatingSchedule/SetNpmOperatingScheduleCommandHandler.cs
EEMOCantilanSDS.Application/Command/Stalls/ToggleStallStatus/ToggleStallStatusCommandHandler.cs
EEMOCantilanSDS.Application/Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandHandler.cs
EEMOCantilanSDS.Application/Common/Interface/ApiClients/IDailyCollectionApiClient.cs
EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthRuntime.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Credits.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Preparation.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.ReadModels.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.cs
EEMOCantilanSDS.Application/Common/Payments/OnlinePaymentSettlementService.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmDailyCanonicalPoster.cs
EEMOCantilanSDS.Application/Common/Revenue/NpmWholePaymentWorkflow.cs
EEMOCantilanSDS.Application/Dtos/Mobile/MobileCollectorReportDto.cs
EEMOCantilanSDS.Application/Dtos/Mobile/MobileNpmCollectionDto.cs
EEMOCantilanSDS.Application/Dtos/Mobile/MobileSyncDtos.cs
EEMOCantilanSDS.Application/Dtos/Mobile/NpmWholePaymentDtos.cs
EEMOCantilanSDS.Application/Queries/Mobile/GetCollectorReport/GetCollectorReportQueryHandler.cs
EEMOCantilanSDS.Application/Queries/Revenue/GetOfficialMonthlyIncome/GetOfficialMonthlyIncomeQueryHandler.cs
EEMOCantilanSDS.Application/Requests/Mobile/RecordMobileNpmCollectionRequest.cs
EEMOCantilanSDS.Client/Components/Pages/Reports/CollectionExceptions.razor
EEMOCantilanSDS.Client/Components/Pages/Shared/Actions/PayBillModal.razor
EEMOCantilanSDS.ComponentTests/Pages/CollectionManagerPartPaymentTests.cs
EEMOCantilanSDS.ComponentTests/Pages/PayBillModalSettleEverythingTests.cs
EEMOCantilanSDS.HttpClients/ApiClients/DailyCollectionApiClient.cs
EEMOCantilanSDS.Infrastructure/Repositories/Auth/CollectorRepository.Mobile.cs
EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.ClosedAccounts.cs
EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.Mobile.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/CollectionActivityReader.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.cs
EEMOCantilanSDS.Mobile.Core/Models/PendingOperation.cs
EEMOCantilanSDS.Mobile/Components/Pages/Menus/Market.razor
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthIngressTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthSettlementBridgeTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmRentMonthWorkflowTests.cs
EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs
EEMOCantilanSDS.Testing/Mobile/PendingOperationMappingTests.cs
docs/planning/NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md
```

## Latest reporting and Head review continuation

### Scope and preservation

This continuation began at `17b9d100` on `codex/local-unified-v8-v10-20261013` with 155 pending paths and an empty index. All entry paths remain present. The installed foundation, active snapshot, migrations, runtime registration and protected Login/Audit Trail/planning files were not changed by this continuation. There was no municipal database connection, posting, backfill, migration application, API restart, deployment, push or commit.

The existing foundation installation evidence above remains the installation record; this continuation did not repeat its database verification. The separate financial schema remains a candidate. These results do **not** authorize activation.

### Completed report engineering

- Facility revenue, monthly/yearly trends, collection rate, fee breakdown, sections, top-stall rows and monthly compliance include signed month-source cash exactly once. Actual Collection business dates and correction effective dates remain distinct from assessment months. A terminal remainder adds money, never a fictitious paid day.
- A shared report position reconstructs the source at a Philippine-date cutoff. Original cash and corrections require both an applicable business/effective date and a server RecordedAt before the cutoff boundary. Later credit revisions are unwound from their immutable audit snapshots for an earlier cutoff.
- The canonical coordinator accepts the existing server clock for the specialized writer. This makes the Collection timestamp and source audit use the same trusted instant; old callers retain their existing UTC default.
- Monthly compliance replaces the corresponding legacy assessment/payment amounts with the reviewed month position. Missed-month and follow-up discovery use the source outstanding amount; current follow-up balances can include later payments against older assessment periods.
- Month-end coverage now receives an optional FullPeriodRentObligation from the activated source, net of approved as-of credits. It does not reconstruct an activated month from today's daily tariff. Legacy rows omit the new nullable field and retain their existing coverage calculation.
- Section cash uses the recorded section key and responsible ContractId. Missing/removed section evidence appears under Historical / unassigned section. Historical top-stall cash is not attached to a new occupant through equal names.
- Mobile payee balances use the same earned/as-of source position. Daily open-item counts exclude activated monthly rent from its activation date; monthly debt remains in the payee/account position rather than appearing as an invented unpaid day. Pre-activation daily evidence remains historical.
- A positive legacy PaymentRecord outside the reviewed daily opening manifest blocks source use with an explicit reconciliation message. No old monthly cash is silently omitted or automatically converted.

Financial/month-end handlers consume the updated facility repository. Official Monthly Income retains its existing original-period/latest-corrected interpretation. ADR-005 canonical AsOf and LatestCorrected queries remain distinct from effective-date cash movement views; they must not be asserted equal when their bases differ. A new source-specific test proves August's as-known cash remains 25 after a September reversal, latest-corrected August is zero with -25 outside-period correction evidence, and the monthly account reopens exactly 25.

### Completed Head review engineering

Collection Manager now hosts an opt-in monthly review drawer using VendorDrawerShell and existing form/button styles. It stays absent when the coordinated runtime is unavailable. New Head/Admin endpoints and typed client methods provide:

- GET api/DailyCollections/npm-month/review
- POST api/DailyCollections/npm-month/credits
- POST api/DailyCollections/npm-month/reverse
- POST api/DailyCollections/npm-month/replace

Review shows the approved obligation, credits, net received money, full-period outstanding, historical excess and actual SRC/cash dates. Remitted or already-corrected entries cannot be selected for ordinary correction.

Credit decisions require an explicit reviewed total, authority reference, reason, actor, stable operation identity and expected source version. The stable ID belongs to the append-only audit event, not a fake cash PostingOperation. Same-intent replay returns the existing audited outcome; changed intent and stale versions cannot revise the month again.

Reversal uses the existing immutable correction/allocation path and remittance restrictions. Replacement owns a serializable transaction containing reversal, a fresh authoritative quote, canonical replacement posting, explicit replacement linkage and a durable replay outcome. Original cash and SRC evidence remain unchanged. A failure after reversal must roll back both operations; PostgreSQL proof is prepared but not yet executed.

Replacement is deliberately limited to current-day office-owned cash, cannot increase the original amount and requires existing partial-payment approval when appropriate. Collector-owned, remitted, historical or excess-cash cases require the established reconciliation/custody path. This drawer does not authorize refunds or silently move a collector's money into office custody.

The drawer freezes the request after submission. A lost/uncertain response retries the same ID and values. A server-confirmed validation rejection refreshes the reviewed version without resubmitting automatically. Closing/reopening the drawer retains an unresolved request within the mounted page.

### Regression and schema evidence

Final commands and results are recorded in the verification table below. Unit/application checks use in-process synthetic fixtures and make no PostgreSQL transaction/concurrency claim.

The new reporting regression was deliberately tested with the old daily-open-item counting condition restored: it failed with expected 0, actual 1. The monthly exclusion was then restored. No mutation remains in the source.

The offline financial candidate check permits only four nullable columns plus reviewed indexes, FK and check changes. It proves the active model/snapshot still omit the financial columns and the compiled registry remains 131 foundation-only migrations. The migration-only tool's verify command also returned offline_verified, operations=7, snapshotMatches=true without opening a connection.

Three additional disposable PostgreSQL cases cover current-day replacement/replay, failure after reversal rolling back the whole replacement, and concurrent credit revisions. All ten financial PostgreSQL cases remain skipped because Docker Desktop's dockerDesktopLinuxEngine named pipe is missing. No test used the municipal database as a substitute.

### Remaining activation gates and limits

1. Run all ten isolated PostgreSQL financial cases with Docker available, including concurrent final payments, source FKs, rollback, versions, SRC and remittance checks. InMemory results are not a substitute.
2. Prepare/review/install the separate typed financial migration only after explicit approval. Do not promote its candidate model into the active snapshot alone. No runtime/configuration change was made.
3. Resolve weekly compliance presentation for activated monthly sources. A partial-week window currently fails closed with an actionable monthly/yearly-view instruction; the code does not allocate a Whole payment across invented days.
4. Validate cross-period month-end/financial report exports end to end. Assessment-period paid balances, actual-date cash movements and latest-corrected original-period income are different bases. Official cross-period RCD treatment remains an Office decision under ADR-005.
5. Complete authenticated Head/Windows visual acceptance, including the new drawer and error/retry flows. No new preview or API process was started in this continuation.
6. Profile/batch report reads before broad tenant activation. Current per-source reads are cached within the request; large-dataset PostgreSQL performance is unverified.
7. Reconcile any historical monthly PaymentRecord opening evidence and collector-owned replacement requirements explicitly. Do not automatically backfill, reassign cash or relax these guards.

Online month-rent posting remains unavailable. Existing legacy/PureDays behavior remains behind its original paths until coordinated cutover. The 900 monthly cap is **not live**. These changes advance the dormant implementation but do not establish full activation readiness.

### Exact continuation file inventory

```text
EEMOCantilanSDS.Api/Controllers/Collections/DailyCollectionsController.cs
EEMOCantilanSDS.Application/Common/Interface/ApiClients/IDailyCollectionApiClient.cs
EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthRuntime.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.HeadReview.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.ReadModels.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.cs
EEMOCantilanSDS.Application/Common/Revenue/CanonicalCollectionPostingCoordinator.cs
EEMOCantilanSDS.Application/Dtos/Facilities/StallComplianceDto.cs
EEMOCantilanSDS.Application/Queries/Facilities/GetMonthEndReport/GetMonthEndReportQueryHandler.cs
EEMOCantilanSDS.Application/Queries/Mobile/GetCollectorReport/GetCollectorReportQueryHandler.cs
EEMOCantilanSDS.Client/Components/Pages/Reports/CollectionExceptions.razor
EEMOCantilanSDS.Client/Components/Shared/NpmMonthReviewActions.razor
EEMOCantilanSDS.Client/Components/Shared/NpmMonthReviewActions.razor.css
EEMOCantilanSDS.ComponentTests/Pages/NpmMonthReviewActionsTests.cs
EEMOCantilanSDS.Domain/Entities/Audit/AuditLog.cs
EEMOCantilanSDS.Domain/Entities/Payments/NpmRentMonth.cs
EEMOCantilanSDS.HttpClients/ApiClients/DailyCollectionApiClient.cs
EEMOCantilanSDS.Infrastructure/Repositories/Auth/CollectorRepository.Mobile.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/CollectionActivityReader.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Breakdowns.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Compliance.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.NpmMonth.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Rates.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Revenue.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Sections.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.Trends.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthHeadReviewTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthReportingTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmRentMonthWorkflowTests.cs
EEMOCantilanSDS.Testing/Application/Reports/GetMonthEndReportQueryHandlerTests.cs
EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs
EEMOCantilanSDS.Testing/Infrastructure/Persistence/NpmRentMonthSchemaReadinessTests.cs
docs/planning/NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md
```

### Final execution evidence

| Verification | Result |
|---|---|
| Full unit suite, Release | 2,863 passed; 0 failed; 0 skipped |
| Full Head component suite, Release | 1,262 passed; 0 failed; 0 skipped |
| Full Mobile component suite, Release | 143 passed; 0 failed; 0 skipped |
| Focused NpmRentMonthFinancialTests, isolated PostgreSQL | 0 passed; 0 failed; 10 skipped: Docker engine unavailable |
| API Release build | Passed; 0 warnings, 0 errors in final incremental build |
| Client Release build | Passed; 0 warnings, 0 errors in final incremental build |
| Foundation offline verify | 131 compiled migrations; exact 7 operations; active snapshot/model match |
| Financial candidate offline model-difference test | Passed as part of unit suite; no connection or migration applied |
| Deliberate daily-open-item mutation | 1 expected failure (expected 0, actual 1); restored implementation passes full suite |
| git diff --check | Passed, exit 0 |
| New scoped drawer CSS braces | 5 opening / 5 closing |

Commands executed separately for the component/unit suites:

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~NpmRentMonthFinancialTests
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore
dotnet run --project tools/npm-foundation-install/Install.csproj -c Release --no-restore -- verify
git diff --check
```

Logs are outside Git under the local TEMP directory: npm-full-unit-final.log, npm-full-head-final.log, npm-full-mobile-final.log, npm-postgres-final.log, npm-api-final.log, npm-client-final.log, npm-offline-foundation.log and npm-open-item-mutation.log. Earlier exploratory runs included resolved compile/test-fixture failures; the table gives final verification results, not those earlier failed runs. Existing build warnings in rebuilt dependencies/tests remain visible in those logs.

Entry/final Git comparison: 155 -> 173 pending paths; no entry paths disappeared; 36 deliberately changed/added paths including this handoff; no hash changes outside that scoped inventory; empty index; HEAD remains 17b9d100. All eight protected paths remain byte-identical to entry. No automatic commit was made.

Final runtime evidence is source/model/test based: the Application registration is still new NpmRentMonthRuntime() with no configuration binding, and the active EF model does not map the four candidate financial properties. No live runtime endpoint or new authenticated preview was started or used. Browser/Windows visual acceptance and real PostgreSQL transaction/concurrency acceptance remain unverified. Financial activation is not approved and is not ready to claim.

## PostgreSQL financial verification and closeout — 2026-10-18

This section supersedes the earlier Docker-unavailable checkpoint. Branch/HEAD remain codex/local-unified-v8-v10-20261013 / 17b9d100. Entry had 173 pending paths and an empty index. No municipal/AWS connection, financial cutover, backfill, API process restart, deployment, push or commit occurred.

### Real PostgreSQL execution and resolved failures

Docker was independently verified as desktop-linux, Linux Docker Desktop 29.5.3. Financial tests created their own postgres:16-alpine container/database stalltrack_tests with an automatically mapped non-5432 port. The month fixture rejects a different database name or port 5432 and accepts no external connection override. Existing unrelated containers were not stopped or changed. Candidate financial DDL ran only inside the disposable fixture; it was not installed in the retained municipal database or promoted into active migrations.

The first run was genuinely red: 0 passed, 10 failed, 0 skipped. Failures were resolved rather than converted to skips:

| Finding | Correction and proof |
|---|---|
| Candidate context could not find the AppDbContext migration snapshot | Fixture reads migration metadata from the foundation context; candidate model remains separate. |
| Seed fingerprint serialized pre-save numeric scale | Fixture reloads persisted historical day evidence before fingerprinting; PostgreSQL numeric precision is now part of the reviewed fixture evidence. No historical records or production fingerprint rules were rewritten. |
| Npgsql wrapped SQLSTATE 40001 in InvalidOperationException -> DbUpdateException | Month posting and reversal catch that specific database wrapper and return a reconciliation/conflict outcome. They do not retry automatically, allocate another SRC, or swallow arbitrary programming exceptions. Concurrent payment/reversal tests now pass. |
| Rollback test tried deleting an immutable referenced policy | A SaveChanges interceptor now fails the replacement save after observing the reversal save. PostgreSQL independently confirms reversal, version, posting-operation and cash rollback. The original policy FK is preserved. |
| Repeated report loads caused excessive SQL | Read-only batch evidence now feeds the SAME Load validation and NpmRentMonthPosition calculation. Posting paths use fresh evidence and cannot receive the private report snapshot. |
| Unsupported weekly scope escaped as an unhandled exception | Facility/financial query handlers convert the specific scope exception to an actionable validation failure. Other exceptions remain unchanged. A regression failed before this correction. |

Final focused financial command:

```powershell
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~NpmRentMonthFinancialTests
```

**23 passed / 0 failed / 0 skipped**, executed against real isolated PostgreSQL. Coverage includes:

- Approved 900 obligation in February: 28 real 30 installments plus an exact 60 month allocation; no invented February days.
- Thirty 30 installments in 30- and 31-day months fully settle 900. Further stall rent is rejected with zero new cash/SRC.
- Exact 25/20 remainders, decimal 25.25/20.05 remainders, and monthly-only 1900 without daily installments.
- Concurrent final payments, same-operation retries, concurrent month creation, concurrent credits and concurrent reversals.
- Tenant-safe allocation FK enforcement, one tenant/stall/month identity, stale versions, rollback, SRC/replay and remitted-cash restrictions.
- Current-day replacement/replay and failure after reversal rolling back the whole replacement.
- Batched cash/credit/as-of evidence, cross-tenant source exclusion, real facility report SQL, later reversal and original cash dates.

The related filter (NpmRentMonthFinancialTests OR NpmDailyCanonicalTests OR RemittanceWorkflowTests) also passed **100 / 0 / 0**: 20 month-source cases at that run, 59 legacy NPM cases and 21 remittance cases. Three calendar-cap cases were then added and included in the final 23-case run. These run totals overlap; they establish 103 distinct PostgreSQL cases, not 123 distinct tests. Related API tests use the existing in-process TestServer and disposable database, not a new listener or the municipal API.

### Reconciliation and performance evidence

The real facility report test posts approved October rent, verifies revenue/section/rent breakdown/coverage, records a November reversal and reruns October. October's as-known cash and zero balance stay intact; the current source balance reopens by exactly 900; November's dated cash movement is -900. Canonical AsOf versus LatestCorrected income is tested separately. Original Collection/SRC evidence is retained. None of this chooses the Office's official cross-period RCD presentation.

The report batch keeps municipality filters and composite tenant joins, responsible-contract validation, legacy-manifest verification, approval checks, original allocations, dated corrections and audited credit reconstruction. It is explicitly passed only to read methods, never stored globally or supplied to writers. A real mixed-source test combines legacy installments, approved credit, two month payments and a later reversal while excluding an equal-scope source from another tenant. An incomplete legacy monthly opening still fails closed.

| 200-source position workload | SQL reads | Observed elapsed |
|---|---:|---:|
| Original repeated loads | 3,200 | 12,616 ms |
| Duplicate load removed only | 2,000 | 12,622 ms |
| Batched evidence, final run | 9 | 556 ms |

The executable budget is at most 12 SQL reads for this fixture. Facility reports batch by assessment month/cutoff; multi-source cash validation batches too. This is a controlled closed-period position workload with synthetic data, not a production latency guarantee. Current-period earned-daily assessments still use the approved existing assessment reader and may add per-stall queries. Full municipality-sized historical/print/UI workload acceptance remains necessary before broad activation.

Weekly compliance remains deliberately unavailable where a Whole/month payment has no defensible weekly allocation. The Head receives “Select a monthly or yearly compliance view.” No daily receipts or allocation rule were invented. Official cross-period RCD interpretation remains gated by ADR-005; original-period/latest-corrected income and effective-date cash movement views remain separate.

### Final regression checkpoint

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Full unit suite, Release | 2,865 | 0 | 0 |
| Full Head component suite, Release | 1,263 | 0 | 0 |
| Full Mobile component suite, Release | 143 | 0 | 0 |
| Final isolated month-source PostgreSQL suite | 23 | 0 | 0 |
| Related isolated PostgreSQL run (overlaps above) | 100 | 0 | 0 |

API and Client Release builds passed with zero warnings/errors in their final incremental builds. Rebuilt dependencies/tests still report existing warnings, retained in the logs. Foundation offline verify returned 131 compiled migrations, exact seven operations and matching active snapshot/model. It opened no database connection. Financial candidate model checks remain part of the passing unit suite. No active financial migration or runtime registration changed.

Logs/TRX evidence are outside Git in local TEMP: npm-closeout-units-final.log, npm-closeout-head.log, npm-closeout-mobile.log, npm-closeout-api.log, npm-closeout-client.log, npm-closeout-offline.log, npm-closeout-pg-calendar-final.log, npm-pg-related-closeout.log, and stalltrack-npm-closeout/npm-closeout-final.trx / npm-related-closeout.trx. Earlier red-run evidence is in npm-pg-closeout*.log and npm-weekly-gate-red.log. Do not confuse earlier expected failures with final results.

### Authenticated Head and Windows Mobile visual-acceptance checklist

**Status: prepared, not performed in this task.** No API was started/restarted and no runtime was enabled for screenshots. Use the configured Client URL (launch profiles currently https://localhost:7167 / http://localhost:5198), or the verified existing process URL. Do not change launch settings or repoint the shared backend to the test container. Windows Debug Mobile keeps http://localhost:5117/.

Use an owner-approved authenticated disposable acceptance host/tenant after separate schema/preview authorization for feature-on checks. This checklist is not permission to activate the retained municipal tenant.

| Step | Action | Expected evidence |
|---|---|---|
| Disabled baseline | Sign in as Head/Admin; open /reports/collections, /npm/reports, /reports, /reports/month-end and /reports/monthly-income/official | No dormant monthly collection/review controls; existing authorized legacy screens retain their behavior. Do not post municipal money. |
| Roles/tenant | Use a Collector and a different-tenant Head against the approved acceptance host | Head credit/correction APIs deny the Collector; guessed source IDs reveal no other tenant cash, counts or balances. |
| Head exact balance | Open synthetic NPM account 900/35 with 875 paid; then 1000/35 with 980 paid | Head quote, payment drawer and account show exact 25 / 20; no extra full day. |
| Full cap | Review synthetic 900/30 with 30 valid installments | Zero rent due; day-31 stall rent unavailable; legitimate independent fees remain separately eligible. |
| Credit review | /reports/collections -> NPM -> source month -> Review monthly rent | Approved assessment, credits, net cash, balance, SRC and dates are readable. Credit requires authority, reason, confirmation and current version. |
| Retry/validation | Simulate lost response; close/reopen drawer; retry. Separately submit a stale reviewed version | Uncertain retry retains the SAME frozen operation and values. Confirmed stale rejection refreshes review, preserves fields, requires confirmation again and never auto-submits. |
| Corrections | Review unremitted current-day office payment; perform permitted synthetic replacement/reversal | Original SRC remains visible, replacement link is correct, amount/balance changes once. Remitted, collector-owned, historical or unsupported partial cases remain blocked/actionable. |
| As-of and export | Capture October source report, CSV/print/PDF; record a permitted synthetic November reversal and repeat October | October's recorded-knowledge result remains unchanged; current balance reopens correctly; November dated movement and latest-corrected original-period income are visibly distinct. Do not certify an official RCD interpretation without Office approval. |
| Weekly | Select a partial-week compliance view for an activated source | Clear monthly/yearly guidance; no manufactured daily allocation or paid-day count. |
| Windows collection | Sign in as assigned Collector; /market -> verified NPM stall | Same authoritative 25 / 20 / zero eligibility as Head; correct stall, historical contract and period; independent fees unaffected. |
| Offline | Freeze an eligible synthetic quote; disconnect, queue, reconnect, retry; separately make its server balance stale | Exact frozen amount/token/operation survives; retry posts once; stale operation is explicitly rejected/reconciled, never silently requoted or doubled. |
| Windows records/reports | Open /records and /reports after approved synthetic posting, reversal and remittance | Actual date/amount/SRC and separate correction evidence; collected/remitted/unremitted remain server-derived; pending items do not count as posted cash. |
| Layout/accessibility | Head widths 1440/1024/768/390; Windows logical 360x800 plus 320/390/430/768 widths; keyboard Tab/Escape | Drawer header/footer/scroll, focus, currency and errors remain usable; no clipped action or page-level horizontal overflow. |

Record browser/app build identity, tenant fixture IDs, role, viewport, screenshots, operation/SRC IDs and before/after report evidence for each accepted row. Use only synthetic data; do not perform live registration, correction or collections to satisfy this checklist.

### Remaining cutover gates

1. Separate approval/review/application of the typed financial migration against the accepted retained-schema baseline; foundation approval is not financial-schema approval.
2. Explicit coordinated runtime/tenant activation approval after the above checklist passes. Default NpmRentMonthRuntime remains disabled, unbound to configuration, with active EF financial properties still ignored.
3. Office decision for official cross-period RCD treatment; weekly allocation semantics remain unavailable unless separately approved.
4. Explicit reconciliation of unsupported historical monthly opening evidence and custody-preserving collector-owned replacement. No automatic legacy conversion, refund or cash reassignment.
5. Acceptance-scale performance and export/visual review using the intended LGU dataset and declared report bases. The synthetic benchmark is not production certification.

Online month-rent posting remains unavailable. The municipal database and its installed foundation were not queried or mutated in this task. No historical cash, SRC, remittance, migration history or retained table was changed.

### Exact closeout file scope

```text
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthRuntime.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Corrections.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.ReadModels.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.ReportBatch.cs
EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.cs
EEMOCantilanSDS.Application/Queries/Facilities/GetFacilityReports/GetFacilityReportsQueryHandler.cs
EEMOCantilanSDS.Application/Queries/Reports/GetFinancialReport/GetFinancialReportQueryHandler.cs
EEMOCantilanSDS.ComponentTests/Pages/NpmMonthReviewActionsTests.cs
EEMOCantilanSDS.Infrastructure/Repositories/Reports/FacilityReportsRepository.NpmMonth.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.Closeout.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthReportingTests.cs
EEMOCantilanSDS.Testing/Application/Reports/GetFinancialReportQueryHandlerTests.cs
EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs
docs/planning/NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md
```

### Closeout Git preservation

Branch remains `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100`. Entry had 173 pending paths; closeout has 178, none staged. SHA-256 comparison preserved every entry path and found changes only in the 15 paths listed above. No unrelated pending file changed. `git diff --check` passed. No commit, push, municipal database operation, API restart or financial activation was performed. Disposable PostgreSQL fixtures were cleaned up; the unrelated running RabbitMQ container was left untouched.

## Final acceptance preparation — 2026-10-18

This continues the PostgreSQL closeout; it does not reopen migration lineage or install another schema. Entry: same branch/HEAD, 178 pending paths, empty index. The retained municipal/AWS databases were neither queried nor changed. The installed foundation, active migration assembly/snapshot, restore-v2 guards and disabled runtime registration were preserved.

### Separate typed financial migration package

Review directory: [`../evidence/npm-financial-migration/`](../evidence/npm-financial-migration/). The exact operation and SHA-256 inventory is [`manifest.json`](../evidence/npm-financial-migration/manifest.json). Candidate ID: **20261018040000_AddNpmRentMonthFinancialSource**. This is a review-only candidate, compiled into the offline readiness executable, never the Infrastructure/API migration assembly.

| Artifact | SHA-256 |
|---|---|
| 20261018040000_AddNpmRentMonthFinancialSource.cs.review | 51acf9db837d8ca30b97faf3b52e64a400ed4dd84c2e717bca3e791c519c8b74 |
| 20261018040000_AddNpmRentMonthFinancialSource.Designer.cs.review | 6306fdd9ddbb7c2a65bafbe25fe719f0589ace24e9bdc7e7329fc1229fac37f4 |
| 20261018040000_AddNpmRentMonthFinancialSource.Up.sql.review | 11747d8d597589b679bfc6aabd6342c4b93dae2906f22486745d98e41c7f0180 |
| AppDbContextModelSnapshot.cs.review | 0f829ce15dad0c947d6e90ca74b5409c99c466845dbc67ef9bda4007ee7342f7 |
| Preflight.sql.review | a0df011cec9059afeae0bdaf1b19679eb88b8cbba44b5c6fcde896b669e6c8e2 |

The compiled candidate has **15 operations**; these are separate from the installed foundation's seven operations:

1. Drop the existing Review check on NpmRentMonths, and SourceShape checks on CollectionLines and CollectionAllocations (three operations).
2. Add nullable SettlementActivatedAtUtc, SettlementActivatedByUserId and SettlementSealedAtUtc to NpmRentMonths, and nullable NpmRentMonthId to CollectionAllocations (four).
3. Add Activation, expanded Review and Sealed month checks; expanded line SourceShape; allocation NpmMonthIdentity and expanded SourceShape (six).
4. Add nonunique IX_CollectionAllocations_MunicipalityId_NpmRentMonthId (one).
5. Add restrictive FK_CollectionAllocations_NpmRentMonths_MunicipalityId_NpmRentM~, joining MunicipalityId/NpmRentMonthId to MunicipalityId/Id (one).

No table, existing row, legacy index, retained object, source numeric value, historical migration or financial amount is rewritten. Existing null allocation references remain valid for other source kinds; source 17 requires a typed reference equal to SourceId. Existing parent keys and numeric(18,2) month amounts remain unchanged. Approval, eligibility, authorization and immutable financial revisions still require the coordinated application workflow; an FK/check alone does not authorize posting.

`tools/npm-financial-migration-readiness` generated the artifacts, then a separate rebuild/verify compiled and checked their migration metadata, designer target, snapshot, exact SQL, artifact hashes, transaction eligibility and money precision against the candidate model and active foundation snapshot. A connection interceptor rejects synchronous/asynchronous database opens; the executable has no apply mode, application host or seeder. Active compiled migrations remain **131**, ending at the foundation. Candidate Down throws before producing destructive rollback operations.

Verification returned offline_verified, 15 operations, designer/snapshot/SQL/hashes matching, Down blocked, zero database connections and no runtime activation. A temporary copied package with altered SQL was rejected with `Compiled/review SQL mismatch`; the original package remained untouched. This is offline package proof, not installation approval or a new retained-database rehearsal.

### Export correction and cross-period evidence

One narrowly scoped production projection was corrected: GetCollectionReportQueryHandler previously ignored FullPeriodRentObligation and recalculated activated coverage using a daily fallback. That produced 1,050 instead of 900 for a 35 installment and 900 instead of 1,900 for monthly-only rent. It also resurrected liability for an authoritative zero obligation. Three regression cases failed on the old projection before the fix. The export now uses the same authoritative nullable coverage as Month-End; a recorded zero remains zero and monthly-only rows retain a zero daily rate. Legacy/PureDays fallback calculations, assessments and writers are unchanged.

An additional in-process test uses the real workflow, FacilityReportsRepository and export query handler: 875 legacy cash plus an exact 25 payment settles August; a September reversal leaves the August exported row unchanged, reopens only 25 in the current source, retains the original Collection identity/received amount with a separate -25 correction and leaves 25 real daily records. The prior real PostgreSQL cross-period cases remain passing too.

Production ExportData component tests feed declared synthetic DTOs through its actual CSV generator and print markup. CSV row counts and sums reconcile with the printed subtotal; Print and Save as PDF invoke the established same print helper. Separate source-position tests prove the dates/calculations; these renderer fixtures do not create an alternative report API or approve official RCD policy.

| 1,000-row synthetic export input | CSV collected | CSV outstanding | Isolated render + CSV | Process-managed allocations during focused test |
|---|---:|---:|---:|---:|
| October as-known DTO | 875,000.00 | 25,000.00 | 819 ms | 88,351,912 bytes |
| October latest-corrected DTO supplied to renderer | 874,125.00 | 25,875.00 | 324 ms | 81,851,360 bytes |

The second input demonstrates rendering a changed supplied amount, not availability of a LatestCorrected selector on Export Data. The real export query's closed-month reproducibility is verified separately above. Its report label/cutoff and the official RCD interpretation must not be inferred from fixture filenames.

Chrome headless printed each actual component HTML fixture using the existing A4 landscape/zero page-margin rules, a private temporary profile and disabled network resolution. PyMuPDF reopened both PDFs and verified 1,000 distinct stall IDs exactly once, expected totals, A4 landscape dimensions and zero off-page text words. Each PDF has **44 pages**: 3,226,376 / 3,227,038 bytes, generated in 4,439 / 3,669 ms. First, second and final pages were rendered to PNG and visually inspected: continuation headings repeat, rows remain legible, totals stay aligned and no records are clipped. This is fixture-based browser print verification, not an authenticated portal or Windows visual signoff.

The account-summary export does not promise one SRC per stall or convert an OR to SRC. Blank receipt fields remain blank. Original SRC evidence is checked separately from canonical PostgreSQL Collections: the large fixture produces 300 distinct real computed SRC-2026 references with original IDs, amounts and business dates, exported as synthetic-src-evidence.json. It does not fabricate daily receipts or place an SRC in the legacy OR column.

### Large synthetic LGU performance

The owned disposable PostgreSQL fixture contains **1,000 stalls, 12,000 assessment months**, 200 fully paid August sources and 100 partially paid sources; all other months remain unpaid. Contract/source ownership and opening-manifest checks run through the same month report reader. Domain-created synthetic receipt evidence is used for report load, not as a new authorized posting route; full writer concurrency/replay/correction tests execute beside it.

Final full-run measurements: **108 SQL reads** over twelve 1,000-source months; **3,729 ms**; 159,429,120 managed bytes allocated; net retained managed-memory increase 656,384 bytes; process working set 300,417,024 bytes. Totals: assessed 10,800,000; received 215,000; outstanding 10,585,000. All 300 movements retain their actual October 2026 cash date while settling August 2025 liability. No daily rows or remittances are manufactured. The executable budget is at most 144 SQL reads and less than 256 MiB net retained managed-memory growth. An earlier isolated run measured 5,244 ms; timing is observational, not an SLA.

Memory counters are process-wide; they are not peak per-request memory or an allocation profiler. Full parallel Head-suite runs include allocations from unrelated components, so the table uses a separate focused export run. This closes the stated synthetic source-position/export scale check, not every LGU's multi-year report/print workload or native Windows memory acceptance.

### Prepared synthetic acceptance environment

Descriptor: [`preview-environment.json`](../evidence/npm-acceptance/preview-environment.json). Artifacts are outside Git under `%TEMP%/stalltrack-npm-acceptance/`:

- head-review.html: actual review drawer with mock client responses, showing the exact 25 balance; no mutation is invoked.
- mobile-summary-25.html, mobile-summary-20.html, mobile-summary-0.html: actual shared Mobile summary, no invented day count.
- Two 1,000-row HTML/CSV/PDF exports, first/continuation/final PNGs, PDF verification JSON, render metrics, large-LGU metrics and synthetic SRC evidence.

These files may be opened directly as inert fixture previews. They have no listening service, authentication session, database connection or functioning posting action. They are not the live system and do not constitute MAUI Windows acceptance. Recreate them using the focused component tests; render PDFs with tools/npm-financial-migration-readiness/render_acceptance.py after the PDF skill's operation marker. The normal Windows Debug endpoint remains localhost:5117; no shared API or client launch configuration was changed.

**Feature-on interactive preview needs explicit owner authorization** for a disposable authenticated preview host/tenant and a preview-only Windows endpoint arrangement. The ordinary Windows Debug app cannot safely exercise that host while hardwired to the shared localhost:5117 endpoint. Do not stop/repoint that shared service, alter its database or silently modify normal Debug settings. No such host, endpoint override, native Windows launch or municipal runtime activation occurred here.

Use the existing authenticated Head/Windows checklist above after that gate. Record server/build/tenant, role, viewport, quote version, stable operation/SRC, current versus historical report basis and screenshots. Required widths remain Head 1440/1024/768/390 and Windows 360x800 plus 320/390/430/768. Exercise exact 25/20/zero, stale versions, offline retry, credit confirmation, replacement, source history and print/export. Static fixture preparation does not mark any of those interactive acceptance rows complete.

### Office decisions and permanent-application approval checklist

The accepted retained-schema baseline is settled; no further general archaeology is requested. Remaining decisions are specific:

1. **Official cross-period RCD**: Office must choose revised original report, later-period adjustment or its approved alternative. AsOf and LatestCorrected are technical evidence views, not that ruling. Do not certify the synthetic PDFs as official RCDs.
2. **Weekly compliance**: no approved allocation of a Whole payment to weeks/days exists. Keep the specific monthly/yearly guidance failure; do not spread money into invented daily receipts.
3. **Historical reconciliation/custody**: ambiguous handovers, incomplete monthly opening evidence, historical excess and collector-owned replacement require reviewed evidence and custody policy. No automatic conversion, refund or reassignment.
4. **Online posting**: remains unavailable for activated month rent until its gateway/accounting contract is separately verified and approved.

For the financial migration, request approval of the exact manifest separately from preview or runtime cutover. The controlled application procedure must:

- Reverify target identity, original 143 historical IDs plus foundation (expected 144 applied), compiled 131 baseline, parent relationships, retained tables, index correction and all hashes. Preflight.sql.review is read-only and was not executed against the retained database in this task.
- Create/verify a fresh complete backup including retained objects; establish writer quiescence. Explicitly prevent API startup migration/seeding without restarting the service for convenience.
- Promote the reviewed migration/designer/snapshot together with the candidate financial mapping, keeping runtime disabled. Rebuild and prove only this candidate is pending; expected future counts are 145 applied / 132 compiled. Do not replay missing historical migrations or rewrite history.
- Use a separately reviewed migration-only execution path, bounded statement/lock timeouts and one transactional application. ALTER TABLE/check validation and normal index creation can block writers; schedule accordingly. No concurrent/nontransactional index operations are in this package.
- Durably record intent before COMMIT. On uncertain acknowledgement, never retry blindly: independently inspect history, objects, constraints and fingerprints from a fresh read-only connection.
- Independently verify exactly the approved objects, unchanged original financial rows/SRC sequence/retained schema, additive history entry, disabled runtime and restore-v2 fail-closed behavior; take a post-install backup. A successful install alone does not authorize source preparation or collection.
- Recover a failed uncommitted transaction by rollback. After a committed install, do not invoke destructive Down: use an approved forward repair, or a separately authorized full-backup recovery after assessing all subsequent writes.

**Concise approvals still needed:** (a) exact financial-schema promotion/application, (b) isolated authenticated feature-on preview and Windows endpoint arrangement, (c) Office report/reconciliation decisions for affected cases, and (d) separately scoped municipal financial activation only after acceptance. None is implied by this preparation.

### Executed regression evidence and scope

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Full unit suite, final Release run | 2,869 | 0 | 0 |
| Full Head components | 1,266 | 0 | 0 |
| Full Mobile components | 146 | 0 | 0 |
| Isolated NPM financial PostgreSQL, including large LGU case | 24 | 0 | 0 |
| Focused export query tests (included in units) | 5 | 0 | 0 |
| Closed-month export/reversal composition (included in units) | 1 | 0 | 0 |
| Focused 1,000-row CSV/print renderer rerun (included in Head) | 2 | 0 | 0 |

API and Client Release builds passed, final incremental results 0 warnings / 0 errors. Offline candidate verification and the tampered-copy rejection passed. Rebuilt test dependencies retain existing compiler/analyzer/package warnings. No new package dependency was introduced; the offline tool uses the same EF Design version as existing readiness tooling. PDF rendering uses the installed Chrome and Python/PyMuPDF tooling outside the application.

Commands:

```powershell
dotnet run --project tools/npm-financial-migration-readiness/Readiness.csproj -c Release --no-restore -- verify
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --no-restore
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~NpmRentMonthFinancialTests
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore
```

Evidence logs are in local TEMP: npm-acceptance-migration-generate/verify/final/negative.log; npm-acceptance-export-red/green.log; npm-acceptance-cross-period-export.log; npm-acceptance-large-lgu.log; npm-acceptance-postgres-final.log; npm-acceptance-unit-final.log; npm-acceptance-head.log; npm-acceptance-mobile.log; npm-acceptance-export-isolated-final.log; npm-acceptance-pdf.log; npm-acceptance-api-build.log; npm-acceptance-client-build.log. The initial compile/mock setup mistakes were repaired before the genuine three-case red assertion run; only final executions are counted above.

Exact task scope:

```text
EEMOCantilanSDS.Application/Queries/Reports/GetCollectionReport/GetCollectionReportQueryHandler.cs
EEMOCantilanSDS.Testing/Application/Reports/GetCollectionReportQueryHandlerTests.cs
EEMOCantilanSDS.Testing/Application/Payments/NpmMonthReportingTests.cs
EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.Acceptance.cs
EEMOCantilanSDS.ComponentTests/Pages/NpmExportAcceptanceTests.cs
EEMOCantilanSDS.ComponentTests/Pages/NpmMonthReviewActionsTests.cs
EEMOCantilanSDS.Mobile.ComponentTests/NpmAcceptancePreviewTests.cs
tools/npm-financial-migration-readiness/Readiness.csproj
tools/npm-financial-migration-readiness/Program.cs
tools/npm-financial-migration-readiness/render_acceptance.py
docs/evidence/npm-financial-migration/20261018040000_AddNpmRentMonthFinancialSource.cs.review
docs/evidence/npm-financial-migration/20261018040000_AddNpmRentMonthFinancialSource.Designer.cs.review
docs/evidence/npm-financial-migration/20261018040000_AddNpmRentMonthFinancialSource.Up.sql.review
docs/evidence/npm-financial-migration/AppDbContextModelSnapshot.cs.review
docs/evidence/npm-financial-migration/Preflight.sql.review
docs/evidence/npm-financial-migration/manifest.json
docs/evidence/npm-acceptance/preview-environment.json
docs/planning/NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md
```

Final preservation check: **178 -> 193 pending paths; 18 scoped changed/added paths; none staged; HEAD 17b9d100 unchanged**. No entry path disappeared and no hash changed outside the inventory above. The installed foundation source/snapshot, financial candidate mapping, dependency injection and restore safeguards are byte-identical to entry. `git diff --check` passed. Default runtime remains `new NpmRentMonthRuntime()` with all gates false, and active EF mappings still ignore the four candidate financial properties. No commit/push, municipal/AWS connection, API restart, native Windows launch, financial migration promotion/application or live activation occurred. Owned test containers were cleaned up; the unrelated RabbitMQ container remains untouched.

## 2026-10-20 — Prospective full opening-month assessment

### Decision and compatibility boundary

Owner-requested implementation, **not municipal billing activation**. Entry: primary checkout,
`codex/local-unified-v8-v10-20261013`, HEAD `17b9d100`, 606 pending paths. Existing concurrent work is retained.
No shared-account lifecycle file is edited by this task. Initial compilation encountered incomplete lifecycle
changes; subsequent compilation succeeded without modifying those files.

The legacy formula remains `daysHeld >= daysInMonth ? monthlyRent : min(monthlyRent, dailyFee * daysHeld)`.
Thus October 20–31 at 30/day produces 360 under the historical interpretation. The legacy Whole loop also
stops at the last complete installment fitting the balance (875 or 980 in the archived F-01 examples).
Neither rule is globally rewritten. Archived characterization remains meaningful.

The new source rule is `ApprovedFullOpeningMonthV1`, recorded inside the existing `NpmRentMonths.RateEvidenceJson`.
It uses the existing typed month allocation/canonical Collection engine for exact settlement. There is no
second ledger, invented day, mutable paid-status substitution or generic RecordPayment routing.

Eligibility requires all of:

- Existing coordinated `NpmRentMonthRuntime` schema/path/tenant gates, plus an explicitly supplied
  `NpmOpeningMonthPolicies` approval. The latter defaults to no approvals and is not bound to settings.
- A verified same-tenant NPM DailyStall/RentGoal facility, new active contract and opening calendar month;
  the approval identifies its actor, reference and effective date and predates the recorded occupancy.
- One responsible occupancy, held through the month end. Shared/handover/terminated months fail closed.
- No preexisting daily or monthly assessment/payment records and no earlier month source.
- An explicit positive `NpmMonthlyStall` rate resolved through `IFeeRateResolver` at occupancy start,
  matching one persisted tenant-owned FacilityRate row. The approved daily installment is resolved for that
  stall/section at the same date. Custom-section monthly authority is not inferred from its daily fee.

Frozen evidence includes tenant/facility/stall/contract, occupancy and rate dates, monthly rate ID and
effective date, approved monthly/daily amounts, rate recorder/timestamp, policy actor/date/reference and
descriptive registration/contract amounts. A mismatch is retained as evidence, not promoted into a tariff.
Later tariff edits do not reprice that assessed opening month or its installment arrangement.

### Implemented contracts and source coordination

- `NpmRentMonthWorkflow.ReviewOpeningMonthAsync`: nonmutating authoritative preview; after preparation
  it returns original evidence rather than recomputing it with today's tariff.
- `PrepareOpeningMonthAsync`: Head-only, serializable source creation and audit, database uniqueness;
  no Collection, SRC, OR/CT, DailyCollection or remittance creation. A database/COMMIT acknowledgement
  failure is reported as unconfirmed and is never automatically retried.
- `GET api/DailyCollections/npm-month/opening-assessment?stallId=…&contractId=…&period=yyyy-MM-dd`
  and `POST …/prepare-opening` (`StallId`, `ContractId`, `PeriodStart` only) are Head/SuperAdmin-only.
  Typed `IDailyCollectionApiClient` / `DailyCollectionApiClient` methods preserve that contract.
- Review/activation revalidates approval, source rate and credits. It does not accept the old prorated
  amount as authority for a prospective source. Activation remains the existing explicit internal review
  operation; no public automatic activation hook is added to registration.
- Daily, Part and Whole use the existing monthly quote/version/token/post pipeline. Whole quotes exact
  outstanding; Part keeps the earned collection boundary and requires both existing Part enablement and
  explicit policy approval. Daily remains tied to a real eligible occupied date, capped by remaining rent.
- The shared source position owns prospective earned arithmetic. Before the existing month-end cutover,
  only actual occupancy days accrue at the frozen installment, capped by assessment and reduced by credits.
  Full opening gross rent is not automatically classified as immediate delinquent debt.
- Current and historical source reports use the same evidence with their existing cash/correction/credit
  cutoffs. The stall-holder roster now reads frozen prospective opening rates in a batch instead of today's
  tariff. Its yearly amount remains an annualized reference rate, not twelve assessments or collected cash.
- Head's existing monthly review drawer exposes collapsed rate provenance only when returned by the
  server. Existing Mobile Whole summary continues to display server monthly obligation/received/balance;
  no device-side calculation or new source discovery is introduced.

### Migration and activation limits

No additional persistence migration is required for this policy/provenance: existing foundation JSON,
amounts, ownership, audit and version fields carry it. The **separate, uninstalled typed financial schema
candidate remains required for posting**. This task does not promote/apply it or edit the active EF snapshot,
foundation migration, history, restore safeguards or default runtime registration.

There is deliberately no automatic reassessment from an existing registration form. Real-LGU acceptance
must separately approve the exact financial schema, verified monthly tariff, tenant/facility/effective-date
policy and source preparation/reconciliation/cutover. An approval alone is not activation of every new
registration. Custom-section or agreement-specific monthly authority without adequate evidence, shared
month allocation, early termination and historical reconciliation remain explicit Office gates. Online
month-source posting retains the existing fail-closed gateway/accounting boundary.

### Executed validation

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Focused NPM/legacy characterization units | 89 | 0 | 0 |
| Full unit suite, final | 3,044 | 0 | 0 |
| Isolated PostgreSQL monthly financial + daily canonical + collection sessions | 220 | 0 | 0 |
| Additional opening endpoint/identity contract guard | 1 | 0 | 0 |
| Additional PostgreSQL Head/Mobile/native-discovery/offline cross-path acceptance | 1 | 0 | 0 |
| Focused Head monthly review / monthly-market / export components | 13 | 0 | 0 |
| Full Head component suite | 1,374 | 0 | 0 |
| Full Mobile component suite | 153 | 0 | 0 |

Docker reported Linux engine 29.5.3. PostgreSQL tests used the existing Testcontainers fixture:
`postgres:16-alpine`, database `stalltrack_tests`, dynamically assigned non-5432 port. The candidate financial
mapping was installed only in that disposable fixture by its existing guarded model-difference mechanism.
No retained or AWS database connection, reset, migration or seed command was used.

New executable cases cover October 1/20/31, February 28/29 and 30-day months; 900/30, 900/35 and 1000/35;
descriptive stall/contract rate mismatches; no stated monthly rate; disabled/missing/wrong-tenant/late/future
approval; PureDays rejection; prior daily evidence; handover conflict; concurrent month creation and final
payments; stale quotes; frozen tariffs; changed rate before activation; full opening-span absence credit;
current/as-of source position; frozen roster/annualized reference and facility compliance/revenue separation.

The additional cross-path case creates a synthetic registered stall/contract and tariff, prepares/reviews
the month, assigns a real synthetic Collector, and exercises the actual offline-sync handler twice against
PostgreSQL. One 30 installment produces one Collection/SRC. Head and Mobile Whole adapters agree on 900 gross
and 870 remaining; native Occupancy discovery offers that exact 870. Mobile Whole posting closes the source.
Two distinct SRCs total 900, both attributed to the Collector, with no DailyCollections or remittances.
Only cache effects and unused legacy/market dispatch dependencies are stand-ins; rates, source discovery,
monthly posting, allocations, idempotency and financial evidence use the real database/workflows.

Regression sensitivity was demonstrated by temporarily restoring the legacy proration expression in the
new assessment branch: **5 failed / 1 passed / 0 skipped** across the six opening/calendar cases. The full-rent
branch was restored before final passing checks. The first full unit run had **3,043 passed / 1 failed**:
the architecture inventory required the new EF-using workflow partial to be listed. Its existing source-owned
transaction exception was extended with a specific rationale; no financial assertion was relaxed.

Release builds: API **0 warnings / 0 errors**, Client **0 / 0**, Windows Mobile **27 warnings / 0 errors**.
The Windows warnings concern existing shared nullability/unused-member issues; no APK was produced or advertised.
The checks use separate `NpmOpening*` output/intermediate directories. Source diff whitespace and unchanged
monthly-review scoped-CSS brace checks pass. No authenticated browser/native visual acceptance was performed;
no service was started/restarted to obtain it. The code is not a declaration of live financial cutover.

Reproduction commands (PowerShell; use the named output/intermediate paths to avoid concurrent outputs):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpeningUnits/net9.0/ -p:IntermediateOutputPath=obj/NpmOpeningUnits/
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpening/net10.0/ -p:IntermediateOutputPath=obj/NpmOpening/ --filter 'FullyQualifiedName~NpmRentMonthFinancialTests|FullyQualifiedName~NpmDailyCanonicalTests|FullyQualifiedName~CollectionSessionTests'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpeningHead/net10.0/ -p:IntermediateOutputPath=obj/NpmOpeningHead/
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpeningMobileTests/net10.0/ -p:IntermediateOutputPath=obj/NpmOpeningMobileTests/
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpening/net10.0/ -p:IntermediateOutputPath=obj/NpmOpening/
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore -p:OutputPath=bin/NpmOpeningHead/net10.0/ -p:IntermediateOutputPath=obj/NpmOpeningHead/
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore -p:OutputPath=bin/NpmOpeningRelease/ -p:IntermediateOutputPath=obj/NpmOpeningRelease/
```

The two additional integration checks were executed separately after the 220-case run. Running the complete
filter now also includes them; the recorded 220 is the actual earlier run, not an invented combined-run total.
Local detailed logs are `%TEMP%/stalltrack-npm-opening-*.log`.

### Exact task source inventory and acceptance boundary

New:

- `EEMOCantilanSDS.Application/Common/Payments/NpmOpeningMonthPolicy.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.OpeningMonth.cs`
- `EEMOCantilanSDS.IntegrationTests/NpmRentMonthFinancialTests.OpeningMonth.cs`

Updated existing paths, preserving their earlier pending changes:

- `EEMOCantilanSDS.Api/Controllers/Collections/DailyCollectionsController.cs`
- `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IDailyCollectionApiClient.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthRuntime.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Preparation.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.ReadModels.cs`
- `EEMOCantilanSDS.Application/Common/Payments/NpmRentMonthWorkflow.Credits.cs`
- `EEMOCantilanSDS.Client/Components/Shared/NpmMonthReviewActions.razor`
- `EEMOCantilanSDS.ComponentTests/Pages/NpmMonthReviewActionsTests.cs`
- `EEMOCantilanSDS.Domain/Common/Billing/NpmRentMonthPosition.cs`
- `EEMOCantilanSDS.HttpClients/ApiClients/DailyCollectionApiClient.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.Register.cs`
- `EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthPositionTests.cs`
- `EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs`
- `docs/business/EEMO_BUSINESS_RULES.md`
- This consolidated handoff.

**Acceptance status:** prospective source implementation and isolated backend verification complete for the
eligible facility-rate path; **real deployment/cutover and full interactive registration-to-payment visual
acceptance are not complete**. The Head registration drawer is not automatically wired to activate a source.
It can use the new typed preview/preparation contracts in a separately accepted activation workflow. Ordinary
municipal registration and legacy billing remain unchanged. In particular, an existing Morloc record is not
repriced by this deployment. Custom/agreement-specific monthly authority and ambiguous lifecycle allocation
remain fail-closed rather than being silently inferred from form values or a new tariff.

Before real activation: approve the separate typed financial schema and its controlled installation;
verify the LGU's explicit monthly tariff and approval evidence; approve the tenant/facility effective policy,
review each eligible source/opening manifest, complete authenticated Head/Windows acceptance, and obtain
financial cutover authorization. Existing history conversion, arrears repricing, online gateway accounting,
new due dates and shared-month allocation are not authorized by this implementation.

Final Git checkpoint: **611 pending paths, none staged; branch and HEAD remain unchanged**. All edits by this
task are the 19 paths listed above. The entry-to-exit status comparison also shows five previously untracked
shared-lifecycle paths no longer pending (`AccountLifecyclePanel.razor`/CSS, `LifecycleHistory.razor`/CSS,
`AccountLifecyclePanelTests.cs`); these belong to the concurrent lifecycle work and were not edited, removed
or restored by this task. No clean/reset/stash/branch switch, commit or push was used. The installed foundation,
financial candidate mapping, active snapshot/migrations, restore safeguards and default runtime registration
were not edited by this task.






---

### 2026-10-20 — Working-copy NPM activation takeover and deployment (post-Codex)

**Scope:** Completed the pending, test-only NPM activation and Head registration integration on the isolated working copy. This supplement supersedes the earlier “uninstalled schema / no service restarted” status **for the working copy only**; production and original municipal DB status are unchanged.

**Implementation repairs:**
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/NPM.razor`: replaced incompatible `EventCallback` method group with typed `OnOpeningMonthChanged` callback; Head Client Release now builds.
- `EEMOCantilanSDS.ComponentTests/Pages/NpmOpeningMonthReviewTests.cs`: completed the bUnit drawer's common DI setup (Setup, Stalls, Payments, Branding) without weakening tests.
- `EEMOCantilanSDS.Api/WorkingCopyNpmMonthlyRegistration.cs`: parse signed UTC approval evidence using round-trip UTC semantics rather than Windows local binder conversion; perform the facility check using mapped `BillingArchetype.DailyStall` instead of unmapped `IsMonthlyRentMarket`. Added an exact-ID, exact-facility, TEST-reference exception for the **restored Cantilan working-copy demonstration only**; existing dedicated `npm-test-*` tenants remain supported. Startup still refuses wrong environment/DB/port/schema/actor/tenant/rate-policy.

**Database:** After confirming the isolated PostgreSQL host port `55491` and database `stalltrack_workcopy_main`, created backup `%TEMP%/stalltrack-workcopy-main-20261018/npm-rescue-before-financial-20261020.dump` (1,798,788 bytes; SHA256 `BFFB028BCBD3BD8E9B64E550DB09E8514691C34EF6E279A88BF0F90E86CCA3E2`). Installed **only** `20261020050758_AddNpmRentMonthFinancialSource20261020` inside a single explicit PostgreSQL transaction from generated, reviewed EF migration SQL. Read-only verification confirmed migration history, three `NpmRentMonths` activation columns and the nullable `CollectionAllocations.NpmRentMonthId`; initially zero month-source records. Previously recorded obligation/collection history was not backfilled or repriced. Test-only Cantilan NPM approved monthly `FacilityRate` was seeded at ₱900 effective 2026-10-20, retaining its original approved ₱30 daily installment. Seed was guarded to fail if that rate already existed.

**Test policy and deployment:** Local-only policy at `%TEMP%/stalltrack-workcopy-main-20261018/npm-rescue-demo-policy.json`, reference `TEST-WORKCOPY-CANTILAN-NPM-20261020`, for Cantilan's restored-copy municipality `0ea98ba6-f7b1-4928-96f1-6177d45741bd` and its NPM RentGoal facility `67c13967-de75-4b31-8939-2c7c04e99e71`. Existing historical occupancies are intentionally ineligible for prospective full-opening-month activation. API and Client were published into staged bundles and deployed only to the owned working-copy API (`https://localhost:7197`) and Head Portal (`https://localhost:7267`), with backed-up previous builds. Normal launcher `%TEMP%/stalltrack-workcopy-main-20261018/start-workcopy.ps1` now restores the isolated TEST policy on ordinary restart; its existing database/owner/port safety guards are retained and the API performs its own additional startup checks. Normal restart was exercised and succeeded. API `/health` and `/health/ready` returned 200, Portal `/login` returned 200, `/npm` redirected unauthenticated users to login, and API refused anonymous monthly endpoints with 401. Log explicitly recorded successful test-policy startup. **No deployment or writes against port 5432, AWS or production.**

**Fresh validation on takeover:**
- Head Client Release build: 0 errors.
- API Release build and publish: 0 errors.
- NPM unit subset: **141 passed / 0 failed**.
- Head NPM review component subset: **9 passed / 0 failed**.
- NPM PostgreSQL financial integration: **51 passed / 0 failed**, including full 900/30 monthly payments, 900/35 and 1000/35 precise remainders, credits, SRCs, idempotent retries, concurrent settlement, and tenant/source ownership.
- Separate PostgreSQL predeployment run: **51 passed / 0 failed**.
- Build diagnostics still include unrelated existing warnings; these are not silently classified as fixed.

**Outstanding interactive acceptance:** A brand-new occupancy created **after** test approval must still be registered by an authenticated Head, reviewed/activated through the new Head drawer, and collected with the Windows Mobile user session, demonstrating ₱900 → ₱870 after a real ₱30 payment → ₱0 after the Whole Payment. Existing Morloc history remains on the legacy interpretation. This browser/native login-and-payment demonstration was **not executed** by this remote session, which had no authorized login credentials or browser automation. The PostgreSQL end-to-end synthetic test covers the same financial handlers and Collection/SRC invariants but does not substitute for actual authenticated client UX. Do not label this last acceptance gate passed until observed.

**Concurrent-work precautions:** Claude's rental-lifecycle commands, source data and shared database on port 5432 were not modified. No git reset, stash, commit or push. The working tree remains concurrently edited by other agents.


### 2026-10-20 — Authenticated deployed API and Head browser acceptance: PASSED

This section updates the prior interactive-acceptance limit **for the deployed Head Portal and API only**; a physical Windows Mobile UI click-through is still a distinct unverified step.

- Ran a new, explicitly synthetic NPM registration through the deployed `POST /api/Stalls` using an actual authenticated **temporary test Head login** on the working-copy DB (`55491` only). Final successful registration: `DEMO-NPM-73AA92C1`, stall ID `2d4dc4ee-c12a-4e22-aee7-2fd397b501e9`, test occupant named as TEST. The original Morloc record was not changed.
- Deployed Head-only opening-month assessment returned **₱900 gross** and approved ₱30 daily installment. The real Head API prepared and activated the financial source without any Collection at preparation/activation.
- Deployed Whole Payment quote was ₱900. Deployed daily quote was ₱30; one authenticated `POST /api/DailyCollections/record` successfully posted ₱30. Whole quote refreshed to **₱870 remaining**. Authenticated `POST /api/DailyCollections/npm-month/whole-payment` posted the exact ₱870, creating canonical SRC `SRC-2026-000035`. Reusing the same client operation ID was correctly reported as an existing payment, with no duplicate. Final balance quote was ₱0.
- Independent **read-only PostgreSQL reconciliation** on the exact working-copy DB confirmed `NpmRentMonths.AssessedAmount=900.00`, credits `0.00`, financially activated state, and **two CollectionAllocations totaling ₱900.00** for this stall. All temporary test Head login records were disabled afterward; read-only SQL confirmed **zero active** temporary test accounts.
- Launched genuine headless Microsoft Edge with Playwright from a temporary test harness outside the repository. A fresh temporary Head signed in through the **actual Head Portal login UI** (`https://localhost:7267/login`), navigated to `https://localhost:7267/npm`, and verified the rendered New Public Market dashboard and Stall Holder List including `DEMO-NPM-73AA92C1`. Screenshot: `%TEMP%/stalltrack-workcopy-main-20261018/npm-head-portal-browser-acceptance.png` (220,831 bytes). Temporary browser Head login was disabled afterward.
- Live services remained healthy (`/health` HTTP 200, `/health/ready` HTTP 200, Portal login HTTP 200). No production or retained municipal database was touched. No commit or push.
- **Limitation:** This is real authenticated Head/API posting plus Head-browser page acceptance, not an automated Windows Mobile GUI tap sequence. NPM financial posting handlers are shared with Mobile, and the existing Mobile component/PostgreSQL cross-client tests passed earlier, but the device UI still merits a final manual glance. One earlier `DEMO-NPM-31043C4B` registration was left unassessed following a test-runner peso-symbol encoding failure; it has no claimed payment success and is TEST-labeled.

Log files for exact test execution are kept in `%TEMP%/stalltrack-workcopy-main-20261018/npm-live-acceptance.log` and `npm-browser-acceptance.log`; temporary harness scripts are there too, not in the shared repository.


### 2026-10-20 — Morloc Reyes existing-occupancy Mobile charge picker recovery

A working-copy Mobile screenshot showed Fish/Meat, Weight & Measure, WCF and ECF under active Fish vendor Morloc Reyes, but no NPM stall-rent charge despite an office-confirmed NPM occupancy link. Read-only PostgreSQL review on loopback port 55491 confirmed vendor registration `658839ec-53a3-4659-91d5-3b03441e8e42`, current confirmed association to NPM Stall 3 (`0fe710a6-4e0c-4618-a3e6-839885262da4`, Contract `20ef417c-6aca-4353-af2a-9d1bfc68f62d`), approved/stored stall daily rate ₱30 and descriptive monthly rate ₱900. Morloc's occupancy was created `2026-10-20 02:34:48 UTC`, *before* the working-copy-only policy approval `2026-10-20 05:39:32 UTC`. His October NpmRentMonth source does not exist. No daily payment is already posted for October 20. The new prospective ₱900 full-opening-month authority therefore does not retroactively apply to this existing registration.

**Root cause:** `NpmWholePaymentWorkflow`, `NpmPartPaymentWorkflow`, `NpmDailyBatchWorkflow`, `NpmDailyCanonicalPoster`, and `CollectionSessionSources.Native` previously used tenant-wide `monthly.Enabled` to route ALL NPM rent charges into the new source, even where the specific legacy stall had no NpmRentMonth, causing no usable picker quote. The old source remains canonical for this existing registration.

**Correction:** New source-owned `NpmRentMonthWorkflow.MustUseMonthlyAuthorityAsync(stall, period)` routes by tenant/stall/month source and original prospective registration authority. An existing monthly source (even pending activation) or a registration made under a valid approved prospective policy is protected from legacy fallback. Existing/pre-approval accounts without such a source use the original daily/whole computation and canonical collection writer. All affected quote and post entry points use the same decision; canonical poster still explicitly refuses activated-month fallback. No synthetic month, debt reassessment, or financial record change was made to Morloc.

**Validation:** API Release compiled with 0 errors; two focused PostgreSQL integration tests passed, including the period authority regression and linked vendor source identity; further **52 NpmRentMonthFinancialTests passed with zero failures** on disposable PostgreSQL. The repaired API was published and started on the *same isolated working copy* using an audited launcher with an archived previous API bundle under `%TEMP%/stalltrack-workcopy-main-20261018/publish-api-main-backup-morloc-rent-20261020-183745`. Final checks: `/health`, `/health/ready`, Client login and Mobile HTTP port 5117 all HTTP 200; original NPM test policy remains enabled for the approved test tenant/facility; Morloc original DB fields and zero month/month-day payment records unchanged. Existing Client/Mobile binaries were not changed. The user should close and reopen Mobile Select charges to force a fresh discovery request; rendered confirmation of Morloc's rent option in the actual Windows Mobile view is pending.

**Strict limitation:** This correction only restores collection eligibility to the preapproved/legacy billing basis for historical accounts. A full ₱900 opening-month assessment for Morloc is not established by the source's saved descriptive monthly amount; that would require separate reviewed, explicitly authorized historical treatment and is not inferred by the picker.

### 2026-10-20 — Automatic NPM opening-month assessment on Head registration

**Owner-approved UX:** Register a new eligible NPM Meat/Fish stall occupant once, without a second opening-month authorization drawer. Reuses the pre-existing tenant-approved financial policy and the authenticated registering Head as the activation actor; does not fabricate new policy approval or payment.

**Source implementation:** CreateStallCommandHandler calls NpmRentMonthWorkflow.ActivateRegisteredOpeningAsync during the same serializable transaction as the stall/contract write. OfficeCollectionWorkflow.CreateNpmVendorOccupancyAsync already wraps its Meat vendor-to-NPM registration and association in an outer serializable transaction; the shared handler participates in it without nesting a second transaction. Prospective rate/credit/identity validations, source uniqueness, Head authorization and payment safety remain mandatory. If an eligible opening cannot be activated, the registration is rolled back instead of silently becoming uncollectible. Older pre-approval records and noneligible/custom-section arrangements keep their original billing paths. The audit decision is derived from the signed tenant policy reference and verified contract identity, marked AUTO-REGISTRATION, never from a form amount.

**Client:** Removed automatic NpmOpeningMonthReview popup and Review opening-month rent action from the NPM registration page and stall profile. Existing review/repair API remains for exceptional authorized recovery. Financial activation creates no Collection, DailyCollection or SRC, and does not make the entire assessed month overdue.

**End-to-end canary against isolated working-copy PostgreSQL and authenticated deployed-configuration API:**
- Direct NPM registration of synthetic DEMO-NPM-C1D55BE8: automatically assessed and activated ₱900 before create response; collected ₱30 Daily then ₱870 Whole, reached ₱0; exactly two canonical allocations totaling ₱900, replay did not duplicate cash; temporary Head login disabled. Log: %TEMP%/stalltrack-workcopy-main-20261018/npm-auto-opening-acceptance.log.
- Registered Meat vendor to new NPM linked occupancy DEMO-NPM-D9D0F3EE: the same authenticated Head operation atomically created the occupancy/association and approved ₱900 source without an extra Head popup; collected ₱30 then ₱870, final ₱0, exactly-once SRC; temporary Head login disabled. Log: %TEMP%/stalltrack-workcopy-main-20261018/npm-auto-linked-occupancy-acceptance.log.
- Unit: 13 passed. Head component: 3 passed. PostgreSQL financial regression: 52 passed. All had 0 failures. API/Client Release builds and publishes succeeded. Broad full suites were not rerun.
- Authenticated Microsoft Edge verification against the final deployed Head Portal confirmed /npm and /profile/npm/<test-stall-id> render with no manual opening-month rent button or drawer. Screenshot: %TEMP%/stalltrack-workcopy-main-20261018/npm-auto-opening-final-browser.png. Browser log: npm-auto-opening-browser-check.log.

**Deployment:** Working-copy API https://localhost:7197, Head https://localhost:7267, local Mobile API http://localhost:5117. One initial staged-folder replacement was blocked by the running isolated test canary locking its own DLL; the canary was explicitly stopped, both staged builds installed, previous API/Client bundles saved as dated publish-*-main-backup-auto-npm folders, and the standard verified launcher restored services. Health, readiness and Client login returned HTTP 200 and the isolated NPM test policy remained enabled after restart. No original municipal port 5432, AWS/production, git commit/push/reset/stash or historical financial repricing.

**Operational boundary:** Prospective full opening-month assessment requires preexisting tenant/facility approval, current Head rights, and an eligible new occupancy. Unsupported cases retain original billing; eligible cases fail registration visibly if new monthly source cannot be activated, rather than hiding a second unresolved approval.

**Final exceptional-account safeguard (same working-copy release):** Existing NPM occupancies that were registered before the automatic-registration deployment may have a missing/unactivated opening-month source. The normal NPM registration page stays popup-free, but the Stall Profile retains the already-existing Head-only Review opening-month rent action exclusively when the server confirms an eligible opening has not been activated. A fully activated opening never shows this action. A final incremental Release Client publish and guarded launcher restart completed successfully; actual browser smoke on the redeployed NPM dashboard and automatically activated Stall profile again confirmed no opening-month prompt. No additional account was repriced or payment recorded by this final UI safeguard.

### 2026-10-20 — Mobile amount collection and cross-period rental receipts

This continues the accepted automatic registration implementation above. No foundation migration, historical repricing or repeated activation of the shared preview was performed for this task.

**Collector presentation and contracts**

- `Market.razor` uses `NpmRentAmountForm` for rental amount entry. The approved daily installment remains separate from editable money received. Exact Daily retains its established operation; other amounts require a server quote. Not Collected/Absent remain operational choices under their existing source rules. Miscellaneous utility entry was removed from this sheet; utility and registered-vendor weighing shortcuts use the existing source-native collection workspace.
- Outstanding rent lists account/payor, stall, section and server balance, including separate responsible contracts after handovers. Blocked accounts show an Office review reason rather than a fabricated zero. Selecting an account opens the same amount form used by `NpmWholePayment.razor`; the normal form has no month/year selector and allocation details are collapsed.
- An empty normal New Collection session's Whole payment selection opens that same page with the server-provided stall and occupancy IDs. Existing correction and composed-session paths retain their specialized contracts.
- Collector-only endpoints: `GET /api/Mobile/npm/outstanding/accounts`, `GET /api/Mobile/npm/outstanding/quote?stallId=...&contractId=...&amount=...`, and `POST /api/Mobile/npm/outstanding/payment`.
- `NpmOutstandingRentPayment` requires `ClientOperationId`, `StallId`, `ContractId`, `BusinessDate`, `Amount`, `QuoteToken`. Quote periods contain source kind, period start, original outstanding, applied and remaining. No client-supplied tenant, tariff, paid-day list or physical receipt number authorizes the payment.

**Financial integration**

- `NpmOutstandingRentWorkflow` reuses Whole/Part quotes and existing day/month writers, in chronological order within the explicitly selected responsible occupancy. It never creates an assessment. It stages all source effects inside one serializable transaction and uses the existing canonical poster/coordinator once: one actual rental payment creates one immutable Collection/SRC with period-specific lines and source allocations.
- The monthly writer's reviewed draft builder is shared with existing posting. Its internal permit supports the exact authorized month set and accompanying legacy stall-rent allocations; it does not open generic month posting to other sources. Legacy period lines retain actual original day/rate evidence. Credits, corrections, ownership, quote conflicts and idempotency remain source-owned.
- Head NPM void supports a fully attributed mixed day/month rental receipt in the coordinated correction transaction, and refuses remitted cash. Corrections preserve original receipts and restore only the original allocations. Cross-period receipts are excluded from the Mobile single-day replacement suggestion.
- Actual verification found the Head ledger summary looking up monthly authority using the occupancy-clamped date (October 20), so it fell back to legacy day rows despite real month Collections. `PaymentRepository.Ledger.cs` now looks up the first day of the billing month. Earned liability remains distinct from the full payable month; received cash is not discarded.

**Policy and offline boundaries**

- Partial payments remain disabled by default. `NpmPartPaymentOptions` can additionally approve exact contract IDs. The optional `WorkingCopyNpmAmounts` host gate requires Development, exact loopback database `stalltrack_workcopy_main:55491`, an explicit municipality/contract allowlist, TEST approval reference and `TEST-NPM-AMOUNT-` stall identity. It does not enable real accounts or all tenants. Monthly sources still require their own frozen partial-payment approval.
- Owner clarification authorized **synthetic test accounts only**. Acceptance approved fresh contracts `d8ac1abf-82ab-47ae-9059-5725bb97a97b` and `ea9df30f-7a9b-4d27-8b72-ce1fae00c96d` in synthetic tenant `npm-test-20261020123712`, facility `0af0c898-83ae-4107-9397-b7088b680b85`. The second account, `TEST-NPM-AMOUNT-2` / Synthetic GUI Payor, remains unpaid for later manual acceptance. Tariffs were established through the Head rate API before registration (900 monthly / 30 daily), and the synthetic opening approval preceded registration. No existing source's frozen approval was rewritten.
- Partial approval does not authorize advance partial payments. Fifty is accepted when the existing source authorizes at least fifty earned/payable rent; a newly opened October 20 account with only thirty earned correctly refuses fifty. Legacy fractions or adjustments that the existing Part/Whole services cannot allocate fail closed for Office review. Broader advance-partial policy remains an Office decision.
- New arbitrary/cross-period capture requires an online quote. `NpmOutstandingRent` offline kind 15 then durably saves the exact reviewed amount, contract, date, token and operation ID before HTTP. Timeout/restart retries that same intent; stale source/date returns reconciliation-required. Queued, unknown or rejected is never presented as confirmed cash. Existing Daily offline operations retain their own schema.

**Isolated running evidence**

- Shared API 5117/7197 and Head 7267 were not restarted or replaced. A separate owned acceptance API runs at `http://localhost:5139` against the verified disposable working-copy DB, with startup migrations disabled. No new migration was needed. Backup `%TEMP%/stalltrack-npm-amount-before.dump`, SHA-256 `2C4C409302110BA3141D0A92969B02FAF6CDBAB0CFF22597DC918675C6DDDD9A`, was taken before synthetic setup.
- Real authenticated Collector HTTP: gross 900; received 30 → remaining 870, `SRC-2026-000040`; received 870 → remaining 0, `SRC-2026-000041`. Both operation replays returned the same SRC. Independent PostgreSQL read confirmed exactly two Collections and two allocations totaling 900, zero invented DailyCollections, assessment 900 and credits 0. After the ledger fix, actual Head ledger API returned collected 900, outstanding 0, one paid month.
- Private synthetic credentials/setup and evidence live outside Git in `%TEMP%/stalltrack-npm-amount-test-setup.json` and `%TEMP%/stalltrack-npm-amount-preview/`. The latter contains HTTP evidence, SQL reconciliation, corrected Head ledger and owned process IDs. Do not copy secrets to repository documentation.
- Windows Debug acceptance was built with a separate application ID (`site.stalltrack.npmamountacceptance`). A Debug-only loopback API override permits connecting this build to 5139 without changing production Release endpoints. Native inventory initially timed out, then recovered; the initial app render inherited the existing Personalunos session despite the separate application ID. No collection or logout was performed in that account. `LocalAcceptanceProfile` now separates Debug acceptance token keys, municipality binding, queue and read cache when the local API override is set; it also avoids consuming the normal profile's pending bind token. Default Debug and all Release profiles are unchanged. The final isolated-profile Debug and Release builds passed, but the final profile was not launched: **the owner deferred GUI testing to their own later session**. No synthetic Windows click-through or Android acceptance is claimed.

**Verification**

- Final full unit suite: 3,057 passed. Final full Mobile component suite, including source-picker navigation: 156 passed. Initial broader NPM PostgreSQL run: 117 passed. Final NPM/Collection Activity PostgreSQL run including the new mid-month ledger regression: 126 passed; additional mixed-source posting/reversal test: 1 passed. No PostgreSQL tests were skipped.
- Proved the chronological allocation regression by temporarily reversing the iteration: both 2,430/full and 1,000/partial cases failed (expected oldest allocation 240, actual 510). Restored chronological ordering before final green tests. The new mid-month Head-ledger test also failed before the shared lookup fix (expected collected 900, actual 0), then passed in the broader green run.
- API publish, Client Release and Windows Mobile Release builds passed. Head component full run: 1,378 passed, 2 failed (`StallHolderListPrintSheetTests.FacilityRosterDoesNotCarryItsScreenHeightOntoLandscapePaper`, `SpaceRentalConsistencyTests.IcePilotKeepsItsSourceTableNavigationAndRegistration` timeout). These presentation files were not changed by this task; do not call the full Head suite green.
- Focused recheck of those two Head tests: ICE passed; the print-source assertion still failed while its shared presentation file was being edited outside this task. No unrelated presentation fix was attempted. Scoped CSS brace checks and focused `git diff --check` passed.
- Logs are `%TEMP%/stalltrack-npm-amount-*.log`. Entry/final Git status evidence is retained outside the repository. Branch/HEAD remain `codex/local-unified-v8-v10-20261013` / `17b9d100`; no staging, commit, push, reset, stash, retained port-5432 write or production deployment occurred.

**Acceptance still required:** exercise the final Windows app at 390–430px, including Daily thirty, edited fifty success on an eligible earned account/refusal on a newly opened account, partial/full outstanding collection, allocation disclosure, retry, keyboard and refresh. Shared-port deployment remains separate from this owned acceptance preview and must be coordinated with its current users.

### 2026-10-20 — Windows Mobile NPM amount-entry/Outstanding Rent local recovery

User-provided Windows WebView screenshot showed pale/unreadable amounts/inputs inside the new Daily Collection amount form and an Outstanding Rent screen saying 'connection is too weak'. Read-only desktop checks found the concrete connectivity cause: the currently running Windows Debug MAUI client targets `http://localhost:5117/`, but the working-copy API had stopped listening on both 5117 and 7197. Codex's independent NPM acceptance API on 5139 remained healthy. No evidence of poor network signal was found; the earlier message incorrectly assumed poor signal for all HttpRequestException/timeout failures.

The isolated database `127.0.0.1:55491/stalltrack_workcopy_main` remained online. Restored the existing published API **only** using the complete startup configuration extracted from the guarded original workcopy launcher (tenant, OfficeCashTurnover, Fish/Meat request installation, NPM approved demonstration policy, no startup migration). No Head Portal restart, database migration, code reset, commit, push, or prod connection. Confirmed `https://localhost:7197/health`, `/health/ready`, `http://localhost:5117/health` each HTTP 200. Anonymous GET `http://localhost:5117/api/Mobile/npm/outstanding/accounts` returned 401, confirming the authenticated route is deployed (not a 404). Native signed-in balance retrieval was not observed.

UI-only corrections in Mobile/Components/Shared/NpmRentAmountForm.razor(.css) and Mobile/Components/Pages/Menus/Market.razor(.css): explicit high-contrast navy/dark text for approved daily rate, outstanding amount, payment input, summary and actionable links; compact consistent spacing, readable typed amount even when disabled, focus/disabled styles; one receipt/SRC notice located before the confirmation action; remove duplicate bulky Instrument/Reference panels from NPM daily-only path; tighter Daily header and payor preview; Outstanding Rent error state now has a real Retry button and neutral 'Unable to reach the office API' copy rather than blaming poor signal. No financial formula, request payload, idempotency, allocation or server source was touched.

Validation: Windows Mobile Release build succeeded with zero errors (its binary is **production-API-targeted and must not be used for local demonstration**); 156/156 Mobile component tests passed, plus a fresh focused five-test NPM rerun with explicit one-SRC-notice assertion passed; separate Windows Mobile **Debug** build succeeded with zero errors at `C:/dev/stalltrack/eemo/EEMOCantilanSDS.Mobile/bin/NpmRentUiRepairDebug20261020/`, using local 5117 by default. The rebuilt Debug executable was launched as a *second* native process (PID 10484) with its own visible WinUI window, leaving the prior Debug app (PID 29528) untouched. Debug app signed-in UI acceptance still needs a Collector to open Daily/Outstanding on the new window. Do not mistake the older already-open Debug instance for this repaired binary.

Package/dependency advisories NU1608/NU1903/NU1902 and existing nonblocking code warnings remain; no release error. No production API or retained original municipal PostgreSQL port 5432 touched. Other agents' ongoing Head reporting and Codex financial source files were not modified.

### 2026-10-21 — Registration, optional receipt evidence and registered-source filters

This continuation does not replace the completed monthly/outstanding-rent implementation or activate any tenant. Changes remain source-only until matching API/Client/Mobile binaries are deployed together.

- **NPM registration/import:** removed Electricity/Water service controls. Existing flags are preserved when editing; imports no longer request new flags. Existing `UtilityServiceEligibility`, WCF source discovery and ECF discovery already recognize registered NPM stall/occupancy identity without these flags. Discovery creates no bill, meter reading, account duplication, assessment or cash. Collector facility/operation assignments and WCF activation remain required.
- **Optional physical OR:** the shared Daily/Outstanding/Whole amount form now accepts `Official Receipt No. (Optional)`. `NpmOutstandingRentPayment`, `NpmWholePaymentRequest` and `NpmRentMonthPayment` accept optional `OfficialReceiptNumber`; native session Daily intents carry the same optional field. Existing Daily command/offline contracts retain `ORNumber`. Sync forwards it, including monthly Daily replay and the older Whole endpoint.
- **Accountability:** `NpmReceiptEvidence` resolves the tenant's normalized registered OR serial and validates unused custody (the authenticated Collector, or in-office stock for authorized Office actors). The existing posting coordinator consumes that document and links it to the actual Collection in the posting transaction. Blank input preserves SRC-only posting and the original intent fingerprint. A supplied OR participates in retry identity; changing it on replay conflicts. Unknown, unavailable, other-custodian or wrong-instrument documents cannot authorize cash. A CT-classified rental cannot be relabelled OR. No free-text serial is fabricated into accountable stock; historical Collections are not backfilled.
- **Receipt presentation:** Collection Activity, its details, shared collection details, Direct Collection Review details, Mobile operation records and recent collections distinguish actual document number/type from SRC. Missing physical evidence says `None recorded`; SRC remains separately accessible in details/search. A real CT remains a CT. One rental payment still produces one Collection/SRC with the same source allocations and money.
- **New Collection categories:** the server returns actual assigned active NPM/TCC/NCC/BBQ/ICE facilities and assigned Kanmanggay/Vegetable-Fruit/Fiesta-Araw sources, alongside Fish/Meat. Exact category predicates route to the existing occupancy/account readers. Old `rentals`/`spaces` query aliases remain compatible but are not advertised. Mobile renders one horizontally scrolling segmented row, with contained width and inset keyboard focus.
- **Monthly space editor:** the Head's Kanmanggay/VF holder editor optionally includes an approved amount and effective month. `CorrectObligationHolderRequest.RateChange` delegates to the existing `SetRateAsync` validation inside one serializable transaction. Holder evidence and approved-rate evidence have distinct audit actions; failure rolls back both. Existing assessments, collections, contract-rate evidence and lifecycle rules remain unchanged. The separate rate endpoint remains compatible.

Final verification evidence: 549 focused unit checks, 111 focused Head component checks, all 157 Mobile component checks and 101 disposable-PostgreSQL checks passed, with no skips. PostgreSQL checks cover optional receipt linkage and actual Activity-reader document evidence, replay/conflicting OR intent, unknown/wrong-custodian/wrong-type rejection without cash, existing monthly/outstanding-rent reconciliation, utility discovery with no flags or invented bills, exact registered-source filters and atomic holder/rate saves. A private negative-control build restored the former behavior of ignoring typed ORs: all five selected receipt regressions failed; the source was restored and the normal 101-test PostgreSQL run passed. API, Client and Windows Mobile Release builds finished with zero errors using separate task outputs. Existing compiler/package advisories remain.

The broader Head selection additionally exposed two pre-existing expectations for the contract-reference input removed by the separate October 21 UI repair; that unrelated form was not restored. Its result was 118 passed / 2 failed. The final focused Head selection above includes the new combined editor test and Activity receipt rendering, import and review checks. The full Mobile run initially exposed a summary-preview test's fixed directory assumption under isolated build outputs; it now discovers the repository root without changing financial assertions.

Evidence logs are local under `%TEMP%`: `stalltrack-receipt-unit.log`, `stalltrack-receipt-final-head-tests.log`, `stalltrack-receipt-mobile-all-final.log`, `stalltrack-registration-receipt-pg5.log`, `stalltrack-receipt-red-test.log`, `stalltrack-receipt-final-api.log`, `stalltrack-receipt-final-client.log` and `stalltrack-receipt-windows-all-final.log`. Focused `git diff --check` passed; the changed scoped CSS has 27 opening and 27 closing braces. A broader dirty-tree check also reports an existing extra EOF blank line in `SH/StallHoldersList.razor.css`, outside this change. Branch/HEAD remain `codex/local-unified-v8-v10-20261013` / `17b9d100`; the pre-task dirty-status snapshot is retained locally. No reset, stash, cleanup, staging, commit or push was performed.

Interactive acceptance remains a release gate: use matching isolated builds, verify Daily/Outstanding/Whole optional OR with assigned stock and blank input, verify SRC separately in records, exercise each assigned source filter and empty state at 390–430px, and save a future approved monthly space amount with a holder correction. No shared process restart, municipal database write, schema migration, production activation or Android release was performed by this continuation.

Read-only runtime checks returned HTTP 200 from the existing API `https://localhost:7197/health` and Head `https://localhost:7267/login`. Those existing processes were not replaced and do not constitute acceptance of these new binaries. Desktop inventory exposed no running Windows Collector window; no authenticated browser/native-Mobile journey was exercised. The Windows Release artifact uses the established production API configuration, so local acceptance must use the established isolated Debug profile or an explicitly configured test build, not launch the Release artifact against production.

### 2026-10-21 — Independent post-Codex receipt and registration review

Verified saved Codex evidence logs: 549 unit, 101 PostgreSQL integration, 111 Head-focused and 157 Mobile component tests all passed with zero skips or failures. API, Head Client and Windows Mobile Release build logs each show successful build. Reviewed source-owned NPM receipt validation (`NpmReceiptEvidence`), registered-source browser category filtering and Space holder rate transaction guards. This supports test coverage but is not a substitute for authenticated browser/native acceptance.

Resolved two *outdated tests only* in `EEMOCantilanSDS.ComponentTests/Pages/ObligationWorkspaceTests.cs`: `TheHeadOpensAnAccount_WithAnExplicitPayorAndAnApprovedAmount` and `AddNew_IsGroupedIntoSections_WithPersistedContractEvidenceAndSpaceOnlyFallback` still demanded a `Contract reference` input deliberately removed from the simplified Space registration UI. Updated assertions to verify absence of that redundant field, preserve signed occupancy and approved amount expectations, and ensure no fabricated contract reference is sent. No production components, database, rate or collection rules were changed. These two tests passed 2/2 in an isolated `CodexReceiptAudit20261021` output, and the **entire ObligationWorkspaceTests class passed 73/73** (zero skips/failures), using the newly built component assembly. Evidence: `%TEMP%/stalltrack-codex-receipt-head-stale-fix-20261021.log` and `%TEMP%/stalltrack-codex-receipt-space-fullclass-20261021.log`.

Work remains source-only. No shared API/Head/Mobile deployment, browser/native acceptance, commit or push was performed during this review. All preexisting dirty working-tree changes were preserved.
