# Task 2C.1 — NPM month-source schema readiness and financial integration gate

**Decision: BLOCKED BY A MATERIAL SCHEMA-HISTORY COMPATIBILITY GAP pending owner/baseline review.**

The preparation model passes its current checks and the proposed seven additive DDL operations are compatible with the inspected parent keys/data. However, the actual local database records **143 applied migrations**, while the **compiled EF registry has 130**. Thirteen applied migrations have no implementation in this checkout. Their provenance/retirement and retained schema are not established. A matching latest migration does not settle that discrepancy. This is a migration-readiness blocker, **not proof that the seven proposed statements would fail**, and not evidence of damaged cash. Do not repair the history, rebaseline or apply a migration under this task.

## 1. Repository checkpoint and preserved work

Checkout `C:\dev\stalltrack\eemo`; branch `codex/local-unified-v8-v10-20261013`; HEAD `17b9d100`, verified at entry. All 15 expected pending paths were present: five Task 2A/2B paths and ten Task 2C paths. Their entry SHA-256 values were captured for final comparison. No existing path was edited in Task 2C.1.

Read with the [Task 1 audit](NPM_MONTHLY_BILLING_STANDARD_REVIEW_20261016.md), [Task 2A/owner ruling](NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md), [Task 2B architecture](NPM_MONTHLY_SETTLEMENT_ARCHITECTURE_DECISION_20261016.md), [Task 2C foundation](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md), root AGENTS and current business/architecture/audit/tenant conventions. Financial-rule review and documentation-governance procedures were applied. This is a scoped review, not an accepted financial cutover decision.

Only two Task 2C.1 paths are added: this handoff and `EEMOCantilanSDS.Testing/Infrastructure/Persistence/NpmRentMonthSchemaReadinessTests.cs`. No existing migration directory, model snapshot, configuration, production source, financial record or historical planning document was modified. No worktree, reset, stash, merge, rebase, automatic commit, push, deployment, database provisioning or API restart.

## 2. Foundation review

| Concern | Verified source evidence / limit |
| --- | --- |
| Identity | `Domain/Entities/Payments/NpmRentMonth.cs:Prepare` checks nonempty same-tenant IDs, NPM facility, stall/facility relationship and contract/stall membership. Identity fields have private setters; assessment revisions do not replace them. |
| Monthly uniqueness | `NpmRentMonthConfiguration` declares unique `(MunicipalityId, StallId, PeriodStart)`. ContractId is deliberately absent. Factory calls alone cannot prevent duplicate database rows; PostgreSQL execution remains pending. |
| Historical ownership | Existing `StallOccupancy.AnsweringForMonth` selects the canonical monthly owner. Same-start duplicate contracts are rejected; missing/wrong owner fails. Complete relevant history must be loaded by future application code. A clear handover still produces unreconciled preparation, never a cash transfer. |
| Assessment/provenance | Amount/credit cent guards; credit cannot exceed assessment. Explicit policy/rate/credit JSON is recorded without a new calculator. JSON shape is not proof of real LGU approval. Future source lookup must verify references, effective date and policy. |
| Unknown basis | Null is retained rather than defaulted. `Reconcile` refuses it until an evidenced revision supplies a basis. PureDays is not converted or removed. |
| Revision/version | Expected-version check and checked increment before mutation; UTC, non-backdated timestamp and reason required. Revision invalidates reconciliation. EF marks SettlementVersion as a concurrency token. There is no database trigger automatically incrementing it. |
| Authorization | Domain actor ID/role text is evidence, not authentication. No new handler/endpoint exists. Future handlers must derive actor/tenant, apply approved Head/Admin policy and load verified source history. |
| Audit | Semantic old/new AuditLog is returned; existing AuditSaveChangesInterceptor also covers the type and persisted LastRevisionReason. Caller must persist semantic evidence atomically. Real PostgreSQL audit transaction behavior is unverified. |
| Collection permission | `CanAcceptCollections` is unconditionally false. NeedsReconciliation=1 and Reconciled=2 are preparation states only. No source enum, allocator, financial DI registration, SRC or remittance path consumes the new type. |
| Tenant reads | Existing global filters cover IMunicipalityOwned and fail closed with an unresolved request tenant. This is application query filtering, not PostgreSQL RLS. |

Source prefixes here refer to the corresponding `EEMOCantilanSDS.*` project. Also inspected `IAppDbContext`, `AppDbContext`, audit interceptor, backup/export inventory, both foundation test files and canonical Collection/allocation/correction/coordinator mappings. No foundation financial-behavior defect was corrected or silently worked around.

## 3. Actual local PostgreSQL inspection

Used the existing API Development connection, selected without printing credentials; host verified explicitly local. Connection/transaction defaults were forced read-only with bounded statement/lock timeouts. Main metadata audit used repeatable-read/read-only; supplemental metadata queries also used read-only transactions. Transactions ended with ROLLBACK. Queries used catalogs, migration IDs and aggregate counts only. No personal identity, cash receipt, password/hash or full financial record was exported.

| Observed item | Actual result |
| --- | --- |
| Database | `EEMOCantilanSDS.DB` on localhost |
| PostgreSQL | 18.4 |
| Main audit isolation / read-only | repeatable read / on |
| NpmRentMonths | Not present |
| Facilities / Stalls / Contracts rows | 13 / 109 / 111 |
| Empty municipality GUIDs | 0 / 0 / 0 |
| Soft-deleted parent rows | 0 / 0 / 0 at audit time; retention behavior still requires tests |
| Stalls -> Facilities orphans / tenant mismatches | 0 / 0 |
| Contracts -> Stalls orphans / tenant mismatches | 0 / 0 |
| Duplicate groups for proposed/reused parent keys | 0 for all three key shapes |
| Same-start nondeleted contract ambiguity groups | 0 |
| Stalls with multiple retained contracts | 2; identities not exported |
| Proposed relation/constraint-name collisions | 0 in public, including generated index/constraint names |
| Parent indexes | All inspected indexes valid and ready |
| Other-session locks on these parents | None observed at the metadata snapshot; not a deployment lock guarantee |
| Parent relation total sizes | Facilities 49,152 bytes; Stalls 131,072; Contracts 147,456, including indexes/storage |
| Parent RLS / forced RLS | false / false for all three |

`Id`, ownership/relationship GUID columns and IsDeleted are NOT NULL. MunicipalityId currently has an all-zero GUID default on the three parents; no such actual values were found. New source/domain guards must continue rejecting empty tenancy; the review does not change those parent defaults.

Existing relevant keys/indexes:

| Parent | Verified current relational structure |
| --- | --- |
| Facilities | PK_Facilities(Id); unique IX_Facilities_MunicipalityId_Code; **no** AK_Facilities_MunicipalityId_Id |
| Stalls | PK_Stalls(Id); AK_Stalls_MunicipalityId_Id; unique canonical-section and null/custom-section stall-number indexes; **no** AK_Stalls_MunicipalityId_Id_FacilityId |
| Contracts | PK_Contracts(Id); AK_Contracts_MunicipalityId_Id; **AK_Contracts_MunicipalityId_Id_StallId already exists**, valid/nondeferrable |

Existing Contracts -> Stalls and Stalls -> Facilities FKs use their single parent IDs, with DELETE RESTRICT. They are not retrofitted to composite tenancy by this plan. New source FKs independently bind tenant, facility and contract/stall ownership. Inspected dependent FKs are validated/nondeferrable. Other dependents include DailyCollections, PaymentRecords, exceptions, payor links, collector assignments, fine assessments, Fish/Meat associations and retained ECF assessment tables. The latter include composite tenancy references. SlaughterTransactions -> Facilities has an existing cascade rule; this plan neither changes it nor authorizes deletion.

Soft deletion is application state and query filtering; it does not remove physical FK references. New month rows are not soft-deletable and their three parent FKs restrict hard deletion. Future historical readers must preserve the original Contract ID even when operational parent views hide a closed/deleted record; do not solve that by discarding tenant filtering or guessing the current owner.

### Migration-history gap

Actual `__EFMigrationsHistory`: 143 entries, latest `20261015234000_NpmRegistrationRequestCancellation20261015`. Offline compiled `IMigrationsAssembly.Migrations`: 130 entries, same latest. Every declared source migration ID was present in the read-only history comparison; these **13 applied IDs were absent from available source**:

```text
20260523083934_FixStallUniqueConstraintIncludeSection
20260928045651_AddGovernedConfigurableServiceFoundation
20260928085335_AddFishMeatVendorFeeSource
20260928091416_AllowFishMeatVendorFeeCanonicalAllocations
20260928100950_AddVegetableSpaceRentalSource
20260928120100_AddVegetableCanonicalAllocationShape
20260928121210_AddEcfApprovedAssessmentSource
20260928123136_AddWcfApprovedChargePolicy
20260928124319_AddLandingBerthingActivitySource
20260928135259_AddEcfAssessmentSubjectKey
20260928202017_AddServiceCollectionGroups
20260929015219_AddKanmanggayMonthlySource
20260929034147_AddNpmOccupancySourceToEcf
```

Source file searches and targeted available Git-history lookups did not establish their implementations here. Some retained schema (for example ECF tables/FKs) is visible, but names are not sufficient evidence to infer what every migration did or whether its retirement was approved. Do not delete history entries, fake their implementation, reverse schema, cherry-pick another branch or infer that the database is corrupt. Owner/core integration must establish the actual accepted local schema lineage or explicitly review/document a compatible retained-schema baseline before migration application is authorized. This discrepancy predates Task 2C.1; the source of the discrepancy was not changed here.

## 4. Exact offline additive operations

The new offline test obtains the existing EF snapshot, builds the design-time Npgsql model, calls IMigrationsModelDiffer and generates SQL text with IMigrationsSqlGenerator. **It never opens a connection, creates a migration or executes its SQL.** The generated review output is `%TEMP%\stalltrack-npm-task2c1-ddl-review.log`.

Exactly **seven EF operations**:

1. AddUniqueConstraint on existing **Stalls**: AK_Stalls_MunicipalityId_Id_FacilityId `(MunicipalityId, Id, FacilityId)`.
2. AddUniqueConstraint on existing **Facilities**: AK_Facilities_MunicipalityId_Id `(MunicipalityId, Id)`.
3. CreateTable **NpmRentMonths**, 26 columns, PK, tenant/ID alternate key, three composite FKs and nine checks.
4. CreateIndex IX_NpmRentMonths_MunicipalityId_ContractId_StallId.
5. CreateIndex IX_NpmRentMonths_MunicipalityId_FacilityId.
6. CreateIndex IX_NpmRentMonths_MunicipalityId_StallId_FacilityId.
7. CreateUniqueIndex IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart.

The new table's PK/alternate key create their own supporting unique indexes as part of operation 3; they are not additional EF CreateIndex operations. **One new table therefore does not mean no existing-table changes.** Contracts needs no new alternate key, column, reindex or constraint replacement. CollectionAllocations, CollectionLines, correction tables and all financial rows are unchanged by this foundation plan. No raw SQL data operation, backfill, DROP, ALTER COLUMN or destructive operation appears in the snapshot difference.

Non-executable review excerpts, **not instructions to run**:

```sql
-- Existing tables affected by the proposed plan:
ALTER TABLE "Stalls" ADD CONSTRAINT "AK_Stalls_MunicipalityId_Id_FacilityId"
  UNIQUE ("MunicipalityId", "Id", "FacilityId");
ALTER TABLE "Facilities" ADD CONSTRAINT "AK_Facilities_MunicipalityId_Id"
  UNIQUE ("MunicipalityId", "Id");
-- New-table FK shapes, all DELETE RESTRICT:
-- (MunicipalityId, FacilityId) -> Facilities(MunicipalityId, Id)
-- (MunicipalityId, StallId, FacilityId) -> Stalls(MunicipalityId, Id, FacilityId)
-- (MunicipalityId, ContractId, StallId) -> Contracts(MunicipalityId, Id, StallId)
```

Column mapping is exactly the 26-field contract in [Task 2C section 4](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md): required UUID identities, date period/as-of values, numeric(18,2) assessment/credits, nullable integer basis, integer preparation state, bigint optimistic version, jsonb rate/credit evidence, bounded text metadata and timestamptz creation/review/revision timestamps. No collection ID, cash, outstanding or SRC column is added.

Checks are CK_NpmRentMonths_Period, Money, Basis, Version, RevisionReason, Identity, Evidence, Review and Updated. They enforce the documented structural invariants, including explicit non-null timestamps in review/update branches. They do not verify authentic rate approval, latest historical occupancy, authenticated actor or semantic completeness of arbitrary JSON. Domain length/fractional-cent validation remains necessary: numeric(18,2) can round an incoming raw SQL value before a check, and JSONB structure is not a pricing-policy validator.

EF's SettlementVersion token supplies a WHERE-original-version concurrency condition on updates; the domain increments it. A raw UPDATE bypassing that discipline is not made safe by merely naming a bigint Version. Real conflict/rollback behavior needs PostgreSQL tests.

## 5. Locking, dependencies and rollback

The two ALTER TABLE ... ADD UNIQUE operations take strong parent-table locks and build supporting indexes. Default ALTER TABLE locking is ACCESS EXCLUSIVE unless the subform specifies otherwise. FK creation also requires SHARE ROW EXCLUSIVE locks on referenced parents; thus Contracts may be locked even though no new parent key is added there. These operations can block normal reads/writes or wait on existing transactions. The observed small local sizes/no competing locks are not a production-duration guarantee. See [PostgreSQL 18 ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html) and [CREATE TABLE](https://www.postgresql.org/docs/18/sql-createtable.html).

Use a separately approved maintenance/timeout plan and stable lock ordering. Keep new empty-table/index work together where transactional DDL is appropriate. For a much larger target, a separately designed concurrent unique-index/attach-constraint procedure may reduce blocking, but CREATE INDEX CONCURRENTLY cannot run in a transaction block and introduces failure/invalid-index cleanup considerations; it is not a silent substitution into the current migration plan. See [PostgreSQL 18 CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html).

Existing valid PKs and zero duplicate/null groups support the two proposed alternate keys. Parent IDs/relationship fields become EF principal-key components; current Stall.FacilityId and Contract.StallId have no supported domain reparenting method. Any future raw restore/reparent workflow still needs tests. Existing inbound FKs are neither dropped nor retargeted.

Before any rows exist, a separately authorized rollback could remove the new table and only the newly introduced parent keys after checking dependencies. After preparation/audit evidence exists, dropping it is data loss; preserve it and prefer a forward correction. Never use CASCADE to erase historical dependencies, remove preexisting Contracts keys, or edit __EFMigrationsHistory to force alignment. No rollback was executed or approved here.

## 6. Backup/export and startup migration gate

Task 2C includes the table in TenantDataTables.Restorable and tenant-scoped export; AuditLogs remain export-only. Restore insert order is derived from EF FKs (parents before month); delete order is reversed. This is code/model evidence, not an executed PostgreSQL round trip.

**Older snapshot risk requiring an explicit compatibility decision:** TenantRestoreRepository checks format/municipality, deletes current restorable rows, then skips missing/empty tables. A same-format older snapshot lacking NpmRentMonths would therefore leave that tenant's month table empty after restore. Before any prepared months are operationally retained, decide whether such a restore must be blocked, explicitly confirmed as a full rollback, or require a versioned compatibility path. Do not silently backfill it or infer that presence in the inventory proves safe restoration. Existing behavior was not changed in this task.

`Api/Program.cs:36–39` conditionally calls DatabaseStartup.MigrateAndSeedAsync when Database:ApplyMigrationsAtStartup is true. The Development configuration sets **true**; base/user-secret/current-shell inspection found no overriding setting. That is configuration evidence, not proof of a running process's effective environment. No listener on the previously supplied ports or API executable was observed during this inspection; no API was started. The database audit used the configured local connection directly and does not claim to validate a running host's overrides.

DatabaseStartup opens the database, sets a 150-second lock timeout, takes a session advisory migration lock (8472113509001), calls MigrateAsync and then reference seeders, finally unlocking. The lock serializes migrating instances; it is not an approval or a protection against accidental migration on restart. A foundation snapshot/migration placed in the active assembly with that flag true can be applied by a routine startup.

Safe future deployment **plan only**:

1. Resolve the 13-ID lineage discrepancy and approve the exact schema baseline, backup/restore contract and lock plan.
2. Authorize isolated PostgreSQL verification; prepare/review migration artifacts outside the active startup assembly until release approval. Use design-time metadata/factory without booting Program; confirm target before any later execution. The factory's fallback connection is not permission to provision another database.
3. Before any approved restart with new migration code, explicitly configure the launch/deployment's effective Database:ApplyMigrationsAtStartup=false and verify overrides. Do not edit committed settings merely for this review. Disabling startup migrations alone does not make new export/model code work against a missing table.
4. With separate owner approval, apply only reviewed DDL/history through the approved migration mechanism, without starting the application/seeders as an incidental migration runner. Validate keys/schema/history and backup behavior read-only afterwards.
5. Only then perform an independently approved API restart with schema/model-compatible code and startup migration still disabled unless specifically authorized. Keep all collection gates closed. Never enable month collection merely because schema exists or evidence is Reconciled.

No step above was executed under Task 2C.1.

## 7. Future typed-allocation contract — proposed, not implemented

| Aspect | Required contract |
| --- | --- |
| Discriminator | Append CollectionSourceKind.NpmRentMonth with a reviewed stable numeric value (17 is the currently unused next candidate, not reserved by this task). Never renumber old kinds. |
| Identity | SourceId is the stable prepared month ID; SourcePart=null. Month's logical key remains tenant/stall/period, with immutable responsible Contract ID evidence. No free-text/month-name matching. |
| Typed allocation | Add nullable NpmRentMonthId on CollectionAllocation, populate from SourceId internally for this kind; require kind/month-ID equality, non-null typed ID for this kind, null typed ID for every other kind. Composite FK `(MunicipalityId, NpmRentMonthId)` -> month `(MunicipalityId, Id)`, Restrict. Extend allocation/line and draft source-shape checks together. Existing rows retain null and their original kinds. |
| Source validation | Authenticated approved role/assigned Collector; resolved tenant nonempty; query-filtered source lookup plus explicit tenant match; same-tenant facility/stall/contract; complete owner history, policy/rates/credits and quote scope verified. A valid FK does not prove authorization or financial eligibility. |
| Activation | NeedsReconciliation/Reconciled are NEVER eligible. Before routing exists, approve a distinct durable financial-activation contract (proposed month activation timestamp, actor and immutable opening/legacy-bridge evidence, plus explicit active state). Merely adding an enum or treating Status=2 as active is forbidden. No current activation record/method implements this. |
| Opening compatibility | Reconcile legacy-only rent opening; identify pre-anchor canonical daily allocation sources and net signed corrections; preserve all original IDs and cash. Reviewed evidence is not a new Collection. Do not label already canonical money as legacy or add a daily projection again. Ambiguous owner/basis/excess cases fail closed. |
| One position | NetRentSettled = reconciled legacy-only opening + verified pre-anchor canonical DailyFee net + new month-allocation net. Outstanding = max(0, approved assessment - valid credits - net settled). Historical excess is separately visible, not deleted/refunded/advanced automatically. Reuse specialized NPM assessment rules; no parallel calculator. |
| Exact allocation | New cash allocates once to this month, not both month and day. 875 received toward 900 permits exactly 25; 980 toward 1,000 permits 20. Whole needs no invented daily rate/day. Daily activity is separate evidence. Partial permissions and earned/as-of versus Whole scope stay explicit. |
| Monthly cap | Thirty valid 30 rent payments settle the stated 900 obligation. All rent discovery/quotes/writers then refuse additional positive rent for that source, including day31. Independent weighing/utilities remain eligible under their own sources. No universal cross-tenant 900 tariff and no conversion of PureDays. |
| Concurrency | Every Head/Mobile/daily/batch/online rent path and linked old-source correction locks/versions the SAME unique month. Under one serializable/version-checked unit, revalidate quote, amount and evidence, consume balance, increment version, post Collection/allocation/PostingOperation/projections/audit, or roll back all. Different daily dates cannot bypass the month lock. |
| Idempotency | Existing tenant/client-operation uniqueness and normalized actor/source/amount/scope/versioned intent apply. Successful matching replay returns original Collection/SRC BEFORE current balance checks, even if later corrected; changed intent conflicts. Preserve legacy operation/origin/version replay dispatch; a new origin must not re-charge an old lost-response retry. Stale new quotes reconcile rather than silently changing amount/month. |
| Corrections | Route by original allocation kind/ID, lock same month, append signed CollectionCorrectionAllocation targeting original allocation. Enforce original amount/cumulative correction limits, role and tenant, closed-remittance rules, exact reopening and version invalidation. Old DailyCollection void/adjustment dispatch remains supported. No guessed current occupancy or edited original cash. |
| Reporting authority | New kind canonical-always for new received events; old kinds keep existing authority. Monthly source/opening/projection is not cash. Adapt source catalogs, cash/period readers, receipt/history, exports and correction displays so a Collection counts once. |
| Remittance | Existing Collection-based remittance and corrected disposition remain authoritative. No remittance/SRC from anchor, assessment, credit or opening evidence. No new receipt/accountable-form policy. |

Deployment unit for any eventual financial enablement: source kind/domain validation + typed FK/checks (including draft shapes) + verified lookup/explicit activation + common month position/atomic posting + every rent writer/legacy retry + correction/reversal + reporting authority and all balance readers + client/offline compatibility and reconciliation. Schema can be prepared earlier while **all** money paths remain disabled. Enabling only Whole Payment, only the enum or only Mobile cannot satisfy the cap/concurrency invariant.

## 8. PostgreSQL verification plan — requires separate authorization

Use the existing IntegrationTests architecture only after explicit permission for a dedicated test-owned PostgreSQL instance/database. It must match the local PostgreSQL 18 major/provider baseline, use synthetic tenant/contract/rate fixtures only, have distinct non-main credentials/connection/role with no privileges on the municipal database, no production/network service targets, no API/seed startup, no payment-provider calls and no mutable shared municipal schema. Validate target/isolation before setup/teardown. No restore of office records or production dump. Provisioning, DDL, writes and disposal are outside this task; a Testcontainers instance is one possible separately approved mechanism, not authorization now.

| Test | Concrete expected result |
| --- | --- |
| Duplicate source | Same tenant/stall/period with different IDs or Contract IDs -> unique violation 23505; another tenant/different legitimate stall or different period succeeds. One obligation identity only. |
| Concurrent inserts | Two connections synchronize before inserting same logical key; one commits, other conflicts/reloads the same anchor; never two sources. |
| Tenant FK | Valid foreign-tenant facility/stall/contract IDs with otherwise valid rows -> 23503; same-tenant correct chain succeeds. Test direct SQL as well as application guards. |
| Wrong relationships | Correct tenant but wrong facility for stall or wrong stall for contract -> 23503. Another operation's facility and wrong monthly owner must also fail domain/application checks; FK alone cannot establish those semantics. |
| Period/money | Non-first period, negative amounts, credits > assessment -> 23514; required NULL -> 23502. Application rejects fractional cents before the DB; verify/document raw numeric coercion rather than claiming the column rejects original fractional input. |
| JSON/review | Malformed JSON -> JSON/type error; empty/non-object JSON, invalid basis/status/version, empty GUIDs and inconsistent NULL review metadata -> relevant check violation. Prove SQL NULL logic and valid zero/full-credit cases. Verify application provenance limits separately. |
| Optimistic revision | Two EF contexts load version N; first revises/audits N+1; second update with N fails concurrency, leaves no accepted revision/audit. Fresh review preserves identity and invalidates prior reconciliation as designed. |
| Audit atomicity | Real interceptor saves old/new/reason/tenant/actor with revision; induced transaction failure rolls both back. Unauthorized/cross-tenant future handlers reject before mutation. |
| History | Retained old/current contracts, clear handover, same-start ambiguity, missing basis, closure/soft deletion: retain identity, require reconciliation, never move old cash. Preparation does not activate any source. |
| Tenant export/restore | Own-tenant export only; two-tenant restore in isolated fixture leaves other tenant unchanged, obeys FK ordering, retains IDs/evidence, does not restore-over AuditLogs. Test old same-format backup lacking new table against the chosen compatibility policy. |
| Zero money | Preparation/review/revision creates zero Collections, SRCs, allocations, daily rent rows and remittance records. No source eligible in either existing preparation state. |
| Later financial contract | Only after separate posting authorization: concurrent Head/Mobile, exact 25/20, 900/30 in Feb/30/31 days, no extra day31 rent, stale/lost-response/replay, legacy correction, partial permissions, report/remittance once-only and no historical rewriting. Do not count these as current foundation acceptance. |

## 9. Executed tests and limits

New offline test validates **exactly two existing-parent key changes**, one 26-column new table, three Restrict FKs, nine checks and four indexes, seven total operations, and outputs the compiled migration count/latest and review SQL. It does not apply any constraint or simulate a PostgreSQL race.

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~NpmRentMonthSchemaReadinessTests --logger 'console;verbosity=detailed'

dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~TenantFilter|FullyQualifiedName~CollectionSourceAuthorityMapTests|FullyQualifiedName~CollectionSourceShapeModelTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests|FullyQualifiedName~CrossTenantReadsAreNamedTests'

dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

Results: offline review **1 passed / 0 failed / 0 skipped**; focused **132 passed / 0 failed / 2 skipped (134 total)**; full unit **2,711 passed / 0 failed / 2 skipped (2,713 total)**. Release test compilation succeeded with existing warnings. The final review reran the offline test after adding compiled-registry output; all test logic stayed unchanged. Logs are `%TEMP%\stalltrack-npm-task2c1-ddl-review.log`, `stalltrack-npm-task2c1-focused.log`, `stalltrack-npm-task2c1-unit.log`.

One skipped theory retains eight F-01 requirements; the second skip is the day31 acceptance Fact. They are **not passing cases**. No active test failed, but those runtime defects remain. No PostgreSQL integration DDL/DML, actual constraints, concurrency, financial posting, live API or browser acceptance was tested. Read-only metadata/aggregate inspection is not a substitute for that proof.

## 10. Approval gates and recommended Task 2D boundary

1. **First resolve the material 143-versus-130 history gap.** Identify provenance/retained schema of all 13 IDs and record accepted compatibility or a separately approved baseline plan. Do not guess or delete history.
2. Approve the exact two-parent/one-table DDL and lock/rollback procedure; decide old-backup compatibility and authorized preparation/review roles. Parent metadata is locally compatible, but this is not database-application approval.
3. Separately authorize isolated PostgreSQL infrastructure and the matrix above. Establish FK/unique/version/audit/restore evidence before approving main schema application.
4. Task 2D should remain a **baseline resolution + reviewed migration preparation/verification** task, with artifacts outside startup-discoverable paths until approved. No money posting, enum/routing enablement, live migration, API restart or onboarding. Only after those gates should a new task implement the complete dormant typed-source/activation integration contract, followed by separately approved financial cutover.

No required destructive schema change or cash backfill was found in the proposed foundation delta. No source correction was made. Readiness is blocked by unexplained existing migration lineage and the explicitly pending execution/restore/authorization gates, not by a fabricated claim that the month model already fixes F-01/day31.

## 11. Final Git and safety checkpoint

Final comparison: **15 initial paths checked, 0 changed** by SHA-256. `git diff --check` passed; both new files have zero trailing-whitespace lines. Existing LF/CRLF notice on the protected Task 2A fixture is unchanged. Nothing staged or committed; HEAD remains `17b9d100` on the original branch. Final status has the same 15 preserved paths plus exactly the two new Task 2C.1 files (17 pending paths total).

No database data/schema/history mutation, migration generation/application, API restart, new database/backend, merge, push or deployment. Read-only catalog/aggregate transactions only; no synthetic municipal financial posting. Stop at this readiness gate; no automatic next phase.
