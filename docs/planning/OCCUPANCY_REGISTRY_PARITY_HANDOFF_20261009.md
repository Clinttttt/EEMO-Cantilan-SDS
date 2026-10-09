# Occupancy registry parity — 2026-10-09

Implementation and local verification evidence, not production release authority. Starting checkout: `C:\dev\stalltrack\eemo`, branch `codex/vegetable-fruit-space-workspace`, HEAD `f8a5ae75`. No branch switch, integration, push, production migration, deployment or APK publication.

## Regression and actual reuse

The original NPM toolbar remained embedded in `Components/Pages/Menus/Vendor.razor`, with its own selector, filters, search and count/action layout. Operation registries instead combined generic `Toolbar`, `RentalWorkspacePicker`, two filter strips and navigation buttons. The recent `.toolbar-registry-order` rules added wrapping and large flex bases; they changed arrangement without sharing the accepted implementation.

`Components/Shared/RentalRegistryToolbar.razor` and its scoped CSS now contain the extracted original Vendor toolbar. Vendor and KM/VF Monthly/FA management all consume that implementation. The old selector component and the registry-order override are removed. Generic overview Toolbar behavior remains separate and unchanged. The NPM search placeholder and clear control now use correctly encoded ellipsis and multiplication-sign text.

The common hierarchy is selector → five status filters → flexible search → source-local count and authorized registration action. Caller callbacks retain filtering and registration authority. Existing facility callers supply their existing selector data; operation callers discover supported sources through existing APIs. Selection never treats KM/FA as FacilityCode values. The selector uses native buttons, accessible expansion/current state, Escape dismissal and visible focus.

Roster and Reports remain secondary links beside the account panel heading. A compact native Lifecycle selector there retains independent combinations without a second primary filter strip. No standalone Import action is restored; registration drawers retain their existing import footer and handlers.

## Filter compatibility and routes

Default All/Paid/Partial/Unpaid applies to active accounts. Closed selects closed accounts regardless of balance. Choosing another primary status after Closed returns to active accounts. Explicit secondary Lifecycle selection enables All/Active/Closed combined with every payment filter, including Closed + Paid. Search only filters visible records and does not change authoritative operation totals. The count belongs to the selected source and visible records; API failures show an unavailable count, not a fabricated zero.

Existing `/vendors` facility selection and `/vendors?workspace=KM`, `workspace=Vegetable%2FFruits`, and `workspace=FA` remain supported. Legacy operation management routes and selected-account links remain intact. Registration, numbering, import, period inspection, holder editing, effective-dated rates and closure handlers are unchanged. Existing eight-column registry and five compact action icons are unchanged. Closed obligation accounts cannot invoke physical Stall reopening; their existing unavailable-action reason remains truthful.

## Validation and visual evidence

The toolbar regression test failed against the original implementation before extraction. Focused Web checks: 69 passed, 0 failed, 0 skipped. Full Web suite run separately: 862 passed, 0 failed, 0 skipped. Client Release build: 0 warnings, 0 errors. Focused Vegetable/Fruits account Unit checks: 3 passed, 0 failed, 0 skipped. Full Unit: 2,526 passed, 0 failed, 0 skipped. Final PostgreSQL obligation suite: 15 passed, 0 failed, 0 skipped.

Commands used:

```powershell
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter "FullyQualifiedName~RentalConsolidationTests|FullyQualifiedName~SpaceHolderWorkspaceTests"
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-build
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ObligationAccountTests
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release
git diff --check
```

Rendered bUnit fixtures were loaded in real headless Chrome for NPM/ICE/NCC/TCC/BBQ/KM/VF/FA at 1536, 1440, 1280, 1024 and 390px: 40 viewport checks. All variants occupy one primary toolbar row at 1440/1536px; toolbar height 67.1px, selector height 49.1px. At 1440px search width is 303.2px for NPM and 281.0px for KM/ICE/VF; source-specific labels explain the difference. No page-width overflow or missing primary action was observed. Smaller widths deliberately wrap in the same order. NPM/KM desktop and KM narrow screenshots were inspected. Ignored evidence lives in `artifacts/registry-toolbar-parity/`; it is not staged.

The live local browser redirected to login. **Authenticated visual acceptance remains pending**. Fixture rendering is not authenticated acceptance, and no production or Android visual check is claimed. Source CSS (162 files) and generated scoped bundle were brace-balanced. Existing integration analyzer warnings are unrelated to this toolbar implementation.

## D1 — lease evidence and database status

Read-only inspection of the configured local PostgreSQL database confirmed migration `20261009094640_KanmanggaySpaceLeaseEvidence20261009` already appears in migration history and its five nullable columns exist: ContractName (varchar 200), ContractEffectiveDate (date), ContractYears (integer), AreaSqm (numeric 12,2), AreaLocation (varchar 250). This turn did not apply a migration to that database. Production migration state was not inspected.

Docker was initially unavailable: the first obligation run skipped all 14 tests. After starting local Docker, all 14 existing obligation tests passed in disposable Testcontainers databases. A further regression test verifies Head-only, tenant-scoped evidence correction after a posted settlement, retaining Collection/SRC identity, allocation identity/amount/source, period position and approved rate. The final rerun passed all 15 tests with no failures or skips. Existing tests cover numbering concurrency/source scoping, exact signed date and term reload, historical nullable evidence, audited changes and canonical financial behavior.

Current descriptive evidence supersedes the early missing-field limitations in `UNIFIED_FACILITIES_PHASE1_HANDOFF_20261009.md` and the generated-but-unapplied checkpoint in the rental parity handoff **for this verified local database only**. Exact signed commencement is separate from monthly billing ActiveFrom. Three-year descriptive evidence does not implement renewal, expiration enforcement or reopening.

## D2 — full allocation history: audited gap, proposal only

`Infrastructure/Repositories/Reports/CollectionActivityReader.cs` projects allocation source references but drops allocation ID and amount; activity lines expose the first matching allocation and the full line amount. `Application/Dtos/Collections/CollectionActivityDtos.cs` has no complete allocation list. `CollectionCorrectionAllocation` can identify original-allocation effects, but correction lines may have no allocation breakdown. Consequently the current activity contract cannot truthfully attribute every historical correction to an account. A full line amount must not be reported as that account's settlement.

Existing `ObligationRentalDetails.razor` retains authoritative period amounts and truthful collection references with the disclosed allocation limitation. No invented facility-payment records or receipts were added.

Proposed read-only contract, requiring review before implementation:

- `GET /api/obligations/accounts/{accountId}/collection-history?from=&to=&cursor=&limit=`.
- Tenant-filtered account ownership, authorized read-only role policy and closed-account access; collector access requires its own explicit policy.
- Resolve actual account period IDs to **all** canonical allocations. Return allocation ID, period identity and allocated amount, Collection/SRC, business date, recorded collector/document evidence and reversal/replacement chain.
- Full original line amount is separate context, never the account settlement amount. Return correction allocation effects only when attributable; identify legacy effects as unattributed rather than prorating them or hiding them.
- Cursor pagination avoids pretending one activity page is complete history. Period totals remain server-authoritative and separate from receipt-date totals.

No schema change is needed for reading existing allocation rows. Policy for legacy unattributed correction effects, date/as-of scope and access must be agreed before claiming complete account history. This slice is **not implemented**; no financial records were modified.

## D3–D5 — lifecycle design gates, not implemented

ObligationAccount still represents one continuous interval. Clearing ActiveTo would erase a closed gap and risk reassessment. Reopening, renewal, successor occupants, closure and approved-rate changes are distinct operations.

Proposed reopening preview: `GET /api/obligations/accounts/{id}/reopen-preview?effectiveFrom=YYYY-MM-DD`; proposed command: `POST .../reopen` with ClientOperationId, effective date and expected preview version. Head-only tenant-scoped validation would preserve predecessor closure and create an audited successor only after office approval. A stable unit/root relationship, interval exclusivity, concurrency lock, replay key and immutable predecessor relationship need an additive persistence design. Reject overlap/subsequent occupants; never bill the closed gap. These are proposed contracts, not available endpoints.

Proposed renewal read/command: `GET .../contract-terms`, `POST .../contract-renewals` with prior term ID, expected version, commencement, years, signed-name/reference, approval evidence and ClientOperationId. Append an approved term rather than overwrite the old one. Whether the same occupant retains the account while terms advance requires an approved decision; successor occupants retain distinct liability. Rate changes must use the existing effective-dated workflow, not a new rental writer.

Existing facility renewal appends Contract records and updates occupancy intervals; month ownership uses the established occupancy/domain rules. That mechanism is evidence to reuse, not authorization to transplant Stall billing semantics into obligation accounts. A future nullable term chain/approval record and predecessor/unit relationship would require separately reviewed additive migrations, no guessed historical backfill, tenant and role tests, overlap/concurrency/replay tests, closed-gap assessment tests and immutable cash/allocation regression tests. No such migration or API was added here.

MEEDO must decide: newly signed renewal evidence; continuity versus new occupancy; month ownership and mid-month treatment; early termination versus temporary closure; holdover and late renewal billing; Space Only exceptions; approval of changed rate independently from renewal; reopened rate and future-occupant conflicts; and term scope beyond KM/NPM/NCC/TCC. ICE/BBQ/VF/FA are not automatically covered. Expiring/expired display may eventually derive from recorded terms, but must not imply approved automatic termination, billing stop, renewal or reassignment.

## Documentation reconciliation and remaining acceptance

Scoped files (paths relative to the checkout):

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor` and `.razor.css`.
- `EEMOCantilanSDS.Client/Components/Pages/Shared/Toolbar.razor` and `.razor.css`.
- `EEMOCantilanSDS.Client/Components/Shared/ObligationWorkspace.razor`.
- New `EEMOCantilanSDS.Client/Components/Shared/RentalRegistryToolbar.razor` and `.razor.css`.
- Removed `EEMOCantilanSDS.Client/Components/Shared/RentalWorkspacePicker.razor` and `.razor.css`.
- `EEMOCantilanSDS.ComponentTests/Pages/RentalConsolidationTests.cs` and `SpaceHolderWorkspaceTests.cs`.
- `EEMOCantilanSDS.IntegrationTests/ObligationAccountTests.cs`.
- This new handoff document.

No backend implementation, API contract, financial persistence, classification, authorization policy, assessment generator, allocation writer or Mobile component changed. Tests create disposable data through existing workflows; no existing posted Collection/SRC was edited. API/Mobile build expansion is therefore not required by this patch.

The concurrent edits in ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE are preserved and excluded from these commits. Proposed canonical reconciliation: add this local implementation checkpoint, mark early field/migration limitations superseded as above, retain production/integration status separately, and keep reopening/renewal/history proposals explicitly pending. No authority or business rule was changed by this handoff.

Completed: common registry toolbar extraction, source-specific callbacks, compact default filters and retained advanced combinations, fixture checks and lease-evidence verification. Pending: authenticated owner visual acceptance and canonical reconciliation with the concurrent document owners. Awaiting contract/policy approval: complete allocation-history contract, successor/reopening and contract lifecycle. No integration is authorized before acceptance.
