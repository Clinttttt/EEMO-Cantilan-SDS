# Task 2D — NPM month-source migration implementation plan

**Checkpoint B — READY WITH EXPLICIT OWNER DECISIONS.** Technical preparation is complete for the seven additive foundation operations. The corrected retained database supports a defensible **bounded forward-compatibility baseline**, subject to owner acceptance and the restore restrictions below. No new material schema mismatch was found. This is a review/application-approval checkpoint, not migration execution or financial activation.

Required decisions are finite: accept the retained-object baseline; approve the fail-closed tenant-restore policy and its implementation gate; authorize the PostgreSQL rehearsal/application sequence. There is no requirement to repeat migration archaeology unless new concrete evidence contradicts this checkpoint.

## 1. Repository, authority and scope

- Checkout `C:\dev\stalltrack\eemo`; branch `codex/local-unified-v8-v10-20261013`; HEAD **`17b9d100e8ddffdd747f5688a8e49818693d078a`**, verified at entry.
- All **29** pending Task 2A–2C.4A paths were inventoried and hashed. They are protected from Task 2D edits/staging/automatic commit.
- Read root AGENTS, current financial/architecture guidance, [permanent correction result](LEGACY_ALLOCATION_INDEX_PERMANENT_CORRECTION_PLAN_20261017.md), [lineage reconciliation](EF_MIGRATION_LINEAGE_RECONCILIATION_20261016.md), [schema readiness](NPM_MONTH_SOURCE_SCHEMA_READINESS_20261016.md), [foundation](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md) and [Option C decision](NPM_MONTHLY_SETTLEMENT_ARCHITECTURE_DECISION_20261016.md). Financial-rule review and documentation-governance procedures apply. Earlier reports' blocked statuses are historical; this document uses the completed correction as new evidence.
- Task 2D creates review artifacts/documentation only. No application, enum, API, financial writer, active migration directory, model snapshot, tenant restore implementation, configuration or earlier handoff is changed.
- No database DDL/DML, history edit, reset-oriented integration test, provisioning, API restart, new worktree/branch, merge, push or deployment. No repetition or reversal of Task 2C.4A.

## 2. Actual corrected database checkpoint

The existing configured local connection was constrained to loopback / port 5432 / **EEMOCantilanSDS.DB**. Inspection used repeatable-read/read-only transactions, connection read-only defaults, bounded timeouts and explicit ROLLBACK. Queries used catalogs, history and aggregate fingerprints; no personal financial records or credentials were exported.

| Read-only check | Actual result |
| --- | --- |
| PostgreSQL | 18.4, loopback `::1`, read-only mode on |
| Obsolete unique allocation index for kind 10 | **Absent** |
| Nonunique allocation source lookup | Present, original definition, valid/ready |
| Separate unique kind-8 index | Present, original definition, valid/ready |
| EF history | **143**, every MigrationId/ProductVersion matches the earlier inventory |
| Compiled migration registry | **130**, unchanged; all its IDs were already applied |
| Public tables | **95**, including 13 retained historical tables and EF history |
| Checked schema fingerprint | `6af7382bf3babb0910aad1476f60f12d6352720b4a63c09fe03482164fe2978d`, exactly the corrected Task 2C.4A checkpoint |
| All-table data fingerprint | `ec22390fe31353fd542a43cca3dc018ee10725a2de1499f8e22cd034eb33edad`, unchanged |
| SRC sequence last_value/is_called | **17 / true**, unchanged |
| NpmRentMonths | Not present |
| Proposed Facilities/Stalls alternate-key duplicate groups | **0 / 0** |

Sanitized evidence: [checkpoint.json](../evidence/task2d/checkpoint.json). That file records actual parent keys, retained-table counts/fingerprints and both remaining index definitions. Exact agreement with the corrected schema/data checkpoint establishes no unexpected changes within the inspected scope; it is not an exhaustive privilege/catalog audit or PostgreSQL constraint execution.

The historical Stalls `(MunicipalityId, Id)` key and Contracts `(MunicipalityId, Id, StallId)` key remain. Existing retained ECF dependencies must not be removed. Prior read-only checks found zero invalid parent tenant/ownership relationships; unchanged whole-table fingerprints corroborate that those parent data have not changed.

## 3. Bounded retained-schema compatibility recommendation

**Recommend owner acceptance of a retained-object, forward-only baseline for this additive foundation.** This means accepting the actual corrected schema plus documented extra objects, not declaring it identical to the 81-table compiled snapshot or asserting complete authentic migration lineage.

Evidence is sufficient for that recommendation:

1. Task 2C.2 compared all 1,202 mapped columns (types/nullability), 116 mapped keys, 89 FKs and 163 mapped indexes against actual metadata; mapped objects were present. All 58 mapped check names existed, with documented deparser/equivalence limits. The corrected schema fingerprint is now stable.
2. Ten extra migration implementations have authentic older Git provenance; two have corroborated historical working-copy source/designer evidence. All thirteen retained tables' structural origins are explained by those recovered implementations. The demonstrated kind-10 incompatibility has been corrected under owner approval and independently verified.
3. The new model's exact snapshot difference contains **only seven additive operations**, independently generated and checked offline. It neither derives a destructive schema diff from the actual database nor imports historical mappings into the current model.
4. The unrecovered May implementation and unverified `vw_Accounts` creation remain bounded uncertainties. Current stall uniqueness has independently known later migration definitions, and the new foundation neither modifies that view nor replaces those indexes. Their provenance is not silently declared resolved.

Acceptance conditions:

- Preserve every existing row, historical table/FK/index/view/sequence and the **143 original history entries**, except the already approved retired index. Do not restore old migration classes wholesale, backfill, rename, reinterpret, delete or manually synchronize history counts.
- Preserve historical numeric source-kind meanings as provenance. Old kind 10 and current kind 10 are not evidence that old Vegetable rows are current ObligationPeriods. Equal names/integers establish no association. This migration does not allocate or move historical money.
- Preserve the prior recovered-source hashes and provenance evidence; no additional archaeology is proposed as a routine prerequisite.
- Treat the thirteen unmapped tables as retained external objects, **not abandoned disposable data**. They contain 15 aggregate rows, all still covered by the unchanged data fingerprint. Generic tenant restore is not a safe preservation mechanism for them; use the restrictions in section 6.
- If any target has a different parent-key definition, invalid dependency, source index shape or migration membership, fail its preflight and review that concrete difference. Do not generalize this one local database's acceptance to every municipality/environment.

No material technical mismatch currently prevents preparing the additive foundation. Owner baseline acceptance and controlled verification/application are still necessary; current financial runtime defects are not cured by a schema anchor.

## 4. Exact prepared artifacts and operations

All artifacts are under **docs/evidence/task2d**, outside Infrastructure's active migration paths. `.review` suffixes and no registration metadata prevent startup discovery. No final migration ID, active designer or updated snapshot has been added.

| Artifact | Purpose / verification |
| --- | --- |
| [NpmRentMonthFoundation.cs.review](../evidence/task2d/NpmRentMonthFoundation.cs.review) | C# Up generated from actual model-difference operations. Compiled in a temporary offline console only. Down throws NotSupportedException instead of discarding evidence. |
| [NpmRentMonthFoundation.Up.sql.review](../evidence/task2d/NpmRentMonthFoundation.Up.sql.review) | Npgsql-generated SQL for exactly those Up operations; no migration-history write, data backfill or financial operation. Not executed. |
| [operations.json](../evidence/task2d/operations.json) | Seven operation kinds, all non-destructive. |
| [LegacyKind10Retirement.Up.sql.review](../evidence/task2d/LegacyKind10Retirement.Up.sql.review) | **Separate** proposed conditional forward correction for other/fresh environments; not part of the seven operations, not executed locally. |
| [checkpoint.json](../evidence/task2d/checkpoint.json) | Read-only retained-schema readiness evidence. |

### The seven EF operations, in generated order

1. AddUniqueConstraint **AK_Stalls_MunicipalityId_Id_FacilityId** on existing Stalls `(MunicipalityId, Id, FacilityId)`.
2. AddUniqueConstraint **AK_Facilities_MunicipalityId_Id** on existing Facilities `(MunicipalityId, Id)`.
3. CreateTable **NpmRentMonths**, 26 columns, primary/alternate keys, three restrictive composite FKs and nine checks.
4. CreateIndex **IX_NpmRentMonths_MunicipalityId_ContractId_StallId**.
5. CreateIndex **IX_NpmRentMonths_MunicipalityId_FacilityId**.
6. CreateIndex **IX_NpmRentMonths_MunicipalityId_StallId_FacilityId**.
7. CreateUniqueIndex **IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart**.

The PK/alternate key create supporting unique indexes within table creation; those are not additional EF CreateIndex operations. The plan **does change two existing parent tables**, through alternate keys; it is not merely a new table. No operation changes Collections, allocations, DailyCollections, remittances, assessment periods, legacy tables or existing Contracts keys.

### Source contract and structural invariants

- One row per **`(MunicipalityId, StallId, PeriodStart)`**. ContractId is deliberately excluded from uniqueness, so a handover/renewal cannot create two full obligations for the same stall/month.
- PK `Id`; alternate key `(MunicipalityId, Id)` prepared for future tenant-safe typed allocation.
- Composite FKs: tenant/facility → Facilities; tenant/stall/facility → Stalls; tenant/contract/stall → Contracts. All DELETE RESTRICT. Existing Contracts triple key is reused; historical Stalls pair key is untouched.
- Required GUID identity/actor fields; first-of-month date; separate ownership/rate as-of dates; nullable explicit MonthBasis, with only RentGoal/PureDays values permitted when known.
- AssessedAmount and CreditedAmount are **numeric(18,2)**, nonnegative, credits no greater than assessment. No editable cash/outstanding balance is stored. Fractional cents must be rejected by domain/application validation before PostgreSQL numeric coercion.
- Required bounded policy reference and nonempty object-shaped **jsonb** rate/credit evidence. These checks validate structure, not authenticity of an ordinance or rate approval.
- NeedsReconciliation=1 and Reconciled=2 enforce their recorded review fields; they are **not financial activation states**. `CanAcceptCollections` remains unconditionally false, ignored by EF. Unknown historical basis is retained as null and cannot be reconciled without approved evidence.
- SettlementVersion is positive **bigint** and an EF optimistic-concurrency token. Domain changes increment it; no database trigger automatically increments it. Creation, revision reason, reviewer and UTC timestamps remain auditable; authentication/authorization must be supplied by future handlers.
- FK membership does not establish canonical monthly ownership, NPM facility type, authentic approval or actor permission. Existing `StallOccupancy.AnsweringForMonth` and the domain's ambiguity checks remain authoritative. No historical owner is guessed from the current holder.

## 5. Migration lineage and separate correction artifact

The current difference of **13** between applied and compiled histories is retained, explained and never "fixed" with manual history edits. A later approved normal EF migration adds its own new history entry; that does not rewrite an old entry or fabricate the absent implementations.

Recommend two separately reviewable source-controlled forward artifacts:

1. Conditional legacy kind-10 retirement, if chosen for reproducible deployment to other retained/fresh environments.
2. The seven-operation NPM foundation.

The separate conditional SQL draft requires the canonical nonunique lookup and verifies an exact old index definition/dependencies **if it exists**. Absence is a legitimate no-op for a fresh 130-migration database and this already-corrected local database. It preserves an optional kind-8 index verbatim, rather than requiring that historical index to exist on fresh databases. A same-name incompatible object fails closed. No automatic Down reinstates obsolete uniqueness. The draft is source-reviewed only; PostgreSQL execution of that conditional draft remains pending approval/testing.

Do not execute it now or replay Task 2C.4A. The existing local permanent correction remains recorded by its owner-approved plan/evidence/journal. If both future artifacts are approved/integrated/applied through EF, the local totals would become **145 applied / 132 compiled**; if only the foundation is approved, **144 / 131**. In either case the historical difference remains 13. These are future arithmetic examples, not current history changes.

Final IDs/designer metadata/snapshot integration must be selected at the subsequently verified HEAD, after the current latest migration. On approval, generate/review the target metadata through the established design-time path without booting the API. Review the snapshot delta: only the two parent keys and NpmRentMonths. Never generate a destructive database-to-model reconciliation, manually insert an EF history row for the local index operation, or restore old classes merely to match counts.

## 6. Tenant backup/restore decision and concrete safety gate

**Recommended policy: fail closed before the first DELETE.** This is a proposed mandatory compatibility change/operational gate, **not implemented protection in Task 2D**.

Current source evidence: `TenantRestoreSnapshot.CurrentFormatVersion` is **restore-v1**. `TenantRestoreRepository.RestoreAsync` validates format/tenant, deletes all current restorable tenant tables, then skips absent/empty snapshot tables. Task 2C adds NpmRentMonths to the model inventory. An older restore-v1 snapshot lacking that table could therefore empty prepared evidence silently after migration. Merely listing the table in export/restore inventory does not solve this.

The thirteen retained tables are outside the EF restore graph/inventory. Some reference mapped parent records. Leaving them unmodeled does not make delete/reinsert safe: retained restrictive FKs may cause a restore failure, while incomplete preparation snapshots can silently lose evidence. Do not claim the current tenant snapshot captures the entire retained database.

Approve these explicit rules before **operational use/restart or retention of prepared month records**:

1. Validate a required-table/schema manifest before any deletion, including an explicit NpmRentMonths entry even when its value is `[]`. Missing is different from deliberately empty. Reject incompatible older snapshots rather than silently inventing an empty table.
2. Use a versioned manifest/format upgrade (recommended next format **restore-v2**, final name subject to owner acceptance). Existing restore-v1 files remain readable as historical evidence/export, but are not silently accepted for destructive restore into the newer foundation schema. Any future conversion must be explicit, separately reviewed and must not invent month history.
3. For this retained baseline, prohibit generic model-only tenant restore while retained historical rows/dependencies would be omitted or endangered. An approved server-side pre-delete guard must discover/check the known retained objects and applicable tenant ownership; it must not rely on user-supplied table lists or disable tenant filters to guess identities. Until implemented/tested, do not authorize this route as a recovery procedure. No route/configuration was changed here.
4. Preserve the retained objects through a **full database backup/recovery** procedure with office-wide impact approval, or design a separately approved faithful legacy preservation contract. Do not bolt old financial entities back into the current cash model or silently delete them. Audit history remains append-only and outside overwrite restore semantics.
5. Test cross-tenant restore isolation, FK ordering, required manifests, absent-table rejection, exact existing SRC retention and failure rollback. Existing sequence reconciliation uses setval; its effects are not transactional. Verify failure/sequence behavior explicitly in the authorized harness; do not promise that ROLLBACK resets sequences or regenerate SRCs.

This policy resolves the design choice: **reject incompatible destructive restores; preserve full retained data; no automatic migration/backfill**. Owner acceptance and guard implementation are still pending. Schema-only installation with the application kept stopped is distinguishable from approving operational backup/restore or creating month evidence.

## 7. Controlled PostgreSQL verification required before application approval

No database-writing integration tests were authorized/run in Task 2D. Existing reset/migration fixtures must never be redirected to the municipal database.

The next owner-approved verification scope should cover:

| Boundary | Required PostgreSQL proof |
| --- | --- |
| DDL applicability | Apply the reviewed exact seven statements in a controlled rehearsal; validate both parent keys, new table/index definitions and preservation of old Stalls pair/ECF FKs; rollback proves no lasting schema/history/data change. |
| Uniqueness | Two proposed rows for one tenant/stall/month rejected regardless of ContractId/status/version. Concurrent attempts must not create two anchors. |
| Tenant/ownership FKs | Wrong municipality, facility for stall, or contract for stall rejected; valid historical relationships retained. FK success alone is not monthly-owner authority. |
| Checks/types | Required nulls/empty GUIDs, non-first dates, invalid basis/status/version, negative/over-credit amounts, malformed/empty JSON, incomplete review/revision metadata rejected. Valid unknown basis/unreconciled state and zero/full-credit assessment remain supported. |
| Audit/version | Two-context optimistic conflict, domain version progression, semantic/interceptor audit persistence and atomic failure rollback. Raw SQL does not automatically increment version. |
| Source inactivity | Preparing/reviewing/revising an anchor creates **zero Collections, SRCs, allocations, DailyCollections or remittance events**. |
| Legacy/restore | Thirteen tables preserved, all 143 original history entries retained, conditional correction absent/present/mismatch cases tested separately, old/incomplete backups refused before deletion, other tenant unchanged. |

A same-database synthetic **rollback-only** DDL/FK/check rehearsal may be proposed under the owner's development-testing policy, but needs explicit authorization for this new scope, a fresh full backup, writer quiescence and the established bounded-lock/savepoint/rollback proof. Past authorization for the index rehearsal is not permission for Task 2D writes. Cross-session visibility/concurrent commit and full restore tests require a genuinely isolated approved fixture lifecycle; one transaction or EF InMemory cannot prove them. If isolated infrastructure is needed, request its scope separately; none was provisioned here.

Do not label future tests as passed. The financial cap/terminal remainder scenarios remain the two intentionally pending NPM acceptance entries, not successful schema-foundation tests.

## 8. Application, locking and recovery procedure after approval

1. **Approval/release preparation:** accept sections 3/6, approve the verification scope, decide whether the separate conditional correction enters the source line, finalize IDs and review active migration/designer/snapshot changes. Stage explicit task-owned paths only after reviewing the 29 preserved pending changes; no automatic commit is authorized here.
2. **Fresh target preflight:** verify exact target/tenant schema, schema/history membership, keys/indexes/dependencies/duplicate groups, expected empty new-table absence, code/artifact hashes, writer/API quiescence and timeouts. Fail on unexpected state instead of using IF NOT EXISTS to conceal a partial/mismatched installation.
3. **Backup/recovery readiness:** create a new full custom backup **after the completed index correction**, verify listing/full decoding and all retained tables/history/view/sequence. The latest Task 2C.4A backup was made **before** the index retirement: restoring it would restore that old index. Record that baseline difference; do not mistake it for a post-correction or post-foundation snapshot. No new backup was needed/created in this read-only preparation task.
4. **Startup isolation:** Development currently enables Database:ApplyMigrationsAtStartup; Program conditionally calls DatabaseStartup.MigrateAndSeedAsync, which obtains the migration advisory lock, migrates and seeds reference data. The lock is not approval. Keep drafts out of the assembly; do not start/restart the API to apply them. A future approved deployment must explicitly control that effective flag/process and use a reviewed migration-only path that does not incidentally run seeders. No setting was changed here.
5. **Lock/transaction plan:** parent ADD UNIQUE builds supporting indexes and takes strong locks (normally ACCESS EXCLUSIVE for this form). FK creation also locks referenced Contracts/other parents. Approve a short maintenance window, deterministic key/table order, advisory migration serialization, bounded statement/lock timeouts and fail-closed contention handling. Local parent row counts remain 13/109/111; small local size is not a production-duration guarantee. Do not silently substitute CONCURRENTLY operations into transactional DDL.
6. **Schema resolution:** generated migration uses the existing model's default schema. The approved runner must verify public is the intended schema and explicitly constrain search_path/resolution to public/pg_catalog; do not let a same-name user-schema table receive the parent keys. Do not broaden model-wide schema configuration in this task.
7. **Execute only separately approved migration:** use the standard EF mechanism with final source metadata/history bookkeeping, not raw SQL plus manual history insertion. Run the pre-reviewed schema operations in their approved transactional scope; no data/backfill/cash operation. If the conditional correction is adopted, deploy it as its own migration, not an eighth hidden foundation operation.
8. **Independent postflight:** a fresh read-only connection verifies new table/26 columns/nine checks/three FKs/four indexes/two parent keys, unchanged old Contracts/Stalls keys, retained objects, original 143 history entries, all prior row contents and SRC sequence; exactly the approved new normal EF history row(s) are additional. Keep anchor creation/writers disabled. Obtain owner acceptance before operational restart/use.
9. **Failure/recovery:** before COMMIT, rollback DDL and verify original state. After lost acknowledgement, use independently checked state and explicit unknown outcomes; do not retry or drop keys to guess recovery. The reviewed C# Down deliberately throws. Once evidence exists, deleting the table is data loss: prefer an approved forward correction. Before any evidence and only with explicit approval, a removal plan could drop the new empty table and only its newly introduced parent keys after dependency checks; never cascade or remove old keys. A full restore has whole-database effects and requires a separate outage/data-recovery approval; it is not an automatic rollback step.

## 9. Executed validation and reproducibility

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~TenantFilter|FullyQualifiedName~CollectionSourceAuthorityMapTests|FullyQualifiedName~CollectionSourceShapeModelTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests|FullyQualifiedName~CrossTenantReadsAreNamedTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

| Executed check | Actual result / limit |
| --- | --- |
| Focused EF/model/tenant/authority/backup/billing tests | **132 passed, 0 failed, 2 skipped; total 134** |
| Full unit suite | **2,711 passed, 0 failed, 2 skipped; total 2,713**, 29 seconds |
| Release compilation through focused command | Succeeded; existing warnings retained |
| Draft artifact checks | C# draft compiled offline; all seven Up operations regenerate **exactly the same SQL** as the model difference; Down throws before database use — three checks passed |
| Read-only actual database | Corrected schema/data/history/index/parent-key checkpoint confirmed |
| PostgreSQL DDL/FK/check/concurrent/restore execution | **Not run / not authorized** |

The draft was generated with EF Core/Npgsql **9.0.4** from IMigrationsModelDiffer and IMigrationsSqlGenerator plus the C# design-time generator. The temporary console referenced Infrastructure with a port-1 unused connection and never opened it or booted a host. Provider design-time registration emitted EF1001 warnings about the provider's internal tooling API; no production dependency/source was added. Compiled registry remains 130. The SQL and C# files were copied as new review artifacts through the file editor, not generated into an active migration directory.

No existing tests were changed. One skipped theory retains eight F-01 scenarios; one skipped fact retains day-31 collection eligibility. They are future requirements, not two passing tests or eight executed green exact-settlement cases. Historical PureDays, financial authority and current characterization checks remain intact.

Logs: `%TEMP%\stalltrack-task2d-focused.log`, `%TEMP%\stalltrack-task2d-unit.log`, `%TEMP%\stalltrack-task2d-artifacts.log`; temporary offline generator under `%TEMP%\stalltrack-task2d`. The durable artifacts themselves contain the complete reviewed C#/SQL/operation list, so application review does not depend on temporary logs. No browser/Mobile check is relevant to this schema-preparation-only change.

## 10. Explicit owner decisions and next boundary

For the next controlled phase, approve or revise **these specific decisions**:

1. **Baseline:** accept the corrected retained-schema checkpoint in section 3 for additive forward work, including bounded May/view/working-copy provenance uncertainty and permanent preservation of all retained data/history. No further archaeology is required by this recommendation absent a concrete new mismatch.
2. **Restore policy:** accept pre-delete rejection of incomplete/old manifests and prohibit unsafe generic tenant restore with retained legacy evidence; authorize implementation/tests of those guards before operational use. Choose the proposed restore-v2 path rather than silent restore-v1 acceptance. Schema-only approval is not restore/use approval.
3. **Execution:** approve the exact seven operations, separate optional conditional correction, verification environment/rollback-only rehearsal and migration-only deployment/backup/locking procedure. Application is a subsequent explicit approval, not implied by this Task 2D checkpoint.

After those gates, the next engineering boundary is **foundation integration + controlled PostgreSQL/restore validation**, still no cash posting. A later separately authorized typed-source task must coordinate CollectionSourceKind, typed tenant-safe allocation/FKs, authority map, quotes, canonical writer, correction/reversal routing and common month balance readers as one dormant contract. Only a further financial cutover may enforce the accepted Cantilan PHP 900 / thirty PHP 30 cap, no extra day-31 stall rent, exact PHP 25/20 terminal settlement and future monthly-only LGUs. Preserve current earned/as-of versus Whole scope and old daily evidence; never activate an incomplete route through the new enum alone.

**Stop at B: technical migration preparation complete, with explicit owner baseline/restore/verification decisions.** No migration, API restart, index replay, financial activation or deployment was performed.

## 11. Final Git checkpoint

Final SHA-256 comparison confirmed **all 29 entry paths are byte-identical**. Task delta is exactly this plan plus five new review/evidence artifacts under docs/evidence/task2d: **35 pending files** in total (six tracked modifications inherited from earlier tasks and twenty-nine untracked files). Nothing is staged or committed; HEAD remains `17b9d100` on the original branch. Existing active migrations, snapshot, application source and startup/connection configuration are unchanged.

`git diff --check` passed with only the preexisting LF/CRLF notice for the untouched characterization fixture. Document links and artifact structural checks passed: seven operations, two parent keys, one table, four indexes, three Restrict FKs, nine checks, no DROP/data/history statements in the foundation, unregistered review C# and forward-only Down. An initial broad text probe matched the legitimate `ON DELETE RESTRICT` clauses; the statement-anchored check confirmed there are no DELETE statements. The separate conditional correction was not executed. No database mutation, migration application, API restart, new backend/database, push or deployment occurred. Stop at the owner-decision checkpoint.
