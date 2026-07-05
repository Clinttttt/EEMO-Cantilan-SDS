# Web Admin Contextual Loading Skeleton Continuation

Purpose: continue the web-admin loading UX cleanup without causing layout flicker, print regressions, or conflicts with other active work.

Important: do not trust this note blindly. Re-open the affected files, compare against the current code, and verify the UI/build before committing.

## UX rule to follow

The page should not feel like it disappears during data loading.

Use this pattern consistently:

1. Keep the stable page shell visible immediately:
   - topbar
   - hero/banner
   - filters/toolbar
   - panel/card container
2. Skeleton only the part that is actually data-dependent:
   - table body/table rows for tables
   - card rows for card grids
   - recent-list rows for lists
3. The skeleton must match the real loaded layout.
   - Do not use generic full-width bars when the real UI is a table.
   - Do not skeleton the hero if the hero can safely display zero/default values.
   - Do not swap a whole report document unless that page is intentionally print/report-sensitive.
4. Prefer "existing stale data + small refresh indicator" when data already exists.
   - Avoid clearing current data during filter refresh unless absolutely needed.
5. Keep print pages careful.
   - Do not change printable sheet structure casually.
   - Any report-page skeleton change must not alter `.no-print`, print CSS, or the printed document layout.

## Already completed before this handoff

These were already fixed in prior work and should be preserved:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Settings.razor`
  - Settings hero no longer skeletons/flickers.
  - The stable settings shell remains visible while specific sections load.

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Collector.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Collector.razor.css`
  - Collector loading state was changed from generic bars to a table-shaped skeleton.
  - The skeleton now matches collector table columns better.

## Completed in the continuation pass

These items have now been completed and verified with a client build.

### 1. Vendor page contextual table skeleton

Files:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor.css`

What changed:

- The old generic loading bars were replaced with a table-shaped loading skeleton inside `.table-wrap`.
- Added CSS for:
  - `vendor-loading-table`
  - `vendor-loading-name`
  - `vendor-loading-actions`
- Loading rows no longer show hover affordance.
- Multi-line vendor-name cells now use skeleton stacks matching the real table.

### 2. Transactions page contextual table skeleton

Files:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Transactions.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Transactions.razor.css`

What changed:

- The old `.sk-rows` / `.sk-row` generic loading block was replaced with a real `<table class="txn-table txn-loading-table">`.
- Added CSS for:
  - `txn-loading-table`
  - `txn-loading-stack`
- Loading rows no longer show hover affordance.
- Multi-line transaction cells now match the real transaction feed shape.

### 3. Shared facility stall table fallback skeleton

Files:

- `EEMOCantilanSDS.Client/Components/Pages/Shared/FacilityStallsTable.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Shared/FacilityStallsTable.razor.css`

What changed:

- The generic fallback `.sk-stack` bars were replaced with a real fallback table-shaped skeleton.
- This affects fallback column sets outside the already-good `Operational` and `MonthlyCard` modes.
- The skeleton now mirrors:
  - Stall No.
  - Actual Occupant
  - Name on Contract
  - Area (sqm)
  - Contract Date
  - Monthly Rent
  - OR No.
  - Actions

### 4. Facility-specific loading blocks

Files:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/SH.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/SH.razor.css`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/TPM.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/TPM.razor.css`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/TRM.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/TRM.razor.css`

What changed:

- Slaughterhouse:
  - replaced the generic `.sk-rows` transaction loader with a real transaction-table skeleton.
  - columns now match Date, Owner/Client, Animal Type, Heads, Rate, Amount, OR, Actions.
- TPM:
  - replaced generic attendance bars with contextual attendance summary, search, and vendor-table skeletons.
  - keeps the selected Friday/header context visible while attendance loads.
- TRM:
  - replaced the two generic panel loaders with actual TRM panel structure.
  - left panel shows transporter-list skeleton rows.
  - right panel shows trip-log table skeleton rows.
  - hero remains visible instead of being blanked.

## Verification completed

Client build command:

```powershell
dotnet build EEMOCantilanSDS.Client\EEMOCantilanSDS.Client.csproj --no-restore
```

Result:

- Build succeeded.
- 0 errors.
- Existing warnings remain, mostly duplicate using directives and nullable warnings. These were not introduced by the skeleton work.

## Still needs to be done

### Print-sensitive report pages

Handle these only after the safe pages are done:

- `EEMOCantilanSDS.Client/Components/Pages/Reports/SlaughterhouseReport.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Reports/MonthEndReport.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Reports/StallHolderList.razor`

Risk:

- These are report/print pages.
- They currently swap larger report skeletons before real printable content.
- Improving them is possible, but verify print preview after changes.

Recommended approach:

- Keep report controls visible.
- Keep document/card shell stable where possible.
- Skeleton only summary cards/table rows.
- Do not alter printed document markup unless necessary.

### Optional remaining audit

The scan still reports these patterns, but they looked acceptable or intentional:

- `Menus/Accounts.razor`
  - `acct-sk-row` is already table/panel-shaped.
- `Menus/AuditTrail.razor`
  - uses compact audit-row skeletons inside the existing panel.
- `Menus/Menu.razor`
  - dashboard loading is intentionally card-like and was previously accepted by the user.
- `Reports/FollowUpQueue.razor`
  - uses `fq-sk-row`; hero/filter shell remains stable.
- `Reports/NpmReports.razor`
  - scan hits `audit-risk-row`, not a generic skeleton issue.

If Opus continues this work, re-check these visually first before editing. Do not change them mechanically just because a class name contains `sk`.

## Pages that looked acceptable during audit

Do not spend time changing these unless a specific bug appears:

- `Menus/Backups.razor`
  - Good reference pattern. Stable shell + contextual recent-backup skeleton.
- `Menus/Accounts.razor`
  - Good table-shaped loading inside panels.
- `Menus/AuditTrail.razor`
  - Stable filters/stats, only audit rows skeletonize.
- `Menus/OnlinePayments.razor`
  - Contextual loading/empty state; not whole-page flicker.
- `Reports/FollowUpQueue.razor`
  - Stable hero/filterbar; section/table skeleton is acceptable.
- Facility pages using `FacilityStallsTable` with `Operational` or `MonthlyCard`.

## Current dirty-worktree caution

At handoff time, known modified/untracked state included UI work and unrelated work:

- `M EEMOCantilanSDS.Client/Components/Pages/Menus/Collector.razor`
- `M EEMOCantilanSDS.Client/Components/Pages/Menus/Collector.razor.css`
- `M EEMOCantilanSDS.Client/Components/Pages/Menus/Settings.razor`
- `M EEMOCantilanSDS.Client/Components/Pages/Menus/Transactions.razor`
- `M EEMOCantilanSDS.Client/Components/Pages/Menus/Vendor.razor`
- `M EEMOCantilanSDS.Mobile/MauiProgram.cs`
- `D breakdown/pending/web-admin-contextual-loading-skeleton-handoff.md`

Do not touch `EEMOCantilanSDS.Mobile/MauiProgram.cs` unless the current task explicitly requires it.

The deleted `web-admin-contextual-loading-skeleton-handoff.md` existed before this handoff. Confirm whether it was intentionally removed before restoring or committing anything.

## Verification checklist before commit

- Build client project.
- Navigate to:
  - `/vendors`
  - `/transactions`
  - one monthly facility page
  - NPM facility page
  - Settings
  - Collectors
- Confirm:
  - topbar/hero/filter toolbar stay visible immediately
  - loading skeleton matches the final component structure
  - no huge generic bars where a table should be
  - no broken responsive mobile layout
  - no console/runtime errors
