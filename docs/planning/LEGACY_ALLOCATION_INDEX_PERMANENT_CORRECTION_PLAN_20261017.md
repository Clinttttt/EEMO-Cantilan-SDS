# Task 2C.4 — Local development obsolete kind-10 index correction

**Current outcome, Task 2C.4A: A — COMPLETED AND VERIFIED.** The single owner-approved obsolete kind-10 index is permanently absent in the authorized local development database. Fresh independent read-only postflight verified no other checked schema or data changes. Earlier review holds below are historical; see the final execution evidence for the actual completed result. This does not approve the broader migration baseline or NPM financial implementation.

**Independent review hold, 2026-10-17:** The owner's later, narrow permanent-change approval is confirmed. The execution artifact nevertheless has the safety gaps recorded below. It was **not executed**, and no database change was made during this review. Approval remains conditional on correcting and reviewing those gaps; this is a technical hold, not an authorization or workstream ambiguity.

**Task 2C.4A repair checkpoint:** The two defects and the normalized-address guard issue are repaired. Final focused safety tests: **25 passed, 0 failed, 0 skipped**. Actual execution is recorded at the end of this document.

**Status:** Approved for a **single permanent index retirement**, subject to strict preflight and independently verified postflight. This document is a narrowly scoped owner-approved correction plan, not a general migration-baseline approval. Created 2026-10-17; complete execution evidence only after the database operation.

## Authorization and target

Owner approved on 2026-10-17: permanently retire only `public."IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId"` in local PostgreSQL `localhost:5432/EEMOCantilanSDS.DB`, after verifying backup, safety and reviewed plan. No other schema, `__EFMigrationsHistory`, cash, SRC, remittance, financial source-kind, configuration, API, or Platform changes are authorized.

## Rationale and prior evidence

Task 2C.2 recovered original `20260928120100_AddVegetableCanonicalAllocationShape`. It created a **UNIQUE filtered index for SourceKind=10**, when kind 10 was VegetableSpaceRentalAssessment. In current source, kind 10 means `ObligationPeriod` and legitimately accepts multiple payments. Task 2C.3 reproduced a second valid PHP 30 allocation failing with PostgreSQL 23505 and proved that a transactional index DROP permitted PHP 60 net allocated / PHP 840 of PHP 900 still owed; ROLLBACK restored the index, data and schema. The current *nonunique* source lookup index was unaffected. The index must not be recreated in an automatic Down: after legitimate installments are posted it could fail or reintroduce the bug.

Backup: `C:\\dev\\stalltrack-v2-worktrees\\local-db-backups\\EEMOCantilanSDS.DB_task2c3_full_2026-10-17T00-57-38-716Z.dump`; original 1,712,039 bytes; SHA-256 `08c54731264fb352b3e6d40c10dc129cd3240873ae8c982df7d047bf9ba7de99`. It passed pg_dump custom-archive full decoding, schema/data listing and coverage of thirteen retained tables plus EF migration history. Confirm checksum and readiness immediately before write; make an additional full backup if target changed. The backup does not include PostgreSQL cluster-global roles; a restore was not rehearsed.

## Mandatory stop/abort conditions

- Target is not the exact loopback host, port, database or PostgreSQL 18-compatible instance; backup missing, changed or invalid.
- EEMO API running or any other database session present; migration/release job, unexpected triggers or writer locks.
- Current index missing or its name, table, three column keys, uniqueness, predicate `SourceKind = 10`, validity/readiness or unexpected dependency differs from the recovered original.
- Verified valid/ready **nonunique** index on (MunicipalityId, SourceKind, SourceId, SourcePart) missing, or separate historical kind-8 index altered/missing.
- Current migration-history count not 143, or table/data baseline unexpectedly differs, or timeout/transaction failure.
- Any operation would require CASCADE, index recreation, schema backfill, history rewrite, financial record change or API restart.

## Transaction plan

Use a dedicated connection with bounded timeouts; verify no other sessions and acquire migration advisory lock `8472113509001` without waiting. Begin one transaction; use a NOWAIT exclusive lock on `CollectionAllocations`. Re-verify the exact index, dependencies, nonunique lookup and kind-8 index; execute **only**:

```sql
DROP INDEX public."IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId";
```

Verify the legacy index is absent, the nonunique and kind-8 indexes remain valid, `__EFMigrationsHistory` is unchanged and selected source/cash row counts are unchanged. COMMIT only if all guards pass; otherwise ROLLBACK. No `IF EXISTS`, `CASCADE`, `CONCURRENTLY`, other DDL/DML or sequence operation. Postflight on a fresh read-only connection must verify that exactly the intended schema object changed and all 95 public tables' contents and SRC sequence state match preflight.

## Migration/baseline follow-up

This is a deliberate local retained-schema correction performed under explicit owner authorization. **Do not create or apply an EF migration or insert an EF history row as part of this one-index operation.** Future source-controlled forward-migration or formally reviewed baseline reconciliation must document that fresh 130-migration databases never had this old index while some retained 143-entry databases did; do not blindly restore the divergent old classes. Thirteen retained tables, the unrecovered May migration, `vw_Accounts`, and tenant backup/restore issues remain separate blockers. No NPM rent-month DDL or typed allocation/writer activation is authorized.

## Execution checkpoint

Operation completed under Task 2C.4A; see final execution/postflight below. Existing Task 2A–2C.3 files remain untouched and unstaged. No push, deployment or API startup.

## Independent review after owner coordination clarification

Verified primary branch `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100e8ddffdd747f5688a8e49818693d078a`; all 26 pending files were inventoried and hashed at review entry. The owner identified this plan and `docs/evidence/task2c4/retire-obsolete-kind10-index.cjs` as intentional drafts and explicitly approved permanent removal of **only** the obsolete kind-10 index subject to the mandatory safeguards. No other migration/schema/data change is authorized. This clarification supersedes the older preparation-only restriction for that single index; it does not waive technical verification.

### Reviewed controls already present

- Script gates an explicit authorization environment value, restricts host to loopback, database to EEMOCantilanSDS.DB, port to 5432 and server major version to 18.
- DDL is one fully qualified, fixed-name DROP INDEX, without CASCADE, CONCURRENTLY or other data/schema changes. Exact index definition, valid/ready flags and dependencies are checked against the prior reviewed evidence. Normal nonunique and separate kind-8 indexes are checked before and after.
- The script checks prior backup size/hash and is designed to create and fully decode a fresh custom archive before the writing transaction, including data coverage of the thirteen retained tables and EF history.
- It checks zero other database sessions, takes the existing migration advisory lock, uses bounded connection/statement/lock/idle-transaction timeouts, locks all public tables against writers and CollectionAllocations exclusively, and checks unexpected public triggers.
- Before COMMIT it compares all public-table fingerprints, history, SRC sequence and the checked schema definitions, accepting exactly one removed index and no added schema definitions. Before-COMMIT errors attempt ROLLBACK; no synthetic financial posting is included.

These are source-review findings, **not evidence that the controls have executed or passed in the current environment**. No existing script was blindly executed.

### Blocker 1 — promised independent postflight is not implemented

The transaction plan above requires postflight on a **fresh read-only connection**. The draft contains only one `new Client(...)` and one `await c.connect()`. After COMMIT it starts another read-only transaction on that same client and calls `snapshot()`/`indexGuard()` functions closed over it. That is a fresh transaction, not the promised independent connection. A separate verifier must reconnect, confirm target identity and freshly read the committed index/data/history/sequence state. Its result must be distinguished from pre-COMMIT assertions. No such connection was added during this stop-at-defect review.

### Blocker 2 — uncertain COMMIT acknowledgement is reported incorrectly

Current ordering is:

```javascript
await c.query('COMMIT');
transaction = false;
committed = true;
```

If PostgreSQL commits but the connection fails before acknowledging COMMIT, the catch path still has `committed === false`. It attempts ROLLBACK and prints that false flag, even though the server may have permanently removed the index. ROLLBACK cannot undo an already committed DROP, and a disconnected client cannot establish the outcome. The final success evidence file is written only after postflight, so this path can also leave no durable result distinguishing the uncertainty.

An **offline synthetic lost-acknowledgement simulation** of this exact assignment ordering produced `simulatedServerCommitted: true`, `reportedCommitted: false`, code ECONNRESET. No database connection or transaction was used for that simulation. It proves the client-state reporting defect, not an actual PostgreSQL failure during this task.

Required correction design: distinguish not-attempted, COMMIT-attempted/outcome-unknown, acknowledged-committed and independently-verified states. Persist safe preflight/intent evidence before the irreversible boundary, report unknown outcome honestly, and stop without automatic retry, inverse DDL or restoration. Use a new read-only connection to determine the committed state when possible; otherwise require explicit reconciliation. A later postflight/evidence-write failure after acknowledged COMMIT must retain that committed status and must not claim rollback restored the index. These are proposed fixes, not implemented workarounds.

### Checks performed and checks deliberately not performed

- Full script and plan were read independently; `node --check` passed.
- Offline client-state diagnostic reproduced the unsafe lost-acknowledgement report. It did not execute any part of the database script.
- No fresh backup, database connection, DROP, COMMIT, migration, API restart or financial operation was attempted after finding these blockers. Prior Task 2C.3 backup/rehearsal evidence remains historical evidence, not a new permanent correction result.
- No regression suite was rerun for this review-only/documentation update; Task 2C.3's executed test counts remain in its unchanged handoff. No new financial behavior is claimed.
- The execution script and the other 24 pending paths are preserved. Only this expressly requested plan update records the review result; nothing is staged or committed.

**Actual result: stopped before database operations.** Fix/review the two identified safety gaps under an explicit follow-up, then verify a fresh full backup, API/writer quiescence and all target/index guards before using the owner's existing narrow application approval. Do not treat a syntax check, the presence of COMMIT, or earlier rollback success as permission to bypass these conditions.

## Task 2C.4A — repaired safety controls and execution evidence

The explicit Task 2C.4A instruction authorizes repair of this plan/script and execution of the same single approved index retirement after all guards pass. Entry branch/HEAD remain `codex/local-unified-v8-v10-20261013` / `17b9d100`; all 26 pending paths were inventoried and hashed. Only this plan and the existing correction script may change; the narrow offline test/evidence additions are permitted by Task 2C.4A. No application, migration, snapshot or financial test is edited.

### Exact repairs and second-pass review

- The shared `executeCorrection` state machine is exported for offline tests; importing it does not resolve credentials, load a PostgreSQL client, execute main, or open a connection.
- COMMIT states distinguish `not_attempted`, `attempted_outcome_unknown` and `acknowledged`. Separate verification/outcome fields distinguish independently verified committed state, independently verified unchanged state and unresolved/manual-reconciliation outcomes. No boolean false is offered as proof that an unacknowledged COMMIT did not apply.
- Immediately before sending COMMIT, the script appends the target, operation, script hash, backup hash, full checked before/expected-after metadata and row fingerprints to a uniquely named journal under the external backup directory, then **fsyncs** it. A failed intent write prevents COMMIT and rolls back the uncommitted transaction.
- After COMMIT acknowledgement or failure, the original session is closed, without retrying COMMIT/DROP or sending a compensating schema operation. A fresh `Client` with `default_transaction_read_only=on` connects independently. It revalidates database/address/port/server and requires a **different backend PID**. It verifies `transaction_read_only=on`, no other sessions and NOWAIT read locks before inspecting the committed state.
- The verifier compares all 95 table fingerprints, the complete migration-history table through that fingerprint (not count alone), SRC sequence, table list and checked schema definitions. Only the exact one-index removal can be classified as committed; exact original state can be classified unchanged after an unacknowledged COMMIT. Any unavailable/mismatched verification remains explicitly unresolved.
- pg idle-connection error events have handlers so a socket event cannot silently terminate the COMMIT recovery path. Writer close is bounded; an original backend still holding exclusive locks makes verification fail closed rather than assuming rollback.
- Result evidence is exclusively created and fsynced. A post-COMMIT journal/result write failure preserves the database outcome, marks `evidenceWriteFailed` and returns a non-success operational result. It never relabels the database change as rolled back. Prior COMMIT intent or an acknowledged/uncertain outcome blocks execution; only a reviewed preflight-only failure with no intent can be resumed, retaining its evidence unchanged.
- Reviewed live adapter still contains exactly one fixed-name DROP INDEX and one COMMIT call, no data DML, no inverse DDL, no retry loop, no EF migrator and no new financial source. Target/backup/API/quiescence/index/dependency/lock/timeout guards remain. The normal lookup and kind-8 index are checked independently on both writer and verifier.

### Offline evidence before permanent execution

```powershell
node --check docs/evidence/task2c4/retire-obsolete-kind10-index.cjs
node --check docs/evidence/task2c4/retire-obsolete-kind10-index.test.cjs
node --test docs/evidence/task2c4/retire-obsolete-kind10-index.test.cjs
```

Initial run: **17 passed, 0 failed, 0 skipped**. Final run: **25 passed, 0 failed, 0 skipped**. These exercise the actual exported orchestration/delta/identity/prior-attempt guards with deterministic fake adapters, not a parallel implementation or PostgreSQL fault injection. The first seventeen cases cover preflight failure, index mismatch, backup failure, failure before COMMIT, failed durable intent, acknowledged COMMIT, committed with lost acknowledgement, uncommitted with lost connection, unavailable verifier after acknowledged/unknown COMMIT, evidence-write failure, unexpected schema/data, accepted exact index delta and three rejected schema deltas. Eight added cases cover strict normalized IPv4/IPv6 identity, wrong host/database rejection, and prior-attempt/COMMIT-intent restrictions. Assertions ensure no second DROP, COMMIT retry, false success or compensating mutation.

Second-pass review checked the shared logic against the live adapter: fresh Client construction and distinct PID, read-only verifier options/transaction, fsync-before-COMMIT ordering, explicit unknown state, unchanged-state recognition, error/event handling, and bounded session cleanup. No PostgreSQL fault injection is performed. Passing simulations establishes recovery/control behavior, not complete financial posting correctness.

### Actual execution checkpoint

**Final classification A — Completed and verified.**

#### Preserved nonmutating failure and final guard review

The first invocation at `2026-10-17T01:37:53.805Z` stopped in preflight: PostgreSQL returned `inet_server_addr()::text` as `::1/128`, while the strict identity guard expected normalized `::1`. Read-only diagnosis confirmed the exact authorized database/port and that `host(inet_server_addr())` yields `::1`. The script now uses PostgreSQL's host normalization and retains exact loopback/database/port checks. No backup, mutation, DROP or COMMIT occurred in that first invocation.

[First-attempt evidence](../evidence/2026-10-17_kind10_permanent_correction.json) is preserved. The subsequent explicit invocation was allowed only after verifying that this record was preflight-only, COMMIT was `not_attempted` and its external journal had no COMMIT intent. Unique timestamped result files prevent overwriting prior outcomes. Any prior journal containing a COMMIT intent now blocks repeated execution. This is not an automatic retry of an uncertain transaction, a widened target guard or an alternate database execution route. The added offline cases prove these restrictions.

#### Fresh backup and approved transaction

The successful invocation reverified the original backup and reviewed database contents/schema, then created a new full custom archive before any mutation:

| Backup evidence | Actual value |
| --- | --- |
| Path | `C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_before_kind10_permanent_2026-10-17T01-39-48-556Z.dump` |
| Verification completed UTC | `2026-10-17T01:39:49.302Z` |
| Size | **1,712,039 bytes** |
| SHA-256 | `6de2ff106738f0caa4c58f1d979b289b6b8a3a89dea5102b23adac41b0165592` |
| Verification | pg_dump exit 0; archive listing/full decoding to NUL exit 0; TABLE DATA coverage individually confirmed for thirteen historical tables plus EF history |
| Durable journal | `C:\dev\stalltrack-v2-worktrees\local-db-backups\kind10-correction-2026-10-17T01-39-45-376Z.journal.jsonl` |

API/process and database-session quiescence, exact target/index/dependencies, lookup/kind-8, bounded locks and unexpected-trigger guards passed. All data/history/sequence and checked schema guards passed before COMMIT, accepting exactly the approved removed index. The commit-intent journal was fsynced before COMMIT. PostgreSQL acknowledged COMMIT. There was **no lost acknowledgement, uncertain COMMIT or post-COMMIT evidence-write failure** in the successful operation. No other schema DDL or financial DML was run.

#### Fresh independent postflight

The original writer was backend PID **10972**. After closing it, the script established a fresh read-only connection with PID **23028**, independently validating EEMOCantilanSDS.DB, loopback `::1`, port 5432, PostgreSQL 18.4 and `transaction_read_only=on`. A further separate read-only inspection used PID **20704** and corroborated the final state, including exact comparison of every migration ID/ProductVersion against the earlier inventory.

| Checked state | Verified result |
| --- | --- |
| Obsolete `IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId` | **Absent** |
| Nonunique `IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId_So~` | Original definition, valid and ready |
| Separate kind-8 `IX_CollectionAllocations_MunicipalityId_SourceId` | Original definition, valid and ready |
| Schema delta | Exactly one removed index, no other added/removed/changed checked definitions |
| Public tables | **95**, unchanged |
| All-table data fingerprint | `ec22390fe31353fd542a43cca3dc018ee10725a2de1499f8e22cd034eb33edad`, unchanged |
| Migration history | **143 entries**, all IDs/ProductVersions unchanged; also covered by the all-table data comparison |
| SRC sequence last_value / is_called | **17 / true**, unchanged |
| Checked schema hash before | `ee67c77e6d7f74cfac2fcef3096f287721e205e3c5542fc838905c91d203b99f` |
| Checked schema hash after | `6af7382bf3babb0910aad1476f60f12d6352720b4a63c09fe03482164fe2978d` — exactly the approved index delta |
| Outcome | `commit: acknowledged`, `verification: committed`, `outcome: independently_verified_committed`; no evidence-write failure |

[Successful execution evidence](../evidence/2026-10-17_kind10_permanent_correction_2026-10-17T01-39-45-376Z.json) records the script hash, backup, distinct writer/verifier identities and data/schema/sequence outcomes. The external durable journal retains the full checked preflight and expected postflight metadata. The executed script SHA-256 is `c490313715992e4b0801442c991e72b7f979df87fcc5d57a7eade212e286391c` and matches the current file.

No synthetic collection, financial posting, SRC issuance, remittance, schema migration or new source activation occurred. All original records, retained tables, migration-history contents, source-kind values and historical PureDays evidence are unchanged. The 130 compiled migrations and NPM preparation snapshot/source were not modified. The unrecovered May source, view provenance and broader tenant-backup/restore compatibility risks remain open; this narrow approved correction is **not** acceptance of the entire retained baseline or implementation of the NPM monthly cap/exact settlement writer.

#### Source-control and test checkpoint

Only the two permitted existing files (this plan and the script) are edited. Added the scoped offline test and two execution evidence files (preflight-only failure and independently verified success). Final SHA-256 comparison confirmed the other **24 entry paths remain byte-identical**. Git now has **29 pending files** (six tracked modifications and twenty-three untracked files). Nothing is staged or committed; branch/HEAD remain unchanged at `codex/local-unified-v8-v10-20261013` / `17b9d100`. Final whitespace/link/syntax checks passed, with only Git's preexisting LF/CRLF notice for the unchanged characterization fixture. No API restart, push, deployment or automatic next implementation phase.

Focused safety validation: **25 passed / 0 failed / 0 skipped**, plus syntax checks and the guarded PostgreSQL operation/independent read-only postflights. No .NET/application source changed, so broad .NET suites were not rerun for Task 2C.4A. Earlier Task 2C.3 test counts remain historical; the two pending NPM financial acceptance skips remain untouched.
