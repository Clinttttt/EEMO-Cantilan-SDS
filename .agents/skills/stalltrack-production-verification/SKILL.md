---
name: stalltrack-production-verification
description: Verify a StallTrack production deployment, release candidate, or reported production version using GitHub Actions, Azure App Service container state, public health/login endpoints, and the scoped CSS bundle. Verification is read-only unless the user explicitly authorizes a deployment or repair.
---

# StallTrack production verification

Establish whether the intended commit is actually running and healthy without changing production state.

## Authorization boundary

Read-only verification is allowed when requested. Do not push, dispatch workflows, create releases, restart applications,
change Azure settings, roll back, or redeploy unless the current user request explicitly authorizes that mutation. A request
to verify is not authorization to repair. Never print credentials, tokens or secret configuration values.

## Verification workflow

1. Identify the intended full commit SHA, branch and change scope. Check the current repository status first. A
   documentation-only commit covered by deployment `paths-ignore` should not be expected to create a production run.
2. Inspect CI and `Deploy production` runs for that SHA. Confirm all three test suites passed in the applicable gate. If a
   migration changed, confirm the fresh-backup gate succeeded before deployment.
3. Confirm both production applications reference images tagged with the intended full SHA:
   - API app/container from `.github/workflows/deploy-production.yml`;
   - portal app/container from the same workflow.
   A successful workflow alone is not proof that both applications now run the intended image.
4. Check the API health endpoint and portal login endpoint from the workflow configuration. Use bounded retries while an
   already-authorized deployment is settling; stop and report after the workflow's expected readiness window rather than
   waiting indefinitely.
5. Fetch the deployed portal's scoped CSS asset and verify balanced braces. A successful build and `/health` response do
   not catch a corrupted scoped CSS bundle.
6. When the task concerns a specific behaviour, perform only the non-mutating production observation needed to verify it.
   Do not use or alter real financial records as a smoke test.

## Result

Report the intended SHA, CI/deploy conclusions, API and portal image tags, endpoint results, CSS result, observation time,
and any check that could not be completed. Distinguish "workflow passed" from "verified running in production". If any
source disagrees, stop at the contradiction and recommend the smallest next diagnostic action; do not mutate production
implicitly.
