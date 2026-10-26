`# StallTrack release candidate — 2026-10-26 (local only)
`
`Status: **candidate, not authorized for production.** Nothing here was pushed, deployed, migrated in production or published. All live checks ran against scratch databases.
`
`EEMO branch `feat/v17-ui-finance-accounts-20261025` (46 commits over production base `7ad13d54`); Platform `integration/st05-st06-st13-20261026` (`e8db12f`).
`
`## 1. What changed (customer-visible)
`Space Rental: multi-month collection, one OR for several months (Kanmanggay, Fiesta/Araw; **not** Vegetable/Fruits), expired unrenewed terms stop accruing new months (ST-08), renewals and closed accounts (ST-01). Arrears: one row per liable account on Head and Mobile, details sheet, issue Payor code, delinquent months of legacy monthly facilities collectable from Mobile (one or many, same month picker as New Collection). Payor: online checkout is monthly Stall Rent only (ST-13), earlier accounts read-only with verified linking (ST-19). Online monthly rent on a canonical row posts exactly one SRC (ST-17). Multi-LGU: no Cantilan rates/labels on other tenants' pages (ST-16), setup-required list (ST-03), per-LGU weighing rates verified (ST-12), fines (ST-14), Collected today (ST-18). Security (ST-11): anonymous vendor registry closed, collector OR-number rewrite closed, tenant-scoped sync lookup, device-token ownership, default-tenant fallbacks removed, lost-race code redemption refused cleanly.
`
`## 2. Evidence
`| Suite | Result | Commit |
`|---|---|---|
`| Release build, whole solution | OK | `ff0c75ad` |
`| Unit | 3,197 pass | `ff0c75ad` |
`| Component | 1,491 pass | `ff0c75ad` |
`| PostgreSQL integration | 921 pass, 14 skip, 0 fail (13m24s) | `ff0c75ad` |
`| Later focused runs | space/obligation/arrears/session 276 pass + 1 new; historical access 35 pass; Mobile/Arrears unit 18+9 pass | after `ff0c75ad` |
`| Platform | payor 74, admin 164, landing 92; payor/admin/landing builds OK | `e8db12f` |
`The 14 skips are documented: 7 need `STALLTRACK_SNAPSHOT_DB`, 7 are opt-in audit runs. **The final full regression on the last commit is recorded in the backlog checkpoint** (re-run it before release if any code changes after it).
`
`## 3. Migrations (never applied outside scratch/Testcontainers)
`1. `20261026001304_AddPayorHistoricalAccess20261026` — creates `PayorHistoricalAccessCodes`, `PayorLiabilityLinks` (XOR identity check, unique code, unique link per payor+identity).
`2. `20261026071822_HardenPayorHistoricalAccessTenantKeys20261026` — same-tenant composite FKs on those two tables only (contract / obligation account / municipality / payor user). Replaces the link's single-column Users FK.
`Both are additive; no existing table is altered or rewritten. The tables are new and empty in production, so the FKs cannot fail on existing rows. `dotnet ef migrations has-pending-model-changes` is clean. Applied on the scratch database with data present: OK (157 history rows, all tables restored identically by the recovery check).
`
`## 4. Deployment order (when authorized)
`1. **Backup gate:** a recent production full-database dump + checksum + off-host copy, and a restore drill (section 6). No release without it.
`2. **Review and apply the two migrations** with the API's normal controlled migration step (never auto-migrate in production), in a maintenance window.
`3. **API** (security fixes + ST-13/17/19/21/08 behaviour + Mobile arrears endpoints). Health-check `/health`.
`4. **Head Portal** (Blazor) from the same commit.
`5. **Platform apps** (payor, admin, landing) from the integration branch. The payor app needs the API's `PayableOnline` and the ST-19 endpoints, so only after step 3.
`6. **Android APK** through the CI workflow `publish-apk.yml` (manual, signed with repository secrets); publish only after step 3 is live. The Mobile app needs `api/mobile/collection-session/arrears`, `api/mobile/monthly/unpaid-months` and the optional billing month on the monthly record.
`**Rollback:** the previous API/Head/Platform builds keep working with the additive migrations (new tables are simply unused). Restore from the step-1 backup only for data faults. Keep the previous APK available; older Mobile builds do not call the new endpoints.
`
`## 5. Behaviour changes to announce
`- Space arrears for expired, unrenewed signed terms fall (no new months after the term's last day; earlier months stay collectible).
`- Payor online checkout no longer covers NPM daily charges, utilities, fish or space rental; those balances stay visible read-only.
`- Mobile Arrears: monthly-facility stalls route to "Collect unpaid months" (legacy monthly records) instead of New Collection.
`- Vegetable/Fruits accounts cannot take an OR number in New Collection (no OR entry for that operation yet); SRC-only works.
`
`## 6. Production recovery strategy (release gate)
`**What exists.** Production is on AWS Lightsail. The host runs `stalltrack-db-backup.timer` (daily PostgreSQL 18 custom-format dump) and `stalltrack-db-restore-drill.timer` (isolated restore). The GitHub workflows `backup.yml`, `restore.yml`, `backup-freshness.yml` deliberately **fail closed**: they never claim a backup exists and never restore into production. `infra/aws/RESTRICTED_DEPLOY_RUNNER.md` already requires, before any deploy: a recent pg_dump archive, checksum, an independent off-server copy and an isolated PostgreSQL 18 restore drill. "Timer enabled" alone is not proof.
`**Why the tenant backup still refuses.** `TenantRestoreSchema.RequireRestorable()` throws because 12+ legacy tables (Kanmanggay*, ConfigurableService*, EcfApprovedAssessments, FishMeatVendorFee*, VegetableSpaceRental*, LandingBerthingActivities…) hold historical financial data that a tenant-scoped snapshot cannot restore. That guard is correct and stays: it must not be bypassed, and no table may be marked restorable to make it pass. The recovery path for these tables is the **full-database dump**, which contains them.
`**Verified here (scratch only).** `scripts/verify-db-recovery.sh` dumps a database (custom format), records the SHA-256, restores into a new isolated database, compares the row count of **every** public table (106) and the migration history (157 = 157), then drops the temporary database. Result: PASS. Earlier manual run matched Collections, CollectionLines, CollectionAllocations, PaymentRecords, Users, Payors, AccountableDocuments and the legacy tables.
`**Recommended policy (needs the operator's approval; no production configuration was changed):**
`- Schedule: daily full dump (keep the existing timer), plus a dump immediately before every release/migration.
`- Retention: 7 daily, 4 weekly, 6 monthly; never delete the pre-release dump until the release is accepted.
`- Protection: dumps encrypted at rest and in transit, off-host copy in a separate account/region, read access limited to the operator role, a separate credential for restore, checksum stored beside each file.
`- Evidence and alerting: record dump size, checksum, age and the last drill result; alert when the newest verified dump is older than 26 hours or the last restore drill failed or is older than 8 days. The GitHub freshness workflow needs authenticated access to that telemetry before it can report green.
`- Drills: weekly isolated restore with \`scripts/verify-db-recovery.sh\`-style row-count comparison, and one production-size drill before this release.
`**Not verified:** the live AWS host (timers, dump age, off-host copy, drill history). Run the checks in \`RESTRICTED_DEPLOY_RUNNER.md\` section E on the host before approving.
`
`## 7. Android
`Debug APK builds (`EEMOCantilanSDS.Mobile/bin/Debug/net10.0-android/site.stalltrack.collector-Signed.apk`, debug-keystore signed). Release signing is done only by `publish-apk.yml` with `ANDROID_KEYSTORE_*` secrets and `GOOGLE_SERVICES_JSON_BASE64`; versionCode must increase. A `stalltrack-release.keystore` exists in the working tree root: it is git-ignored and untracked (not in history); it was not used. **No release-signed APK was built and no device install was possible here.**
`
`## 8. ST-20 (monthly Stall Rent SRC coverage) — prepared, pending authorization
`Mobile posts an SRC for monthly rent only when the `PaymentRecord` is `Canonical`; legacy rows stay legacy until their controlled row cutover (`SettlementCutoversController`, SuperAdmin/Admin readiness endpoint). Procedure when authorized, per tenant and facility (ICE, BBQ, TCC, NCC, custom Stall Rent): (1) full backup + drill; (2) run the readiness check and review its report (no mixed legacy components, no open reconciliation); (3) activate in a maintenance window for the chosen month range only (prospective), never historical rows; (4) verify one collection posts one SRC and Collection Activity, Monthly Income and Financial Summary agree; (5) stop on any reconciliation-required. No cutover, backfill or fabricated SRC was run.
`
`## 9. Open items before production
`Live PayMongo test-mode webhook; browser checks of the Head issue-code, Payor Earlier accounts and activation; release-signed APK and a device install; production-host backup evidence; ST-20 authorization; extend OR entry to Vegetable/Fruits (ruling); ST-10 minor Audit Trail polish; ST-15 smoke on a device.
`
`## 10. Presentation path (scratch data)
`Platform onboarding → activation → Head sign-in (Carrascal and Cantilan) → space/stall setup → collector assignment → Mobile New Collection (stall rent; fine partial; 3 months with one OR) → SRC/OR → Collection Activity = Monthly Income = Financial Summary → Arrears (Head = Mobile; collect unpaid months) → Payor portal (monthly rent only; earlier accounts read-only).
