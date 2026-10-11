# Fine Assessment migration compatibility fix — October 10, 2026

Implementation evidence; no change to business authority or financial contracts.

## Baseline and reported failure

- Checkout: `C:\dev\stalltrack\eemo`.
- Branch: `codex/vegetable-fruit-space-workspace`.
- Starting HEAD: `edf6f8d1`.
- API startup failed in `20261010013040_GovernedFineAssessments20261010` with PostgreSQL `42P07`: `AK_Contracts_MunicipalityId_Id` already exists.

The pending migration unconditionally added a unique constraint on `Contracts (MunicipalityId, Id)`. The reported database already contains a same-named object. Its provenance and exact shape were not inspected in the user's database; the fix does not assume which earlier action created it.

## Scoped correction

The pending migration now checks PostgreSQL catalogs before adding the shared tenant key:

- Reuse the same-named, validated, non-deferrable unique constraint only when its table and ordered columns match.
- Attach a matching, valid, standalone unique B-tree index as the constraint when it has the exact keys, no predicate, expressions or included columns, and no existing constraint owner.
- Create the key normally when absent.
- Reject an incompatible same-named object with a descriptive error. Do not drop it or suppress the error.

Rollback retains the shared key because it can predate Fine Assessments and support unrelated foreign keys. Fine Assessment tables retain their existing rollback behavior.

The failing pending migration is corrected directly: a later migration cannot execute until this migration succeeds. Model, snapshot, financial source identities and migration history are unchanged. No manual history insertion, cash mutation, startup exception suppression or additional migration was introduced.

## Verification

The regression test first ran against the original migration on disposable PostgreSQL: the clean case passed; both pre-existing matching constraint/index cases failed with the reported `42P07`.

After the correction, all five compatibility cases passed: matching constraint, matching index, clean schema, incompatible index and incompatible constraint. Successful cases exercise upgrade, rollback, re-upgrade, all later migrations and repeated migration startup. They verify one matching constraint remains and no Collection is created. Incompatible cases verify the migration is not recorded as applied.

Executed commands:

```powershell
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter 'FullyQualifiedName~FineAssessmentMigrationCompatibilityTests' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter 'FullyQualifiedName~FineAssessment' -v quiet
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release -v quiet
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj --configuration Release -v quiet
git diff --check
```

Fine Assessment PostgreSQL regression suite (including the five compatibility cases): 42 passed, zero failures/skips. Full Unit suite: 2,529 passed, zero failures/skips. API Release: succeeded with zero warnings/errors. Existing Unit analyzer warnings are unrelated. `git diff --check` passed.

## Environment and safety

PostgreSQL tests use Testcontainers and isolated disposable upgrade databases. They apply migrations only there and dispose those databases afterward. No API runtime was started against the user's tenant database. No user-local or hosted database migration, production action, push, integration or deployment was performed.

UI/CSS and financial posting behavior are unchanged, so browser acceptance and Web component/CSS checks are not part of this migration-only correction. The user's startup has not been re-run: local runtime confirmation remains pending. An incompatible existing object requires inspection rather than deletion or a guessed history repair.

Protected planning-document edits and unrelated Login/AuditTrail/Menu changes are excluded from the fix commit.
