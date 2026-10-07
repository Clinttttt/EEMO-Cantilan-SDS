# October 8 single-repository consolidation verification

Status: application validated automatically and accepted through the user's fresh-app visual checkpoint for same-checkout local fast-forward integration.

## Repository

- Physical checkout: `C:\dev\stalltrack\eemo`; no new worktree or repository copy.
- Starting branch: `codex/final-consolidation`, clean, HEAD `16c19627`; the expected integration branch pointed to the same HEAD.
- Feature branch: `codex/final-eemo-consolidation`.
- Application commit: `6051a66d` — Terminal menu/source-native regression coverage.
- Final destination: `integration/report-governance-ui`, accepted application checkpoint `6051a66d` plus this documentation checkpoint.
- No reset, historical cherry-pick, origin push, master merge, deployment or APK publication.

## Application behavior

Today's Work excludes assigned legacy TRM/PerTrip facilities from both active and unavailable current-work rows. Historical routes, APIs, database rows and reports remain intact. Ready/assigned Terminal is grouped under **Income from Terminal** and opens `/income-terminal`; Transportation/Parking stays under Income from Market and opens its separate direct-amount operation. The catalog's Terminal group and discovery-provided family are preserved.

Selected-source New Collection already uses server source-native discovery. No second product fix was necessary. Regression coverage proves that an occupancy picker loads only its source response and never requests direct-session choices. Existing server checks reject unrelated direct intents and duplicate Daily/Whole stall-month events.

Whole Payment selectors, arrears receipt semantics, immutable Collections/SRC, rate calculations, canonical authority, offline/idempotency behavior and financial classification were not changed.

## Local Cantilan evidence

Only the configured `localhost:5432` / `EEMOCantilanSDS.DB` and local API were inspected. Authenticated local accounts were used; credentials/tokens are not recorded here.

- NPM Daily was already Active, enabled and Mobile-enabled, effective `2026-10-08`, when first inspected. Supported configuration is Head-only `PUT /api/governed-services/NPM_DAILY`, appending an effective-dated setting. No setting or database data was changed by this session.
- Server business date: `2026-10-08`. `personal1` readiness: five eligible, zero already collected, total ₱150. The batch quote returns exactly those same five identities/amounts and permits recording.
- Ana Reyes: `New Public Market · Stall 1`; Daily ₱30, Whole ₱900 remaining, Water and Electricity are independently eligible. The selected-source response contains no Market Fees, Landing/Berthing, Transfer Large Cattle, Transportation/Parking or Terminal.
- Source-less discovery retains authorized direct/walk-up operations. Terminal is currently **not assigned** to `personal1`, so its tile must remain hidden for that account. Assigned/ready Terminal grouping/routing is proven by actual Razor component tests.
- A live Daily-plus-Whole quote for Ana Reyes returns `DuplicateBusinessEvent` and `CanRecord=false`.
- No local Collections were posted. Partial/completed Daily transitions are proven by the PostgreSQL/component suites, not claimed as manually exercised local postings.
- Fish/Meat migrations `20261006173902_OfficeSourceNativeIdentity`, `20261007000912_NativeCollectionSourceShapeAndSpaceHolder`, and `20261007072024_FishMeatRegistryLifecycle` are in current migration history. The actual `FishMeatVendorRegistrations` table has all 19 expected columns, lifecycle/renewal unique indexes, lifecycle check and tenant-scoped prior-registration FK. The Head management API loads successfully for tax year 2026 / October 2026. No repair, history-row deletion, duplicate migration or data reset was needed.

## Validation

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Focused Today's Work Unit, before fix | 13 | 4 | 0 |
| Focused Today's Work Unit, after fix | 17 | 0 | 0 |
| Focused actual Menu Razor components | 2 | 0 | 0 |
| Full Unit | 2,523 | 0 | 0 |
| Full PostgreSQL Integration | 366 | 0 | 7 |
| Full Mobile components | 64 | 0 | 0 |
| Full Web components | 746 | 0 | 0 |

Seven Integration skips require a restored production snapshot; throwaway PostgreSQL financial tests ran. Existing source-native, Daily/Whole conflict, five-stall/partial/completed/blocked Collect All, vacant/nonpayable exclusion, Whole popover and optional-arrears-receipt tests remain green.

API Release, Client Release and Mobile Windows Release builds passed with zero errors. Existing compiler/package warnings remain. `git diff --check` passed. No CSS was changed.

## Fresh runtime and visual acceptance

API, Client and Windows Mobile were launched using `dotnet run` from their primary-checkout project directories after application commit `6051a66d`. Verified running targets:

- API: `EEMOCantilanSDS.Api\bin\Debug\net9.0\EEMOCantilanSDS.Api.exe`.
- Client: `dotnet exec C:\dev\stalltrack\eemo\EEMOCantilanSDS.Client\bin\Debug\net10.0\EEMOCantilanSDS.Client.dll`.
- Mobile: `EEMOCantilanSDS.Mobile\bin\Debug\net10.0-windows10.0.19041.0\win-x64\EEMOCantilanSDS.Mobile.exe`.

Every target is under `C:\dev\stalltrack\eemo`. No obsolete-worktree application process was found. Only identified StallTrack processes were stopped for restart. Mobile DLL timestamp `2026-10-08 01:11:55 +08:00` is newer than application commit time `01:10:39 +08:00`. API `/health` and Client `/login` return 200.

Native Computer Use initialization failed twice, including after a kernel reset, with `windows sandbox failed: helper_unknown_error: setup refresh had errors`. Browser control also failed to initialize. A user manual checkpoint was therefore requested against the freshly restarted apps: legacy tile exclusion, separate Transportation, Ana's eligible items, five-stall Collect All list, Whole popover geometry, and Fish/Meat Web loading. The user explicitly confirmed **“Checked — all passed.”** Visual acceptance is user-reported; no automated screenshot/browser review is claimed. Assigned/ready Terminal itself remains conditionally tested by actual Razor tests because `personal1` is unassigned.

The manual gate passed. The authorized remaining sequence is documentation commit, same-checkout fast-forward into local integration, and final integrated-branch rebuild/restart. No additional test-suite repetition is required for a documentation-only integration with identical application code.

## Documentation and production

### Subsequent UI follow-up (2026-10-08)

Starting local integration HEAD was `b7c91cd9`. Work continued in the same physical checkout on `codex/terminal-input-contrast`, without additional worktrees. Application commits: `4df9bfda` (Mobile Terminal card/Name and Collect All contrast), `2249573b` (Web drawer hierarchy and Terminal row label), `ed4b295b` (two-column Terminal choices and vehicle-only optional Name, superseding the earlier section-total Name presentation).

Terminal section labels and vehicle choices remain server-provided. Sections are not split or renamed to invent choices. Vehicle type remains an optional assisted mode within its official section. Direct amount entry, optional ticket count, CT instrument, source-native identity and historical compatibility remain intact. An uncommitted vehicle Name is cleared when switching to a section total; posted/history evidence is untouched.

The Web registration drawer keeps the original segmented controls at the existing compact width, placing Type on one row and Registration on the next; name/business and address/reference remain paired. Renewal, validation and tax-year behavior are unchanged. Only the Operations child row reads Terminal; the group and workspace section rules are unchanged.

Validation: full Mobile components **70/0/0** (passed/failed/skipped), focused Web **20/0/0**, full Web **746/0/0**. Mobile Windows Release **17 warnings/0 errors**. Client Release standalone retry **0 warnings/0 errors**; its first concurrent attempt failed with one compiler-output lock error, resolved by sequential execution. `git diff --check` and edited source plus generated Release Mobile CSS brace checks passed. Unit/Integration results above remain the consolidation run; no backend logic changed in this follow-up.

Fresh Mobile PID 17860 points into this checkout; DLL timestamp **01:44:51 +08:00** is newer than `ed4b295b` at **01:44:10 +08:00**. Fresh Client PID 37912 points to this checkout's Debug DLL and `/login` returns 200. Follow-up visual acceptance has been requested for Terminal, 346px/390px Collect All, and the two Web changes. It is pending; the prior user acceptance does not cover these later changes.

`CURRENT_RELEASE_STATE.md` and `ACTIVE_WORKSTREAMS.md` record accepted local application commit `6051a66d` and this verification record. `CONTEXT.md` was reviewed; its domain semantics remain current and required no change. Stable business rules were not rewritten.

Production remains the separately recorded October 5 release (`db75418b`, APK `collector-1.1.12-14`). These local checks do not declare production activation or deployment.
