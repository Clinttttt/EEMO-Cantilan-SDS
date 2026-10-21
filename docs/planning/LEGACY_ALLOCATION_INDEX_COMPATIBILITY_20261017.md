# Task 2C.3 — Legacy allocation index compatibility rehearsal

**Assessment: REHEARSAL PASSED — ADDITIONAL BASELINE RISKS REMAIN.**

Actual PostgreSQL testing confirmed the legacy kind-10 unique index rejects a valid second installment. Temporarily dropping that index inside one transaction allowed two PHP 30 allocations against a synthetic PHP 900 period: PHP 60 allocated / PHP 840 remaining. Explicit rollback restored the original index, all checked schema definitions and every public table's contents. **No permanent correction or NPM financial cutover was performed.**

Read with [migration lineage reconciliation](EF_MIGRATION_LINEAGE_RECONCILIATION_20261016.md), its [metadata evidence](../evidence/2026-10-17_ef_migration_lineage_metadata.json), [schema readiness](NPM_MONTH_SOURCE_SCHEMA_READINESS_20261016.md), [foundation](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md) and [architecture decision](NPM_MONTHLY_SETTLEMENT_ARCHITECTURE_DECISION_20261016.md). Those nineteen pending paths were preserved, not rewritten. Financial-rule review and documentation-governance guidance continue to apply; the current owner's Task 2C.3 authorization expressly permits these rollback-only local database writes.

## 1. Repository and authorization checkpoint

Checkout `C:\dev\stalltrack\eemo`; branch `codex/local-unified-v8-v10-20261013`; HEAD `17b9d100e8ddffdd747f5688a8e49818693d078a`. Initial status: six tracked modifications and thirteen untracked paths from Tasks 2A–2C.2. Entry SHA-256 values were captured for all nineteen paths.

No branch/worktree change, reset, stash, clean, merge, rebase, migration generation/application, history edit, API restart, production access, financial engine implementation, push or deployment. Platform and older worktrees were not edited. The only lasting database-related artifact is the newly authorized full backup outside Git. Test fixture rows and the temporary index drop were rolled back.

Task deliverables are this handoff, [sanitized execution evidence](../evidence/2026-10-17_legacy_allocation_index_rehearsal.json), and three credential-free reproduction helpers under [docs/evidence/task2c3](../evidence/task2c3/rehearse.cjs). They are evidence tooling, not a production writer or active EF migration. Nothing is staged or committed.

## 2. Mandatory backup — completed before writes

Verified target: **localhost:5432 / EEMOCantilanSDS.DB**, PostgreSQL 18.4. Helpers reject any other database/port or a non-loopback host. Existing API configuration/user-secrets/environment resolution was reused without printing connection secrets. No connection configuration was changed.

| Backup item | Actual evidence |
| --- | --- |
| Full custom archive | `C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_task2c3_full_2026-10-17T00-57-38-716Z.dump` |
| Verification completed UTC | `2026-10-17T00:57:39.647Z` (08:57:39 Philippine time) |
| Size | **1,712,039 bytes** |
| SHA-256 | `08c54731264fb352b3e6d40c10dc129cd3240873ae8c982df7d047bf9ba7de99` |
| pg_dump | PostgreSQL 18 tooling; custom full database archive, no schema/data/table exclusions; exit 0 |
| Archive listing | `pg_restore --list <archive>`; exit 0 |
| Full archive readability | `pg_restore --file NUL <archive>`; complete SQL/data decoding to the Windows null device, exit 0; **no database target and no SQL execution** |
| Historical coverage | TABLE DATA entries individually checked for all thirteen historical tables plus `__EFMigrationsHistory` |

The archive includes the database's schema/data/history, retained tables, view, indexes, sequence state and relationships. It is not a cluster-global roles backup or an executed restore rehearsal. Its checksum was revalidated before the writing transaction. Backup contents must remain outside Git and must not be uploaded or quoted; they contain private development records and configuration evidence.

The executed pg_dump argument shape was:

```text
pg_dump --host localhost --port 5432 --username <configured-user>
  --dbname EEMOCantilanSDS.DB --format=custom --file <new-archive>
  --no-password --lock-wait-timeout=1500ms
```

The password was passed only in the child process environment; no credential-bearing command line or output was recorded.

## 3. Quiescence, transactional scope and nontransactional risks

Preflight `pg_stat_activity` found **zero other sessions** in the target database. There were no listeners on the supplied API ports 5117/7097; an additional process inspection found no EEMOCantilanSDS.Api process. Nothing was stopped. No user-defined public-table triggers were present.

The writing connection repeated the zero-other-session check. It began a repeatable-read transaction, then briefly acquired **SHARE ROW EXCLUSIVE NOWAIT locks on all 95 public tables in deterministic name order**. This excluded competing writers and made full before/after table fingerprints meaningful; it was not a long-lived application outage or maintenance change. DROP INDEX can require stronger locks on its own table, so quiescence and the bounded lock timeout were essential. Actual writing/rehearsal interval was `00:58:44.393Z` to `00:58:45.234Z`, approximately **0.84 seconds** including final read-only verification.

Connection limits: statement timeout 15 seconds, lock timeout 1.5 seconds, idle-in-transaction timeout 30 seconds. Expected errors were isolated using SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT. The outer transaction was explicitly rolled back. The helper's error path also attempts rollback and closes the connection; no COMMIT statement exists.

**Sequence risk addressed:** ordinary Collection inserts use the nontransactional `CollectionReferenceNumberSeq` default. These fixtures explicitly supplied unused high bigint ReferenceNumber values, so **nextval was never called**. ReferenceCode is a stored computed column and therefore did compute temporary test strings for these uncommitted parent rows. No official reference was issued, returned to a client or published. No sequence-backed defaults, payment providers, authenticated posting commands, accountable-form consumption, remittance service or external integrations were invoked. No synthetic reference number is presented as a real collection.

## 4. Synthetic fixture and verified defect

All mutable fixture identities were newly generated UUIDs. A new synthetic municipality owned its classification/policy, source-native Kanmanggay-style account (kind 2, explicit ActualOccupant; no invented signed contract), approved test rate and first-of-month period. No existing municipal financial record, payor, rate or occupancy was borrowed or updated.

The period was assessed at **PHP 900**. Two independent synthetic Collection parents and CollectionLines each represented **PHP 30**, with distinct client-operation IDs. Both allocation attempts referenced **the same period ID**, same tenant, SourceKind **10**, and null SourcePart. No source-ID substitution or fake daily record was used.

Before DROP, the helper verified the exact unique index definition, valid/ready state and zero incoming pg_depend references. It independently verified the current nonunique source-lookup index exists and is valid/ready.

```sql
-- Verified original object; not a permanent-change instruction:
CREATE UNIQUE INDEX "IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId"
ON public."CollectionAllocations" ("MunicipalityId", "SourceKind", "SourceId")
WHERE ("SourceKind" = 10);
```

The first PHP 30 allocation succeeded. The second PHP 30 allocation failed with:

| Field | Actual PostgreSQL result |
| --- | --- |
| SQLSTATE | **23505** (unique violation) |
| Constraint/index name | **IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId** |
| Same-period allocation IDs | Distinct; changing IDs was not used to evade the tuple conflict |
| Recovery | ROLLBACK TO SAVEPOINT; transaction remained usable |

This is an actual database reproduction, superseding Task 2C.2's explicitly inference-only conflict evidence. The authentic recovered original is `20260928120100_AddVegetableCanonicalAllocationShape` at older-repository commit `ea3cd0da80f7c816f4fa6f9e58fbc7f0e0d79db9`. It made kind 10 unique when 10 meant VegetableSpaceRentalAssessment. Current `CollectionSourceKind.ObligationPeriod = 10` permits successive installments, so the retained index enforces the wrong lifetime uniqueness contract.

## 5. Temporary corrective rehearsal and independent guards

Within the **same uncommitted outer transaction**, after recovering the expected error:

```sql
DROP INDEX public."IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId";
-- Retry the identical second allocation tuple and amount.
-- Verify period, tenant, line and collection consistency and totals.
ROLLBACK;
```

No CONCURRENTLY, CASCADE, replacement unique index or history edit was used. The current nonunique lookup remained:

`IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId_So~ (MunicipalityId, SourceKind, SourceId, SourcePart)`.

| Rehearsal check | Actual result |
| --- | --- |
| Second identical intended allocation after transactional DROP | Succeeded |
| Period assessment | **PHP 900.00** |
| Allocation rows | **2** |
| Total allocated | **PHP 60.00** |
| Assessment less allocated | **PHP 840.00** |
| Tenant/source/line/collection/amount inconsistencies in fixture joins | **0** |
| Duplicate synthetic ClientOperationId | Still rejected: **23505**, `IX_Collections_MunicipalityId_ClientOperationId` |
| Allocation with different municipality referencing fixture line | Still rejected: **23503**, `FK_CollectionAllocations_CollectionLines_MunicipalityId_Collec~` |
| Legacy kind-8 index | Remained present and valid; never dropped or altered |

These are constraint/fixture results, not a call through the canonical financial coordinator. The current polymorphic SourceId is not itself an ObligationPeriod FK; positive period identity was explicitly checked in the fixture query. Actual source eligibility, authentication and correct period loading still depend on existing application guards. The 23503 proves the compound allocation-to-line tenant FK, not every possible tenant boundary in the product.

## 6. Explicit rollback verification

After the outer ROLLBACK, a new repeatable-read/read-only transaction verified:

| State | Before | After |
| --- | --- | --- |
| Public tables checked | 95 | 95 |
| Migration history count | 143 | 143 |
| Full table-content fingerprint | `ec22390fe31353fd542a43cca3dc018ee10725a2de1499f8e22cd034eb33edad` | Identical |
| Checked schema-definition fingerprint | `ee67c77e6d7f74cfac2fcef3096f287721e205e3c5542fc838905c91d203b99f` | Identical |
| Legacy kind-10 index | Original definition, valid and ready | Exact original definition, valid and ready |
| SRC sequence last_value / is_called | 17 / true | **17 / true** |
| Synthetic municipality/account/period/collection/line/allocation rows remaining | Not present | **0** |

Table fingerprints were calculated server-side from sorted per-row JSON digests with counts, then combined into a SHA-256 summary. No row contents or personal identifiers were exported. Every one of the 95 tables was included, so this covers migration-history contents, all thirteen historical tables and original financial records, not merely fixture deletion counts. Schema fingerprints included all public indexes, constraints, column types/nullability/default/computed definitions and view definitions; the SRC sequence value was checked separately. Fingerprints are evidence of the checked state, not a substitute for a restore rehearsal or exhaustive catalog/security equivalence.

The original legacy index is therefore **still in the development database and still capable of rejecting successive kind-10 allocations**. A passing rollback rehearsal does not mean the installed problem is permanently fixed.

## 7. Related financial behavior and limits

Reviewed current Collection/CollectionLine/CollectionAllocation mappings, ObligationCollectionSource, obligation allocation callers, and existing tests protecting successive installments, source history and once-only collection/remittance semantics. Important application/PostgreSQL evidence includes:

- `IntegrationTests/ObligationAccountTests.cs:VendorFeeIsAMonthlyGoal_SettledByThirtyPesoInstallments_OnItsOwnClassification` expects two PHP 30 installments and PHP 840 remaining; this is the contract the retained index violated.
- Its over-allocation and `PaymentHistory_IsOneAccountsCanonicalAllocations_NotItsOwnersOtherAccounts_AndIsTenantScoped` cases protect balance and source attribution.
- `CollectionLedgerPersistenceTests`, `CollectionReferenceCodeTests`, `CollectionActivityExactlyOnceTests` and collection-session atomicity tests address posting/SRC/history consistency.
- `RemittanceWorkflowTests` includes same-operation replay, concurrent same-collection remittance, cross-tenant access and voiding remittance without changing collection/SRC evidence.

The integration project uses reset/migration-oriented fixture infrastructure. It was **not redirected at the municipal database or executed here**: such test setup is not guaranteed rollback-only and can reset tables or advance sequences. No Testcontainers or second database was provisioned. A future authorized retained-baseline integration run must deliberately replace that setup with a verified controlled harness, rather than assuming today's authorization permits its resets.

This rehearsal proves the specific index failure, its narrow transactional removal, retained lookup behavior, selected uniqueness/FK guards and rollback. It does **not** prove canonical replay returns the previous result, concurrent financial posting/optimistic locking, quote freshness, over-allocation prevention, correction/reversal routing, SRC issuance once, remittance once, API authorization, or Head/Mobile shared balances. Existing non-database regression suites remain useful but are not substituted for those write-path proofs.

### Independent kind-8 review

`IX_CollectionAllocations_MunicipalityId_SourceId` remains unique on `(MunicipalityId, SourceId)` filtered to kind 8. Its historical source was ConfigurableServiceAssessment; current kind 8 is GovernedService. Current allocation-shape checks exclude kind 8; governed direct cash uses its established line semantics. Its immediate implications differ from kind 10. It was not removed on analogy, and no old source integer was reclassified. It still requires an explicit retention/retirement decision in any broader baseline review.

## 8. Eventual permanent correction — proposal only

The narrow engineering correction is to retire **only** the obsolete kind-10 unique index while retaining the current nonunique source lookup and all current amount/source/tenant/client-operation constraints. Actual PostgreSQL evidence supports that correction. No accounting amounts, historical rows, typed source values or migration history need rewriting for this specific index retirement.

A separately approved **new forward migration** can record this existing-schema correction without restoring/replaying the older migration family or editing an applied migration. It must:

1. Preflight the exact table, column order, uniqueness, predicate, valid/ready state and dependencies when the legacy index exists; abort on a same-name incompatible object.
2. Permit its legitimate absence on a fresh current-line database, whose 130 migrations never created it. Verify the normal nonunique lookup remains available; avoid a blind DROP of an arbitrary same-named object.
3. Drop only this confirmed obsolete index under a reviewed transactional locking/timeout/deployment plan. No CONCURRENTLY or CASCADE is needed for this small verified target; production sizes/load require their own assessment.
4. Preserve all history/financial rows and the kind-8 index. Prove fresh-schema and retained-schema compatibility, successive installments, canonical replay, concurrent balance consumption, corrections and reporting before application approval.
5. Declare an honest forward-only rollback policy. After legitimate repeated installments exist, recreating the obsolete unique index could fail and would reintroduce the defect. Do not supply a misleading automatic Down that reinstates it or deletes rows. A subsequent approved forward correction/full recovery plan is required instead.

Retiring an existing index is a deliberate existing-schema correction, requiring its own review/authorization under the repository's additive migration discipline. It must not be disguised within the seven-operation additive NPM anchor plan. **No active migration or snapshot change was generated in Task 2C.3.** Permanent application, even to this development database, remains separately gated by the owner.

## 9. NPM foundation and remaining baseline risks

The technical kind-10 conflict is now **understood and reproducibly correctable**, but the installed schema remains unchanged. The proposed NPM foundation remains two parent alternate keys (Facilities tenant/ID and Stalls tenant/ID/facility), one NpmRentMonths table with three restrictive tenant-safe FKs/nine checks, and four new indexes. The existing Contracts tenant/ID/stall key is reused. The existing Stalls tenant/ID key must remain for retained ECF references.

No new NPM DDL was run or generated. The offline schema-readiness test still verifies the seven-operation proposal; this rehearsal adds no proof of its real uniqueness, FK/check or concurrency enforcement. The month model still has zero collection authority, and no typed month source/writer is activated.

Outstanding baseline issues remain:

- Unrecovered original May migration implementation and unverified `vw_Accounts` creation history.
- Two historical source/designer pairs still have only working-copy provenance; ten others have recovered committed originals. No recovery files were altered or restored.
- Thirteen retained tables, including nonempty evidence/configuration, remain outside current EF-model tenant backup/export coverage. The new full backup preserves them; it does not repair model-based restore compatibility.
- Same-format older backups omitting future NpmRentMonths still need an explicit restore policy.
- Full canonical financial replay/concurrency/correction/remittance tests on an accurately retained baseline remain unexecuted.

Keep all 143 applied history entries, 130 compiled migrations, retained tables/view/sequence, original cash/SRCs and pending acceptance tests. Do not interpret rollback success as approval of an automatic baseline conversion, PureDays retirement, Task 2D writer, monthly-only onboarding or a deployed Cantilan cap. The accepted PHP 900 cap, exact terminal PHP 25/20 and no extra day-31 rent remain future runtime acceptance requirements.

## 10. Reproduction artifacts and executed tests

Credential-free helpers retain the actual parameterized SQL and fixture operations:

- [config.cjs](../evidence/task2c3/config.cjs): strict authorized target, bounded connection options, existing secret resolution without secret output.
- [preflight.cjs](../evidence/task2c3/preflight.cjs): writer/trigger inspection, new full backup, archive listing/full decoding and historical-table coverage.
- [rehearse.cjs](../evidence/task2c3/rehearse.cjs): rechecks backup hash/quiescence, explicit locks, fixtures/savepoints/index drop, assertions and rollback verification; **no COMMIT**.

The original execution used temporary copies under `%TEMP%\stalltrack-task2c3`. Retained copies add an explicit acknowledgement guard to prevent accidental execution. To reproduce **only within a currently authorized rollback-only rehearsal**, from this checkout:

```powershell
$env:STALLTRACK_TASK2C3_AUTHORIZATION = 'rollback-only-local-development'
node docs/evidence/task2c3/preflight.cjs
node docs/evidence/task2c3/rehearse.cjs
Remove-Item Env:\STALLTRACK_TASK2C3_AUTHORIZATION
```

Preflight always creates/verifies a new backup; rehearse requires that evidence file. A later script failure is not permission to repair the shared database manually. These commands were not rerun merely to validate documentation copies; `node --check` passed for all three retained scripts. New PostgreSQL integration attempts must still respect current authorization and quiescence.

Executed regression commands:

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~Obligation|FullyQualifiedName~Idempoten|FullyQualifiedName~Correction|FullyQualifiedName~Remittance|FullyQualifiedName~PostingCoordinator|FullyQualifiedName~CollectionSource|FullyQualifiedName~TenantFilter|FullyQualifiedName~TenantBackupCoverage|FullyQualifiedName~NpmMonthlyBillingCharacterization|FullyQualifiedName~NpmExactMonthlySettlementAcceptance|FullyQualifiedName~NpmMonthlySettlementArchitectureProof'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

Focused result: **191 passed, 0 failed, 2 skipped; total 193**. Full unit result: **2,711 passed, 0 failed, 2 skipped; total 2,713**, duration 37 seconds. Release compilation succeeded through the focused command. The two skips retain the eight F-01 examples as one skipped theory and the day-31 writer requirement as one skipped fact. No test source or skip marker changed. Temporary logs: `%TEMP%\stalltrack-task2c3-focused.log` and `%TEMP%\stalltrack-task2c3-unit.log`.

## 11. Final checkpoint and recommended next step

Final SHA-256 comparison confirmed **all nineteen prior pending paths remain byte-identical**. Task delta is exactly five new documentation/evidence/helper files; total pending files are now 24 (six tracked modifications and eighteen untracked files). Retained helper syntax checks and document link checks passed. `git diff --check` passed with only the existing LF/CRLF notice for the untouched characterization fixture. Branch/HEAD remain unchanged at `codex/local-unified-v8-v10-20261013` / `17b9d100`; nothing is staged or committed. No automatic Git commit was made to clean the earlier pending tasks.

**Recommended next step:** owner review of a narrow forward-only legacy kind-10 index correction and its retained-baseline financial regression plan. Approving that proposal/application is separate from approving any NPM month migration or financial cutover. Task 2C.3 stops here; the original index is restored and no permanent database repair has been applied.
