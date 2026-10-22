# Task 2D.1 — fail-closed restore-v2 safeguards

## Scope and approved baseline

The owner accepted the bounded retained-schema baseline described in
[Task 2D](NPM_MONTH_SOURCE_MIGRATION_IMPLEMENTATION_PLAN_20261017.md), including preservation of the
143 original migration entries, thirteen retained historical tables and documented provenance uncertainties.
That acceptance does not authorize schema application, financial activation or a restore of municipal data.
No further general migration archaeology was performed.

This task implements the approved restore safeguards only. It does not execute the seven-operation rehearsal,
apply a migration, inspect or mutate municipal rows, restart the API, enable the month source, change payment
writers, or change receipts/remittance. The new source remains preparation-only. No PostgreSQL connection was
opened by the tests or commands in this task.

## Repository checkpoint and preservation

- Primary checkout: `C:\dev\stalltrack\eemo`.
- Branch: `codex/local-unified-v8-v10-20261013`.
- Entry and completion HEAD: `17b9d100e8ddffdd747f5688a8e49818693d078a`.
- The 35 pending entry files were individually hashed. **34 remain byte-identical.** The existing pending
  `TenantExportRepository.cs` was necessarily extended to avoid querying an unmigrated `NpmRentMonths` table.
  Its prior NPM export query is preserved, now guarded by actual physical availability; no earlier work was reverted.
- No staging, commit, reset, stash, branch/worktree operation or modification of earlier planning handoffs.
  The earlier NPM characterization/architecture tests and both deliberate acceptance skips remain intact.

## Before and after

Previously, restore-v1 checked the outer tenant/version, deleted every modeled restorable tenant table, then
silently skipped missing or blank uploaded table entries. Individual row tenant IDs and complete column shape
were not checked before deletion. Export queried `NpmRentMonths` even on the pre-foundation database. Restore
also called `setval`, whose effects PostgreSQL does not roll back with the surrounding transaction.

The revised contract is **restore-v2** with a required matching `SchemaFingerprint` at the destructive boundary.
The fingerprint identifies the server's available restorable table/column/type/nullability/generated-column
manifest. It is a compatibility check, not a signature or authorization credential. Existing SuperAdmin,
password, confirmation phrase, authenticated tenant resolution and cache-invalidation boundaries are unchanged.

1. Reject unresolved/wrong tenant, missing manifest and unsupported versions before schema inspection.
   Freeze the uploaded dictionary before asynchronous work. Historical restore-v1 files remain readable and
   downloadable through the existing backup repository, but cannot be used for destructive scoped restoration.
2. Read server catalogs and the exact tenant directory entry under normal filters. Derive required tables
   from the server inventory/model and physical schema, never from uploaded names. Reject missing required
   tables or mismatched column sets; only absence of the not-yet-migrated NPM foundation table is allowed.
3. Reject destructive restore and restore-ready snapshot creation whenever public retained/unmanaged tables
   exist. This conservative presence-based rule applies even when such a table is empty. Do not guess its
   tenant ownership, backfill it, delete it or treat model-only backup as complete physical recovery.
4. Require exact table membership, matching tenant code and schema fingerprint. Every table needs an explicit
   JSON array, including `[]`. Missing, blank, null, duplicate or non-string serialized manifest entries are
   rejected. Unknown tables, including attempts to restore AuditLogs/Users, are rejected.
5. Validate each row's full column set, duplicate fields/IDs, GUID identity, required values, scalar types,
   numeric precision/scale and tenant ID. Unsupported representations fail closed. PostgreSQL typed-record
   preflight additionally validates actual timestamp/bytea/interval and other input casts before any DELETE.
6. In the mutation transaction, acquire the established migration advisory lock without waiting, bounded
   table locks, then re-read and revalidate the schema. Lock timeout is five seconds; statement timeout is
   sixty seconds. Validate tenant-owned foreign-key targets using snapshot parents or exact-tenant live
   nonrestorable principals. This also protects older single-ID relationships.
7. Only then enter the existing reverse-FK delete / forward-FK insert phase, scoped to the caller's municipality.
   There is no missing-table skip. Generated SRC code is checked against the original year/number and is
   recomputed by the existing database expression, never allocated anew.
8. Remove the scoped restore's sequence mutation entirely. Reject incoming references above the already-issued
   target watermark, including the unissued `last_value` when `is_called=false`. Sequence recovery belongs to a
   separately approved full recovery, not an incidental tenant restore. No new SRC is allocated here.
9. Keep the success audit append-only and in the restore transaction. Guard rejection adds no success audit.
   Detach the candidate audit if save/commit fails so a later SaveChanges cannot accidentally persist it.

Snapshot capture uses a repeatable-read, read-only transaction and validates its own output before the existing
backup repository stores it. Existing stored history is neither rewritten nor deleted by these guards.

## Old-schema and retained-object compatibility limits

| Target | Logical export | Restore-ready capture / destructive restore |
| --- | --- | --- |
| Pre-foundation, no unmanaged tables | Omit unavailable NpmRentMonths with warning | V2 manifest excludes unavailable table; exact old schema required |
| Foundation physically migrated, no unmanaged tables | Include NpmRentMonths | NpmRentMonths is mandatory, including explicit empty array |
| Retained/unmanaged public tables present | Model-only export with explicit warning | Blocked before destructive work |
| Old V2 snapshot against newly upgraded manifest | Export unaffected | Rejected; no implicit missing-table substitution |
| Restore-v1 or snapshot needing sequence recovery | Historical files still readable | Destructive scoped restore rejected |

On the accepted local retained-schema baseline, **manual and scheduled restore-ready tenant backup creation
will also fail closed**, rather than create a misleading partial restore artifact. The existing daily backup
service catches per-tenant failures. This is an intentional safety/availability tradeoff, not a replacement
backup engine. Logical JSON export is still available with compatibility warnings; it does not capture the
thirteen historical tables and must not be represented as a complete physical backup. Full database backup
and recovery remain the separate existing path, requiring their existing approvals.

The physical schema reader is PostgreSQL-specific. The existing InMemory export coverage test retains
model-only behavior; it does not establish live-schema compatibility. Normal runtime DI constructs the
schema reader from the existing scoped AppDbContext. No new connection configuration, backup engine,
IgnoreQueryFilters call, active migration, snapshot change or service registration is introduced.

## Exact task files

| File | Purpose |
| --- | --- |
| `EEMOCantilanSDS.Application/Dtos/Backup/TenantRestoreSnapshot.cs` | V2 version, schema fingerprint, strict table-map converter |
| `EEMOCantilanSDS.Application/Dtos/Backup/RestoreTableManifestConverter.cs` | Reject malformed/duplicate uploaded table entries before dictionary collapse |
| `EEMOCantilanSDS.Application/Dtos/Backup/TenantExportPayload.cs` | Explicit logical-export compatibility warnings |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantRestoreSchema.cs` | Server-owned schema/sequence read-only evidence and retained-table gate |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantRestoreValidation.cs` | Pure header/manifest/row/SRC preflight |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantRestoreRepository.cs` | Pre-delete orchestration, transactional preflight, append-only audit and no sequence mutation |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantExportRepository.cs` | Conditional prepared-table export and retained-object warnings; preserves pending foundation query |
| `EEMOCantilanSDS.Testing/Infrastructure/Repositories/TenantRestoreV2SafetyTests.cs` | Offline rejection, compatibility and control-flow regression cases |
| `docs/planning/NPM_RESTORE_V2_SAFEGUARDS_20261017.md` | This handoff and approval boundary |

## Verification and limits

Focused command:

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~TenantRestoreV2SafetyTests|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~TenantBackupRepositoryTests|FullyQualifiedName~RestoreTenantFromBackupCacheTests|FullyQualifiedName~CrossTenantReadsAreNamedTests'
```

- Focused result: **67 passed, 0 failed, 0 skipped**. Includes Release compilation of Application,
  Infrastructure, API and unit-test dependencies; existing warnings remain.
- Regression sensitivity proof: temporarily bypassing the outer manifest/row validation produced
  **17 failed, 4 passed** across the 21 rejection cases. Restoring the guard returned the focused suite to green.
  This mutation was offline and is not present in the final source.
- Full unit command: `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore`.
  Result: **2,756 passed, 0 failed, 2 skipped; total 2,758**, 44 seconds. The two deliberate NPM acceptance skips
  are unchanged. The new scoped file contributes 45 passing cases.
- `git diff --check` passed. Final status is **43 pending paths: 9 tracked modifications and 34 untracked files**;
  the index is empty and HEAD/branch are unchanged. Eight newly dirty paths were added by this task, plus the
  compatibility extension to the one already-pending export repository. No prior pending path was removed.
- `KIRO_PG_RESTORE_TEST` was confirmed absent before the full suite. Its existing opt-in round-trip test returns
  early without it; that test being reported passed is **not** evidence of a PostgreSQL restore exercise.
- New tests use pure validation, mocks and EF InMemory with an observed destructive-phase boundary. They prove
  rejected input cannot reach deletes, success-audit creation or sequence-writing code, and blocks do not create
  stored backup rows. They do **not** prove live PostgreSQL FK/check enforcement, locking, triggers, transaction
  rollback, concurrency, SRC sequence state or successful end-to-end restore/audit persistence.
- No main-database read/write test, seven-operation rehearsal, reset, migration, API restart or financial command
  was executed. Logs: `%TEMP%\stalltrack-task2d1-focused.log`, `stalltrack-task2d1-mutation.log`,
  `stalltrack-task2d1-build.log`, `stalltrack-task2d1-unit.log`.

## Remaining approval gates

1. Separately authorize PostgreSQL foundation/restore rehearsal and its exact backup, quiescence, rollback and
   postflight protocol. It must cover typed row casts, invalid/source relationships, no sequence movement on
   rejection, append-only transactional audit, concurrent writers/migrations and both old/upgraded schemas.
2. Authorize the seven additive operations only after their independent compatibility verification. None are applied here.
3. Define any future tenant-level retained-object recovery strategy explicitly. Current generic scoped restore
   stays blocked; this task does not reinterpret or discard retained data.
4. Authorize deployment/restart separately. Existing handlers' general commit/lost-response reporting has not been
   redesigned by this bounded guard task; reconcile uncertain transactional outcomes independently before retrying.
5. Typed month allocations, unified settlement, the Cantilan day-31 cap, terminal remainders and monthly-only
   onboarding remain subsequent financial tasks. The two skipped acceptance tests remain pending requirements.

**Stop after Task 2D.1.** No schema or financial cutover is implied by successful offline tests.
