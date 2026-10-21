# Task 2D.2 — NPM month-source PostgreSQL foundation rehearsal

**Result: controlled rehearsal passed and was rolled back. Ready for separate permanent-migration review/approval, not installed or financially active.**

The owner's explicit follow-up authorized the seven foundation operations and synthetic fixtures inside a
rollback-only transaction against `localhost:5432 / EEMOCantilanSDS.DB`. It superseded the preparation-only
boundary for this rehearsal. It did not authorize a permanent migration, EF history changes, a service restart,
generic tenant restore, payment posting or source activation.

References: [Task 2D migration plan](NPM_MONTH_SOURCE_MIGRATION_IMPLEMENTATION_PLAN_20261017.md),
[restore-v2 safeguards](NPM_RESTORE_V2_SAFEGUARDS_20261017.md),
[foundation contract](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md), and
[completed kind-10 correction](LEGACY_ALLOCATION_INDEX_PERMANENT_CORRECTION_PLAN_20261017.md).
Current owner rulings govern this test. Financial-rule-review and documentation-governance procedures applied;
no billing, source identity, occupancy ownership or approved policy semantics changed.

## 1. Repository and read-only baseline

- Checkout: `C:\dev\stalltrack\eemo`.
- Branch: `codex/local-unified-v8-v10-20261013`.
- Entry/final HEAD: `17b9d100e8ddffdd747f5688a8e49818693d078a`.
- All **43 entry pending paths** were inventoried and SHA-256 hashed. All remain **byte-identical**.
  No earlier source, test, handoff, migration, snapshot or startup configuration was edited. No Platform changes,
  worktree/branch operations, staging, commit, push or deployment.
- The current baseline matches the accepted Task 2D checkpoint: **95 public tables, 143 exact original migration
  ID/ProductVersion pairs**, thirteen retained historical tables with fifteen aggregate rows, and no NpmRentMonths.
- The obsolete kind-10 unique index remains absent. The nonunique source lookup and separate kind-8 unique index
  remain valid, ready and definition-identical. This task does not repeat or undo index retirement.
- Existing Contracts triple alternate key and historical Stalls pair key remain. The two proposed parent keys
  are absent outside the rehearsal. No changed dependency or unexpected baseline delta was found.
- API processes/listeners and other database sessions were absent at the execution gates. Read-only inspections
  used loopback `::1`, port 5432, the exact database name and explicit read-only transactions.

## 2. Exact DDL exercised

Source: [review SQL](../evidence/task2d/NpmRentMonthFoundation.Up.sql.review),
[review C#](../evidence/task2d/NpmRentMonthFoundation.cs.review),
[operation manifest](../evidence/task2d/operations.json).
SQL SHA-256: `1cfc71caaaf302042e4c2fac66c74f5b24ea9fecb499f4a22d0ebc5ec4120a31`.

| Operation | Actual reviewed shape |
| --- | --- |
| 1 | Stalls alternate key `(MunicipalityId, Id, FacilityId)` |
| 2 | Facilities alternate key `(MunicipalityId, Id)` |
| 3 | NpmRentMonths: 26 columns, primary/tenant alternate key, nine checks, three composite restrictive FKs |
| 4 | Index `(MunicipalityId, ContractId, StallId)` |
| 5 | Index `(MunicipalityId, FacilityId)` |
| 6 | Index `(MunicipalityId, StallId, FacilityId)` |
| 7 | Unique index `(MunicipalityId, StallId, PeriodStart)`; ContractId excluded |

Exactly these seven statements ran in the successful transaction. Three parent links reference existing
Contracts `(MunicipalityId, Id, StallId)`, Facilities `(MunicipalityId, Id)` and Stalls
`(MunicipalityId, Id, FacilityId)` with DELETE RESTRICT. Existing parent keys and historical relationships are
not replaced. There is no eighth correction, backfill, historical-object change, EF history statement,
new CollectionSourceKind or financial writer. The drafts remain outside the active migration assembly.

## 3. Reproducible harness and safety controls

- [Harness](../evidence/task2d2/rehearsal.cjs) and [offline tests](../evidence/task2d2/rehearsal.test.cjs).
- Successful executed script SHA-256:
  `ff6533c5f49eebe8da6a1382a6fe5a8e561314457741ba593e4f302c7d0bcdc7`.
- `preflight` mode is read-only. `rehearse` additionally requires
  `STALLTRACK_TASK2D2_AUTHORIZATION=owner-approved-task2d2-seven-operations-rollback-only-local`.
  That acknowledgement is a guard, not a substitute for owner authorization. Do not replay automatically.
- Connection settings reuse the existing local API configuration/secrets without logging credentials. The
  configured host, database, port and actual server endpoint are independently checked; other targets are refused.
- No API is started/stopped. Native/dotnet API processes, known listeners and all other database sessions are
  checked before execution. Complete table locks bound the test against concurrent table writers; contention
  fails closed. There were no observed competing sessions.
- A new complete database backup is created and decoded before opening the mutation transaction. Its hash is
  rechecked before mutation. There is no reset, restore, alternate database, Testcontainers or financial command.
- One repeatable-read writing transaction per attempt; established migration advisory lock; 1.5-second lock
  timeout, 15-second statement timeout, 60-second idle-in-transaction timeout. All 95 original public tables are
  locked SHARE ROW EXCLUSIVE / NOWAIT in deterministic order. Original fingerprints are checked again under locks.
- The session creation schema is explicitly **public**. The harness rejects public functions shadowing the
  foundation's built-in check functions, user row triggers, DDL event triggers and non-view rewrite rules.
- SQL is pinned to the reviewed hash/shape. Synthetic INSERT is restricted to Municipalities, Facilities,
  Stalls, Contracts and NpmRentMonths. UUIDs are generated client-side and all fixtures are explicitly synthetic.
  No Collections, allocations, receipts, remittances, DailyCollections or real municipal records are written.
- Each expected failure uses SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE. Numeric coercion is similarly isolated.
  No `nextval`, `setval`, nontransactional index operation or external integration is called.
- A durable, fsynced intention/checkpoint is written outside Git before DDL. The runner has no COMMIT path and
  always requests final ROLLBACK. It closes the original connection before opening an independent read-only
  verifier. Lost rollback acknowledgements and failed postflight are reported unresolved, never retried as writes.
- Postflight compares all captured table contents/counts, column/default/type definitions, constraints/FKs,
  indexes/validity, views, object ownership/ACL/RLS flags, trigger/rule inventories, every public sequence's state,
  and all exact migration entries. Hashes are aggregate evidence; no personal financial rows are exported.

Commands executed:

```powershell
node --check docs/evidence/task2d2/rehearsal.cjs
node --check docs/evidence/task2d2/rehearsal.test.cjs
node --test docs/evidence/task2d2/rehearsal.test.cjs
node docs/evidence/task2d2/rehearsal.cjs preflight
# Owner authorization was acknowledged in the child process environment for the following run only.
node docs/evidence/task2d2/rehearsal.cjs rehearse
```

### Initial harness failure, preserved rather than hidden

The first writing attempt returned **42501** in the DDL phase. The initial harness incorrectly put pg_catalog
first in search_path. An unqualified CREATE TABLE then targets the system schema; PostgreSQL has
`allow_system_table_mods=off`. Read-only diagnosis showed the local role owns the parent tables, can create in
public and can acquire the advisory lock. This was a harness creation-schema error, not evidence of an NPM
constraint or a need to grant permissions. The two parent-key operations may already have executed in that
attempt; the original instrumentation did not record individual DDL steps. Do not label it a pre-DDL failure.

The transaction was explicitly rolled back and a separate read-only PID **18104** verified the exact original
state. [Failure evidence](../evidence/task2d2/result-2026-10-17T03-09-47-851Z.json) remains intact. Read-only
permission diagnostics remain under task2d2/permissions-*.json. No permission, role or server setting changed.

The harness was corrected to the reviewed `public, pg_catalog` order, with a current_schema assertion and
shadow-function rejection. Individual operation progress and script hash are now recorded. Offline checks were
rerun, all safeguards were rechecked, and a **new full backup** preceded the corrected owner-authorized attempt.
This was not an automatic write retry or a workaround around database permissions. Initial preflight also
required canonicalizing inet_server_addr with host() because inet-to-text includes its network prefix.

## 4. Fresh backup used by the successful rehearsal

| Property | Actual verified evidence |
| --- | --- |
| Path outside Git | `C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_task2d2_full_2026-10-17T03-12-22-080Z.dump` |
| Verified timestamp UTC | `2026-10-17T03:12:22.629Z` |
| Size | **1,711,539 bytes** |
| SHA-256 | `5138c96d761f738d309138e62951257e45a59353c777f7d511c8b195ba5f8e6c` |
| Verification | pg_dump custom full database: exit 0; pg_restore list: exit 0; full decode to NUL: exit 0 |
| Archive data coverage | All **95 public table data entries**, including each of the thirteen retained tables and EF history |

This is a post-kind-10-correction, pre-foundation full database backup. It contains no permanent NPM foundation.
The earlier failed-attempt backup is also preserved; it is not substituted for the newer successful-run backup.
No recovery/restore was performed. Cluster-wide role definitions are outside a pg_dump database archive.

Durable successful-run intent:
`C:\dev\stalltrack-v2-worktrees\local-db-backups\task2d2-intent-2026-10-17T03-12-22-079Z.json`.

## 5. Actual PostgreSQL results

[Successful execution evidence](../evidence/task2d2/result-2026-10-17T03-12-22-079Z.json) records the backup,
seven completed operations, 26-column/constraint metadata, exact expected SQLSTATEs and independent postflight.
UTC run: **03:12:22.079–03:12:28.992 on 2026-10-17**.

| Executed scenario group | Result |
| --- | --- |
| Duplicate physical month, including a different valid ContractId for that same stall | **2 expected 23505**, exact tenant/stall/month unique index |
| Wrong-tenant facility/stall/contract and wrong facility/stall/contract parent combinations | **5 expected 23503** |
| Invalid month boundary, money/credit relationship, basis/state, evidence shape, review metadata, actor/version/revision metadata | **19 expected 23514**, named checks recorded |
| Malformed JSON | **1 expected 22P02** |
| Missing required evidence | **1 expected 23502** |
| numeric(18,2) overflow | **1 expected 22003** |
| Valid preparation records | **4 accepted**: unknown-basis NeedsReconciliation, complete known-basis Reconciled, separate tenant, and legacy PureDays basis with cent-valued amount/credit |
| Fractional-cent boundary | PostgreSQL accepted `900.001` and stored **900.00**; rolled back to its savepoint |

Total: **29 expected rejection scenarios passed**, four valid row cases accepted, one precision-boundary
observation passed. The wrong-tenant-stall case was rejected by the contract composite FK first; the distinct
wrong-stall/facility case independently exercised the Stalls composite FK. Do not claim every conflicting FK
necessarily fires for one row; PostgreSQL stops at the first violated constraint.

Only the five allowed fixture tables changed inside the transaction. All other table fingerprints, including
Collections, lines/allocations/corrections, SRC-related records, DailyCollections, remittances and AuditLogs,
remained unchanged. All original indexes remained. Exactly eight new index objects were observed: two parent
unique-key indexes, NPM PK/AK indexes and four explicit NPM indexes. All thirteen retained tables were unchanged
even before final rollback. These direct SQL fixtures do not prove application audit generation or posting.

### Database versus application responsibilities

- Database checks enforce the tested shape, membership, money bounds, explicit basis/state values and review
  field consistency. JSON object shape is not proof of an approved rate/ordinance, or even semantic agreement
  between opaque JSON and assessment fields.
- PostgreSQL numeric coercion does **not** reject fractional cents. Existing domain validation and restore-v2
  validation must reject them before persistence; no SQL cast should be treated as a financial rounding policy.
- FKs do not establish NPM facility type, correct historical monthly owner, complete renewal/handover history,
  authorized reviewer, authentic approval or source eligibility. Existing domain rules and future authenticated
  application handlers remain mandatory.
- SettlementVersion is positive and configured as an EF concurrency token. SQL does not automatically increment
  it. Concurrent EF revisions/posting, exact monthly settlement, correction/reversal and idempotent allocation
  are **not executed or proved** by this foundation rehearsal.
- Neither preparation state authorizes collections. No new typed source or writer exists in this task.

## 6. Explicit rollback and independent postflight

Writer PID **29412** explicitly received ROLLBACK acknowledgement and was closed. Fresh read-only verifier PID
**25492** checked the exact loopback target, no other sessions and the original baseline. A further independent
read-only preflight after completion corroborated the same state. It did not perform another writing rehearsal.

| Postflight invariant | Actual result |
| --- | --- |
| NpmRentMonths / all its keys, checks, FKs and indexes | **Absent after rollback** |
| Two newly added parent alternate keys/indexes | **Absent after rollback** |
| Existing Facilities/Stalls/Contracts keys and all original historical relationships | Definition-identical |
| Original public tables / synthetic parent rows | **95 tables**, every original count/row fingerprint identical; no synthetic rows remain |
| Historical retained tables | All **13** and their original contents preserved |
| EF migration history | **143**, every ID/ProductVersion pair unchanged |
| Obsolete kind-10 unique index | Still absent |
| Current nonunique lookup / separate kind-8 index | Original definitions, valid and ready |
| SRC sequence | **last_value 17 / is_called true**, unchanged; all other public sequence states also identical |
| All-table data SHA-256 | `ec22390fe31353fd542a43cca3dc018ee10725a2de1499f8e22cd034eb33edad` |
| Checked schema SHA-256 | `6af7382bf3babb0910aad1476f60f12d6352720b4a63c09fe03482164fe2978d` |
| Extended object/ACL/RLS/trigger/rule inventory SHA-256 | `9bf0e1bea6b418cd59a4e95b262c4f0e5cf220bc3fc94931fa101d9a31d9dba3` |
| Exact history SHA-256 | `e310e9cd9bee20f9703aba284268c2a1b13f394b38b855c3d45fa1ae110e3445` |

These are logical catalog/data comparisons, not byte-for-byte database-file equality. PostgreSQL transaction
IDs, WAL and internal OID allocation may advance during rolled-back DDL. No user sequence was advanced and no
external side effect was invoked. This distinction does not justify any financial or schema mutation.

## 7. Restore-v2 and regression verification

The restore-v2 source is unchanged. Its tests retain separate old/new schema manifests, require an explicit empty
NPM table after migration, reject missing/incompatible manifests and restore-v1, reject retained/unmanaged targets,
validate row tenant/identity, and observe rejection before destructive work or success audit. SRC watermark
recovery is rejected rather than mutating the sequence. The thirteen retained tables **continue to block generic
scoped restore and restore-ready tenant snapshots** on this baseline. Logical export remains a disclosed partial
model export. No successful or destructive tenant restore was attempted in PostgreSQL.

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TenantRestoreV2SafetyTests|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~NpmRentMonth|FullyQualifiedName~CollectionSourceAuthorityMapTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

- Node syntax: both new scripts pass. Offline lifecycle/DDL safety tests: **13 passed, 0 failed**.
- Focused .NET: **150 passed, 0 failed, 2 deliberately skipped; total 152**.
- Full .NET unit result: **2,756 passed, 0 failed, 2 deliberately skipped; total 2,758**, 37 seconds. No production
  C# changed; tests use the existing Release build verified in Task 2D.1. No API, Client or Mobile process was launched.
- `KIRO_PG_RESTORE_TEST` was confirmed absent. The existing reset-oriented opt-in test did not exercise a database.
- The two financial acceptance skips remain unchanged: eight exact-settlement cases and day-31 eligibility are
  still future requirements, not green financial features.
- PostgreSQL payment concurrency, EF end-to-end restore/audit, backup restoration, tenancy handover admission,
  correction/reversal, SRC issuance, remittance and monthly-only runtime billing remain untested/not authorized.

Logs are under `%TEMP%\stalltrack-task2d2-*.log`. Durable redacted metadata, failure evidence, permission diagnosis
and successful result are in `docs/evidence/task2d2/`; database dumps and fsynced intent files are outside Git.

## 8. Remaining gates and exact task delta

**Technical conclusion:** the reviewed seven-operation foundation is compatible with the accepted local retained
baseline under this controlled rehearsal. No foundation DDL defect was found. Permanent installation remains a
separate owner decision and was not performed. This does not generalize the baseline to other databases/LGUs.

Before permanent application:

1. Explicitly approve permanent installation of these exact seven operations and the deployment window.
2. Finalize reviewed EF migration ID/designer/model-snapshot integration at the then-current HEAD. Keep the
   optional conditional kind-10 correction distinct; never replay local retirement or manually insert history.
3. Use a new verified full backup and fresh target/quiescence/locking preflight. Control automatic startup
   migration explicitly; do not use an incidental API restart as migration approval or invoke seeders unintentionally.
4. Independently verify the permanent new schema and exactly the new normal EF history entry while preserving
   all original 143 entries, historical objects/data and sequence state. Lost acknowledgement requires independent
   reconciliation, not blind rerun or compensating drops.
5. Keep generic tenant restore blocked with retained history. Successful full recovery or any future retained-aware
   tenant strategy requires its own approved test/recovery procedure. This rehearsal did not relax the guard.
6. Future typed month allocation, unified Head/Mobile balance/eligibility, exact terminal remainders, day-31 cap,
   financial concurrency/corrections and monthly-only onboarding need separate engineering/financial approval.

Task-created files only:

- `docs/evidence/task2d2/rehearsal.cjs`: reproducible, explicit-owner-guarded rollback-only runner and read-only diagnosis.
- `docs/evidence/task2d2/rehearsal.test.cjs`: deterministic offline safety tests.
- `docs/evidence/task2d2/preflight-*.json`: independent read-only baseline/parent-column observations.
- `docs/evidence/task2d2/permissions-*.json`: read-only diagnosis of the initial harness failure.
- `docs/evidence/task2d2/result-*.json`: original failed/rolled-back attempt and successful/rolled-back attempt,
  preserved separately.
- `docs/planning/NPM_MONTH_SOURCE_POSTGRES_REHEARSAL_20261017.md`: this scoped evidence/handoff.

No existing path was changed, no schema remains installed, no financial writer or source kind is enabled, no
financial record/history entry was edited, and there was no API restart, push, deployment or automatic next phase.

Final Git checkpoint: **55 pending paths** (9 tracked modifications inherited from earlier tasks, 46 untracked
files), an empty index, unchanged branch/HEAD. This task adds twelve new files: two harness files, four read-only
preflight artifacts, three read-only permission-diagnosis artifacts, two execution-result artifacts and this
handoff. All 43 entry SHA-256 hashes still match. `git diff --check`, both Node syntax checks, thirteen offline
tests and all eleven local Markdown links pass. Nothing was committed automatically.
