# Space-holder workspace restructure — 2026-10-08

Status: implemented on the existing feature branch; automated and rendered-fixture verification passed. Logged-in localhost review and user visual acceptance remain pending. Not integrated or production deployed.

## Final UI consistency follow-up — 2026-10-08

This section supersedes the initial presentation/test checkpoint below. Started at `0dab3777` on `codex/vegetable-fruit-space-workspace`, in the same physical checkout. The three concurrent planning-document diffs remain untouched and unstaged. No branch/worktree/checkout was created and integration remains pending explicit user visual acceptance.

Implementation and regression tests are committed at `190142a5` (`fix(web): align space rental workspaces and stall terminology`). The integration branch remains `4b4be8f8`.

Kanmanggay and Vegetable/Fruits Monthly now use the same compact hero → toolbar → **Space Rentals** table hierarchy. The overview shows space, occupant, recorded effectivity, approved monthly amount, collected, remaining and lifecycle, without an Account/Actions column or per-row Open account link. Overview All/Paid/Partial/Unpaid show active accounts; Closed shows historical closed accounts, matching the existing facility overview convention. Search/filter changes preserve server-supplied operation totals; their full-account/to-date scope is accessible on the hero.

Dedicated management pages retain their routes, account operations and authorization. The long introduction/scope paragraph is removed. Active/Closed and payment-position filters are distinct groups within one toolbar; the panel shows a concise `n of N spaces shown` count. Total scope remains `All accounts · to date`, with an explanatory tooltip. Account facts use a bounded two-column description grid, followed by the canonical period register. At laptop/narrow widths the management rows stack with labelled balances and visible actions; the smallest overview also stacks. The shared toolbar now avoids a desktop flex basis creating a 150px mobile search field. Contextual breadcrumbs wrap complete links instead of splitting their words.

`FacilityState.HolderUnitOf` is the single enum-based presentation distinction: NPM/NCC/TCC/BBQ use Stall; ICE/custom retain Space. ActionBar, the shared facility overview/table, facility roster and occupancy management consume that distinction. NCC says **Stall Holder List**, **Manage Stall Holders**, **Total Stalls**, **Stall No.** and **Stall Rent**. Its document/CSV number-column heading agrees; existing contract/area/amount fields remain intact. Area measurement units are unchanged. Global `/stall-holders` is unchanged.

The management action remains `/vendors?facility=ncc`, an existing stall occupancy registry with inspection, edit, closure/reopening and profile workflows. Inspection exposed that the destination previously ignored the facility query and could select NPM. It now selects the requested registered facility, updates its holder terminology, and offers a contextual facility breadcrumb. No new registry or registration engine was added. The roster destination remains `/list-stall-holders?facility=ncc`.

Vegetable/Fruits Monthly receives the shared improvements and keeps its dedicated roster/management routes. Daily CT implementation is unchanged. Monthly OR settlements, import/numbering, account kinds, canonical balances and all financial writers/contracts remain unchanged. No backend, Domain, Application, Mobile or database change was needed.

Validation: focused Web **83 passed / 0 failed / 0 skipped**; final full Web **779 passed / 0 failed / 0 skipped**; final Client Release **0 warnings / 0 errors**. CSS sources/bundle **155** brace-balanced; diff whitespace check passed. Unit/Integration/Mobile suites were not repeated for this Client-only follow-up; no shared financial contract changed. The earlier full Unit result below is historical evidence, not a rerun for this follow-up.

Fresh Chromium fixture screenshots cover Kanmanggay overview/management/expanded canonical periods/roster, Vegetable/Fruits Monthly overview/management/roster, and NCC overview/management/roster at 1440px, 1024px and 390px. Fixture PDFs retain 1-page small documents and a 5-page 90-row A4-landscape roster. These are real rendered components with test data, not authenticated tenant browser acceptance. Computer-use initialization still fails with the Windows sandbox-helper error. Local API/Client listeners were present on 7097/7167 during this follow-up; their running processes were not restarted or used to claim fresh authenticated acceptance. Review the latest built apps and confirm visual acceptance before integration.

### Exact files changed by this follow-up

- `EEMOCantilanSDS.Client/Services/FacilityState.cs`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/ButtonGroup/ActionBar.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/FacilityStallsTable.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/SH/StallHoldersList.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/Toolbar.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/Toolbar.razor.css`
- `EEMOCantilanSDS.Client/Components/Shared/FacilityPage.razor`
- `EEMOCantilanSDS.Client/Components/Shared/ObligationWorkspace.razor`
- `EEMOCantilanSDS.Client/Components/Shared/ObligationWorkspace.razor.css`
- `EEMOCantilanSDS.Client/Components/Shared/OperationTopbar.razor.css`
- `EEMOCantilanSDS.Client/Components/Shared/SpaceAccountSnapshot.razor`
- `EEMOCantilanSDS.Client/Components/Shared/SpaceAccountSnapshot.razor.css`
- `EEMOCantilanSDS.Client/Components/Shared/SpaceHolderManagement.razor`
- `EEMOCantilanSDS.ComponentTests/Pages/ObligationWorkspaceTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/SpaceHolderWorkspaceTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/SpaceRentalConsistencyTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/VendorRegistryTerminologyTests.cs`
- `docs/planning/SPACE_HOLDER_WORKSPACE_HANDOFF_20261008.md`

Ignored screenshot/PDF/HTML/check artifacts remain under `artifacts/space-holder-workspace/` and are not committed.

## Checkout and authority

Physical checkout: `C:\dev\stalltrack\eemo`. Starting branch: `codex/vegetable-fruit-space-workspace`, clean at `5fc26ae6`. Existing Vegetable/Fruits commits remain in its ancestry. No branch switch, reset, stash, second checkout, worktree, origin push, master merge, migration, deployment or APK publication occurred in this follow-up.

The unchanged integration branch resolves to `4b4be8f8641ad50f43198b0f724d3d4c3d5a817e`. Both the earlier Vegetable/Fruits checkpoint and this restructure await visual acceptance before same-checkout integration. Concurrent edits to the three existing planning files were preserved rather than staged as this session's work; this handoff supplies the final validation evidence.

Kanmanggay uses `KanmanggaySpaceRental`; Vegetable/Fruits Monthly uses `VegetableFruitSpaceRental`. NCC keeps its stall/occupancy source and existing component. No Application/Domain/API/Mobile contracts or financial writers changed.

## Routes and responsibilities

| Operation | Overview | Official roster | Account management |
|---|---|---|---|
| Kanmanggay | `/operations/kanmanggay` | `/operations/kanmanggay/space-holders` | `/operations/kanmanggay/manage-space-holders` |
| Vegetable/Fruits Monthly | `/operations/vegetable-fruit` | `/operations/vegetable-fruit/space-holders` | `/operations/vegetable-fruit/manage-space-holders` |

New hosts retain `SuperAdmin,Admin` authorization and operation breadcrumbs/back links. Overview holder/management actions navigate independently; `_holders` is removed. Overview retains the accepted hero, search, Reports, Import and Add New, with at most ten matching account snapshots. Open account links carry the actual account ID to management.

`SpaceAccountPresentation` owns source-specific display/navigation configuration. `SpaceAccountSnapshot`, `SpaceHolderRoster` and `SpaceHolderManagement` separate responsibilities. Existing `ObligationWorkspace` retains account/register loading and registration/rate drawers; the roster is separate from that operational component.

## Official roster and unavailable fields

`ReportLetterhead` uses Branding for seal, office, municipality and province. Title: **SPACE HOLDER LIST**. Operation subtitle: **KANMANGGAY** or **VEGETABLE / FRUITS**.

Columns: row number, actual occupant, server-assigned space number, occupancy basis, recorded signed-contract reference, occupancy effectivity, current approved monthly rental and lifecycle status. Source-owned ActualOccupant is preferred over compatibility payer display. SpaceOnly says **No contract (space only)** and never emits a contract reference. Effectivity is expressly occupancy start, not proof of a signed lease.

Search and Active/Closed/All scope apply to the document, count, CSV and Excel. Excel reuses typed `SpreadsheetBuilder`; CSV uses `CsvCell` neutralization. Print/Save as PDF reuse `stalltrackPrint.landscape`, exclude controls, keep rows whole and repeat table headers. Failed loads offer retry and disable issuance rather than claiming no holders exist.

The API cannot reconstruct an historical roster, so this is explicitly a current register with a successful-load date and no year selector. Duration, area/location, separate signed-lessee name, expiration and annual target are unavailable and not invented. No rate-history timeline is constructed; only recorded current amount/effective month is shown. An unavailable closed-account rate remains a dash.

## Management and financial scope

Active/Closed lifecycle and All/Paid/Partial/Unpaid payment position are independent. Closed accounts retain historical balances. Totals preserve existing full-operation/to-date scope including closed accounts; search/filter changes do not recalculate them. Matching/registered/active counts state their different scopes; failed loads do not show zero money.

Periods use the existing account-ID register endpoint with truthful failure/retry states. Late responses cannot replace the newly selected account's periods. Approved amount changes preserve the existing effective-month request/validation and refresh the open register. Head/SuperAdmin closure requires confirmation, sends the existing command and preserves assessed periods/Collections. No delete action exists. Registration/import retain source identity, preview/conflict validation and server numbering.

Vegetable/Fruits Daily CT markup/activity/writer remain unchanged, without a Monthly account or assessment prerequisite. Monthly OR settlement and canonical allocations remain backend-owned. No second financial identity, assessment engine or collection writer was introduced.

## NCC and existing documents

The former NPM-only conditional mislabeled NCC as Space Holder List. NPM/NCC/TCC/BBQ now consistently use **Stall Holder List**, Stall search/count nouns and document/chrome titles. ICE/custom facilities keep their existing Space terminology. `/list-stall-holders?facility=ncc` keeps its source data and all paper columns. CSV filenames already say `stallholders`; their data contract is unchanged. Global `/stall-holders` is unchanged.

Actual PDF review exposed an existing 900px screen minimum height that printed a blank second landscape page. Print now clears that floor, with a regression test. A narrow print rule lets the two holder-document families use the paper width rather than reserving hidden-sidebar width; other reports are unaffected.

## Final validation

- Focused Web run: **56 passed / 0 failed / 0 skipped** before the additional print-height regression. Final full Web: **770 passed / 0 failed / 0 skipped**, including that regression and existing imports, global holder-report, NPM PureDays and Monthly/Daily checks.
- Full Unit: **2,525 passed / 0 failed / 0 skipped**.
- Final Client Release: **0 warnings / 0 errors**. Earlier recompilation emitted existing compiler/package warnings; this is the final standalone build result.
- `git diff --check` passed. **154** scoped CSS sources, print/bundle stylesheets checked were brace-balanced.
- PostgreSQL Integration and Mobile tests/builds were not rerun: no backend/shared-contract/persistence/Mobile change. API Release was not separately run; Unit compilation built its existing dependency.
- Chromium rendered actual bUnit components with the application's CSS at **1440px and 390px**, with distinct overview/roster/management structures and no horizontal page overflow. These are test fixtures, not a logged-in API-backed review.
- Actual PDFs were opened, text/bounds checked and rasterized: small Kanmanggay **1 page**, Vegetable/Fruits **1 page**, NCC **1 page**, and a **90-row** Kanmanggay roster **5 A4 landscape pages**. Every test holder appears once, continuation headers repeat, no page is blank and text stays within paper bounds. No printed controls or unsupported columns appear. NCC's former blank second page was reproduced before the height fix.
- Ignored `artifacts/space-holder-workspace/` holds HTML, screenshots, PDFs, page PNGs and JSON checks. No fixture account or Collection was written to a database.

Computer-use initialization fails with `windows sandbox failed: helper_unknown_error: setup refresh had errors`. Logged-in routes, real tenant data and interactive local-data print dialogs remain a human acceptance gate at configured `https://localhost:7167`. Earlier Vegetable/Fruits acceptance is also pending. The earlier runtime note is historical; neither API nor Client had a local listener when checked in this follow-up.

## Next gate

Review both operation overviews and their new routes, NCC and global `/stall-holders` with freshly built local apps. Confirm desktop/narrow behavior, registration visibility/numbering, authorized account actions and local-data PDFs. Only after user acceptance should this branch fast-forward into `integration/report-governance-ui` in this checkout. Production remains the separately recorded October 5 release.
