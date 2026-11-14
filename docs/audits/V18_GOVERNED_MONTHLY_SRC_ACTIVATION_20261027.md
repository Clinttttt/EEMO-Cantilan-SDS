# V18 Governed Monthly SRC Activation — Engineering Checkpoint
Date: 2026-10-27 (Asia/Manila)
Branch: `feat/v18-monthly-src-activation-ui-20261027`
Worktree: `C:\dev\stalltrack\v18-monthly-src-activation-ui-20261027`
Base: `origin/master` at `5a3103171a8c9c3a7c6b08aa42aaaea3753b0ebf`

## Goal and boundary
User requests genuine SRC issuance as the default for new eligible monthly-rent collections without touching previously paid legacy payments. Actual code has two authorities: legacy PaymentRecord status/physical OR and canonical Collection with generated SRC. The ST-20 server activation workflow is already present and posts zero money; after activation the normal Head/Mobile/online canonical writer issues SRCs. Auto-switching all rows would ignore offline collector queues, in-flight payments, historical settlement evidence, recovery readiness and legal receipt custody. **No implicit cutover was made.**

## Implemented
- Tenant-scoped Head settings page `/settings/monthly-rent-src` with active configured monthly-rent facility selector, billing year/month, read-only plan listing eligible and excluded rows, and server-provided blockers.
- Newly exposed `AffectedCollectorIds` in the plan comes from same-tenant facility assignments, is optional for compatibility, and is independently guarded by server cutover readiness per source.
- User-attested checklist includes legacy writer stoppage, drained offline queues, complete field-device inventory, drained online payments, OR/CT evidence, reconciled reports and **independently restorable system backup/restore drill** (not limited archive-v1). Recorded evidence reference required.
- Each assigned collector requires a manually verified Mobile version and queue-drain confirmation; source authorizations remain server-enforced. All evidence inputs freeze after server approves a plan.
- Expected plan fingerprint and reviewed evidence are reused exactly for activation. Separate explicit `ACTIVATE` confirmation, disabled by default. Stale or changed evidence is blocked; server rechecks current state in transactions and refuses conflicts.
- New authenticated Head Portal HTTP client wraps the existing `POST /api/rent-activations/plan` and `POST /api/rent-activations` endpoints. No new database migration, source rule or payment writer.
- Head Settings links to this page.

## Verified so far
- Client Release build, no new errors (focused incremental rerun succeeded with 0 errors/0 warnings).
- New focused component suite: **5 passed / 0 failed**. Covers permission metadata, no automatic write, no activation for already-paid legacy row, activation disabled without verified evidence, and clearing all scope-specific attestations when the facility/month changes.
- Disposable PostgreSQL 18 ST-20 integration: **11 passed / 1 intentionally skipped / 0 failed**, including genuine SRC creation from both Head and Mobile, stale legacy payload preservation, authorization and tenant isolation. A first run skipped all PostgreSQL tests because Docker Desktop was stopped; Docker was restarted and tests were rerun successfully.
- Additional integration assertion that affected collector IDs match the actual assigned facility collector and do not leak across tenants: focused PostgreSQL rerun **2 passed / 0 failed**.
- Git diff review shows scoped UI/client contract/read-model/test only, no financial records changed.

## Known remaining gates
- Do not deploy or activate Cantilan until latest focused assertion rerun, full CI, reviewed current source-specific safety evidence, and authenticated Head visual acceptance.
- A verified site backup/download of limited archive-v1 does **not** establish complete recoverability. Retained 13 unmanaged legacy financial tables remain a known recovery constraint; ST-20 readiness may refuse production activation until an independent full-database restore drill has passed.
- This change is a governed operator tool, **not universal monthly auto-activation**. Already-paid historical payment rows do not receive retroactive SRCs.
- No production activation, financial record mutation, migration, Android APK, deployment or backfill was performed by the feature work.

## Next action
Review final staged diff, commit this isolated feature and open a draft PR with CI. Do not merge/deploy or flip any production authority until release and financial review complete.
