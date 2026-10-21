# Task 2D.3 — finalized NPM month-source EF migration readiness

**Ready for reviewed source promotion and separate permanent-application approval. Not promoted, applied, restarted or financially activated.**

This task finalizes the EF artifact, designer and candidate snapshot for the accepted seven-operation foundation.
It reuses the accepted [Task 2D.2 PostgreSQL evidence](NPM_MONTH_SOURCE_POSTGRES_REHEARSAL_20261017.md);
it does not repeat that rehearsal or open a database connection. The accepted retained-schema baseline and
[restore-v2 restrictions](NPM_RESTORE_V2_SAFEGUARDS_20261017.md) remain unchanged.

## 1. Repository checkpoint and scope

- Primary checkout `C:\dev\stalltrack\eemo`, branch `codex/local-unified-v8-v10-20261013`.
- Entry/final HEAD `17b9d100e8ddffdd747f5688a8e49818693d078a`.
- All **55 entry pending files** were individually hashed and preserved byte-for-byte. No earlier test,
  foundation source, restore safeguard, evidence or planning handoff was rewritten.
- Active migrations, active model snapshot, API startup, configuration, source enum, financial writers,
  historical records and Platform are unchanged. No staging, commit, branch/worktree operation, database
  connection, backup, rehearsal, application, API restart, push or deployment.
- Applied financial-rule review and documentation-governance guidance; used the smallest complete offline
  preparation rather than another migration runner or financial abstraction.

## 2. Finalized migration package

Final candidate ID: **`20261017040000_AddNpmRentMonthFoundation`**.
It follows the actual latest compiled ID **`20261015234000_NpmRegistrationRequestCancellation20261015`**.

| Artifact under `docs/evidence/task2d3/` | Purpose / SHA-256 |
| --- | --- |
| [20261017040000_AddNpmRentMonthFoundation.cs.review](../evidence/task2d3/20261017040000_AddNpmRentMonthFoundation.cs.review) | Final Up and forward-only Down; `5a4df8d065aac62b858748a9aedc0e951435f969bcbe34c7aed84a730feb2346` |
| [20261017040000_AddNpmRentMonthFoundation.Designer.cs.review](../evidence/task2d3/20261017040000_AddNpmRentMonthFoundation.Designer.cs.review) | Real Migration/DbContext attributes and complete target model; `01eeca24f4797404ad2d846ee808d588f932d10baaffae14f2f23c34f5516ef7` |
| [AppDbContextModelSnapshot.cs.review](../evidence/task2d3/AppDbContextModelSnapshot.cs.review) | Complete candidate replacement snapshot; `15129f5b2e9aa7c19cfb2005f2feb92297178c7be48dcbd33942d776a519796f` |
| [20261017040000_AddNpmRentMonthFoundation.Up.sql.review](../evidence/task2d3/20261017040000_AddNpmRentMonthFoundation.Up.sql.review) | Final generated executable statements; `f65f63121fe7d439f549ebb5b8a96ab0774a302d207a8558e04b6391ed2722c7` |
| [ReadinessTestBaseline.patch.review](../evidence/task2d3/ReadinessTestBaseline.patch.review) | Checked, unapplied compatibility patch for the existing protected readiness test |

These are compiled EF sources, not pseudocode. The isolated offline project
`tools/npm-month-source-migration-readiness/Readiness.csproj` explicitly compiles the `.cs.review` files into
its own assembly. The API's Infrastructure migration assembly remains at **130** and cannot discover them.
No active snapshot was replaced. This avoids an incidental startup applying an unapproved migration and
preserves the protected working files.

The final migration contains exactly:

1. Stalls alternate key `(MunicipalityId, Id, FacilityId)`.
2. Facilities alternate key `(MunicipalityId, Id)`.
3. NpmRentMonths with 26 columns, PK/tenant alternate key, nine checks and three restrictive composite FKs.
4. Index `(MunicipalityId, ContractId, StallId)`.
5. Index `(MunicipalityId, FacilityId)`.
6. Index `(MunicipalityId, StallId, FacilityId)`.
7. Unique tenant/stall/month index, excluding ContractId from the assessed identity.

The compiled migration's generated commands match the accepted Task 2D SQL **exactly after removing its review
header and normalizing line endings**. Its statement order, columns, checks and keys are unchanged. The final
SQL file has a different file hash solely because it omits the old review-header comment. No correction migration,
data operation, destructive operation, backfill, source-kind change or retained-object removal is added.
Every generated command has `TransactionSuppressed=false`.

`Down` throws NotSupportedException before generating any reversal operation. There is no automatic drop of
the month table, parent keys or preparation evidence. That is deliberate forward-recovery policy, not an empty
Down that would misleadingly remove history while leaving schema behind.

## 3. Snapshot and designer compatibility proof

The final [offline verification record](../evidence/task2d3/verification-20261017034328278.json) establishes:

- Correct final Migration ID and AppDbContext metadata on the compiled candidate.
- **Zero EF relational-model differences** between designer target and current design-time model.
- **Zero differences** between candidate snapshot and designer target.
- The old active snapshot to candidate snapshot remains exactly seven additive operations, no destructive delta.
- Active snapshot textual comparison is **152 insertions only**: the new entity/relationships and parent keys.
- Compiled Up SQL still matches the rehearsed statements; Down is blocked; both money columns remain required
  decimal `numeric(18,2)`; no command suppresses the migration transaction.

An initial verifier assumption required snapshot-generated SQL to be text-identical too. That failed because
the generated snapshot declares the new table properties alphabetically, whereas the real migration preserves
the rehearsed declaration order. EF reports zero model differences; column order is the difference. The final
verifier checks semantic model identity and exactly seven additive snapshot operations while retaining strict
SQL equality for the actual executable migration. It does not regenerate or substitute the rehearsed DDL.

### Concrete historical metadata finding

The latest compiled migration lives at
`EEMOCantilanSDS.Infrastructure/Persistence/Migrations/20261015234000_NpmRegistrationRequestCancellation20261015.cs`.
It is hand-authored and has no BuildTargetModel/designer. Its TargetModel is therefore empty and cannot be used
as the full pre-foundation baseline. The existing active
`EEMOCantilanSDS.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` is the complete accepted baseline.
No historical migration was repaired or reconstructed; this is not a new schema conflict or another lineage audit.

After promotion, the current protected readiness test cannot continue expecting a pending seven-operation
difference from the *updated* active snapshot. The review patch makes that test inspect the final migration's
UpOperations instead, retaining every existing key/table/FK/check assertion. `git apply --check` passes; the patch
is **not applied** in this task. Current offline verification already proves both the original-to-candidate delta
and candidate-to-current zero difference. Do not pin the future test to the empty latest historical TargetModel.

## 4. Financial precision validation

No financial production code needed alteration. Existing `NpmRentMonth.ValidateAssessment` already rejects
negative values, credits above assessment, values beyond **9,999,999,999,999,999.99**, and fractional cents before
Prepare or ReviseAssessment changes state. It does not round an invalid input into an approved amount.

The new `EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthPrecisionTests.cs` reuses existing source fixtures:

| Cases | Actual coverage |
| --- | --- |
| Six valid cases | Zero, one cent/full credit, independent cent-valued assessment/credit, numeric(18,2) maximum, maximum full credit, equivalent trailing-zero decimal representation |
| Nine rejected cases | Assessment/credit fractional cents including .001/.005, maximum plus one cent, credit overflow, decimal extrema and negative fractional credit |
| Rejected revision | Original 900/0 amounts, version 1 and null UpdatedAtUtc remain unchanged; collections stay disabled |

This captures the accepted PostgreSQL fact that `numeric(18,2)` coerces `900.001` to `900.00`; a post-coercion SQL
CHECK is not a fractional-cent rejection mechanism. No such misleading check or new rounding rule was added.
Restore-v2's raw JSON value checks remain intact. Future application writers must use the domain factory/revision
guards and validate their input boundary; raw SQL and prematurely rounded external input are not legitimized by
these typed-decimal tests. No new API or monthly posting route is enabled here.

Cantilan rates, historical PureDays, daily cash evidence, monthly settlement, corrections, remittance and
collection permissions remain unchanged. Exact terminal settlement and the day-31 cap are still deliberately
pending financial acceptance requirements, not implemented by this schema package.

## 5. Migration history safety

The accepted Task 2D.2 evidence supplies the **143 applied ID/ProductVersion pairs**. No database is queried again
in Task 2D.3. The offline verifier proves that all **130 current compiled IDs** occur in that accepted history,
retains the thirteen extra IDs without reconstructing or deleting them, and simulates adding the final candidate.
The resulting proposed registry has **131** IDs and exactly **one pending ID**, the foundation.

If separately approved/applied, the expected local totals are **144 applied / 131 compiled**. All original 143
entries remain unchanged; the difference of thirteen remains. The provider-generated normal bookkeeping is:

```sql
-- Preview only. EF owns this insertion as part of its approved migration; never run it manually.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261017040000_AddNpmRentMonthFoundation', '9.0.4');
```

No history row is inserted, deleted or edited here. No missing old migration is replayed and no synthetic
historical class is introduced. The already-retired kind-10 index remains a separate accepted correction;
this candidate does not record/replay it as a hidden eighth operation. The thirteen retained tables remain
outside the EF model and are preserved by the unchanged additive Up, not silently claimed as modeled backup data.

## 6. Controlled permanent-application procedure — not executed

The following is a reviewable procedure for a **separately approved maintenance operation**. This task does not
authorize promotion into the live migration assembly, application, restart or deployment.

1. **Approve source integration and permanent scope.** Review the four candidate hashes above and underlying
   foundation/restore work. Approve only the seven operations and normal EF bookkeeping. Integrate/commit scoped
   reviewed work explicitly; do not stage all 55 earlier paths as cleanup. A different HEAD/model invalidates the
   assumptions until a fresh offline delta review passes.
2. **Control startup before promotion.** The API is not started. Its Program conditionally calls
   DatabaseStartup.MigrateAndSeedAsync when Database:ApplyMigrationsAtStartup is true; that path also seeds data.
   Approve/control the effective startup setting and process window outside this task. A normal API restart is not
   an acceptable migration-only runner. No active setting is changed here.
3. **Promote exact artifacts with the API inactive.** Remove `.review` from the migration and designer filenames
   into `EEMOCantilanSDS.Infrastructure/Migrations/`, replace that directory's active AppDbContextModelSnapshot.cs
   with the reviewed candidate, and apply the reviewed test compatibility patch. Do not leave a second snapshot
   with the same context or overwrite the archived original evidence. Build/test without launching the host.
   Verify active compiled count 131, final ID, exactly seven Up operations, zero current-model/snapshot difference,
   forward-only Down and no new financial route. Current active files intentionally remain unpromoted in Task 2D.3.
4. **Generate/review the complete EF execution script offline.** Include normal EF history bookkeeping in review,
   not in a manually executed history fix. Confirm only the foundation is pending relative to the fresh target's
   exact applied set. An older missing current-line migration or a new unexpected schema object is a stop condition.
5. **Fresh target/backup/quiescence gate.** Revalidate loopback localhost:5432 / EEMOCantilanSDS.DB, original history,
   thirteen retained objects, corrected indexes, parent keys and foundation absence. Create/verify a fresh complete
   post-correction database backup, all historical data/history coverage and a durable checksum. The Task 2D.2 backup
   is evidence of that earlier rehearsal, not automatically fresh enough for permanent application. Require no API
   or competing database writers; capture fresh schema, per-table data, history and all sequence baselines.
6. **Use a migration-only EF path, not the web host.** After separate approval, use the existing AppDbContext/
   canonical EF migrator with explicit validated local connection and exact final target ID. Hold the existing
   DatabaseStartup migration advisory lock for the connection lifetime, use reviewed bounded lock/statement
   timeouts, and release it explicitly in finally. Use EF's normal migration transaction; do not wrap it in an
   untested nested transaction, invoke seeders, add synthetic fixtures or bypass existing history locks. Write/fsync
   the intended target, artifact/backup hashes and preflight outside the DB **before invoking the migrator**, since
   it owns COMMIT internally. No custom payment command or generic migration engine is needed.
7. **Independent postflight before any restart.** Close the original connection and verify from a fresh read-only
   connection: new table/26 columns/nine checks/three FKs/four explicit indexes, only the two new parent keys, empty
   NpmRentMonths, exact new EF history row and all original 143 entries. Compare every *preexisting non-history
   table* and all retained rows/indexes/FKs/sequences unchanged, including SRC. The whole-database data hash must
   change for normal history insertion/new table membership; do not mistakenly require the pre-application global
   hash to remain identical. Only that expected bookkeeping/data-manifest delta is acceptable.
8. **Handle failure and recovery honestly.** Before acknowledged completion, a connection error may represent an
   unknown COMMIT outcome. Do not assume rollback, retry, delete history or issue compensating drops. Inspect exact
   schema/history/data from a fresh connection and reconcile. After a committed schema, prefer an approved forward
   correction. Full-database recovery affects all municipal data and needs separate approval/outage verification.
   Down intentionally refuses automatic reversal, even if the new table appears empty.
9. **Keep financial activation separate.** Schema approval does not authorize month creation/backfill, typed
   allocations, cash posting, source discovery, changes to old daily writers, Head/Mobile balances or monthly-only
   onboarding. Generic tenant restore remains fail-closed with retained historical tables; no restore-v1 or missing
   table workaround is permitted. Obtain separate restart/deployment acceptance after schema verification.

No permanent-application command was executed or tested against PostgreSQL in this task. End-to-end EF history
commit/recovery behavior and operational lock duration remain deployment-time verification gates; the accepted
raw-DDL rehearsal is not a claim that those EF application paths have already run.

## 7. Actual verification

```powershell
# Generation was performed once into new, non-discoverable files; it refuses to overwrite them.
dotnet run --project tools/npm-month-source-migration-readiness/Readiness.csproj -c Release -- generate
dotnet run --project tools/npm-month-source-migration-readiness/Readiness.csproj -c Release --no-restore -- verify
git apply --check docs/evidence/task2d3/ReadinessTestBaseline.patch.review

dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~TenantRestoreV2SafetyTests|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~CollectionSourceAuthorityMapTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

| Check | Actual result |
| --- | --- |
| Final candidate compilation and offline EF verification | Passed; ID/context, exact Up SQL, seven additive operations, designer/snapshot models, precision, transaction eligibility, Down and proposed history set verified |
| Focused unit suite | **165 passed, 0 failed, 2 deliberately skipped; total 167** |
| Full unit suite | **2,771 passed, 0 failed, 2 deliberately skipped; total 2,773**, 41 seconds |
| New precision cases | **15 passed**; no production financial change required |
| Review patch check | Passed; not applied |
| Active snapshot/startup diff | No changes |
| PostgreSQL/application/rehearsal | **Not run**, no database connection opened |

The offline context uses an unused port-1 configuration and an interceptor that throws before any connection
opens; it does not read real credentials or start another backend/database. Tool generation uses installed EF
Core/Npgsql **9.0.4**. Existing build warnings and the provider design-time EF1001 tooling warning remain; no
production dependency or package upgrade is introduced. The two pending financial skips are untouched.
KIRO_PG_RESTORE_TEST was absent, so the existing reset-oriented opt-in test did not exercise a database.

The initial snapshot-SQL equality check and attempted latest-migration-target baseline check failed offline;
their concrete causes and final correct comparisons are recorded above. No financial engine, constraint,
historical migration or existing regression test was weakened to pass. Verification files are timestamped;
the linked final record, not earlier intermediate records, is the promotion manifest.

Logs: `%TEMP%\stalltrack-task2d3-generate.log`, `stalltrack-task2d3-verification.log`,
`stalltrack-task2d3-focused.log`, `stalltrack-task2d3-unit.log`.

## 8. Exact task files and remaining approval

Added only:

- `tools/npm-month-source-migration-readiness/Readiness.csproj` and `Program.cs`: offline generation/compilation/
  verification, isolated from the startup assembly and guarded against database connections.
- `EEMOCantilanSDS.Testing/Domain/Payments/NpmRentMonthPrecisionTests.cs`: fifteen existing-rule boundary cases.
- `docs/evidence/task2d3/`: four finalized EF/SQL artifacts, an unapplied compatibility patch and timestamped
  verification manifests containing hashes and the exact proposed history delta.
- `docs/planning/NPM_MONTH_SOURCE_PERMANENT_MIGRATION_READINESS_20261017.md`: this readiness handoff.

**Remaining owner decision:** approve the scoped source promotion and the controlled permanent local schema
application/verification window. Financial activation and API restart remain separate permissions. There is no
new unresolved schema conflict requiring Task 2D.2 to be repeated. This working tree remains a preparation
checkpoint, not a claimed startup-ready deployed build: the old active snapshot still has the seven intentional
pending model operations until promotion is approved.

Final Git status: **69 pending paths** (9 inherited tracked modifications and 60 untracked files), empty index,
unchanged branch/HEAD. This task added fourteen paths: the two-file offline tool, one precision-test file, four
finalized EF/SQL artifacts, one unapplied review patch, five timestamped offline verification records and this
handoff. All 55 entry hashes match. `git diff --check`, the review patch's dry check, all eight local Markdown
links and clean active-migration/startup diffs pass. No automatic commit was made.
