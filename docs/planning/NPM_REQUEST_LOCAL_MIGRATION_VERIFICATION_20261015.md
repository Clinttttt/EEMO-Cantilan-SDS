# NPM registration requests — approved local migration and verification
Date: October 15, 2026

## Approval and scope
The office owner explicitly approved applying `20261015030030_NpmRegistrationRequests20261015` to the **local development PostgreSQL** at `localhost:5432/EEMOCantilanSDS.DB`. No production, cloud, APK, or production API changes were authorized or performed.

## Root cause
The initial request from Mobile reported `Request concurrency conflict; refresh before retrying`. API logs demonstrated PostgreSQL `42P01: relation "NpmRegistrationRequests" does not exist`. This was not evidence of a duplicate stall, overlapping NPM tenancy, or competing submission. The backend now classifies this exact missing-table database error as a service/setup failure rather than a misleading 409 concurrency conflict.

## Backup
Fresh custom-format compressed PostgreSQL dump created and verified with `pg_restore --list`:

`C:\dev\stalltrack-v2-worktrees\local-db-backups\EEMOCantilanSDS.DB_pre_npm_registration_requests_20261015_224135.dump`

Length: **1,554,871 bytes**; 658 archive TOC lines.
SHA-256: `97578B8E5B1A13B5D99AE1A651864D554D1D168E58685036A4396646EF3034C8`

Backup was verified before applying the migration. It was not restored; local database history was preserved.

## Approved migration
EF Core connected migration status showed exactly **one pending migration**, `20261015030030_NpmRegistrationRequests20261015`. Applied using `dotnet ef database update` with environment overrides pointing specifically to the local PostgreSQL connection. Startup auto-migration was disabled throughout.

EF reported `Applying migration ... Done`. PostgreSQL independently confirmed:
- Migration-history row: 1
- `public."NpmRegistrationRequests"` table: present
- Rows immediately after migration: 0
- Four foreign key constraints, nine indexes (including unique operation and active request protections)
- `AK_FishMeatNpmAssociations_MunicipalityId_Id` association constraint: present
- Latest recorded migration: `20261015030030_NpmRegistrationRequests20261015`

The migration creates an administrative registration request schema, not cash, Collection, OR, SRC, rental charge, or remittance entries. No actual NPM registration request was submitted on behalf of the collector.

## Runtime
The locally updated API from the isolated branch `fix/mobile-native-records-npm-polish-20261015` is running at `http://localhost:5117` with migrations-at-startup **disabled**; authenticated access is required for the request and suggestion endpoints (unauthenticated returns HTTP 401). API health HTTP 200. Staging API on port 5118 was stopped; Head Portal, primary API and the Windows Mobile preview were not restarted or modified.

Mobile ComponentTests: 136 passed (0 failed).
Debug API build: 0 errors (existing warnings remain).
Actual authenticated request submission and Head review still require human acceptance testing. Do not infer that the source-office approval was granted or payment recorded. Existing user form details and pending/cash/OR state must be handled according to source-native financial policy.

## File safety
The primary local checkout `C:\dev\stalltrack\eemo` retains its eight protected uncommitted files unchanged. All scoped code changes were made in the isolated mobile-native-records-npm-polish worktree; no push/deployment occurred.

## Safe acceptance
Open the existing MEEDO Mobile Windows Debug preview (which points to `http://localhost:5117`). Sign in as an authorized local collector. Navigate to Fish/Meat vendor → NPM registration → choose section/stall/evidence → submit intentionally. Check that the request appears under **My NPM requests**. The request is nonfinancial: it must not issue SRC, create a collection, or count as revenue. Head review requires a verified NPM association and contract, and is not automatic.
