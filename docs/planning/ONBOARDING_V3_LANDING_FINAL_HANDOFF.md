# Onboarding V3 + Public Landing — Final Handoff (CORE BRAIN V8)

Date: 2026-10-12. Nothing here was pushed, merged to `master`, deployed, migrated in production or published as an APK.
The main checkout and its uncommitted files were not touched.

## A. Baseline

| Repo | Branch | Starting point |
|---|---|---|
| EEMO (Head Portal/API) | `codex/onboarding-initial-operations-v2` | `b8e740a4` (V3 handoff) |
| EEMO utility work | `codex/utility-workspace-ecf-wcf` | `aa697f3d` at first integration; **it moved twice during this pass** (see B) |
| Platform (Angular Landing + Admin) | `feat/onboarding-tenant-operations-v2` | `025627c` |

## B. Integration

Integration branch: **`codex/integration-onboarding-v3-ecf-wcf`** in the isolated worktree `C:\dev\stalltrack-v2-worktrees\integration-v8`.

* The ECF/WCF branch was **not** frozen: Claude 1 kept committing. The branch was merged three times, and the exact tip is verified, not assumed:
  `aa697f3d` → `72a231d8` (7 commits, adds the additive `DirectCollectionReviewEvents` migration) → **`3fde28ca`** (6 commits, no migration; Mobile recent-collection fixes, backup/export of review events, EF-boundary allow-list).
  The final merge is `git merge --no-ff codex/utility-workspace-ecf-wcf` at `3fde28ca`. **If the utility branch moves again, merge again before release** — `git merge-base --is-ancestor <tip> HEAD` is the check.
* One textual conflict in total (`Operations.razor`): resolved by keeping the ECF/WCF `Workspace · Statements · Report` links **and** tenant-aware listing/labels. No migration conflict.
* Migration order is `…NpmPartialInstallments` → `TenantOperationSettings20261012` → `DirectCollectionReviewEvents20261012`. `dotnet ef migrations has-pending-model-changes` reports none after each merge.
* Utility financial safeguards (ECF OR, WCF CT, SRC, prepared/direct settlement, eligibility, collector authority, idempotency, history) are **unchanged**: the onboarding work touches none of those files, and the utility suites pass inside the integrated integration suite.

## C. Tenant configuration

* `TenantOperationSettings`: no rows = not adopted = legacy fallback (every operation listed under its established name). Activation seeds all 11 catalog operations explicitly. The first Head edit of a legacy tenant adopts the current state first (labels preserved).
* `IsListed` is presentation only. Hiding never deletes or reinterprets a record, and never blocks a direct URL (Carrascal hid ECF; `/operations/ecf` still opens).
* `Version` gives optimistic concurrency: a stale update returns **409** and changes nothing.
* **Kanmanggay three-year rule.** Only the default municipality gets no notice; any other tenant sees "Policy confirmation required" on the operation (Settings and Operations directory). `PolicyNotice` is read only by presentation files (`OperationSettings.razor`, `Operations.razor`) and the DTO/store; **no collection, contract or validation handler reads it**, and the Mobile test asserts `IsCollectible/IsAssigned/Status` are identical with and without it. It authorizes nothing and removes no validation. A real per-municipality duration policy is still **not modelled** (see L).
* Settings now shows facts only per operation (collectors assigned, whether collections exist). There is no readiness verdict and no "Ready".

## D. Onboarding

Browser-verified on the integrated build: workspace with the five official groups, progressive facility cards, draft save/reload, operation local names, submit → Validation → Activation. The platform shows "Declared revenue operations" at validation and an activation preview, then an activation queue vs history split (Carrascal moved from queue "LIVE" to history "ACTIVE").
The three earlier smoke defects are covered by platform specs (admin 97, landing 55 passing); they were **not** re-clicked in the browser this pass.

## E. Head Portal

* Central tenant-aware resolver `OperationNameService`: tenant label → neutral standard name; legacy tenants unchanged; Head-set policy names still win; official report captions, remittance/classification screens, receipts and SRC keep canonical names.
* Applied to operation headers, dashboard cards/actions, Transactions, Collection Activity and the Mobile `Name`.
* Carrascal Head sees exactly its declared 4 operations (+ its own facility and the always-on Others lines); Cantilan still sees all 11 groups unchanged.

## F. Account lifecycle (real HTTP/browser)

* First-run Cantilan Head → lands on `/menu`, no forced change.
* Platform activation link comes from the API (`ACTIVATION_LINK_BASE`); the console never builds a host. Carrascal Head activated via that link; **re-using the link is refused** (400).
* Admin invitation: account inactive and cannot sign in until accepted; one-time, single-use, hash-only; accepted Admin signs in normally.
* Temporary-password fallback: sign-in succeeds, every protected call returns **403 `password_change_required`**.
* Cross-tenant: Cantilan Head cannot see Carrascal's admins, settings or names, and vice versa.
* Tokens/passwords: not in API logs. **Finding:** the Head Portal's default `Information` logging writes the one-time activation URL (`GET …/api/activation/context/{token}`) through `System.Net.Http.HttpClient`. Pre-existing, not introduced here. **Fixed in the correction pass (section O.2)** in code, not configuration: URLs are redacted at log time. Production logs were not accessed.

## G. Public Landing page (platform repo)

Commit **`cfc3fa9`** `feat(landing): redesign the public landing page`.

* New home: compact header (Features · How it works · Municipalities · "Request an assessment"), hero "Municipal enterprise collections, organized.", a four-step record-flow card (structure, not figures), five real capabilities, "How StallTrack works" (4 steps; states that review precedes activation and that requesting does not create an account), "Bring StallTrack to your LGU" band, short footer with verified links only (Features, How it works, Municipalities, Privacy, Terms, mailto).
* Removed fabricated content: the "₱248,650 / 312 stalls / 92%" sample card, trust-strip numbers, marketing sections, and the representative profile.
* Removed the formsubmit.co demo modal and the representative modal; the real assessment flow is unchanged: header/hero/band CTA → `/municipalities` → municipality → assessment modal → `POST /api/assessment/requests`.
* Existing tokens only; added the Head Portal's civic blue (`#1f5fbf`) to the shared brand preset. Focus-visible ring, skip link, one `<h1>`, one `<main>`, reduced-motion respected, no gradients/glows/glass.
* Verified with real viewport emulation (1440, 1024, 768, 430, 390, 360): `scrollWidth ≤ innerWidth` at every size (no horizontal overflow). Screenshots below.

## H. Compatibility

* **Mobile app/APK:** `git diff 72a231d8..HEAD` over `EEMOCantilanSDS.Mobile`, Mobile tests, `Application/Dtos/Mobile` and `MobileApiClient` is **empty**. The only server change is the existing `Name` string for tenants that adopted configuration (`GetCollectorOperationCapabilitiesQueryHandler`, `CollectorOperationAssignmentWorkflow`); codes, assignment, status, reasons, families are identical. No APK rebuild or update is required.
* Public API: two additive controllers (`OperationSettingsController`, `AdminsController` invitations). No existing route changed.
* Landing: assessment form/API, `/onboarding/:token`, draft save/submit, operator navigation, activation links preserved (browser-verified).

## I. Testing (final, integrated tip)

| Suite | Result |
|---|---|
| Whole solution Release build | 0 errors |
| Unit tests | **2593 passed** |
| Head Portal component tests | **1108 passed** |
| Mobile component tests | **108 passed** (107 in the first version of this table was a stale test binary; see O.4) |
| PostgreSQL integration tests | **568 passed, 7 skipped (pre-existing), 0 failed** |
| Platform landing (vitest) | 55 passed (9 files; 5 new for the home page) |
| Platform admin (vitest) | 97 passed |
| Landing + Admin production builds | succeed |
| ESLint on every file touched here | clean |
| `git diff --check` | clean |

The counts above are from the final corrected tip (section O); the first version of this handoff reported 1086 / 107 / 558 for the earlier tip. One real defect found and fixed along the way: the migration-upgrade test used an unscoped `Single()` over `FacilityRates` (the fixture does not truncate that table), failing in a full-suite run only (`766dac48`).

`nx lint landing` as a whole reports 25 errors, all in files **not changed here** (`onboarding-workspace.*`, `request-assessment-modal.ts`, `reveal.ts`: label-association and selector rules). Pre-existing lint debt; not fixed to keep this change scoped.

## J. Visual evidence

`docs/planning/evidence/onboarding-v3-landing/` (fictional local data, no tokens or credentials):
`landing-1440/768/430/390/360.png`, `assessment-modal.png`, `assessment-received.png`, `onboarding-workspace.png`, `platform-validation.png`, `platform-activation-queue.png`, `cantilan-operations.png`, `carrascal-operations.png`, `carrascal-operation-settings.png`.
A screenshot showing the activation link was deliberately not kept.

## K. Database

* **Clean database:** the API applied all 124 migrations on an empty PostgreSQL 16 at startup (latest `20261012111213_DirectCollectionReviewEvents20261012`).
* **Existing database:** `TenantOperationSettingsMigrationTests` migrates a real schema to `…NpmPartialInstallments`, seeds Cantilan data, upgrades twice (idempotent), asserts data untouched, 0 settings rows, legacy read intact, unique `(MunicipalityId, OperationCode)` index present; rolling back drops only its own table. Passes after both merges.
* The migration is one `CreateTable` + one index: no data change. No production migration was run; all migration testing used disposable containers (removed).

## L. Remaining limitations / integration dependencies

1. **[RESOLVED in section O.1]** ECF/WCF classification for a new tenant. Original finding: For a freshly activated tenant, `GET /api/ecf-collections/obligations` returns 409 *"ECF has no active revenue classification for this tenant."* The ECF workspace opens, but ECF **Statements** shows the generic "couldn't be loaded" instead of that reason. Activation does not seed the ECF/WCF classification; the Head must set it up (Revenue Setup) before ECF can be used. Needed: either activation seeds the classification when ECF/WCF is declared, or the Statements page surfaces the 409 text. This sits in the utility owner's files, so it was recorded, not edited.
2. Kanmanggay three-year duration is still a platform-wide number; non-default tenants are warned, not given a per-municipality policy.
3. **[RESOLVED in section O.2]** Head Portal `Information` logging wrote activation URLs; a separate production-log assessment is still recommended.
4. Utility branch is still moving; re-merge and re-run before release.
5. Not exercised: real Collector device/APK, real email delivery (`emailSent:false` locally), production API, the earlier three smoke defects in a browser (covered by specs only).
6. Pre-existing lint errors (I).

## M. Release readiness

Merge order (all require explicit authorization, none done): (1) utility branch to its target; (2) `codex/integration-onboarding-v3-ecf-wcf` (contains both); (3) platform `feat/onboarding-tenant-operations-v2` (Landing redesign + onboarding + admin).
Staging prerequisites: apply the two additive migrations to a **copy** of production first; set `ONBOARDING_LINK_BASE` and `ACTIVATION_LINK_BASE` to the real hosts (an unset base yields an explicit message, never a guessed link); decide the ECF classification dependency (L1); lower `System.Net.Http.HttpClient` logging; verify the platform builds point at `https://api.stalltrack.site` (local overrides lived only in a deleted throwaway worktree and were never committed).

## N. Git

| Repo | Branch | Commits from this pass |
|---|---|---|
| EEMO | `codex/integration-onboarding-v3-ecf-wcf` | `12589213` merge ECF/WCF; `ce4f109d` tenant names; `93ad3f26` policy flag + facts; `766dac48` test isolation fix; `d3a13090` merge `72a231d8`; final merge of `3fde28ca` + this handoff (see `git log --first-parent`) |
| EEMO | `codex/onboarding-initial-operations-v2` | unchanged at `b8e740a4` |
| Platform | `feat/onboarding-tenant-operations-v2` | `cfc3fa9` Landing redesign (on `025627c`) |

Working tree: integration worktree clean after the handoff commit; platform clean; main checkout untouched (its 8 modified files are not mine). Disposable PostgreSQL container, local servers, Chrome profiles, token files and the throwaway platform worktree were removed. `onboarding-manual-pg` and port 7297 belong to someone else and were not touched.


## O. Release-blocker correction pass (2026-10-13)

Three findings from the first handoff were fixed (each a scoped commit on the integration branch), the Mobile count was explained, and the whole thing was re-verified. The Landing page was **not** touched (platform stays at `cfc3fa9`).

### O.1 New LGU: ECF/WCF "no active revenue classification" (fixed — `49dab456`)

* **Root cause.** Revenue classifications are seeded for exactly one tenant, the one whose rulings they are (`RevenueClassificationSeeder` reads only `CANTILAN`, and says its instrument mappings "must never be copied to every municipality"). Onboarding/activation wrote only *presentation* rows (`TenantOperationSettings`) and never a classification, so a new office could list ECF/WCF but the utility workflow had nothing to resolve: `ResolveEcfPolicyAsync` / the WCF equivalent returned 409. The Statements pages then threw the reason away and printed a generic "couldn't be loaded" with a pointless **Try again**.
* **Fix.** (1) At activation, for ECF/WCF the LGU **selected**, create the `RevenueClassification` and nothing else: **no policy version, no instrument, no source, no obligation**. It exists so Revenue Setup lists the source ("No policy configured") instead of the Head having to know and type the internal code. (2) ECF/WCF Statements show the server's Conflict reason as **"Setup required"** with a link to Revenue Setup; temporary failures (e.g. 503) keep the generic message and Try again.
* **What it deliberately does not do.** It does not choose an instrument. ECF=OR / WCF=CT are Cantilan's confirmed rulings; another office approves its own instrument in Revenue Setup. The dormant classification authorizes nothing (test: `GetObligations` still refuses with "does not approve Official Receipt"), creates no utility bill and no Collection. Unselected operations, older onboardings that declared nothing, and existing offices get nothing (tests for each). Landing/Berthing selected alongside gets nothing (only the two utilities need one).
* **Browser-verified (fresh DB, disposable stack).** Carrascal activated with ECF+WCF selected → DB shows `ECF|0 policies`, `WCF|0 policies`. ECF and WCF Statements: *"Setup required. The active ECF policy does not approve Official Receipt collection."* / *"…WCF policy does not approve Cash Ticket collection."* with no retry button. Revenue Setup lists "Electricity (ECF)" and "Water (WCF)" with "No policy configured". The Head approved **Official Receipt** for ECF through the real Revenue Setup form → ECF Statements now load (empty: no source exists, nothing invented); WCF correctly **still** says Setup required.
* **Same gap, not fixed here.** Other operations that need a classification/policy (Fish/Meat, Kanmanggay, Landing…) have the same "Head creates it in Revenue Setup" path and no dormant row. Not changed: the instruction was ECF/WCF and "do not blindly activate all classifications". Owner decision whether to extend.

### O.2 Activation-token logging (fixed — `480d9678`)

* **Root cause.** The Head Portal's default `Information` logging included the framework's per-client HTTP logger, which prints the full request URI; the token-bearing lookups (`/api/activation/context/{token}`, `/api/AdminAuth/reset-context/{token}`, `/api/mobile/bind/{token}`) put the one-time credential in that URI. Separately, the API's exception middleware logged the raw request path (`{Path}`), which for `/api/onboarding/{token}…` and the same lookups is the credential too — the first edit missed one of its two log calls and the regression test caught it.
* **Fix.** `SensitiveRoutes.Redact` (shared, path+query) replaces credentials with `[redacted]` (activation, reset, verify-email, onboarding, device-bind, and query keys `token/code/key/…`). The portal replaces the framework logger with `RedactedHttpLoggingHandler` (method, redacted route, status, duration; exception *type* only). Both API exception log calls redact the path. Hashing, 7-day expiry and single-use are unchanged and were not touched.
* **Tests.** 11+5+1 redactor cases; the portal's **real client pipeline** with every logger category and scope captured (no token, `System.Net.Http.HttpClient` logger absent, redacted diagnostics present); API exception path for four link routes. Proven to fail without the fix (mutation check). Live: across Head activation, Admin invitation, onboarding workspace and reset-context lookups, token occurrences in API+portal logs = **0** (was 12 before).
* **Audit results.** No other `Log*` call in Auth/Admins/Onboarding handlers prints tokens (MFA logs usernames only); the SMTP sender logs the recipient address, never the body/link; Angular apps log only bootstrap errors and have no analytics. Passwords: none logged.
* **Production logs.** Before this fix, any deployment running the portal at `Information` for `System.Net.Http.HttpClient.*` (the default) will have written **activation/reset/invite URLs of lookups made through the portal** to its log sink. **A separate security assessment of production logs is recommended** (look for `/activate/`, `/reset-context/`, `/api/activation/context/`, `/mobile/bind/` in portal logs and any shipper/retention copies; links are single-use and expire in 7 days, so exposure is limited to unredeemed, unexpired links). **No production logs were accessed or purged.**

### O.3 Kanmanggay three-year rule for non-Cantilan offices (guarded — `555be769`)

* **Audit.** Yes — it *was* imposed: `ObligationAccount.SetLeaseDetails` rejects any signed Kanmanggay duration other than 3 for **every** tenant, and the Kanmanggay form offered "3 years" as its only choice (`KmContractYearOptions`, and the setter forced `3`), so another municipality's contract was stamped "3 years" with no approved policy. Both write paths go through it: `CreateAccountAsync` and `CorrectHolderAsync`. Duration is descriptive evidence only (never prices, assesses or closes).
* **Guard.** For a signed Kanmanggay contract, an office that is not the default (Cantilan) cannot **record or change** a duration; the workflow refuses with an explicit message. Saving the contract **without** a duration, space-only occupancy, every other operation (Vegetable/Fruit and event lots have no such rule), reading, assessing and collecting are unaffected. A duration an account **already** holds is preserved and the record stays editable (only a *change* is refused); nothing is rewritten. Cantilan's rule is unchanged (3 ok, 5 rejected with the original message). The form shows "Policy confirmation required" instead of the choice and never sends a value.
* **Tests.** 3 integration (non-default refuses 3 and 5 / accepts no-duration, space-only, Vegetable/Fruit; existing 3 preserved and editable, change refused; default office exact) + 1 component. Three older tests that signed 3-year contracts now seed the default office, which is what they modelled.
* **Owner decision still required.** How a municipality's contract-duration policy is approved and modelled (per-tenant setting, allowed values, effective date). Until then non-Cantilan offices record contracts without a duration. This guard is the single place that decision plugs into (`ObligationWorkflow.SignedContractDurationProblemAsync`).

### O.4 Final integration verification

* **Utility V9 `3fde28ca` is integrated** (ancestor check passes; Mobile app/tests/DTOs identical to it).
* **Mobile 107 vs 108 — explained.** `EEMOCantilanSDS.Mobile.ComponentTests` is **not part of `EEMOCantilanSDS.slnx`**, so a solution build never rebuilds it. My earlier runs used `--no-build` and therefore ran a binary built **before** `abb183cc` was merged (it is missing exactly one test: `CollectionSessionEditorTests.A_utility_item_cannot_be_added_until_the_earlier_collections_check_has_answered`). Same source on both sides (`git diff 3fde28ca HEAD -- EEMOCantilanSDS.Mobile.ComponentTests` is empty). Built explicitly: utility tip in a clean worktree = **108**; integration tip = **108**. The count of 107 in section I of the first handoff was a stale-binary artefact, not a code difference. **Always `dotnet test` that project without `--no-build`.**
* **Migrations.** Fresh PostgreSQL 16: 124 migrations applied at API start (latest `20261012111213_DirectCollectionReviewEvents20261012`); upgrade path test (existing schema + data → latest, twice, rollback) passes; `has-pending-model-changes`: none. This pass added **no migration**.
* **Existing tenants.** Cantilan unchanged (legacy fallback, full directory, first-Head-edit adoption preserved labels); all earlier scenario checks held in the integration suite.
* **Admin invitations remain tenant-bound.** Cantilan cannot see or resend Carrascal's invitation (404); the invitee is inactive until acceptance; the link works once (second lookup 400).
* **Landing assessment works** end to end through the redesigned page (submit → `/assessment-received`, browser requests only to localhost + fonts); Landing source unchanged since `cfc3fa9`.
* **Final test results** (integrated tip, each suite its own command): Unit **2593**; Head Portal component **1108**; Mobile component **108**; PostgreSQL integration **568 passed / 7 skipped (pre-existing) / 0 failed**; whole-solution Release build **0 errors**.

### O.5 Overlap with Rental UI V10 (`codex/rental-ui-consistency-v10`, `f1498345`, based on `3fde28ca`)

V10 was not edited or merged. A trial merge in a throwaway worktree (since removed) shows **real but small overlap**, mostly caused by O.3:

* **Textual conflicts: 2 files.** `ObligationWorkspace.razor` (two hunks: the create-request `ContractYears` expression — V10 makes Vegetable/Fruit record its own term, this branch adds `!DurationUnconfirmed` — and the edit-request expression) and `ObligationAccountTests.cs` (both sides append tests at the end). Resolution is mechanical: keep V10's Vegetable/Fruit term **and** the `!DurationUnconfirmed` guard for Kanmanggay. V10's Vegetable/Fruit duration is compatible with the guard (the guard only applies to Kanmanggay).
* **Auto-merged but both touch:** `ObligationWorkflow.cs`, `ObligationWorkspaceTests.cs`, `VegetableFruitImport.razor`, `IAppDbContext.cs`, `AppDbContext.cs`, `AppDbContextModelSnapshot.cs`, `TenantDataTables.cs`, `TenantExportRepository.cs`.
* **Migration.** V10 adds `20261012145430_RentalAccountContractRateAndImportRuns20261012`; it orders after both existing migrations here. After merging, run `has-pending-model-changes` and the fresh+upgrade migration tests again (not done: V10 is not integrated).
* **Recommendation.** Merge V10 into the utility line first (it is based on `3fde28ca`), then this branch (resolving the two conflicts above), then re-run the O.4 verification.

### O.6 Remaining dependencies after this pass

1. Owner decision on contract-duration policy per municipality (O.3).
2. Whether other operations should also get a dormant classification at activation (O.1).
3. Security assessment of production portal logs (O.2).
4. V10 merge and re-verification (O.5); utility branch may move again.
5. Not exercised: real Collector device/APK, real e-mail delivery, production API.
6. Pre-existing `nx lint landing` errors in files not touched (first handoff, section I).

### O.7 Commits in this pass (integration branch `codex/integration-onboarding-v3-ecf-wcf`)

| Commit | Subject |
|---|---|
| `49dab456` | fix(activation): selected ECF/WCF get a dormant classification; statements say "Setup required" |
| `480d9678` | fix(security): keep one-time link credentials out of logs |
| `555be769` | fix(rental): do not impose Cantilan's three-year signed-contract duration on other offices |
| (this file) | docs(planning): correction-pass handoff and evidence |

Evidence added: `docs/planning/evidence/onboarding-v3-landing/fix-*.png` (statements "Setup required" for ECF and WCF, Revenue Setup listing, the ECF policy form, ECF statements after setup, the Kanmanggay form without a duration choice). No tokens or credentials are shown.
