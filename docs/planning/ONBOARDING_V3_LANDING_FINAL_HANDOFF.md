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
* Tokens/passwords: not in API logs. **Finding:** the Head Portal's default `Information` logging writes the one-time activation URL (`GET …/api/activation/context/{token}`) through `System.Net.Http.HttpClient`. Pre-existing, not introduced here. Fix (configuration, deliberately **not** changed here because it is a logging setting): raise `System.Net.Http.HttpClient` to `Warning` in the Head Portal's production configuration.

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
| Head Portal component tests | **1086 passed** |
| Mobile component tests | **107 passed** |
| PostgreSQL integration tests | **558 passed, 7 skipped (pre-existing), 0 failed** |
| Platform landing (vitest) | 55 passed (9 files; 5 new for the home page) |
| Platform admin (vitest) | 97 passed |
| Landing + Admin production builds | succeed |
| ESLint on every file touched here | clean |
| `git diff --check` | clean |

The counts above are from the final integrated tip (after merging utility `3fde28ca`); the same counts held on the intermediate tip `72a231d8`. One real defect found and fixed along the way: the migration-upgrade test used an unscoped `Single()` over `FacilityRates` (the fixture does not truncate that table), failing in a full-suite run only (`766dac48`).

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

1. **ECF/WCF classification for a new tenant (BACKEND GAP / dependency).** For a freshly activated tenant, `GET /api/ecf-collections/obligations` returns 409 *"ECF has no active revenue classification for this tenant."* The ECF workspace opens, but ECF **Statements** shows the generic "couldn't be loaded" instead of that reason. Activation does not seed the ECF/WCF classification; the Head must set it up (Revenue Setup) before ECF can be used. Needed: either activation seeds the classification when ECF/WCF is declared, or the Statements page surfaces the 409 text. This sits in the utility owner's files, so it was recorded, not edited.
2. Kanmanggay three-year duration is still a platform-wide number; non-default tenants are warned, not given a per-municipality policy.
3. Head Portal `Information` logging writes activation URLs (F).
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
