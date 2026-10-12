# Onboarding V3 — tenant operation configuration, Head Portal integration, account lifecycle

Written 2026-10-12. Builds on `ONBOARDING_V2_TENANT_OPERATIONS_HANDOFF.md` (which stated that local names were *not yet* available in
the Head Portal — **that limitation is now closed**; read that document for the V2 facility-mapping and draft-compatibility rules,
which still stand). Nothing here is pushed, merged, deployed or applied to a production database.

## Branches

| Repo | Branch | Base |
|---|---|---|
| `platform` | `feat/onboarding-tenant-operations-v2` | `main` @ `db94c2e` |
| `eemo` (worktree `C:\dev\stalltrack-v2-worktrees\onboarding-ops`) | `codex/onboarding-initial-operations-v2` | `origin/master` @ `63a70e86` |

The ECF/WCF branch (`codex/utility-workspace-ecf-wcf`) was still **local-only** (not on `origin/master`) when this started and was never
read-modified, staged or merged by this work.

## Verified root causes

| Defect | Root cause (verified in code and in a browser) |
|---|---|
| Carrascal saw Landing / Berthing it never chose | `Operations.razor` rendered static `IncomeFromMarketServices` / `SpaceRentalServices` lists and an unconditional Terminal row; only facility-backed rows were data-driven. Nothing persisted what an LGU selected. |
| Local names never reached the Head Portal | The declaration lived only in `OnboardingDraft.ConfigJson`; `ActivateMunicipalityCommand` did not carry it; no tenant store existed; names were literals in Razor. |
| Cantilan Head forced to change a password they just chose | `CreateFirstAdminCommandHandler` used `AdminUser.Create`'s default `mustChangePassword: true`. (`SetAdminPasswordByToken` and the operator setup already cleared it.) |
| Local Platform Admin produced production Head links | `activation.ts` built `https://console.stalltrack.site/activate/{token}` itself. |
| Head-created Admin relied on a shared temporary password | No invitation path existed. |
| Three smoke defects | Locked-block notice wrongly promised saves; discarding unreadable entries also wiped block-level fields; monthly-rental rows showed an empty "Base rate ₱ per month". |

## What was built

### Tenant operation configuration (additive; one table)

`TenantOperationSettings(Id, MunicipalityId, OperationCode ≤64, IsListed, LocalDisplayName ≤80, Origin, Version, audit…)`,
**unique `(MunicipalityId, OperationCode)`**, tenant query filter, `Version` is an EF concurrency token. Migration
`20261012074910_TenantOperationSettings20261012` is one `CreateTable` + one unique index — no data change, no seeding, nothing touched.

* **Stable identity.** `TenantOperationCatalog` (Domain) is the single server catalog: 11 independent operations with `Code`, neutral
  `DefaultLabel`, official `GroupKey` (`MARKET`/`SPACE`, from `OfficialMonthlyIncomeStructure`), the `ClassificationCode` used only for the
  "has recorded activity" hint, and `LegacyDirectoryLabel`. A code is never derived from a label. Facility-owned lines (NPM/TCC/NCC/BBQ/ICE rent,
  Tabo, Ice Plant, Terminal, Slaughterhouse) stay facility-driven — not duplicated here.
* **Three states, never conflated.** *Never asked / not adopted* = **no rows** → legacy fallback (everything listed, established names).
  *Chose none* = rows for all 11 with `IsListed=false`. *Selected* = a listed row, optionally with the LGU's own name.
* **Activation seeds it, atomically.** `ActivateMunicipalityCommand.OperationDeclarations` (`null` = never asked → no rows; `[]` = chose none →
  11 unlisted rows). Written in the same `SaveChanges` as the facilities, rates and Head. Validated like a Head edit (known code, once, no
  impersonating/duplicate selected names). The platform console sends it only when the saved draft carried a readable block; an unreadable
  block is a **blocking error**, never sent as an empty list.
* **Existing municipalities.** Cantilan and every previously activated LGU have **no rows** and are read with the documented legacy fallback.
  Nothing is rewritten. The first Head edit **adopts explicitly**: rows for all 11, listed, each recording its established name as the label
  (only where it differs from the neutral one), so editing one operation can never hide or rename another (browser-verified: Cantilan 21 lines →
  20 after hiding Landing / Berthing, all names unchanged).
* **API.** `GET /api/operation-settings` (Head/Admin) and `PUT /api/operation-settings/{code}` (Head only). The municipality comes from the
  token, never the request. Edits are refused as `409` on a stale `Version`; a name that reads as another operation's standard name or repeats a
  listed one is `400`.
* **Listing ≠ authorization.** Hiding an operation removes nothing, changes no Collector assignment and gates nothing server-side; financial
  authorization remains the existing classification / source / assignment / role checks. Direct URLs of an unlisted operation still resolve.
  `HasRecordedActivity` is shown so a Head hiding an operation with Collections is told nothing is removed.

### Head Portal

* `Operations.razor` (small, merge-friendly diff): lists what the office chose, under its own names; facility-backed lines (Tabo, Ice Plant,
  Terminal) only with the facility; group counts and empty groups follow what is shown; the legacy path is unchanged. While configuration is
  unknown or failed, **no independent line is guessed** (error + retry). Instruments (OR/CT) and routes come from the line, never the label.
* `/settings/operations` (Head only, linked from Settings): list/unlist, local name, "Use standard name", server messages shown verbatim,
  explicit that this is presentation only.

### Account lifecycle

* **First-run Cantilan Head** is created with `MustChangePassword=false` (they chose it). Not applied to any other account; no data correction.
* **Head activation link** comes from the API (`ActivationResultDto.ActivationUrl`, from `ACTIVATION_LINK_BASE`); the console never rebuilds a
  host. An API that returns none produces an explicit message, not a guessed link. Production behaviour is unchanged where the env var is unset.
* **Admin invitations** (Head only; reuses the Head activation primitives): `InviteAdmin` creates the account **inactive** with a random
  password nobody is told; the one-time link is stored only as a SHA-256 hash, single-use, 7-day expiry, tenant-bound. `Resend` replaces the
  hash (old link dies); `Revoke` clears it and soft-deletes the pending account (frees username/email). The temporary-password route remains an
  explicit fallback and **still forces a change at first sign-in**; `MustChangePasswordMiddleware` is untouched.
* **Completed activation** appears in Activation history immediately, with an explanation of the queue → history move.

### Onboarding workspace (Landing)

Head and branding first; one **Revenue Operations** section under the five official groups (Income From Market; Rent Income — Stall Rental;
Space Rental; Income From Terminal; Income From Slaughterhouse). Facility cards render *inside* their group via an `ng-template` (the same card,
same logic); independent operations are checkboxes in their group with an optional local name; an office's own facility sits under rent with a
note that its place on the statement is still awaiting an approved grouping. Draft compatibility rules from V2 are unchanged.

## Compatibility, migration and deployment

* **Fresh database:** migrated to 78 tables on a disposable PostgreSQL 16 and exercised end to end.
* **Upgrade path:** additive table only; an existing tenant has no rows and behaves exactly as before (integration-tested and browser-verified
  with a legacy tenant).
* **Order:** deploy EEMO (API + Head Portal + migration) **before** the platform apps; the platform now sends `operationDeclarations`, which an
  older API would ignore (tenant then stays legacy) — safe but not useful. No rollback needed for the platform; to roll back EEMO the table can be
  dropped (`Down` does) with no data dependency.
* **Backups:** `TenantOperationSettings` is in the scoped export/restore set (`TenantDataTables`, `TenantExportRepository`, usage labels).
* **Do not** apply this migration to the live database from a local machine; the production API applies pending migrations at startup.

## Tests (actual results, 2026-10-12)

| Suite | Result |
|---|---|
| EEMO whole-solution Release build | 0 errors |
| EEMO unit | 2593 passed, 0 failed |
| EEMO component (bUnit) | 1058 passed, 0 failed (includes 36 Operations incl. tenant configuration, 6 settings page, 3 invitations) |
| EEMO integration (Testcontainers PostgreSQL 16, all) | 524 passed, 0 failed, 7 skipped (pre-existing conditional skips; none in files added here) |
| Platform admin / landing (vitest) | 97 / 50 passed |
| Platform production builds (admin, landing) | both succeed |
| `eslint` (admin + landing) | 0 errors, 17 warnings |
| `git diff --check` | clean |

New coverage: tenant settings (legacy fallback, explicit adoption, isolation, Head-only, concurrency, unique index, impersonating/duplicate names,
activation seeding incl. rollback), first-run Head flag, activation URL, invitation lifecycle (inactive until accepted, hash-only, single-use,
expired, re-issued, revoked, in-use accounts refused, cross-tenant NotFound, invalid links change nothing), Operations/Settings/Accounts pages,
admin mapper (absent vs empty vs unreadable declarations), workspace groups and discard behaviour.

## Browser acceptance (headless Chrome 155 via DevTools Protocol, isolated stack)

Stack: API `https://localhost:7397`, Head Portal `https://localhost:7367`, Landing `https://localhost:5373`, Admin `https://localhost:5374`,
PostgreSQL `127.0.0.1:15532` (disposable). Every API call from the apps went to `localhost:7397`. The earlier-running manual-test stack on
7297/15432 was **not touched**. All executed, none simulated, unless stated:

* **B** first-run Cantilan Head → straight to `/menu`, no change-password screen; DB `MustChangePassword=false`.
* **A** Carrascal: assessment → approval (link on local Landing) → workspace (grouped layout, desktop + 390px, no horizontal overflow) →
  Market Fees + Municipal Space Rental named "Carrascal Plaza Rental", Landing / Berthing not selected → save, reload, submit → Admin
  Validation/Activation → activation → Head link host `localhost:7367/activate/<token>` (not production) → Head accepted it → reused link
  did nothing → scoped sign-in (`?lgu=CARRASCAL`) OK, `?lgu=CANTILAN` refused.
  **Carrascal's Operations**: Market Fees; Carrascal Pamilihan (NPM); Carrascal Tindahan (TCC); Carrascal Plaza Rental (OR) — no Landing, no
  Terminal, no Slaughterhouse. No collections, sources, bills, rates beyond the facility rates, or Collector assignments were created.
* **E** Head edit persisted and re-rendered the directory; impersonating name refused with the server's message; Cantilan unaffected, then
  adopted explicitly with names unchanged.
* **D** invitation via the real drawer (default, no password field); account inactive; accepted once; the inviting Head's session unchanged;
  new Admin signs in to Carrascal as "Administrator", turned away from `/settings/operations` and `/accounts`.
* **C** temporary-password Admin: forced to `/change-password`; **API directly** returned 403 `password_change_required` on every protected endpoint
  and refused re-using the same password; after the change a normal session and `MustChangePassword=false`.
* **Defect checks:** monthly-rental rows now read "Set for each space after activation"; locked-block text no longer promises saves.

**Not browser-verified:** Test G's locked / unknown-entry notices after the regroup (covered by component tests and the V2 browser run, but
the V3 layout was not re-driven with a version-2 draft); the Admin-console Activation-history list after a *page reload* (covered by unit
test); Test F beyond the five-custom limit (covered by V2 browser run + integration tests); same-username Admins across two LGUs through the UI
(covered by the per-tenant activation integration test, not the browser).

## Remaining limitations and decisions

1. **Names apply to the Operations directory and the settings page only.** Workspace page titles, report headings, `SourceNames`,
   `DashboardOperations`, sidebar entries, Mobile and receipts still use the fixed names. A central resolver
   (`TenantOperationSettingsStore` / `TenantOperationSettingDto.DisplayName`) exists to extend this per surface; each needs its own review so
   official reports and issued documents are not rewritten. This was deliberately not a search-and-replace.
2. **Municipal Space Rental is not policy-neutral.** `ObligationAccount.cs:141` enforces *"Kanmanggay's recorded signed contract duration must
   follow the MEEDO three-year standard"* for the Kanmanggay kind. Another LGU that lists "Municipal Space Rental" inherits that rule, which is
   not tenant-configurable. The settings page now says so. **Policy verification / a configurable rule is required before another LGU relies on
   it as a fully authorized financial workflow.** Not changed here (financial behaviour).
3. **Custom facilities** remain limited to `Custom1`–`Custom5` in total (shared with Add Facility) and are not NCC; their statement grouping is
   still "awaiting an approved official grouping".
4. **No per-operation readiness.** "Listed" is presentation; there is still no server-side Ready/Needs-setup status per operation (not faked).
5. An Admin whose invitation email could not be sent (SMTP unconfigured locally) is given the link once in the drawer; a lost link needs *Resend*.
6. The login endpoint is rate limited; rapid repeated sign-ins during testing needed a pause (expected behaviour, not changed).
7. The two catalogs on the platform side and the server catalog are kept equal by contract tests, not a shared package.

## Integration prerequisites with the ECF/WCF branch

* `Operations.razor`: ECF/WCF changed two link lines (`Accounts` → `Statements`). This work edits the same file in different regions (injection,
  notices, group wrappers, `@code`). Expect a mechanical merge conflict; keep both.
* `IAppDbContext` / `AppDbContext`: one `DbSet` line each; `TenantDataTables` / `TenantExportRepository` / `TenantUsageRepository` /
  `TenantBackupContentsDto`: one entry each; `PostgresFixture.ResetAsync` truncate list: two names added (`TenantOperationSettings`, `Users`).
* **EF migrations:** one new migration, timestamp `20261012074910`. If the utility branch adds a migration with a later or earlier timestamp,
  regenerate neither — EF orders by timestamp and both are additive — but confirm the shared `AppDbContextModelSnapshot` merges cleanly.
* `ApplicationEfBoundaryTests.Allowed`: two files added.
* No ECF/WCF workflow, DTO, controller, reader or component was edited.

## Merge-readiness verdict

**Ready for review and a controlled staging deploy; not ready to merge blind.** Automated suites are green and the end-to-end acceptance passed in a
real browser against an isolated database. Before merging: (1) land or rebase against the ECF/WCF branch and resolve the `Operations.razor` and
snapshot merges; (2) decide how to treat limitation 2 (policy) for any non-Cantilan LGU that lists Municipal Space Rental; (3) take a database
backup before the first deploy that applies the migration; (4) re-drive Test G in the new layout once on staging.
