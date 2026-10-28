# V17 FCM production activation execution — 2026-10-27

This is task-specific execution evidence, not a new business ruling or a replacement for the protected AWS release procedure.

## Isolation and source

- Isolated checkout: `C:/dev/stalltrack/eemo/.worktrees/fcm-production-20261027`.
- Branch: `feat/v17-fcm-production-activation-20261027`.
- Created from verified `origin/master` `1a13ecb359dd037e89c0780eedf8a3cc3c7f2bce`; CI `40389906635` and image publisher `40391006153` succeeded.
- Reused PR #65 commit `a1d660ff4be69568999afb62075d8f3b56f89ec6` by fast-forward. No duplicate notification implementation.
- PR #65 subsequently merged as `12b68664d1ce1c72255884e9554ca70677659eb9`; its CI and publisher passed. This task's branch fast-forwarded to that merge.
- No changes to the Backup & Restore session's checkout, branch, builds, or temporary files.

## Reviewed boundaries

Head Portal messages call the role-protected Notifications controller, then `IPushSender`. Device registration derives collector and municipality from authenticated context. Token lookup and removal are tenant-filtered; removal also matches collector ownership. Cross-municipality token takeover is refused.

PayMongo signature verification pins the authorized merchant municipality before settlement. A settled transaction is a no-op on retry. Monthly rent financial writes commit before notification. PR #65 separates cache failure from the subsequent notification attempt. Collector recipients come from the paid stall's tenant-filtered facility and active facility assignments; the FCM device lookup is tenant-filtered again. No gateway signature, amount, source authority, payment, OR/SRC, or assignment semantics were changed.

The Android service, token bridge, notification channel and Android 13 permission path already exist. These are implementation evidence, not proof of handset receipt. Push remains best effort without durable retries.

## Local verification

- Focused settlement, webhook, token-registration/removal and push routing: **38 passed**.
- Reintroduced the cache-failure defect in the isolated checkout: `CommittedMonthlyPayment_PushStillRunsWhenCacheInvalidationFails_AndRetryDoesNotReNotify` failed with `Unavailable cache`. Restored PR #65 exactly: **38 passed** again.
- Full unit suite: **3,272 passed**, no skips/failures.
- Head Portal Client Release build: **passed**, 96 warnings, zero errors. API compiled with the unit/integration projects.
- PostgreSQL/Testcontainers device isolation plus new actual recipient-query regression: **4 passed**, no skips/failures. Includes two municipalities, inactive/unassigned collectors, another facility and unresolved tenancy. Disposable database only.
- Linux credential-install configuration safety checks: **6 passed** in a network-disabled Python container with read-only source mount. Wrapper PowerShell parse check passed.
- Full component and full integration suites were not rerun locally; PR #65's GitHub CI passed.

## Credential procedure and observed production

The previous activation drafts were inspected but not executed. Their weaknesses included a fixed workstation IP, fixed temporary paths for resolved secret-bearing Compose data, and no reuse of the release gate's current-master approval.

The corrected task scripts are under `tools/diagnostics/`:

- `fcm-production-preflight.ps1`: defaults to read-only inspection. Uses temporary Lightsail SSH credentials, a workstation-only rule, strict existing host-key verification, and cleanup. `-ConfigureFirebase` explicitly selects installation.
- `configure-fcm-production.py`: exact source/image and installed-gate checksum pins; existing deployment lock; installed release approval, backup and readiness checks. Reads the Admin credential only from encrypted SSH stdin. Accepts only the exact observed API-base checksum and legacy ownership, makes the updated file root-private, and compares complete effective Compose configurations in memory, allowing only the Firebase setting and read-only mount. Recreates only API; verifies protected containers and unchanged images. Restores original configuration/metadata on failure. Does not run migrations or financial SQL.
- `test_configure_fcm_production.py`: tests exact config delta, immutable image/DB/other-secret preservation, read-only mount, disabled startup migrations, and fail-closed handling of pre-existing or unknown structures.

Installed secret: `/etc/stalltrack-firebase/collector-admin.json`, root-owned, group `1654`, mode `0440`, directory mode `0750`. Container mount: `/run/secrets/stalltrack_admin.json:ro`; setting: `Firebase__ServiceAccountPath`. The existing workstation credential's structure and project match were validated without displaying its contents. No credential is committed or passed as a command argument.

Initial production read-only observation:

- API digest `sha256:c77bca5bd1095159b656def46b9985fab90ec2a4a10443827ab2719e4d8877f1` and portal digest `sha256:f504dc67fe7a89b01d59ca746e919a1fd3bf24b5d594f2f749cae7747324aece` matched original V17.
- API user/group `1654`; startup migrations `false`; Firebase secret and settings absent; readiness and portal login passed.
- Root gate enabled, approval mode `current-master`, rollback proven. Installed stack/base helper checksums were recorded and pinned in the installer.
- Temporary SSH firewall rules were removed after probes. Two temporary key cleanup ACL errors were corrected by granting direct file delete rights; both exact directories were deleted. Subsequent cleanup was corrected in the wrapper.
- First activation attempt stopped at expired AWS login before transfer. After reauthentication, the release gate refused the old source pin because master advanced to merged PR #65; no secret installation occurred.

## Release and delivery status

Protected private workflow `40447371421` was dispatched for merged PR #65 through `promote-client.yml`, action `promote`, exact source SHA `12b68664d1ce1c72255884e9554ca70677659eb9`. It retains the established CI/image, backup, migration, rollback and release-ledger controls.

Expected new digests:

- API: `sha256:8e0c407e07d6fb46b1009a5c4d83db84c5099abfa65eb33affe5b3b4029ea691`.
- Portal: `sha256:e13d873bd7564d735bf518da065148dbca8a3079a48bd915d1765a1a90d51e02`.

Protected rollout completed successfully: `verify`, `deploy` and `record` all passed. Live API and portal digests match the new pair above.

Firebase installation then completed successfully through the corrected operator procedure. The existing API base was verified as checksum `b9a867e01491e7ecb3606eb727514dd9ffd7df3fa4cb7ddcfe8cd2fa9df38737`, UID/GID `1000:1000`, mode `0664`. Its updated version is root-owned `0600`, checksum `2ce6dfe57d79701ad1be52d1ffb7dba7353fcbd0fcaa1ccc1db6b702535376e7`. Original bytes are preserved root-private at `/home/ubuntu/stalltrack-demo/config/compose.v10-clean-db.before-fcm-activation.yaml`.

Post-activation observations:

- API container `be85b60ebd0b6cb8e6ee22a37b6e82c6a04e0b9743bf237a23eaa5c9f7e4ab6a` serves the expected PR #65 digest.
- Portal container `e2601218d832a552c9ff6ba0bac7bed6d52e317216747c00673439090b9153bf` was unchanged by the credential operation.
- PostgreSQL container `c146b2e50f09ead57f82bbf826e3975c0d6055b7933a8c54fced8bc8efeafb9d` and Caddy container `c710c56d2b7cec34febfaffc3690e29fffe8a6ea97e2bd397f4c1f989eff1601` retained their original identities through release and activation.
- Secret metadata `0:1654`, mode `0440`; live mount `writable=false`; application UID can read the nonempty file; startup migrations remain `false`.
- API readiness and portal login pass. Deployed scoped CSS has **13,896 opening and closing braces**.
- Anonymous Head-send and device-registration requests both return **401**. No financial request was used for these checks.
- Read-only SQL against explicitly named production database `stalltrack_v10_presentation_clean` confirms `collector11` active with **9** token rows. An earlier probe using `POSTGRES_DB` returned no matching row; that database was not used as evidence of production registration.
- Initial sanitized post-activation FCM log counts show zero disabled/init/send/collector-push errors. No successful send is inferred from lack of errors.
- Temporary SSH rules removed with original CIDRs preserved; task temporary credential directories removed.

## Demonstrated Android delivery

The user performed the authorized Head Portal send to `collector11` and confirmed the notification arrived on Android: "its working now it notify". For the follow-up test the portal displayed **"Sent to 2 device(s)."** The user explained two mobile installations were signed in and confirmed receipt. When explicitly asked whether the message arrived after pressing Home, the user answered **"Yes, received in the background"**.

This demonstrates production API FCM acceptance for two registered installations and user-observed Android reception, including background reception. The API count represents FCM acceptance, not an SDK delivery receipt; actual device evidence is the user's handset observation. Browser automation failed to initialize in this tool session, so the authenticated Head Portal action was performed by the user, not automation. No screenshot, ADB trace or independent device telemetry was collected.

## Exact changed files and review

Reused and deployed PR #65 (`a1d660ff`, merged as `12b68664`):

- `EEMOCantilanSDS.Application/Common/Payments/OnlinePaymentSettlementService.cs`.
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Collector.razor`.
- `EEMOCantilanSDS.Testing/Api/CollectorOnlinePaymentPushTests.cs`.
- `EEMOCantilanSDS.Testing/Application/OnlinePayments/OnlinePaymentSettlementServiceTests.cs`.
- `docs/audits/V17_FCM_ONLINE_STALL_RENT_PUSH_2026-10-27.md`.

New in this task, draft [PR #66](https://github.com/Clinttttt/EEMO-Cantilan-SDS/pull/66), initial commit `71c6ad0e88667169a4ff76c71381034587084913`:

- `EEMOCantilanSDS.IntegrationTests/CollectorPushRecipientIsolationTests.cs`.
- `tools/diagnostics/configure-fcm-production.py`.
- `tools/diagnostics/fcm-production-preflight.ps1`.
- `tools/diagnostics/test_configure_fcm_production.py`.
- `docs/audits/V17_FCM_ACTIVATION_EXECUTION_2026-10-27.md` (this report).

Draft PR #66's GitHub CI run `40448477790` was still running at this report update. No merge or deployment of that operational/test/evidence PR is claimed. PR #65 is the deployed application change.

Production-only changes are the secret directory/file, the two allowed API Compose settings plus root-private ownership, the protected original-base backup, existing release/audit state records, and the controlled API recreation. Original Compose sequence, protected gate executables/control settings, startup migration policy, database and Caddy were preserved. Temporary firewall access was removed.

## Remaining unverified

- No genuine PayMongo payment occurred during this activation, so live payment-triggered handset delivery is **not demonstrated**. Signed webhook merchant/tenant boundaries, post-commit behavior, duplicate no-op behavior and facility recipients were reviewed/tested; a future legitimate payment can supply live end-to-end evidence without altering history or replaying a settled payment.
- Force-stopped/offline devices, denied Android permission, expired/revoked FCM tokens and external FCM failure were not induced on production. Live anonymous send/registration rejection and PostgreSQL tenant/facility negative cases were verified.
- Push remains best effort; there is no durable outbox or guaranteed retry for a failed post-commit notification. A push fault must not roll back a payment.

No fabricated production payment, webhook replay, financial-history edit, OR/SRC edit, source cutover, tenant assignment edit or Android release was performed.
