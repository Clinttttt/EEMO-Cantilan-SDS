# Fish / Meat registry UI refinement — 2026-10-15

Implementation evidence and integration handoff to CORE BRAIN V6. This is a local, unmerged feature; it changes no business, financial or security authority.

## Checkout and integration

- Initial commit: `0fd23ac2`.
- Branch: `codex/fish-meat-registry-ui-refinement`.
- Worktree: `C:\dev\stalltrack-v2-worktrees\fish-meat-registry-ui-refinement-20261015`.
- Commit reference: the focused commit containing this document (`git log -1` on this branch).
- During verification the primary checkout advanced independently to `40a3b1cb` through mobile-only changes. Those changes were not merged here. The separate rental worktree at `28bfc69f` was not merged or modified.
- No integration, push, deployment, schema migration or production access was performed. The primary checkout's eight protected modifications were not edited or staged.

## Root causes and resulting presentation

The registry's list selectors and Manage link were separated from its primary toolbar. Its Import shortcut also competed with Register vendor. These now use one page-scoped sticky toolbar, retaining the existing shared Toolbar implementation:

`All | Fish | Meat | Registered N | Needs reg. N | NPM links N | Manage | Reports | Office register | Register vendor | Search`

Collection Activity's page shortcut is removed; global navigation is untouched. Registered counts still come from active registry data, Needs reg. from direct-payor follow-up, and NPM links from eligible unresolved association review. Missing follow-up/NPM counts remain unknown, not a fabricated zero. Fish/Meat filters are disabled in Needs reg. because an unregistered payor has no inferred vendor type.

The toolbar stays below `--topbar-h` and uses a contained horizontal rail where necessary. A scoped override prevents the shared toolbar's small-screen vertical flex rule from turning the search field's desktop basis into a 150px height. No generic Toolbar, shell, navigation or design-system CSS was changed.

The registration form now groups identity and office registration details into balanced two-column fieldsets, collapsing to one column below 560px. Nature of business and Address span the available width. Vendor name remains the required identity; optional structured names never replace it during editing. Existing ChoiceCards, selects, maximum lengths, enums, form model, submit callbacks and shell are reused.

Import list is a leading secondary footer action, opt-in through `ShowImport`. It appears only for an ordinary SuperAdmin registration with no NPM source context. Renewal, NPM confirmation, details and Admin contexts do not offer it. Navigation with modified fields uses an explicit Keep editing / Continue confirmation and preserves the form model until navigation. Opening Import does not submit Register.

Manage reuses FacilityHero for its civic-blue identity and keeps the back link. Vendor cells show business name only on their secondary line; Address stays in its own column. Details / Renew / Close form a compact neutral action group with their existing authorization and lifecycle conditions. Details show only populated optional office fields, alongside the unchanged NPM evidence, historical references, creation information and separate Vendor Fee / Weight & Measure figures.

Import Sex, Type and Registration native selects have scoped borders, spacing, hover/focus/disabled states. ImportGrid's existing single SVG chevron and native keyboard behavior remain intact. Parsing, preview and save code are unchanged.

## Changed files

| File | Purpose |
| --- | --- |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorFees.razor` | Unified controls, compact labels and opt-in drawer import access |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorFees.razor.css` | Scoped sticky horizontal rail and compact controls |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorImport.razor.css` | Scoped native-select presentation |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorManage.razor` | Shared hero, business-only secondary line, grouped actions and populated office details |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorManage.razor.css` | Action alignment, status and address hierarchy, narrow facts layout |
| `EEMOCantilanSDS.Client/Components/Shared/VendorRegistrationDrawer.razor` | Shared form grouping, opt-in import navigation and unsaved navigation guard |
| `EEMOCantilanSDS.Client/Components/Shared/VendorRegistrationDrawer.razor.css` | Responsive form grid and footer import placement |
| `EEMOCantilanSDS.ComponentTests/Pages/FishMeatVendorRegistryTests.cs` | Toolbar order/counts, failure semantics, form grouping and existing registry workflows |
| `EEMOCantilanSDS.ComponentTests/Pages/FishMeatVendorManageTests.cs` | Hero, populated office fields, no duplicate address and retained action rules |
| `EEMOCantilanSDS.ComponentTests/Pages/NpmVendorAssociationTests.cs` | NPM-assisted registration does not expose import navigation |
| `EEMOCantilanSDS.ComponentTests/Pages/VendorRegistrationDrawerTests.cs` | Opt-in/role/context restrictions and unsaved navigation behavior |
| This document | Implementation and verification handoff |
| [PLATFORM_OPERATOR_LOCAL_DIAGNOSTIC_20261015.md](PLATFORM_OPERATOR_LOCAL_DIAGNOSTIC_20261015.md) | Separate read-only database/account findings |

## Safeguards and shared-component review

The registration API, tenant context, server authorization, operation IDs and retry/idempotency rules are unchanged. Registration remains independent from NPM; association still requires its existing evidence. Vendor type is separate from Nature of business. Import remains registry-only and ignores monetary spreadsheet columns. Collection amounts, receipts, history and lifecycle operations were not modified.

VendorRegistrationDrawer is the only shared component changed. The new action defaults off, with tests for renewal, NPM context and Admin restrictions. Existing ordinary/NPM/renewal callbacks and models remain authoritative. Toolbar, FacilityHero, ChoiceCards, VendorDrawerShell and ImportGrid were reused without modifying their source. Other facility and space import components are unchanged.

## Automated verification

- Focused registry, Manage, NPM association and shared drawer tests: **47 passed, 0 failed, 0 skipped** (Release).
- Full Head Portal component suite: **1,217 passed, 0 failed, 0 skipped** (Release). An earlier full run caught three boolean ARIA attributes in the new controls; these now render explicit `true`/`false`, and the full rerun passed. A subsequent focused run passed after strengthening the unavailable-count assertion.
- Client Release build with `--no-restore`: succeeded, **0 warnings, 0 errors** on final incremental build.
- Existing package/analyzer warnings appear during component compilation, including the pre-existing AngleSharp 1.2.0 advisory. Dependencies were not changed in this UI task.
- `git diff --check` passed; all four changed scoped CSS files and the generated Client CSS bundle passed brace validation.

No database integration suite was required for these frontend-only changes. The separate local database diagnostic used read-only transactions and rollback; it made no mutations.

## Real Chrome verification and limitations

Disposable loopback fixture: `http://127.0.0.1:5310/operations/fish-meat-vendor-fees`. It renders the actual feature components and CSS with an isolated fake Head identity and mocked APIs, not the authenticated full municipal portal. It contains no real credentials and performs no real API/database writes. It is intentionally separate from existing local portal/API processes.

Tested **1600, 1440, 1280, 1024, 768 and 390px**, at 900px height:

- Registry toolbar, count labels, contained rail, sticky topbar offset and no page-level horizontal overflow.
- Ordinary registration grid, single-column mobile layout, body scrolling, fixed footer, independent personal-name edits and unsaved import confirmation.
- NPM candidate review and absence of import navigation there.
- Manage hero/table, business-only secondary line, recorded address preserved, office-information details, separate collection figures, Renew/Cancel and Close/Cancel.
- Legacy six-column CSV upload into review, editable Sex / Type / Registration selects, one chevron, focus and keyboard traversal; no import submission.
- Keyboard access to Register, navigation confirmation, and Escape dismissal. A mocked registration validation failure is safe fixture behavior, not a real registration.

Machine results and screenshots are outside Git in `C:\dev\stalltrack-v2-worktrees\fish-meat-registry-qa`: `browser-results.json`, `check.cjs`, `registry-*.png`, `drawer-*.png`, `details-*.png`, `manage-*.png`, `import-*.png`, `focused-verified.log`, `component-release.log`, and `client-release.log`.

CORE BRAIN V6 must review/integrate this branch before treating it as the primary baseline, then rebuild/restart only its owned Head Portal process using the existing launch/environment configuration. No launch settings or existing listener ports were changed. Hard-refresh after that process serves the integrated build. Existing API, Platform Admin, rental preview and other agents' processes must remain running.

The local fixture is useful for presentation review; acceptance against a real authenticated tenant portal remains an integration-stage check. No real registrations, imports, renewals, closures or financial transactions were used to verify this UI.
