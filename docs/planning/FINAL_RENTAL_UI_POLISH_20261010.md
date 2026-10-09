# Final rental UI polish — October 10, 2026

Local implementation checkpoint, not integration or production release. Checkout `C:\dev\stalltrack\eemo`; branch `codex/vegetable-fruit-space-workspace`; starting HEAD `cb0922d3`. The owner has accepted the shared NPM-style registry toolbar. That acceptance does not imply acceptance of all drawers, reports or pending lifecycle proposals.

## Changes and preserved behavior

- Removed the management-only panel heading from ObligationWorkspace. Its shared table now immediately follows the accepted toolbar, like ICE/NPM. Ordinary non-registry headings and selected-account profile headings remain.
- Retained independent lifecycle/payment combinations in a small keyboard-accessible advanced-filter disclosure beside count/Add. Closed remains in the primary strip. The disclosure opens within the viewport on narrow screens. Roster and Reports remain on the operation overview/navigation and at existing bookmarked URLs; they no longer occupy a second table-heading strip.
- KM uses the actual ICE `FhSelect` duration component and scoped styling, with only the approved `3 years` option. ICE's existing presets/custom behavior is unchanged. Existing server validation still rejects other recorded KM durations; historical null terms stay null. No expiry, renewal, reopening or billing behavior was introduced.
- Shared RentalUnitNumberField now says `Auto-suggested next Space No.` and falls back to `Assigned upon registration`. Preview remains read-only; existing source-scoped final server allocation and concurrency handling are unchanged.
- RentalRateFields accepts an optional label; KM/VF use `Approved Monthly Rental (₱)`, while ICE defaults remain unchanged. Both input/card still edit one existing approved-amount state; annualized equivalent is display only.
- Investigated the mismatched facility URL observation: physical-facility selection previously changed ActiveFacility without updating the URL. Vendor now navigates to the matching existing facility query; query changes reselect their source, including returning to NPM when the facility query is absent. This explains a possible stale URL without asserting that the supplied screenshot proved a tenant leak. Tests exercise source switching and query restoration. Authenticated refresh/back/forward acceptance is pending.

Unchanged: eight-column RentalRegistryTable, compact five-icon RentalRegistryActionButtons, source-specific APIs and role gates, actual occupant versus signed name/reference, exact contract evidence, optional area/location, signed/SpaceOnly semantics, original registration/import/save guards, OR/CT policies, source numbering, authoritative periods/rates, and canonical Collection/SRC history. FA remains event-based and receives no KM duration or monthly rental inputs. Obligation reopening cannot invoke a physical Stall toggle.

## Validation

| Executed check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Focused RentalConsolidation + SpaceHolderWorkspace components | 65 | 0 | 0 |
| Full Web components, separate from Unit | 864 | 0 | 0 |
| Full Unit | 2,526 | 0 | 0 |

Client Release: 0 warnings / 0 errors. Source CSS (162 files) plus generated scoped bundle: brace-balanced. `git diff --check` passed. Initial focused failures were test expectations tied to the removed header and incorrect direct assignment of query-supplied parameters; corrected tests navigate through NavigationManager. Existing component restore/analyzer warnings, including AngleSharp NU1902, were observed; no package or unrelated warning remediation is included.

Commands: `dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RentalConsolidationTests|FullyQualifiedName~SpaceHolderWorkspaceTests"`; full Web with `--no-build`; full Unit `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release --no-restore`; Client `dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release --no-restore`.

Rendered bUnit fixtures in headless Chrome: 40 registry viewport checks for eight sources, plus 25 drawer/open-duration checks across 1536/1440/1280/1024/390px. All standard desktop registries retained one compact primary row; no page-width overflow. ICE/KM duration triggers both measure 42px. All checked drawer footers remain visible and bodies scroll independently. Screenshots were inspected for ICE/KM desktop registry, ICE/KM duration open, KM narrow drawer, long-name narrow registry and missing-number fallback. Native advanced filtering was opened by keyboard in long-name fixtures at 1440/390px; inspection caught and corrected its initial left clipping. Evidence is ignored under `artifacts/registry-toolbar-parity/`, not committed. These are rendered fixtures, not authenticated tenant data or full browser financial workflows.

The in-app browser still shows the local Administrator Login page. **Authenticated final visual acceptance remains pending**, including runtime refresh/back/forward, collection evidence, dropdown/focus behavior and actual source data. No Android, production or new PDF verification is claimed; roster/print/export components were not changed.

## Documentation and gates

Reviewed the master/current-release/authority, operational, architecture, ADR-007, V3 and decision guidance and recent handoffs. Corrected the authoritative MEEDO clarification's stale KM capability matrix to reflect already implemented nullable descriptive evidence. Added a supersession notice to the historical Phase 1 handoff, preserving its original evidence. The three concurrently modified planning documents remain untouched and excluded from staging; their older implementation/test statements need coordinated reconciliation later.

The October 9 PostgreSQL checkpoint remains historical verified evidence (15 passed). Its local migration inspection recorded KanmanggaySpaceLeaseEvidence20261009 as applied locally, not in production. This UI-only turn did not reapply or inspect a production migration and did not change backend/contracts, so additional PostgreSQL/API/Mobile validation was not required. Complete allocation-history attribution is not implemented; reopening, renewal, term-chain and expiration enforcement remain policy/design gated. Unknown historical contract evidence is not backfilled.

## Exact scoped files

- Client `Components/Pages/Menus/Vendor.razor`.
- Client `Components/Shared/ObligationWorkspace.razor` and `.razor.css`.
- Client `Components/Shared/RentalRegistryToolbar.razor` (optional secondary-action slot; original layout retained).
- Client `Components/Shared/RentalUnitNumberField.razor` and `RentalRateFields.razor`.
- ComponentTests `Pages/RentalConsolidationTests.cs` and `SpaceHolderWorkspaceTests.cs`.
- `docs/business/MEEDO_CONTRACT_CLARIFICATION_20261009.md`.
- `docs/planning/UNIFIED_FACILITIES_PHASE1_HANDOFF_20261009.md`.
- This handoff.

No branch/worktree change, integration, push, master merge, production migration/data change, deployment or APK publication. Next step: owner acceptance of the final rendered/runtime polish; remaining lifecycle and full-history work require their separate approvals.
