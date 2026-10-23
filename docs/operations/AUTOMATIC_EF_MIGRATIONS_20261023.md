# Automatic EF migration release path — StallTrack V13

## Goal
The approved private AWS release gate runs a **separate one-shot EF migration process** before replacing production API/Head images. No routine manual SQL is required. The container uses the exact immutable API image already verified and published by CI.

## Runtime interface
The normal API container is unchanged. For a one-off Compose container only, set the environment variable `STALLTRACK_MIGRATION_MODE` to `check`, `apply`, or `verify`. The entry point detects this mode **before constructing the web host**, so PayMongo, authentication, background jobs, seeders and HTTP listeners are not started. Only an `AppDbContext` is constructed, using the explicitly required `ConnectionStrings__DefaultConnection` environment variable. A missing connection string fails before any database access. Do not pass credentials via command-line arguments.

- `check`: reject unknown/newer EF history and print one `STALLTRACK_MIGRATION_PENDING_IDS=<comma-separated IDs>` marker.
- `apply`: obtain the existing PostgreSQL migration advisory lock, apply pending EF migrations once, recheck, and print `STALLTRACK_MIGRATION_APPLIED_IDS=...`. No business seeders.
- `verify`: reject unknown or pending EF migrations, emit an empty pending marker.

## Host orchestration
The private `stalltrack-stack-deploy.py` only accepts the approved source SHA and verified immutable API/Head digests. It pulls images and runs temporary Compose containers using an ephemeral root-private overlay (no modification to the live overlay before image cutover). No database/Caddy service is recreated.

When `check` returns no pending migrations, proceed with existing release checks. Otherwise, the gate only accepts migration identifiers explicitly reviewed in its private additive allowlist. It checks the established backup/restore-drill prerequisites, triggers a fresh backup service, verifies its checksum and timestamp, applies the pending migration and checks that the protected service identities and old API readiness remain intact. Any error aborts promotion. A later API/Head failure restores **images only**, never rewinds financial data or schema.

## Release verification
- Local .NET Release API compilation passed.
- Linux private-gate unit suite (33 tests, one pre-existing skip) passed.
- Disposable PostgreSQL 18: migrated through the preceding baseline, `check` found exactly `20261023095152_AddIndependentWeightMeasureRateVersions`, `apply` applied it, `verify` found no pending version, repeated `apply` was idempotent, and new table had eight columns.
- Schema use on live production, production host gate installation, and authenticated browser E2E are **not** claimed as verified until executed on host.

## Production hold
`AWS_DEPLOY_ENABLED=false` must stay in place until the reviewed root-owned gate is installed on the Lightsail host and its current backup/restore checks pass. The existing on-host binary is protected; a repository commit alone does not update it. The one-time gate installation through an authorized host session is separate from the routine no-manual-SQL release automation.

New incompatible, destructive, or financially material schema changes must **not** be added to the automatically approved migration set. Review the next change before allowing the private gate to apply it.
