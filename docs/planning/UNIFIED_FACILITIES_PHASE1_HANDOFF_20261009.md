# Unified Facilities Workspace V3 — Phase 1 pilot

## Current checkpoint: actual component consolidation, 2026-10-09

This follow-up starts at `5ae3483b` on `codex/vegetable-fruit-space-workspace`, in the same physical checkout. Implementation commit: `8bd944c8` (`refactor(web): consolidate rental registration tables and source registries`); tests and this handoff are a separate verification commit. The original Phase 1 checkpoint below is historical. Its visual primitives were insufficient: KM still independently rendered registration, overview and management tables. The current patch extracts the actual ICE/NCC field and table implementations and makes both source families consume them. No domain migration, source cutover, integration or production release is included.

### What is genuinely shared

- `AddVendorModal` and the monthly-space branch of `ObligationWorkspace` both render `RentalLesseeFields`, `RentalContractFields`, `RentalUnitNumberField`, `RentalEffectivityField` and `RentalRateFields`. The original common markup has been removed from the callers. Their existing registration validation and save handlers remain source-specific.
- `FacilityStallsTable` delegates its monthly table to `RegisteredRentalTable<TStall>`; KM, Vegetable/Fruits Monthly and FA use the same table with `ObligationAccountDto` rows. `RentalSummaryStrip` is extracted from the facility overview. The previous independent `SpaceAccountSnapshot` and its CSS are removed.
- `/vendors` and source-scoped management render the same `RentalRegistryHero`, `RentalRegistryTable<TItem>`, existing `Toolbar` and `RentalWorkspacePicker`. The original stall registry's actions, contract-expiry warning, authorizations and handlers remain intact. The separate monthly-space management table branch is replaced by this shared registry.
- Shared field state is `IRentalRegistrationState`. `VendorModalForm` implements it; `ObligationRentalRegistrationState` delegates to the existing occupant, arrangement and approved-amount fields. The rate input and Base Rental spinner edit one value. Annualized amount is display-only monthly rate × 12.
- `RentalTableRow<TItem>` keeps the original typed source object. Facility adapters remain in `FacilityStallsTable`/`Vendor`; obligation adapters remain in `ObligationWorkspace`. `ClosedObligationRentalRow` is a read-only adapter, not a universal account or financial writer.

Existing `VendorDrawerShell`, `AddNewDrawer`, `AccountStatusPill`, `RentalWorkspaceHero`/`FacilityHero`, municipal topbar and navigation primitives are reused. Source-specific facility selection, NPM section/rates, area/notes, utilities, event registration, import engines, reports and mutation handlers remain separate. No large facility-code switch replaces these boundaries.

### Registration field compatibility

| ICE/NCC field | Existing facility destination | KM destination / current behavior |
|---|---|---|
| Basis of Occupancy | Stall/occupancy arrangement through existing create command | `ObligationAccount.Arrangement`; same choice cards; Space Only retained |
| Actual Occupant | Existing actual-occupant/occupancy record | `ObligationAccount.ActualOccupant`; shared editable input |
| Name on Contract | Existing `NameOnContract`/contract lessee evidence | Unsupported; noneditable unavailable indication. Never written to `ContractReference` |
| Facility / Operation | Existing `FacilityCode` | Fixed Kanmanggay presentation label; source remains `KanmanggaySpaceRental` |
| Stall / Space No. | Existing suggested/manual number with server validation | Server-assigned `SubjectLabel`; same field layout, no client allocation |
| Contract Effectivity | Existing contract date, day precision | Existing `ActiveFrom`; manual UI remains month precision and submits first day. Not represented as a verified signed-contract commencement day |
| Contract Duration | Existing `ContractYears` and term calculation | Unsupported; no editable duration, expiry or renewal field |
| Area (sq.m.) | Existing `AreaSqm` | Unsupported; no invented zero or unsaved input |
| Area Location / Note | Existing `AreaNote` | Unsupported; no substitute use of name, number or reference |
| Monthly Rental | Existing `MonthlyRate`/contract rate | Existing approved amount and effective-dated rate records; one shared form value |
| Whole-year equivalent | Presentation calculation | Same display-only calculation; no annual or three-year assessment |
| Additional information | Existing source-supported notes | Only genuinely supported contract reference; no discarded arbitrary notes |
| Applicable Charges / Base Rental | Existing rate and approved utility controls | Shared Base Rental component backed by the same monthly amount; no ICE utilities attached |
| Contract reference | Existing source contract evidence where provided | Existing `ContractReference`, separate from signed-name evidence |
| Import footer | Existing import and historical-import routes | Existing source-specific import route; no manual submit or second importer |

Full editable field parity is **not complete**: the current API cannot persist KM contract name, area, location or contract terms. Unsupported fields remain absent or noneditable. FA retains event, holder, event date, approved event amount, event numbering and advance-registration confirmation; it does not acquire monthly-rate, annualization or lease-term fields.

### Proposed descriptive extension — approval required, not implemented

The smallest descriptive extension is one additive EF migration adding three nullable columns to `ObligationAccounts`: `NameOnContract varchar(200)`, `AreaSqm numeric(12,4)`, and `AreaNote varchar(200)`. Existing records stay null; there is no historical backfill, identity/index change, rate change, period regeneration or collection/allocation update. Optional DTO/create/import-preview fields and read mapping would be added together, with the same manual/import validation: trimmed length limits, signed-only contract name, positive known area (unknown remains null), supported source-kind checks, tenant scoping and audited Head-authorized changes. Import compatibility must preserve existing templates when these optional columns are absent.

This proposal is deliberately separate from contract terms. Genuine commencement dates, three-year terms, expiry, prior-term history, renewal and successor/reopening semantics need the separately approved term model and migration. MEEDO's reported three-year standard covers KM/NPM/NCC/TCC, not automatically ICE/FA/Vegetable-Fruits. Changing an approved rate is not renewal; clearing a closure date is not safe reopening. See [the clarification](../business/MEEDO_CONTRACT_CLARIFICATION_20261009.md) and [the successor-account audit](MOBILE_SPACE_SOURCE_FOLLOWUP_20261009.md). No such persistence extension is implemented or executed here.

### Routes, actions and source authority

`/vendors?workspace=KM`, `/vendors?workspace=FA` and `/vendors?workspace=Vegetable%2FFruits` select obligation adapters; optional `account=<guid>` preserves source account inspection. Existing KM and Vegetable/Fruits `manage-space-holders` URLs still render this shared registry and preserve account deep links. Unknown workspace keys fail closed instead of falling back to NPM. The picker resolves tenant facilities and existing available operations, rather than turning presentation codes into `FacilityCode` values.

The shared overview actions retain their order:

1. **₱:** existing authoritative account register, assessed/settled/remaining by period. Clearly labelled account position, not a fabricated receipt; no payment write.
2. **↺:** `ObligationRentalDetails` reads canonical Collection Activity and matches actual `ObligationPeriod` IDs from the account register. Shows real SRC/document/correction/replacement information for the selected collection month; no name matching.
3. **→:** source-aware account profile in the shared management experience. KM/VF legacy account links remain supported; FA uses the source-scoped registry. No obligation ID is passed to `/profile/ice/...` or the facility payment API.

**History limitation:** the existing activity DTO exposes a collection line's first source allocation only, and its line amount can span multiple allocations. The view explicitly states this limitation, supports month selection within the endpoint's date window and flags truncated feeds. It is a truthful partial canonical history, not a complete account settlement/receipt audit. A complete per-account allocation and receipt read contract still requires approval. An assessment without a collection is never presented as a payment.

Original Head/SuperAdmin restrictions and server guards remain. Active monthly accounts retain Periods, Change amount and Close account; closed accounts retain historical read access without unsupported Reopen/Renew. FA shows event-appropriate actions and facts. Authoritative overview totals remain account-to-date for obligation sources; ICE retains its current-period reporting scope.

### Closed Accounts coverage and money scope

The existing `/reports/closed-accounts` document keeps its stall endpoint, original records and supported facility lifecycle actions. It additionally reads existing source workspaces for KM, Vegetable/Fruits Monthly and FA, including only matching-kind accounts with an actual `ActiveTo`. A past event date alone is not a closed/ended event account.

Source groups show actual account identity, unit/holder, dates and server assessed/collected/outstanding amounts, with profile links and no invented lifecycle actions. Stall “stated here only”, lapsed/live-follow-up and stall lifetime collected figures remain separate. Obligation balances are already tracked in their source workspaces and are **not added** to those figures; this scope warning is printed. CSV/Excel retain source/account identity and balance scope. Unsupported stall assessed figures remain blank. Failed source loads show retry and withhold complete exports instead of claiming empty success.

### Verification and remaining acceptance

Executed Web commands use `EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release`: focused rental/drawer/registry selection **151 passed / 0 failed / 0 skipped**, registry/expiry/consolidation regression selection **27 / 0 / 0**, full suite **838 / 0 / 0**. The first full run found two regressions (a moved source-inspection assertion and a lost existing expiry warning); both were corrected before the passing full run.

Relevant Unit selection (`Obligation`, contract billing/expiry, closed occupancy, registry currency and Collection Activity) on `EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release`: **90 / 0 / 0**. Client Release build succeeded. No backend or shared financial contract changes were made; API/Mobile builds and PostgreSQL integration were not rerun for this Client-only patch. Final CSS/whitespace verification is recorded with the implementation handoff.

Ignored `artifacts/rental-consolidation/` contains real bUnit component HTML and Chrome renderings: **48 viewport checks** across 16 screens at **1440 / 1024 / 390px**, with no page overflow or offscreen drawer footer controls. ICE/NCC/KM overview/drawers, KM/FA registry, FA registration, Monthly/Daily and Closed Accounts were checked. Visual inspection caught and corrected Razor literal bindings and narrow Closed Accounts overflow. These are rendered fixtures, **not authenticated tenant/browser acceptance**.

Chrome printed the actual Closed Accounts fixture as A4 landscape. The 90-account document has **four nonblank pages**, with **15 / 27 / 27 / 21** holder rows, repeated table headers, all 90 records, printed scope warning and signatures. First/last page images were inspected for clipping and pagination. These local artifacts are not committed.

Remaining gates: authenticated route/data verification and user visual acceptance; approved descriptive metadata extension; complete account allocation/receipt history; separate contract lifecycle approval. No integration, push, production migration/deployment or APK publication is authorized or performed. The three concurrent planning-document edits remain unstaged and untouched.

Final checks: Client Release **0 warnings / 0 errors**; **160** scoped CSS source files balanced, generated bundle **11,821 opening / 11,821 closing braces**; `git diff --check` passed. A deliberately reintroduced double-counting defect failed the focused assertion (`₱4,200` instead of `₱0`); restoring separate scopes then passed all **838** Web tests again. This negative run was diagnostic, not an unresolved failure.

Commands for the final full checks:

```powershell
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release --filter 'FullyQualifiedName~Obligation|FullyQualifiedName~ContractBillingMonths|FullyQualifiedName~ContractExpiryAsOf|FullyQualifiedName~ClosedStallAccountsMultiOccupancy|FullyQualifiedName~VendorRegistryContractCurrency|FullyQualifiedName~CollectionActivity'
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release
git diff --check
```

### Exact consolidation file inventory

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor`.
- `EEMOCantilanSDS.Client/Components/Pages/Reports/ClosedAccounts.razor`.
- `EEMOCantilanSDS.Client/Components/Pages/Reports/ClosedAccounts.razor.css`.
- `EEMOCantilanSDS.Client/Components/Pages/Shared/AddVendorModal.razor`.
- `EEMOCantilanSDS.Client/Components/Pages/Shared/FacilityStallsTable.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/FacilityPage.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/ObligationWorkspace.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/SpaceAccountSnapshot.razor` (removed).
- `EEMOCantilanSDS.Client/Components/Shared/SpaceAccountSnapshot.razor.css` (removed).
- `EEMOCantilanSDS.Client/Components/Shared/SpaceHolderManagement.razor`.
- `EEMOCantilanSDS.Client/Services/RentalWorkspaceCatalog.cs`.
- `EEMOCantilanSDS.Client/Services/SpaceAccountPresentation.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/ClosedAccountsRenewTests.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/ObligationWorkspaceTests.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/RentalWorkspacePilotTests.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/SpaceHolderWorkspaceTests.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/SpaceRentalConsistencyTests.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/VendorRegistryTerminologyTests.cs`.
- `docs/planning/UNIFIED_FACILITIES_PHASE1_HANDOFF_20261009.md`.
- `EEMOCantilanSDS.Client/Components/Shared/ObligationRentalDetails.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/ObligationRentalDetails.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/RegisteredRentalTable.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RegisteredRentalTable.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalContractFields.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalEffectivityField.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalLesseeFields.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalRateFields.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalRateFields.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalRegistryHero.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalRegistryTable.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalRegistryTable.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalSummaryStrip.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalSummaryStrip.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalUnitNumberField.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalWorkspacePicker.razor`.
- `EEMOCantilanSDS.Client/Components/Shared/RentalWorkspacePicker.razor.css`.
- `EEMOCantilanSDS.Client/Services/ClosedObligationRentalRow.cs`.
- `EEMOCantilanSDS.Client/Services/RentalRegistrationState.cs`.
- `EEMOCantilanSDS.Client/Services/RentalTableRow.cs`.
- `EEMOCantilanSDS.ComponentTests/Pages/RentalConsolidationTests.cs`.

The existing concurrent edits to `ACTIVE_WORKSTREAMS.md`, `CURRENT_RELEASE_STATE.md` and `VEGETABLE_FRUIT_WORKSPACE_20261008.md` are excluded from the task commits. Artifacts and generated outputs are also excluded.

## Historical Phase 1 checkpoint

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
