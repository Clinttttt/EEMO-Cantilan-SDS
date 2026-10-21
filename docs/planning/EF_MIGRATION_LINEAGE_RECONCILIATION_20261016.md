# Task 2C.2 — EF migration lineage reconciliation

**Assessment: STILL BLOCKED.** Investigation completed on **2026-10-17**; the requested October 16 filename is retained. No financial implementation or schema change is authorized by this document.

The 143-versus-130 difference is now substantially explained: ten missing migrations have authentic committed source in a separate older local repository; two more survive as uncommitted historical source/designer pairs. The May migration remains unrecovered. More significantly, a retained unique allocation index from the older Vegetable design conflicts with the current obligation-installment model. Therefore this database cannot yet be accepted as a compatible baseline simply because the currently mapped tables match.

Read with [schema readiness](NPM_MONTH_SOURCE_SCHEMA_READINESS_20261016.md), [foundation](NPM_MONTHLY_SETTLEMENT_FOUNDATION_20261016.md), [architecture decision](NPM_MONTHLY_SETTLEMENT_ARCHITECTURE_DECISION_20261016.md), [exact settlement design](NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md), and [monthly billing review](NPM_MONTHLY_BILLING_STANDARD_REVIEW_20261016.md). This follow-up supplies new evidence; it does not rewrite those historical checkpoints. Financial-rule review and documentation-governance procedures were applied.

## 1. Repository checkpoint and boundaries

- Primary checkout: `C:\dev\stalltrack\eemo`.
- Branch: `codex/local-unified-v8-v10-20261013`.
- Entry HEAD: `17b9d100e8ddffdd747f5688a8e49818693d078a`.
- Initial status: six tracked modifications and eleven untracked paths, all seventeen belonging to Tasks 2A–2C.1. Their entry SHA-256 values were captured. They are preserved, including the two deliberate pending acceptance skips.
- Task 2C.2 adds only this document and [sanitized metadata/provenance evidence](../evidence/2026-10-17_ef_migration_lineage_metadata.json). No source, snapshot, active migration, configuration, financial test or protected planning index is edited.
- No branch/worktree change, reset, stash, clean, merge, rebase, restoration of migration files, database provisioning, API startup/restart, migration application, financial posting, production access, push or deployment. Platform is untouched. Nothing is staged or committed.

## 2. Evidence collection and confidence

The existing Development connection was selected without printing credentials. The host was explicitly restricted to localhost/loopback. Each database inspection used connection-level read-only defaults and `BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY`, bounded statement/lock timeouts, and `ROLLBACK`. Only catalogs, migration history, aggregate counts and synthetic SELECT expressions were read. No tenant identities, account records, money amounts, passwords, password hashes or tokens were exported.

The inspected database is `EEMOCantilanSDS.DB`, PostgreSQL **18.4**. This is configured local-database evidence, not verification of a running API's effective configuration or any production database.

Offline review instantiated AppDbContext with an unused port-1 connection string and never opened it. It read the compiled `IMigrationsAssembly`, initialized its snapshot and current design-time relational model, and inspected `UpOperations` for all 130 compiled migrations and twelve recovered migrations. Inspecting those operation objects does **not** execute their SQL. Temporary console helpers referenced existing Infrastructure and historical files directly; they did not boot an API or modify a migration assembly in the application.

### Authorized historical sources searched

- Main repository reachable branches/tags/object paths and current migration directories.
- Relevant older worktrees under `C:\dev\stalltrack-v2-worktrees`, read-only.
- Their actual Git common repository, discovered through their `.git` pointers: **`C:\dev\stalltrack-v2-clean`**. This is different from the primary EEMO Git repository. No modifications were made there.
- Relevant historical migration source under these project roots; a filename/source search of the StallTrack thesis root found no missing May implementation.
- Retained Infrastructure assemblies under the EEMO, older clean repository and worktree roots: **353 DLL files, 132 distinct SHA-256 contents**. Neither the May migration class name nor ID was found in their metadata string bytes. This is a bounded artifact search, not proof that no copy exists elsewhere.
- Existing reviewed local backup `local-db-backups/EEMOCantilanSDS.DB_pre_fish_meat_office_20261014_1527.dump`, read using `pg_restore --schema-only --file <temporary-file>`, with **no database target**. Its schema already contains the thirteen retained tables and the conflicting allocation index. No backup was restored and no row data was extracted.

### Confidence labels

**G:** committed original source recovered, with matching working/index Git blob and matching designer Migration attribute. **W:** historical uncommitted working source/designer recovered, ID and structural effects corroborated; original executed bytes/commit cannot be established. **U:** implementation unavailable in searched sources. Database history records IDs/ProductVersion, not the original SQL, source hash or application timestamp. Migration-ID timestamps are not proof of when a migration was applied.

## 3. Complete migration membership reconciliation

| Membership | Verified count |
| --- | ---: |
| Applied history entries | 143 |
| Compiled AppDbContext migrations | 130 |
| Compiled IDs absent from database | **0** |
| Applied IDs absent from compiled registry | **13** |
| Missing implementations recovered from committed older history | 10 |
| Missing implementations recovered only as working files | 2 |
| Missing implementations still unrecovered | 1 |

Both lists end with `20261015234000_NpmRegistrationRequestCancellation20261015`. The thirteen extras report EF ProductVersion `9.0.4`. The evidence JSON preserves **all 143 applied IDs/ProductVersions and all 130 compiled IDs**, plus original source/designer hashes and provenance. This arithmetic concerns registered migration classes, not a filename count.

### Thirteen-migration evidence matrix

Paths below use `O = C:\dev\stalltrack-v2-worktrees\ui-completion\EEMOCantilanSDS.Infrastructure\Migrations`. Every recovered file is `<full-ID>.cs`, with the matching `.Designer.cs`. The worktree is at `357f358967bdebcd9ea89cb2828f1d0e6a53ae44`; its Git common repository is the older `stalltrack-v2-clean`. Commit dates below are author dates at +08:00, not application times.

| Full applied ID | Source / introduction evidence | Recovered implementation and verified surviving effects | Current-model relationship |
| --- | --- | --- | --- |
| `20260523083934_FixStallUniqueConstraintIncludeSection` | **U**. No authentic implementation found in the bounded history, source, or assembly searches. Name/date alone are insufficient. | Its exact original operations remain unknown. Current section uniqueness is independently explained by the recovered current-line May 24, June 2 and July 22 migrations described below. | Possible historical predecessor, **not proved**. No fabricated class or history repair. |
| `20260928045651_AddGovernedConfigurableServiceFoundation` | **G**, O; `9405d715deab045ac63b50e878f3d96d3cf69592`, Sep 28 13:16:31 | Creates ConfigurableServiceDefinitions/Policies/Assessments, keys/FKs/checks/indexes; rewrites line/allocation source-shape checks; adds unique allocation `(MunicipalityId, SourceId)` filtered to kind 8. All three tables and that index remain. | Tables absent from current model. Kind 8 was ConfigurableServiceAssessment; it is now GovernedService. Current shape checks supersede old check expressions, but the old unique index remains. |
| `20260928085335_AddFishMeatVendorFeeSource` | **G**, O; `74236d3ca20db8aa127bb73cd8e8c76f88ac4e66`, Sep 28 16:57:08 | Creates FishMeatVendorFeePolicies/Obligations with period/policy/classification references and indexes. Both tables remain. | Both absent from model; modern independent registrations/obligation accounts do not establish a conversion of these records. Old kind 9 is now PenaltyDefinition. |
| `20260928091416_AllowFishMeatVendorFeeCanonicalAllocations` | **G**, O; `29db4849567d3a87143313631139245608ef66c6`, Sep 28 17:32:58 | Drops/replaces two source-shape checks to admit old kind 9. | Original check bodies have been superseded by later current-line migrations. Current allocation shape excludes 9; do not restore old check bodies. |
| `20260928100950_AddVegetableSpaceRentalSource` | **G**, O; `0a008d6a11608143fec9355b684b50ec54a67d5d`, Sep 28 18:14:36 | Creates VegetableSpaceRentalPolicies/Assessments, relationships, checks and indexes. Both remain. | Neither is mapped. Old kind 10 was VegetableSpaceRentalAssessment; current kind 10 is ObligationPeriod. |
| `20260928120100_AddVegetableCanonicalAllocationShape` | **G**, O; `ea3cd0da80f7c816f4fa6f9e58fbc7f0e0d79db9`, Sep 28 20:09:19 | Rewrites two shape checks; creates **unique** `IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId`, filtered to kind 10. Index remains valid/ready with the exact column order and predicate. | Check bodies superseded; **index is a material conflict with current successive obligation installments**. See section 5. |
| `20260928121210_AddEcfApprovedAssessmentSource` | **G**, O; `4a06961750afdfb1269e6c9db620c6e31acae5cc`, Sep 28 20:26:50 | Creates EcfApprovedAssessments, tenant-safe Payor/classification-policy FKs, checks and indexes. Table remains. | Unmapped; old kind 11 is now NpmWeighing. It is not an alternative current ECF writer. |
| `20260928123136_AddWcfApprovedChargePolicy` | **G**, O; `6920044ab2119b3c629d104282efc61160439ed9`, Sep 28 20:40:13 | Creates WcfApprovedChargePolicies and its keys/checks/FK/indexes. Retained. | Unmapped; current WCF authority is not switched by retaining the table. |
| `20260928124319_AddLandingBerthingActivitySource` | **G**, O; `93014c71a04c1466071fcd9b8ae79f5de0e4a955`, Sep 28 20:59:49 | Creates LandingBerthingActivities, relationships/checks and activity/operator indexes. Retained. | Unmapped; newer governed-service representation is not proof of historical data migration. |
| `20260928135259_AddEcfAssessmentSubjectKey` | **G**, O; `db2ad8b7b4e11b8d087ff26354a7d95511802481`, Sep 28 21:55:51 | Adds required varchar(160) ServiceSubjectKey with empty default; **contains data SQL** setting it to upper(btrim(ServiceSubjectReference)); adds unique tenant/subject/period index. Column/index survive. Aggregate: one row, zero normalization mismatches. | Unmapped. Present normalization does not prove original SQL execution chronology. Never replay its data SQL as a guessed repair. |
| `20260928202017_AddServiceCollectionGroups` | **G**, O; `73889807bc06c2ece38015182d64fded7fbe9095`, Sep 29 04:27:13 | Adds nullable varchar(80) CollectionGroupCode to ConfigurableServiceDefinitions and tenant/group index. Both survive. | Unmapped old definitions. |
| `20260929015219_AddKanmanggayMonthlySource` | **W**, O; source/designer are untracked in the historical worktree; no introduction commit found in that repository's reachable history. ID encodes Sep 29 01:52:19 without establishing timezone or application date. | Creates KanmanggayMonthlyPolicies, KanmanggayRentalAccounts and KanmanggayMonthlyObligations, keys/FKs/checks/indexes. All survive with matching column types/nullability. | Unmapped. Old kind 12 is now FishMeatVendorFee. Do not restore this writer or reinterpret money from its old enum. |
| `20260929034147_AddNpmOccupancySourceToEcf` | **W**, O; same untracked-source limit. ID encodes Sep 29 03:41:47. | Adds nullable NpmStallId/NpmContractId, paired-null check, two tenant indexes, composite FKs to Stalls/Contracts and alternate keys `(MunicipalityId, Id)` on both parents. All survive. | ECF table/FKs and Stalls alternate key are outside current snapshot; Contracts alternate key is represented today. Do not remove keys used by retained FKs. |

All ten G working files match their older repository's indexed Git blobs. Their committed history and designer IDs were verified individually. Source/designer SHA-256 values for all twelve pairs are recorded in the evidence JSON. No files were copied into active migrations. The two W pairs are recoverable historical evidence, **not** authenticated build artifacts or newly reconstructed originals.

### What explains the discrepancy, and what does not

The September implementation family is available in the separate historical repository/worktrees but absent from the primary repository's reachable migration paths. Its schema remains in the same local database. That supports **divergent historical implementation lines with retained schema**. The searches did not establish a primary-repository commit deliberately deleting these twelve migrations or approving a baseline retirement. Do not report a proved source-cleanup event or blame a particular consolidation commit without that evidence.

Later current-line migrations overwrite shared source-shape checks, but do not remove the thirteen old tables or the two old allocation indexes. Consequently, "later migrations superseded everything" is false. The May migration is still genuinely unexplained at the implementation level.

## 4. Database versus snapshot/current model

| Compared item | Verified result |
| --- | --- |
| Actual public tables | 95: 81 snapshot tables + 13 retained historical tables + EF history |
| Snapshot columns | 1,202; **no missing/extra columns on the 81 mapped tables; no store-type/nullability differences** |
| Snapshot primary/alternate keys | 116; names and ordered key columns present |
| Snapshot FKs | 89; named relationships, ordered child/parent columns and delete actions match inspected metadata |
| Snapshot checks | 58; all names present. Text can differ through PostgreSQL deparsing; see limits below |
| Snapshot indexes | 163; all names, ordered key expressions, uniqueness and normalized predicates match |
| Retained migration-created tables | 13; recovered create/add column types and nullability match; named PK/AK/FK/check objects are present |
| Recovered CreateIndex operations | 42; all indexes present with matching ordered key expressions and uniqueness |
| Global index/constraint health | All inspected indexes valid/ready; no unvalidated constraints |
| Current preparation model | 82 tables; only NpmRentMonths is missing; its two proposed parent alternate keys are absent as expected |

The thirteen retained table names and their aggregate row counts are in the evidence JSON. They contain **15 rows in aggregate**: ConfigurableServiceAssessments 1, ConfigurableServiceDefinitions 6, ConfigurableServicePolicies 5, EcfApprovedAssessments 1, KanmanggayMonthlyPolicies 1 and VegetableSpaceRentalPolicies 1; the other seven tables are empty at inspection. These counts do not establish whether records are test data, approved assessments or settled cash. Preserve them all.

There are zero CollectionAllocations with current numeric kinds 8, 9 or 10 at this audit snapshot. Tenant-and-ID joins between CollectionLines and each corresponding old source table for historical kinds 8–12 found zero matching lines. This is useful negative evidence, not authorization to delete old source records, reinterpret historical cash, or assert that every historical event has been reconstructed.

### Objects outside the snapshot

- All thirteen historical tables are outside the current model and its tenant backup/export inventory. A current model-based tenant backup is therefore **not a complete preservation mechanism for this retained schema**. Any later baseline operation requires separately approved full schema/data preservation; no backup or restore was performed here.
- Extra `AK_Stalls_MunicipalityId_Id` and its supporting unique index are explained by the recovered W ECF occupancy migration; retained ECF FKs depend on it.
- The two extra allocation indexes are explained by the recovered G migrations. Their runtime effects cannot be dismissed as harmless unmapped metadata.
- Three other extra expression indexes are explicitly managed by current migration SQL: `UX_AssessmentRequests_ActiveMunicipality` (20260918090000), `UX_SlaughterAnimalRates_Municipality_NormalizedName` (20260917073034), and `IX_Stalls_NullSection_Facility_CustomSection_StallNo` (20260722024344). EF does not represent these as ordinary snapshot indexes.
- `CollectionReferenceNumberSeq` is explained by current SRC migration `20261004060231_AddCollectionReferenceCode`.
- A public `vw_Accounts` view also exists and is not represented in the model or located in the searched migration/source scripts. Its definition projects Users with a derived account-type label; no view rows were read. Its creation/ownership history remains unverified. No authorization or account change follows from this finding.

### Defaults and check-expression limits

The comparison flagged 44 default/computed-expression text differences. Inspection distinguishes textual formatting (`0`/`0.0`, casts, sequence regclass rendering and CASE formatting) from database defaults retained by migration operations but not configured in today's model. Examples include zero-GUID MunicipalityId defaults from `20260704065733`, one-time new-column defaults for office/accountable-form metadata, and default 0/false values introduced by existing current-line migrations. These are not thirteen newly inferred financial changes. WaterPartialAmount traces through the `PartialAmount` rename in `20260703180108`; its default originated earlier. Do not silently remove these defaults or infer rates from them.

Twenty-three check definitions differ under a deliberately simple text normalization, chiefly IN versus `= ANY(ARRAY[])`, BETWEEN expansion, casts, trim/interval spelling and parentheses. No general SQL theorem-equivalence claim is made. For the four financially relevant source-shape checks, direct read-only SELECT evaluation compared model and actual definitions on **378 synthetic nullable kind/part/ID combinations each: 1,512 comparisons, zero differences**. These SELECTs do not insert payments or prove every other check on every possible value. PostgreSQL constraint execution, stored-generated behavior and write/concurrency remain separate gates.

### May uniqueness lineage: independently known versus unknown

Current source `Migrations/20260524000000_AllowSectionScopedStallNumbers.cs`, introduced by `b0d12b45` on May 24, splits uniqueness into non-null Section and null Section indexes. `20260602045818_ChangeStallNoToString` explicitly drops/recreates those indexes; `20260722024344_ReplaceNullSectionStallUniqueIndexPerCustomSection` replaces the null-section index with the expression index on FacilityId/COALESCE(CustomSectionName,'')/StallNo. Actual current indexes agree with these later definitions. This explains current uniqueness without pretending to recover what the missing May 23 migration originally executed. Additional earlier effects, if any, remain unknown.

## 5. Material incompatibility: old unique index versus current installments

Verified retained index definition, **evidence only, not an instruction to execute**:

```sql
CREATE UNIQUE INDEX "IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId"
ON public."CollectionAllocations" ("MunicipalityId", "SourceKind", "SourceId")
WHERE ("SourceKind" = 10);
```

Its original source is the recovered `20260928120100_AddVegetableCanonicalAllocationShape`. Historical `CollectionSourceKind` in the same worktree maps 10 to VegetableSpaceRentalAssessment. Current `Domain/Enums/CollectionSourceKind.cs` maps 10 to **ObligationPeriod**.

The current `CollectionAllocationConfiguration` declares a nonunique source lookup index and permits kind-10 period allocations. `Application/Common/Revenue/CollectionComposerWorkflow.MobileObligation.cs` emits allocations against the period ID; `ObligationCollectionSource` and `ObligationWorkflow.GetRegisterAsync` aggregate them. Existing PostgreSQL test `IntegrationTests/ObligationAccountTests.cs:VendorFeeIsAMonthlyGoal_SettledByThirtyPesoInstallments_OnItsOwnClassification` intentionally posts two separate PHP 30 allocations to the same PHP 900 period and expects PHP 60 collected / PHP 840 remaining.

Those two legitimate allocations have the **same tenant, kind=10 and period SourceId**. A unique index on exactly that tuple rejects the second row, regardless of different Collection IDs, operation IDs or amounts. This conclusion follows from verified index semantics and the writer/test contract. **No INSERT, observed 23505, live failed payment or reproduction against municipal cash is claimed.** There currently are no kind-10 allocations, so this inspection establishes a latent schema conflict, not damage to an identified receipt.

The older kind-8 unique index is also retained; current allocation shape excludes 8, so its immediate effect differs. Both indexes require explicit source-authority review. Historical/current enum reuse reinforces why restoring old financial writers or automatically mapping old records is unsafe.

**Required separate correction proposal:** preserve the evidence/history; review retirement of the obsolete kind-10 uniqueness rule and any dependent assumptions; prove successive canonical installments, corrections, replay and backups on an expressly authorized isolated PostgreSQL reproduction of this retained baseline. A constraint/index retirement is an existing-schema change requiring explicit review and approval, not part of this documentation task and not silently bundled into an otherwise additive NPM table migration. Do not drop either index now, run its Down migration, alter history, or use retries/new IDs to bypass it.

## 6. Recovery alternatives and recommendation

| Alternative | Evidence and consequence | Decision |
| --- | --- | --- |
| A. Recover/restore authentic historical migration implementations | Ten committed implementations are recoverable. Two W pairs need stronger archival/build evidence; May remains missing. Restoring old classes would affect fresh-database replay and historical Down paths, recreate obsolete numeric source rules/indexes, and require reviewed ordering/snapshot integration. An existing applied ID would not normally rerun its Up, but that does not make restoring the entire divergent history safe. | Preserve provenance; **do not restore/cherry-pick automatically**. Recovery is evidence, not blanket integration approval. |
| B. Accept a retained-schema baseline | Mapped table structure is strongly corroborated and the retained tables' origins largely explained. Acceptance also requires explicit preservation/ownership of the extra records, unsupported backup coverage, the unknown May/view history, and resolution of the live index incompatibility. Counts/latest ID cannot authorize it. | **Not yet defensible as a compatible baseline** because a concrete financial writer conflict exists. Could be reconsidered after the controlled correction and PostgreSQL proofs. |
| C. Keep migration application blocked | Avoids accidental unreviewed migration or loss of retained evidence while the material conflict and provenance gaps are resolved. | **Recommended now.** Do not proceed to Task 2D or a schema application. |

The safest next task is a separately scoped baseline-compatibility correction design/rehearsal, with explicit authorization for isolated PostgreSQL infrastructure and for any necessary legacy index retirement. It is not an automatic rebaseline, removal of the 13 history rows, guessed migration reconstruction or conversion of old money. Recovery of May source from an approved original archive/build remains desirable; if unavailable, an owner must explicitly accept a bounded, documented residual-lineage uncertainty after structural/behavioral proof.

## 7. NPM seven-operation proposal against the actual baseline

The source model still proposes exactly two existing-parent keys, one new table and four indexes. No migration was generated.

| Proposed operation | Actual-baseline finding |
| --- | --- |
| Facilities `AK_Facilities_MunicipalityId_Id` | Absent; proposed pair has zero duplicate groups; parent IDs/tenancy columns present. Additive key introduction, with locking review still required. |
| Stalls `AK_Stalls_MunicipalityId_Id_FacilityId` | Absent; proposed triple has zero duplicate groups. The **existing pair** AK_Stalls_MunicipalityId_Id is different and must remain for retained ECF dependencies. |
| NpmRentMonths table, three composite restrictive FKs and nine checks | Table absent; existing Contracts `(MunicipalityId, Id, StallId)` key is available. Zero mismatched/orphaned Stall→Facility and Contract→Stall relationships in the aggregate checks. This is metadata/data-readiness evidence, not executed enforcement. |
| Four NpmRentMonths indexes | Names/table are new; no observed collision. PostgreSQL creation, concurrency and checks remain untested. |

Facilities/Stalls/Contracts counts remain **13 / 109 / 111**. These seven operations do not resolve the unrelated retained allocation index or recover missing history. They remain structurally additive at this snapshot; **the database-level integration gate remains closed**. Do not combine a legacy-index removal into their advertised seven-operation additive plan without a separately reviewed change.

The month anchor remains preparation-only. Nothing here adds a source kind, typed allocator, cash writer, SRC, remittance event, historical backfill or collection permission. NeedsReconciliation/Reconciled are not permission to collect.

## 8. Startup migration safety

`Api/Program.cs:36–39` gates `DatabaseStartup.MigrateAndSeedAsync` with `Database:ApplyMigrationsAtStartup`. Development configuration enables that flag. `Infrastructure/Persistence/DatabaseStartup.cs` opens the existing connection, sets its lock timeout, obtains advisory lock `8472113509001`, calls `Database.MigrateAsync`, then municipality/facility/rate/classification seeders. A lock serializes execution; it does not approve migrations or make seeders read-only.

`AppDbContextDesignTimeFactory` does not start the API; its fallback connection is not authorization to create another database. Compiled assembly/snapshot inspection can remain entirely offline, as demonstrated here.

Until separately approved, retain prospective DDL as documentation/offline operations only, outside the active migrations directories and snapshot. Do not restore migration classes or restart an application to test whether EF will tolerate history drift. A future approved artifact must be reviewed together with its effective startup configuration/deployment path so a routine Development restart cannot apply it incidentally. No active flag, connection, startup code or process was changed in this audit.

## 9. Required isolated PostgreSQL verification and owner gates

No disposable PostgreSQL or Testcontainers was provisioned. Separate authorization is needed for an isolated instance/database that cannot connect to the municipal database, with separately identified connection/environment, no production clients, no copied personal data and cleanup ownership. Use recovered schema-only DDL plus synthetic evidence to reproduce retained constraints; testing only the current 130-migration blank schema misses the conflicting index.

Required proof before acceptance/application:

1. Establish a preserved baseline inventory including thirteen legacy tables, records, indexes, view, sequence and history; obtain full-backup/restore authorization separately. Confirm artifacts and rollback preserve all retained records; do not execute unknown Down histories.
2. Reproduce both the current-line fresh-schema path and the retained-schema path. Demonstrate the legacy kind-10 index rejects a second same-period allocation in isolation, then verify any specifically approved correction permits genuine installments without duplicate financial events. Preserve original migration-history rows.
3. Test existing source discriminators, source lookup, successive installments, stale/replay requests, corrections/reversals, SRC and remittance once. Decide explicitly how any old source identities would be interpreted; no automatic conversion by equal integer kind or name.
4. Rehearse the NPM parent keys/FKs/checks and duplicate/concurrent month inserts, wrong facility/contract/tenant references, JSON/amount/date rules, optimistic-version conflicts and atomic audit persistence. Creation of an anchor must produce zero Collections/SRCs.
5. Verify full and tenant-scoped backup/export/restore behavior, including legacy unmapped objects and the previously identified old-backup/NpmRentMonths omission risk. Define acceptance rather than assuming model-based backup is complete.
6. Approve the baseline strategy, any existing-index correction, authentic-source restoration if chosen, startup application procedure and later additive NPM migration **as distinct gates**. Migration preparation/application and financial activation require separate authorization.

Cantilan's accepted future PHP 900 / thirty PHP 30 cap and no extra day-31 stall rent, exact PHP 25/20 remainders, PureDays compatibility, source-owned identity and shared Head/Mobile monthly balance remain unchanged requirements. None becomes implemented through this audit or by accepting the anchor alone. F-02/F-03, onboarding, accountable-form policy and financial writer cutover remain excluded.

## 10. Executed verification

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmRentMonth|FullyQualifiedName~TenantBackupCoverageTests|FullyQualifiedName~TenantFilter|FullyQualifiedName~CollectionSourceAuthorityMapTests|FullyQualifiedName~CollectionSourceShapeModelTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests|FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlySettlementArchitectureProofTests|FullyQualifiedName~CrossTenantReadsAreNamedTests'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

| Actually executed | Result |
| --- | --- |
| Focused model/domain/authority/tenant/backup/NPM checks, Release compilation | **132 passed, 0 failed, 2 skipped; total 134** |
| Full unit suite | **2,711 passed, 0 failed, 2 skipped; total 2,713** |
| Offline compiled registry / migration-operation inspection | 130 compiled migrations and 12 recovered migration UpOperations read; no SQL executed |
| Schema metadata comparison | Results and limits in sections 3–4; no mutations |
| Source-shape read-only equivalence probes | 4 × 378 = **1,512 comparisons; zero differences** |
| Real PostgreSQL INSERT/index conflict, migrations, concurrent updates, restore, financial integration | **Not run / not authorized** |

The two skips remain the pending exact-settlement theory (eight examples, reported as one skipped theory by xUnit) and the day-31 writer acceptance fact. No test was weakened or skip removed. No new test source was necessary: existing offline safety tests were reused; temporary audit helpers perform metadata comparison only. Passing these tests does not prove PostgreSQL compatibility, and in particular cannot neutralize an unmapped unique index absent from fresh-schema fixtures.

Logs/helpers are local temporary evidence under `%TEMP%\stalltrack-lineage-2c2`, `%TEMP%\stalltrack-task2c2-focused.log`, `%TEMP%\stalltrack-task2c2-unit.log`, and `%TEMP%\stalltrack-task2c2-manifest-build.log`. The committed-source provenance, complete migration membership and sanitized key findings are retained in the linked JSON so this conclusion does not depend solely on temporary logs.

## 11. Final checkpoint and next action

Final verification confirmed that **all seventeen entry SHA-256 values are unchanged**. Active migration directories, snapshot and configuration are unchanged. `git diff --check` passed; Git reported only its existing LF/CRLF notice for the protected characterization fixture. Task delta: **two documentation/evidence files only**, alongside the seventeen preexisting pending paths (six modified tracked files and thirteen untracked files in total). Nothing is staged; HEAD remains `17b9d100`; no commit was made merely to clear the pending work.

**Task 2C.2 investigation is complete; migration readiness is still blocked.** The concrete next decision is authorization for an isolated retained-schema compatibility rehearsal and a narrow legacy-index correction proposal. Do not apply an NPM migration, restore old migrations wholesale, edit history or proceed to payment implementation on the strength of this report.
