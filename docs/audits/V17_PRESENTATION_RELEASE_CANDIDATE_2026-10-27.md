# V17 presentation release candidate — October 27, 2026

Status: **local isolated candidate only**. No push, merge to master, production deployment, migration, APK publication, or financial rule activation was performed.

## Integration
- Worktree: `C:\dev\stalltrack\v17-presentation-release-20261027`
- Branch: `integration/v17-presentation-20261027`.
- Base: EEMO `feat/v17-ui-finance-accounts-20261025` at `b8f09b84`.
- Cherry-picked verified changes from `85e0ddcd` as `f045f535`: Dashboard selected-period recurring rent balance and precise future Fiesta/Araw event message. No backend collection eligibility changed.
- Primary EEMO checkout and Codex's dirty tenant-recovery worktree preserved; Codex changes are **not** included in this candidate.

## Collector version
- Previously published GitHub latest (verified via `gh release list`): Collector 1.1.21 / versionCode 23, October 25.
- Production `/api/mobile/version` response observed October 27: 1.1.13 / versionCode 15. **Advertisement trails latest release; must be reconciled for any publication.**
- Candidate source version: 1.1.22 / versionCode 24. Version bump is reproducible from csproj, not an override.
- A compiled local Android APK is **not** equivalent to a release-signed, published upgrade. Established `.github/workflows/publish-apk.yml` is required for release signing. It also publishes a GitHub Release and can advertise the version, so it is deliberately NOT dispatched by this candidate preparation.

## Verification evidence
- Pre-integration isolated fix branch: DashboardOverviewViewsTests 18 pass, SourceBrowserTests 16 pass, git diff --check clean.
- Integrated worktree cherry-pick completed with no conflicts.
- Android Release build / full suites / browser and real phone: verify and update status separately; never assume pass from a pending command.

## Release gates
1. Confirm actual Android Release build output, signing identity, versionName and versionCode.
2. Run component, Mobile, and applicable wider regression; verify CSS bundle integrity and authenticated browser flows.
3. Assess newer Claude/Codex commits for independent inclusion; never merge unverified work or erase dirty files.
4. Complete production backup/off-host recovery release gate; review pending migrations, ST-20 decision, online payment, and API compatibility.
5. Publish through signing workflow only with explicit authorization and compatible API deployment, then verify APK download and `/api/mobile/version` convergence.
