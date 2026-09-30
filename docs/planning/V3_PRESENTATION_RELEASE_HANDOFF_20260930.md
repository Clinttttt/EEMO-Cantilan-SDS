# StallTrack V3 presentation release handoff — 2026-09-30

## Release base

- Old production master: `396b7ee5f13c9887b896d8c6cb3380e9945d4679`.
- Backend ancestry: `2d42a39a` and `354a6149` were verified ancestors of `interface-v3/claude-ui`; neither backend branch was integrated twice.

## Release candidate

- Branch: `release/v3-presentation`.
- Candidate commit: `e1c306f84947964e46815f64d27eb1d5371e6a51`.
- Candidate worktree was clean and fast-forwarded from old master.
- Claude's four uncommitted CSS files were preserved in commit `6cbe3c9f`.
- TPM now shows the confirmed Official Receipt policy; the terminology test asserts current behavior in `e1c306f8`.

## Integrated

- V2 clean-adoption foundation and canonical itemized Collection foundation.
- WCF source/cutover protections and Web corrections.
- NPM Meat weighing evidence and Collection source-part support.
- Collector operation assignments and operation-only Collector support.
- Contextual Tabo/Vegetable instrument policy.
- Canonical Monthly Income reader foundation.
- Mobile operation-capability query and WCF capability gating.
- Weight & Measure shadow comparison and V3 presentation.
- Operations directory and Accountable Forms UI.
- Truthful workspaces for operations without a released writer.

## Validation

- Unit: **2,277 passed, 0 failed**.
- Component: **590 passed, 0 failed**. TPM test now checks Vendor terminology and the confirmed Tabo OR policy.
- Integration: **113 passed, 0 failed, 7 skipped**. The skipped cases require an explicitly configured restored production snapshot. Testcontainers PostgreSQL applied the migration chain.
- API Release build: passed, 0 warnings, 0 errors.
- Client Release build: passed, 0 warnings, 0 errors.
- Local Android Release build: passed, 50 warnings, 0 errors. No APK was published.
- EF pending model check: no pending changes.
- `git diff --check`: passed on the clean release worktree.
- Client scoped CSS: 111 files checked; braces balanced.
- Migrations: seven new post-master migrations through `AddContextualRevenueInstrumentPolicy`; no existing migration deleted or edited; snapshot current.

## PR and GitHub checks

- PR: [#20 — StallTrack V3 Web, Revenue Foundation, and Operations Release](https://github.com/Clinttttt/EEMO-Cantilan-SDS/pull/20).
- PR state: merged to `master` on 2026-09-30.
- Merge commit / new production master: `5489c527f7de43e107d342d1a7dbfb392b0a964a`.
- PR CI run `36670328795`: success. API/Client builds, unit, component, and Testcontainers integration steps passed.
- Master CI run `36670656773`: success.

## Production

- Deployment workflow: [run 36670656838](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/36670656838), success.
- Backup/schema gate: successful; fresh backup workflow [run 36670818762](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/36670818762) completed successfully before deployment.
- API image build/push, API deploy, and API health step: success.
- Portal image build/push, portal deploy, and portal health step: success.
- Direct public read-only checks after deployment: API `/health` HTTP 200; portal `/login` HTTP 200.
- No manual production migration or SQL was run; the deployment pipeline owns schema application.

## Production smoke test

- Verified deployment workflow success, API health, and portal login availability.
- Authenticated UI smoke checks remain unverified. The Windows Computer Use bridge could not connect to the native pipe, and the web inspection tool could not access the portal URL. As a result, this session could not inspect login/logout, tenant branding, navigation, facility rows, historical records, financial accounts, or authenticated screens including Overview, Operations, NPM, TCC/NCC/BBQ/ICE, Collection Activity, Online Payments, Payors & Accounts, Monitoring, Reports, Collectors, Accountable Forms, Audit Trail, Settings, WCF, Weight & Measure, and truthful operation workspaces.
- Do not treat the HTTP 200 checks as evidence for those authenticated flows.

## Deferred capabilities

- Market Fees production writer.
- Vegetable/Fruit writer.
- Landing/Berthing writer.
- Transfer Large Cattle writer.
- Fish/Meat Vendor Fee source.
- Weight & Measure production cutover.
- New production Collector APK.

No force push, APK publication, unapproved source cutover, historical backfill, or ad-hoc production SQL was performed.

## Cloud continuation

- Branch: `interface-v3/mobile-v3`.
- Base and starting commit: new production master `5489c527f7de43e107d342d1a7dbfb392b0a964a`.
- Cloud handoff: `docs/planning/CLOUD_MOBILE_V3_HANDOFF_20260930.md`.
- Mobile V3 implementation has not started.
