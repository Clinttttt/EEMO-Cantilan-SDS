# October 8 single-repository consolidation verification

Status: application validated automatically and accepted through the user's fresh-app visual checkpoint for same-checkout local fast-forward integration.

## Repository

- Physical checkout: `C:\dev\stalltrack\eemo`; no new worktree or repository copy.
- Starting branch: `codex/final-consolidation`, clean, HEAD `16c19627`; the expected integration branch pointed to the same HEAD.
- Feature branch: `codex/final-eemo-consolidation`.
- Application commit: `6051a66d` — Terminal menu/source-native regression coverage.
- Final destination: `integration/report-governance-ui`, accepted application checkpoint `08232726` plus documentation checkpoints. The initial consolidation application was `6051a66d`; later accepted addenda are recorded below.
- No reset, historical cherry-pick, origin push, master merge, deployment or APK publication.

## Application behavior

Today's Work excludes assigned legacy TRM/PerTrip facilities from both active and unavailable current-work rows. Historical routes, APIs, database rows and reports remain intact. Ready/assigned Terminal is grouped under **Income from Terminal** and opens `/income-terminal`; Transportation/Parking stays under Income from Market and opens its separate direct-amount operation. The catalog's Terminal group and discovery-provided family are preserved.

Selected-source New Collection already uses server source-native discovery. No second product fix was necessary. Regression coverage proves that an occupancy picker loads only its source response and never requests direct-session choices. Existing server checks reject unrelated direct intents and duplicate Daily/Whole stall-month events.

Whole Payment selectors, arrears receipt semantics, immutable Collections/SRC, rate calculations, canonical authority, offline/idempotency behavior and financial classification were not changed.

## Local Cantilan evidence

Only the configured `localhost:5432` / `EEMOCantilanSDS.DB` and local API were inspected. Authenticated local accounts were used; credentials/tokens are not recorded here.

- NPM Daily was already Active, enabled and Mobile-enabled, effective `2026-10-08`, when first inspected. Supported configuration is Head-only `PUT /api/governed-services/NPM_DAILY`, appending an effective-dated setting. No NPM setting or database data was changed during this initial verification. Later local Terminal mapping configuration is recorded separately below.
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

The initial manual gate passed and the initial documentation checkpoint `b7c91cd9` was fast-forwarded into local integration. Later requested addenda and their final acceptance are recorded below. No additional test-suite repetition is required for a documentation-only integration with identical application code.

## Documentation and production

### Latest requested presentation addenda

The current application checkpoint is `08232726` on the same `codex/terminal-input-contrast` branch. Accepted office images were directly inspected before implementation: the Downloads Fish Retailing Business Table and repository Monthly Income first/continuation pages. Handwritten monthly totals were not used as registration rates or balances.

- `0d67c325`: Fish/Meat drawer fields each occupy a full row (vendor, business, Type, Registration, Address), using the shared drawer controls and spacing. Reference is removed from register/renew input and ordinary search/table emphasis; new Register requests explicitly send null. Historical Reference values remain persisted and visible, when present, as Historical reference in Details. Renewal retains the existing compatibility snapshot; import compatibility was not rewritten. No migration or backend change.
- `c8d7691c`: Official Monthly Income renders two semantic sheet containers through one shared table-rendering loop and identical colgroups. Only sheet 1 has letterhead/Receipts/A; sheet 2 owns B/C, Terminal subtotal, overall total and configured signatories, with a forced print break before it. Screen preview separates sheets. DTO, row identities and calculations are unchanged. Standard A4 landscape print must still be visually confirmed to be exactly two pages without clipping or a signature-only third page.
- `08232726`: Terminal Web Sections uses Section/Today/This month/Contribution and Recent collections uses Date/SRC/Section/Collector/Amount. Contribution is section monthly net divided by the existing monthly Terminal net total, formatted to one decimal; a zero denominator shows a dash. Mobile removes current count input/detail and sends null for new item counts. Historical backend count fields and original posted records remain untouched. Two-column choices, vehicle-only optional Name, direct amount and CT instrument remain.

Latest checks (passed/failed/skipped): Fish/Meat **17/0/0**; report **27/0/0** after fixing the new test's subtotal-row count/fixture; Terminal **5/0/0**; full Web **748/0/0**; full Mobile **70/0/0**. The net/gross mutation proof produced **1 failed/1 passed/0 skipped**, then net was restored before the passing full Web run. Client Release **0 warnings/0 errors**; Mobile Windows Release **16 warnings/0 errors**. Whitespace and edited/generated Web/Mobile CSS brace checks pass. Unit and Integration counts above are the earlier consolidation runs, not repeated for these presentation-only addenda.

Native/browser control again fails at initialization (`helper_unknown_error: setup refresh had errors`). The user checked the fresh application checkpoint `08232726` and explicitly confirmed **“Checked — all passed, including two-page PDF”**. The acceptance covers even Terminal borders, configured vehicle choices and conditional Name, no count input, 346px/390px Collect All contrast/overflow, full-width Fish/Meat fields without ordinary Reference, Web Terminal contribution/no Tickets, and actual A4 landscape Print/Save as PDF: exactly two pages, complete A on page 1, B/C/overall total/configured signatories together on page 2, without clipped columns or a third page. This is user-reported acceptance, not automated browser/PDF validation. Same-checkout fast-forward and final integrated-branch restart are authorized. No push, master merge, production change or APK publication occurred.

Final visual-review runtime: Client PID **32900** uses this checkout's Debug DLL (timestamp **02:21:16 +08:00**), Mobile PID **8528** uses this checkout's Windows Debug executable/DLL (timestamp **02:24:28 +08:00**). Both are newer than application commit `08232726` at **02:20:07 +08:00**. Client `/login` returns 200. Only the verified primary-checkout processes were stopped/restarted. The accepted manual gate includes actual Print/Save as PDF at A4 landscape, exactly two pages with all closing totals/signatories together. Local fast-forward into `integration/report-governance-ui` succeeded through `ab13fcb1`; this documentation reconciliation follows, before the final affected-component restart from that integrated branch.

### Subsequent UI follow-up (2026-10-08)

Starting local integration HEAD was `b7c91cd9`. Work continued in the same physical checkout on `codex/terminal-input-contrast`, without additional worktrees. Application commits: `4df9bfda` (Mobile Terminal card/Name and Collect All contrast), `2249573b` (Web drawer hierarchy and Terminal row label), `ed4b295b` (two-column Terminal choices and vehicle-only optional Name, superseding the earlier section-total Name presentation).

Terminal section labels and vehicle choices remain server-provided. Sections are not split or renamed to invent choices. Vehicle type remains an optional assisted mode within its official section. Direct amount entry, optional ticket count, CT instrument, source-native identity and historical compatibility remain intact. An uncommitted vehicle Name is cleared when switching to a section total; posted/history evidence is untouched.

The Web registration drawer keeps the original segmented controls at the existing compact width, placing Type on one row and Registration on the next; name/business and address/reference remain paired. Renewal, validation and tax-year behavior are unchanged. Only the Operations child row reads Terminal; the group and workspace section rules are unchanged.

Validation: full Mobile components **70/0/0** (passed/failed/skipped), focused Web **20/0/0**, full Web **746/0/0**. Mobile Windows Release **17 warnings/0 errors**. Client Release standalone retry **0 warnings/0 errors**; its first concurrent attempt failed with one compiler-output lock error, resolved by sequential execution. `git diff --check` and edited source plus generated Release Mobile CSS brace checks passed. Unit/Integration results above remain the consolidation run; no backend logic changed in this follow-up.

Earlier review runtime: Mobile PID 17860 pointed into this checkout; DLL timestamp **01:44:51 +08:00** was newer than `ed4b295b` at **01:44:10 +08:00**. Client PID 37912 used this checkout's Debug DLL and `/login` returned 200. Visual acceptance was pending at that checkpoint; the final accepted addenda above supersede this presentation.

The user subsequently rejected the Terminal screenshot presentation and reported missing specific vehicle types. Diagnosis found six existing active Cantilan vehicle classes with approved effective-dated rates but no Terminal section association: their local stable codes were `JN`, `VN`, `MC`, `PUB`, `PUBB`, and `TC`, so the exact-code default mapping did not apply. Before repair, the Head Terminal vehicle-choice endpoint returned an empty list. Using the supported Head-only `PUT /api/office-sources/terminal/vehicle-section`, the existing Jeepneys, Vans, Multicabs, Public Utility Buses and Public Utility Baby Buses were explicitly associated with Pull Pul Vans/Cargo Vans, and Tricycle with Tricycad, as required by the October 6 office ruling. This changed **only local development configuration**, not class IDs, rates, historical rows, production or product code. No direct DB write or new activation mechanism was used.

After mapping, the actual `personal1` source-less discovery returns three section totals and six vehicle choices with existing approved rates. All nine have `DirectAmount`; no rate multiplication or fixed price was introduced. Vehicle-only optional Name remains a snapshot. Commit `838c4f51` fixes the screenshot's transparent left borders with complete equal-width outlines, compact spacing and a 64px minimum touch-target height; selected tiles retain blue outline, tinted fill and checkmark. At this intermediate checkpoint, focused editor tests were **37/0/0**, Windows Release **17 warnings/0 errors**, and diff/CSS source and generated bundle checks passed. Final visual acceptance and integration are recorded above.

`CURRENT_RELEASE_STATE.md` and `ACTIVE_WORKSTREAMS.md` record accepted local application commit `08232726` and this verification record. `CONTEXT.md` was reviewed; its domain semantics remain current and required no change. Stable business rules were not rewritten.

Production remains the separately recorded October 5 release (`db75418b`, APK `collector-1.1.12-14`). These local checks do not declare production activation or deployment.
