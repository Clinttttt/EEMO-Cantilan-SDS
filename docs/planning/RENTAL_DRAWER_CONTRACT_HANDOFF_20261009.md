# Rental drawer consistency and contract governance handoff

Date: October 9, 2026. Local feature only; user visual acceptance and integration remain pending.
Checkout: `C:\dev\stalltrack\eemo`, branch `codex/vegetable-fruit-space-workspace`, starting HEAD `c92b46f9`.

## Business governance

[The authoritative local clarification and capability matrix](../business/MEEDO_CONTRACT_CLARIFICATION_20261009.md) records the project owner's relayed MEEDO three-year standard for Kanmanggay, NPM, NCC and TCC. `EEMO_BUSINESS_RULES.md` links it. It separates duration from payment frequency and renewal from successor occupancy, reopening and effective-dated rental approval. Existing NPM/NCC/TCC Contract/occupancy history is reusable; Kanmanggay lacks a signed-term chain/expiry. Prior successor-account reopening advice is not a blanket renewal requirement.

Pending office decisions include signed renewal evidence, early termination approval, Space Only/Extension exceptions, expiry/holdover and billing effects, rate approval at renewal and other-facility scope. No new persistence, migration, renewal/reopening, automatic expiry, historical backfill, financial API or assessment change was implemented. Existing Mobile Monthly OR / Daily CT and all source IDs/writers remain untouched.

## Implemented interaction

- Kanmanggay overview/management and Vegetable/Fruits Monthly overview/management no longer expose standalone Import list. Reports, roster, management and Add New remain; Daily is unchanged.
- Add New retains every current field, signed/Space Only choice, server numbering and account submit request. Import list is the leading footer action; Cancel and Open account follow on the right.
- `AddNewDrawer.LeadingFooterActions` is optional. Other callers keep their footer; Cancel and Submit are unavailable during save. The caller's import action is a non-submit button with both a disabled state and a save-in-progress handler guard.
- Existing routes remain `/operations/kanmanggay/import`, `/operations/vegetable-fruit/import` and `/operations/fiesta-araw/import`. Fiesta/Araw shares the same placement because it already had import. No upload engine or preview/validation behavior changed.
- NCC/TCC/NPM/BBQ/ICE already use registration-footer import via `AddVendorModal`; their handlers and `/import/{facility}` routes are unchanged. No import capability was invented for other sources. History import remains its separate existing action.
- Responsive review found the existing facility import pair off-screen at 390px. The shared drawer now wraps its existing import group and footer. Desktop placement is preserved; fixed footer and scrollable form body remain intact.
- The verified convention is recorded in [V3 presentation direction](../interface/STALLTRACK_UI_V3_DIRECTION.md), independently of contract policy.

## Verification

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Focused Web drawer, space-account and existing facility registration tests | 112 | 0 | 0 |
| Full Web component suite | 813 | 0 | 0 |

Client Release passed initially with 71 warnings / 0 errors; final incremental build after the responsive CSS fix passed with 0 warnings / 0 errors. Client scoped CSS sources plus generated bundle: 155 brace checks passed. `git diff --check` passed. No backend/shared contract changed, so Mobile, API, Unit and PostgreSQL suites were not rerun for this UI-only patch; the contract audit uses existing source/tests as evidence rather than speculative APIs.

New coverage checks all five source-page combinations, independent source import routes, non-submission, cancel, server account submit preservation, duplicate/save guards, optional footer/form association and five existing facility imports. Existing authorization and Fiesta advance-confirmation coverage passed in the full suite. During test development, a dynamic-render compile error and two missing-service fixture failures were corrected; final focused and full runs are green.

Rendered bUnit fixtures with actual Client generated CSS were checked in headless Chrome at 1440, 900 and 390px: eight registration drawers and five overview/management/Daily fixtures (39 viewport checks), including import placement, footer bounds, keyboard tab order and no page overflow. The initial facility-footer overflow was reproduced before the wrapping fix. Screenshots/facts/test logs/TRX files stay in ignored `artifacts/rental-drawers/` and `artifacts/space-holder-workspace/`. These are component fixtures, not authenticated localhost screenshots. UI automation reported no app/browser surfaces; actual signed-in operation/drawer/import review remains a user visual gate.

## Changed files and source control

Implementation:

- `EEMOCantilanSDS.Client/Components/Shared/AddNewDrawer.razor`
- `EEMOCantilanSDS.Client/Components/Shared/ObligationWorkspace.razor`
- `EEMOCantilanSDS.Client/Components/Shared/VendorDrawerShell.razor.css`

Tests:

- `EEMOCantilanSDS.ComponentTests/Pages/AddNewDrawerFooterTests.cs` (new)
- `EEMOCantilanSDS.ComponentTests/Pages/AddVendorModalSpaceNumberingTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/ObligationWorkspaceTests.cs`
- `EEMOCantilanSDS.ComponentTests/Pages/SpaceHolderWorkspaceTests.cs`

Documentation: business rules index, new contract clarification, V3 direction and this new handoff. Business clarification commit: `3654efb2`. UI/test checkpoint: `4c52d920` (`refactor(web): align rental import action with add-new drawers`).

The concurrent unstaged `ACTIVE_WORKSTREAMS.md` (4 additions), `CURRENT_RELEASE_STATE.md` (7 additions / 1 removal) and `VEGETABLE_FRUIT_WORKSPACE_20261008.md` (10 additions) remain unchanged by this work and excluded from commits. Their future canonical checkpoint merge should link this handoff once the owning edits and visual acceptance are reconciled.

Next: review the safe UI in authenticated localhost; approve contract design only after MEEDO answers the open questions. Integration is a separate later action after acceptance. No branch/worktree creation, integration, push, master merge, production database change, deployment or APK publication occurred.
