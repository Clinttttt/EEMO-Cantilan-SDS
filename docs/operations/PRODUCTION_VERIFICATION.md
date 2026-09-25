# StallTrack Production Verification

**Status:** Canonical release-verification checklist.

A successful GitHub Actions run is necessary but does not by itself prove the intended production revision is serving traffic.

## Standard Web/API release

After a production deployment:

1. Verify the deployed container/image tag matches the intended master HEAD.
2. Verify the API health endpoint returns HTTP 200.
3. Verify the portal login route returns HTTP 200.
4. Verify the deployed scoped CSS bundle is brace-balanced.
5. Confirm no migration/schema failure occurred during API startup.
6. For a schema-changing release, confirm the deployment backup gate completed before the migration-bearing deployment.
7. Review application logs for startup/auth/tenant errors introduced by the release.
8. Verify the bounded user-facing behavior that motivated the release; do not rely only on health checks.

## Database changes

Production applies migrations at startup.

Migrations must be additive. A migration-bearing deployment requires the release workflow's fresh-backup gate and explicit review of the migration diff.

Do not perform destructive DDL as an ordinary application deployment.

## Authentication / tenancy releases

Additionally verify:

- login/refresh still succeeds for the affected role;
- tenant-specific data remains scoped;
- no role was broadened by route/navigation changes;
- protected endpoints continue to reject unauthorized access.

## Mobile release

Collector changes require a signed RELEASE APK before collectors can receive them.

Application/display version, publish workflow values, and API-advertised latest version must describe the same build.

Publishing Web/API does not automatically update installed collector apps.

## Evidence

Record the exact commit SHA, workflow/run result, health checks, and any production-only verification performed.

For the repeatable operational workflow, also use .agents/skills/stalltrack-production-verification/SKILL.md.
