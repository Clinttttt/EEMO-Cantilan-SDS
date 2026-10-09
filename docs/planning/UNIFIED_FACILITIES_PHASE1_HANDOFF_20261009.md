# Unified Facilities Workspace V3 — Phase 1 pilot

Date: 2026-10-09. Implementation on `codex/vegetable-fruit-space-workspace`, starting at `4e6b3a32`, in `C:\dev\stalltrack\eemo`. Local only; authenticated visual acceptance and integration remain pending.

## Implemented boundary

Option B is implemented as a small presentation foundation, piloted by ICE, KM and FA. `RentalWorkspaceCatalog` is Client-only metadata; presentation keys and display codes are not persistence keys. Its three entries describe existing routes, typed source ownership, terminology, registration/import availability, supported sections/actions, numbering, report availability and contract-policy scope. It does not authorize actions or execute writes.

Facility names remain resolved by tenant-backed `FacilityState`. KM reuses existing `SpaceAccountPresentation` names/routes rather than introducing another route authority. Further rental operations can adopt the foundation in a later approved slice; the pilot catalog is not a replacement for the tenant's facility activation catalog or the sidebar.

| Pilot | Presentation adapter owner | Preserved backend | Scope and navigation |
|---|---|---|---|
| ICE | `FacilityPage<IceplantStall>` with `FacilityRentalSource(ICE)` | Existing Stalls/Facilities/Payments APIs and registration command | Current Philippine business month; `/facility/ice` and `/ice` retained; existing holder, vendor-management and `/ice/reports` destinations |
| KM | `ObligationWorkspace` with `MonthlySpaceRentalSource(KanmanggaySpaceRental)` | Existing obligation account, period/rate/closure and import APIs | All registered accounts to date, including closed accounts; `/operations/kanmanggay`, dedicated roster/management, existing report/import |
| FA | `ObligationWorkspace` with `EventLotRentalSource(FiestaArawLotRental)` | Existing event-lot obligation and import APIs | All event accounts to date; `/operations/fiesta-araw`, existing event report/import; no invented roster or management routes |

Adapters reject a mismatched presentation/source pairing. Existing handlers own registration, search/filter state and server results. Catalog capabilities describe availability; the original route authorization, Head-only controls and server guards remain authoritative.

## Shared and source-specific composition

- New: `RentalWorkspaceHero` wraps the existing `FacilityHero` and accepts caller-owned figures and a scope label. `FacilityHero.Scope` is optional; other callers retain their prior output.
- New: `RentalWorkspaceNavigation` renders only existing supported destinations, with responsive wrapping and focus-visible links. Registration is caller content, retaining its authorization and handler.
- Reused: municipal topbars, `Toolbar`, `AccountStatusPill`, `AddNewSection`, `AddNewDrawer` and `VendorDrawerShell`, including scrolling body and Import-left / Cancel-submit-right footer.
- Source-specific: `AddVendorModal`, ICE stall mapper/table, obligation registration/assessment-period table and all account mutation handlers, official rosters, imports and reports.

KM registration now uses Lessee Information, Space & Contract Details and Rental Rates. Signed-contract reference and Space Only remain unchanged. Space number is informational and server-assigned; no number input or unsupported contract-name, duration, area or renewal field was introduced. No annualized figure was needed for this slice.

FA retains Event & Occupant Information and Lot & Event Details. The panel and record count use lots; responsive cards reuse the management table's existing layout so event facts, approved amount, balances and actions stay visible on narrow screens. The event selection, date, optional lot label/server numbering, approved amount and future-month confirmation remain unchanged. It has no monthly/annual rent or three-year term controls.

ICE retains its registration fields, suggested-number validation, contract inputs and import/history-import footer. Its existing three-year default is not interpreted as confirmation that the new MEEDO three-year rule applies to ICE. KM's reported standard is catalogued as pending contract-lifecycle approval, with no persistence or billing implementation. See [the contract clarification](../business/MEEDO_CONTRACT_CLARIFICATION_20261009.md) and [the Mobile successor-account audit](MOBILE_SPACE_SOURCE_FOLLOWUP_20261009.md).

## Validation

Before the presentation refactor, the obligation/drawer characterization selection passed **112 / 0 / 0** (passed/failed/skipped); the facility selection including the new ICE characterization passed **9 / 0 / 0**. Draft test failures during development were corrected: old KM section expectations, test authorization setup, permitted FA operation discovery, and ICE's empty-result view without a table.

Final executed commands:

```powershell
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter 'FullyQualifiedName~RentalWorkspacePilotTests|FullyQualifiedName~SpaceHolderWorkspaceTests|FullyQualifiedName~ObligationWorkspaceTests|FullyQualifiedName~SpaceRentalConsistencyTests|FullyQualifiedName~AddVendorModal|FullyQualifiedName~AddNewDrawerFooterTests'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-build
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release
git diff --check
```

Results: focused Web **131 passed / 0 failed / 0 skipped**; full Web **824 passed / 0 failed / 0 skipped**; Client Release **succeeded, 0 warnings / 0 errors** on the final incremental invocation. All **155** Client `.razor.css` sources have balanced braces. Generated scoped CSS also checked. Whitespace check passed.

No backend/shared financial contract or Mobile code changed, so no API/Mobile build, PostgreSQL integration or financial unit suite was required or rerun for this presentation slice.

## Visual evidence and acceptance gate

Ignored `artifacts/facilities-phase1/` contains before/after HTML from real bUnit-rendered components, Chrome screenshots and viewport measurements at **1440 / 1024 / 390px**. The after set covers ICE overview/drawer, KM overview/drawer/management and FA overview/drawer/success/advance confirmation. These are component fixtures with test data, not authenticated tenant screenshots. Review found no page overflow or footer overlap; FA now exposes its amounts/actions in narrow cards. The ICE drawer body remains independently scrollable, including its existing import-history action.

The computer/browser connector reported no available apps or browser sessions. Authenticated route inspection and user visual acceptance were **not performed**. These remain the gate before integration. No production, database, push, integration, master merge or APK action occurred.

## Exact task file inventory

Under `EEMOCantilanSDS.Client/`:

- `Services/RentalWorkspaceCatalog.cs` (new).
- `Components/Shared/RentalWorkspaceHero.razor` (new).
- `Components/Shared/RentalWorkspaceNavigation.razor` (new).
- `Components/Shared/RentalWorkspaceNavigation.razor.css` (new).
- `Components/Shared/FacilityHero.razor`.
- `Components/Shared/FacilityHero.razor.css`.
- `Components/Shared/FacilityPage.razor`.
- `Components/Shared/ObligationWorkspace.razor`.
- `Components/Shared/SpaceHolderManagement.razor`.
- `Components/Pages/Menus/Facilities/ICEPLANT.razor`.
- `Components/Pages/Menus/Kanmanggay.razor`.
- `Components/Pages/Menus/FiestaAraw.razor`.

Under `EEMOCantilanSDS.ComponentTests/Pages/`:

- `RentalWorkspacePilotTests.cs` (new).
- `SpaceRentalConsistencyTests.cs`.
- `ObligationWorkspaceTests.cs`.

Documentation: this handoff only. Concurrent changes in `ACTIVE_WORKSTREAMS.md`, `CURRENT_RELEASE_STATE.md` and `VEGETABLE_FRUIT_WORKSPACE_20261008.md` were preserved and excluded from staging. Canonical planning reconciliation belongs to their current editing session.

## Later phases — not implemented

1. Phase 2: source-supported registration-field parity and adoption by further approved rental operations.
2. Phase 3: holder-document and management presentation consolidation, retaining source-specific APIs.
3. Phase 4: shared report shell using verified endpoints; no fake Monthly/Yearly/Status/History tabs.
4. Phase 5: separately approved contract-term persistence, expiration policy, renewal and audited successor occupancy. No reopen toggle, automatic backfill or new assessment behavior is implemented here.

Collections/SRC, official Space Rental classification, approved rate history, assessment/allocation engines, OR/CT policies, tenant isolation, source identity, closure evidence and Mobile Monthly/Daily collection paths remain unchanged.
