# Task 2C: specialized NPM rent-month foundation

**Status:** domain and EF preparation implemented; no financial activation, typed allocation source or executable migration. Review alongside [Task 2B architecture](NPM_MONTHLY_SETTLEMENT_ARCHITECTURE_DECISION_20261016.md), [Task 2A evidence/owner clarification](NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md) and [Task 1 compatibility audit](NPM_MONTHLY_BILLING_STANDARD_REVIEW_20261016.md). Those documents and their pending tests were not edited.

## 1. Repository and authorization checkpoint

Primary checkout `C:\dev\stalltrack\eemo`, branch `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100` verified before work. Initial status was exactly Task 2A/2B's five pending paths. No branch/worktree, merge, reset, stash, clean, push, deployment or API restart. Platform was not changed. No PostgreSQL connection, live cash posting, migration application, Testcontainers provisioning or second API/database.

Applied root AGENTS, financial-rule-review and documentation-governance guidance. Current owner decisions govern the future outcome; current code characterizations do not approve the day31/F-01 defects. All existing five Task 2A/2B paths remain byte-for-byte unchanged, including the two Skip markers (eight exact-settlement inline requirements plus one day31 requirement).

## 2. Implemented domain contract

`NpmRentMonth` is a non-soft-deletable, tenant-owned preparation record, not a payment, a new calculator or a collected balance. It records:

- Stable ID, municipality, NPM facility, physical stall, responsible Contract ID and first-of-month period; ownership-as-of context.
- Explicit assessment and credit amounts, nullable historically recorded `NpmMonthBasis`, rate-as-of date, policy/evidence reference and bounded rate/credit JSON provenance.
- `NeedsReconciliation` and `Reconciled` preparation states. **Neither is active. `CanAcceptCollections` is always false.** No activation or posting method exists.
- Positive settlement/concurrency version, creator/reviewer identity and UTC timestamps, reconciliation evidence and last revision reason.

`Prepare` accepts existing tenant-stamped Facility/Stall/Contract objects with the complete relevant contract history. It validates nonempty same-tenant identity, NPM facility type, stall-to-facility and contract-to-stall ownership, recorded contract membership and the existing `StallOccupancy.AnsweringForMonth` rule. Missing/wrong owners and same-start duplicate-contract ambiguity fail rather than choosing a name or manufacturing evidence. A clear mid-month handover uses the canonical one month owner but remains unreconciled; no old cash is attributed/transferred. The factory cannot detect database duplicates or prove that a caller loaded complete history; a future authorized application command must load/verify that history. Relational uniqueness provides the proposed duplicate backstop.

`NpmRentMonthAssessmentEvidence` carries inputs already established by the specialized rule/policy. It does not derive a monthly rent from a daily rate or enforce a Cantilan tariff. Amounts are nonnegative cents within numeric(18,2); credits cannot exceed assessment. Zero assessment/full credit is allowed. Invalid/unknown enum numbers are rejected; null basis preserves unknown historical evidence and prevents reconciliation until explicitly revised. JSON must be a bounded nonempty object; the foundation does not claim to verify that an opaque rate reference was actually approved by an LGU. That requires the future tenant-scoped evidence resolver and authorization.

No daily rate is required by this evidence model, so a future approved monthly-only assessment of 1,900 can be represented without DailyCollection rows. This is not an implemented monthly-only billing or proration policy.

`ReviseAssessment` requires matching expected version, identified actor/actual role metadata, UTC/non-backdated time, valid assessment and explicit reason. It preserves all identity fields, increments version and invalidates prior reconciliation. `Reconcile` records review only with equivalent guards; it cannot activate cash. Both return old/new semantic AuditLog evidence. Role text/actor ID are **not an authorization token**: no new command/endpoint is exposed, and future application code must derive them from authentication, enforce the authorized role/tenant and save the returned semantic audit in the same transaction.

The new type is also registered with the **existing AuditSaveChangesInterceptor**. Its persisted revision reason means generic creation/revision audit evidence survives even if a future caller forgets to attach the returned semantic audit. The in-process test verifies actual interceptor old/new/actor/tenant recording. Existing audit types and behavior are unchanged. No second audit table was introduced.

## 3. Financial separation and intentionally inactive source

The anchor stores no actual cash total, allocated-cash total, remaining balance, historical excess, SRC, OR, remittance or daily payment projection. These must come from authoritative old/new allocations, corrections, reviewed opening evidence and the specialized monthly position in a subsequent task. Month preparation/tracking creates no Collection, PostingOperation, allocation, DailyCollection or accountable document.

**Typed allocation deferred under Task 2C's explicit fallback.** `CollectionSourceKind` remains unchanged; no `NpmRentMonth` member, money-source registration, allocation-FK column or collection-source authority change was added. Existing `CollectionAllocationConfiguration` source-shape checks, `CollectionLine.ValidateSource`, the exhaustive authority map, `NpmDailyCanonicalPoster` daily-only void and Mobile correction allow-lists are not a complete month-source route. Exposing the enum while merely labeling it canonical-always would permit an incomplete source shape without the unified authorization/balance/correction contract. A source snapshot or a forged existing PaymentRecord source is not a supported substitute.

Tests show an unregistered month source kind is rejected by the existing Collection aggregate for both same-tenant and foreign-tenant classifications. This is deliberately **not** proof of a future cross-tenant allocation FK: no such typed allocation is implemented. The anchor's `(MunicipalityId, Id)` key prepares the eventual tenant-safe FK. Adding that FK/type requires coordinated source lookup/approval-state validation, positive eligibility, correction dispatch and reporting authority tests before any writer is enabled.

Source search confirms the only non-test consumers of `NpmRentMonth` are its domain/configuration, context contract, audit registration and backup/export inventory. No current quote, payment writer, controller, DI financial registration, canonical posting coordinator, SRC generator or remittance workflow consumes it.

## 4. Proposed additive schema and compatibility review

The EF model is implemented; **no executable migration or snapshot update was generated**. This avoids an unfinished schema being automatically applied by the API's startup migration behavior. Offline Npgsql model/create-script and snapshot-difference tests require no database connection.

Proposed `NpmRentMonths` columns:

| Group | Columns / relational types |
| --- | --- |
| Identity | `Id`, `MunicipalityId`, `FacilityId`, `StallId`, `ContractId`: required UUID |
| Period context | `PeriodStart`, `OwnershipAsOf`, `RateAsOf`: required date |
| Assessment | `AssessedAmount`, `CreditedAmount`: required numeric(18,2); `MonthBasis`: nullable integer |
| Provenance | `PolicyReference`: required varchar(1000); `RateEvidenceJson`, `CreditEvidenceJson`: required jsonb |
| Preparation/concurrency | `Status`: required integer (1/2); `SettlementVersion`: required bigint, concurrency token |
| Creation | `CreatedByUserId`: required UUID; `CreatedBy`: required varchar(100); `CreatedAtUtc`: required timestamptz |
| Revision | nullable `UpdatedByUserId` UUID, `UpdatedBy` varchar(100), `UpdatedAtUtc` timestamptz, `LastRevisionReason` varchar(4000) |
| Reconciliation | nullable `ReconciledByUserId` UUID, `ReconciledAtUtc` timestamptz, `ReconciliationEvidence` varchar(4000) |

Keys and FK contract:

| Constraint | Proposed shape / purpose |
| --- | --- |
| Primary key | `Id` |
| Alternate key | `(MunicipalityId, Id)` for a future typed allocation reference |
| Unique index | **`(MunicipalityId, StallId, PeriodStart)`**, no ContractId/version/status in the key |
| Facility FK | `(MunicipalityId, FacilityId)` -> Facilities `(MunicipalityId, Id)` |
| Stall FK | `(MunicipalityId, StallId, FacilityId)` -> Stalls `(MunicipalityId, Id, FacilityId)` |
| Contract FK | `(MunicipalityId, ContractId, StallId)` -> Contracts `(MunicipalityId, Id, StallId)` |
| Delete behavior | Restrict for all three relationships; no cascade deletion of history |

EF reuses or adds the necessary parent alternate keys. Existing global parent IDs make their composite identity unique; actual database/schema alignment still requires PostgreSQL verification. Stall.FacilityId and Contract.StallId are created privately and have no current domain reassignment method, so the new compound principal identity does not replace a supported reparenting operation. No existing parent row, money field or historical record is backfilled or modified by the model.

Check constraints cover first-of-month period; nonnegative assessment/credits and credit ceiling; nullable known basis; positive version; nonempty identity/creator GUIDs; nonempty policy/creator and object-shaped nonempty JSON evidence; consistent preparation/reconciliation state; nonempty reason and complete/non-backdated update metadata. Explicit `IS NOT NULL` clauses avoid SQL CHECK's unknown/null acceptance for review/update timestamps. Precision is configured in EF; domain guards reject fractional-cent inputs before a future write. PostgreSQL execution remains untested.

Relational FKs prove linked tenant/facility/stall/contract identity, **not** NPM facility type, latest monthly ownership, genuine rate approval or actor authorization. Those are domain/future application checks; no cross-table SQL CHECK or policy inference was invented.

The offline schema-difference test compares the last compiled EF migration snapshot with the new design-time model. It proves only additive CreateTable/CreateIndex/AddUniqueConstraint/AddForeignKey operation types, exactly one new table (`NpmRentMonths`), and no SQL data operation, destructive table/column operation or backfill. It does not prove that the existing local database matches the snapshot, that its constraints enforce correctly or that locks/concurrency behave correctly.

### Backup and rollback boundary

The existing backup-completeness tests require every tenant-owned table to have explicit treatment. `NpmRentMonths` is added to the existing restorable inventory and tenant-scoped export, while AuditLogs remain export-only. Restore ordering is already derived from model FKs; no new restore engine or transaction was introduced. No real backup/export/restore ran.

**Do not deploy/restart this foundation against an unmigrated database.** The newly registered/exported table would not exist there, and model/migration alignment is intentionally pending. Source is for engineering review only. Before migration application, approve schema, database readiness, backup-format/restore behavior, parent-key lock implications and separately authorized PostgreSQL rehearsals. Once preparation evidence exists, rollback must preserve/export it and its audit rather than silently dropping it. No automatic Down migration or removal of real evidence is authorized.

## 5. Exact files changed in Task 2C

| File | Purpose |
| --- | --- |
| `EEMOCantilanSDS.Domain/Entities/Payments/NpmRentMonth.cs` | Preparation-only month/evidence contract, identity/amount/revision/review validation and semantic audit |
| `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/NpmRentMonthConfiguration.cs` | Table, tenant-safe relationships, period uniqueness, check constraints and concurrency metadata |
| `EEMOCantilanSDS.Application/Common/Interface/Persistence/IAppDbContext.cs` | New preparation DbSet contract only |
| `EEMOCantilanSDS.Infrastructure/Persistence/AppDbContext.cs` | Model DbSet; existing automatic tenant filter covers the type |
| `EEMOCantilanSDS.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` | Include new preparation evidence in existing authenticated-actor audit pipeline |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantDataTables.cs` | Explicit backup/restore inventory coverage |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantExportRepository.cs` | Existing tenant-scoped export includes prepared evidence |
| `EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthTests.cs` | Identity, owner, evidence, revision, amount, unknown-basis, handover and ambiguity tests |
| `EEMOCantilanSDS.Testing/Infrastructure/Persistence/NpmRentMonthModelTests.cs` | Relational model/offline schema diff, filtering/audit, no-cash and inactive-source proofs |
| This handoff | Scope, schema, verification limits and follow-up gates |

No other Task 2C paths are intended. The five preexisting pending files are preserved and are not included as Task 2C changes.

## 6. Executed regression evidence

Initial foundation run: **41 passed, 0 failed, 0 skipped**. After adding audit, offline additive-diff and empty-source-ID coverage, the final foundation has **46 active cases**, included in the focused run below.

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~TenantFilterCoverageTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

| Executed check | Result |
| --- | --- |
| Final focused run: foundation, prior billing proofs, backup/tenant coverage | **93 passed, 0 failed, 2 skipped; total 95** |
| Full unit suite, Release | **2,710 passed, 0 failed, 2 skipped; total 2,712** |
| Release compilation through focused test command | Succeeded, including Domain/Application/Infrastructure/API references; existing repository warnings remain |
| Offline model/create-script/diff checks | Passed without opening a PostgreSQL connection |

The skips are unchanged: one theory contains the eight pending F-01 examples; the other is the pending day31 requirement. Neither financial correction is implemented. Existing RentGoal/PureDays, cash-preservation and reversal characterizations remain intact. The Cantilan 900 cap, terminal 25/20 allocation and prevention in current daily writers remain future runtime acceptance, not a green result inferred from preparation tests.

InMemory is used only for tenant-filter and existing audit-pipeline execution with nonfinancial fixture evidence. It proves neither real duplicate prevention/FK enforcement nor cross-session financial concurrency. The no-cash test tracks only month/audit objects without saving. The undefined-source rejection test proves the source remains inaccessible, not a working cross-tenant typed allocation.

Logs: `%TEMP%\stalltrack-npm-task2c-focused.log`, `%TEMP%\stalltrack-npm-task2c-unit.log`. No PostgreSQL integration, Client/Mobile browser test or live quote/post was run. The test projects at this verified checkpoint target net9.0; no target-framework change was made.

## 7. Required follow-up and approval gates

Before applying any schema:

1. Approve the preparation schema and the explicit deferral of typed allocation/cash activation. Decide whether review/assessment mutation will be Head-only or include existing office Admin; application authorization is not implemented here.
2. Authorize isolated PostgreSQL test infrastructure separately. Verify actual uniqueness (including concurrent inserts), cross-tenant and wrong-stall FK failures, checks, principal keys, JSON/date precision, concurrency and backup/restore compatibility. Do not use shared municipal financial records as fixtures.
3. Inspect the actual target schema and parent-key readiness read-only; review migration DDL/locking and additive rollback plan. Generate/review a controlled migration only after that readiness decision; **applying it is a separate approval**. No financial backfill or activation follows merely from schema application.

Subsequent unified-settlement work must implement the typed source/FK and authority/correction contract; tenant-scoped verified source loading; one common authoritative month position and exact allocation plan; month-wide transaction/version consumption across all Head/Mobile/daily/batch/online paths; old daily canonical/legacy opening evidence exactly once; stale-quote/replay/correction/remittance safeguards; and all monthly debt/arrears/closed-account/report readers. It must enforce the approved Cantilan day31 cap without blocking separate non-rent fees, preserve PureDays history and never convert real historical cash automatically. Only then may a separately authorized financial cutover be considered. Monthly-only onboarding, F-02/F-03 and receipt-policy work remain excluded.

## 8. Final Git checkpoint

Final `git diff --check` passed; new files have no trailing-whitespace errors. All five protected Task 2A/2B paths match their initial SHA-256 values. Nothing staged, no new commit; HEAD remains `17b9d100` on the starting branch.

Working-tree contents: five Task 2C modified tracked source paths; five new Task 2C paths (domain, mapping, two test files and this handoff); plus the original five pending Task 2A/2B paths. No unrelated Login/Audit Trail files, release/planning indexes, Platform paths or existing migration/snapshot files changed. The existing LF/CRLF notice on the protected Task 2A fixture is not a new whitespace error.

No commit is made merely to clean pending Task 2A/2B files. The foundation is left reviewable for the next controlled integration decision. No production deployment, API restart, migration or live financial operation was performed. Stop after this foundation; there is no automatic next implementation phase.
