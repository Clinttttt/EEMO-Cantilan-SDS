# Web Admin Contextual Loading Skeleton Handoff

## Purpose

Several web admin pages still show a full-page or generic loading state during navigation/data load. This causes a visible flicker: the URL changes, but the page body appears blank or out-of-context until data finishes loading.

Target behavior: keep each page shell/header/filter layout visible and skeletonize only the specific data region being loaded, similar to the Backups page. This makes navigation feel immediate without changing business logic.

Important: do not change API calls, report calculations, financial logic, or persistence behavior. This is UI loading-state work only.

## Already completed in this Codex pass

These Razor loading blocks were already replaced from generic text/full-page states into contextual skeleton layouts:

1. `EEMOCantilanSDS.Client/Components/Pages/Menus/Report.razor`
   - Replaced `rpt-state Loading report…`
   - New loading UI keeps report context and shows:
     - KPI card skeletons
     - main chart/report panel skeleton
     - side-list skeleton

2. `EEMOCantilanSDS.Client/Components/Pages/Reports/MonthEndReport.razor`
   - Replaced `me-state Loading month-end report…`
   - New loading UI shows:
     - summary KPI skeletons
     - document/report header skeleton
     - facility table skeleton sections

3. `EEMOCantilanSDS.Client/Components/Pages/Reports/ExportData.razor`
   - Replaced `ex-state Loading report…`
   - New loading UI shows:
     - summary KPI skeletons
     - export report document skeleton
     - facility/table skeleton sections

4. `EEMOCantilanSDS.Client/Components/Pages/Reports/StallHolderList.razor`
   - The contextual skeleton block appears to be already inserted.
   - It replaced `sh-state Loading stall holders…`
   - It now shows:
     - summary card skeletons
     - official document header skeleton
     - facility/table skeleton sections

## CSS still required for completed Razor patches

The Razor files above reference new helper classes that may not yet exist in their `.razor.css` files. Add styling for these before building:

- `Report.razor.css`
  - `rpt-loading`
  - `rpt-loading-grid`
  - `rpt-loading-panel`
  - `rpt-loading-chart`
  - any small helper spacing classes used by the new markup

- `MonthEndReport.razor.css`
  - `me-loading`
  - `me-sk-center`
  - `me-sk-gap`
  - `me-table-skeleton`

- `ExportData.razor.css`
  - `ex-loading`
  - `ex-sk-center`
  - `ex-sk-gap`
  - `ex-table-skeleton`

- `StallHolderList.razor.css`
  - `sh-loading`
  - `sh-sk-center`
  - `sh-sk-gap`
  - `sh-table-skeleton`

Use existing page design tokens/colors. Do not introduce a different visual system.

## Remaining high-priority pages to fix

1. `EEMOCantilanSDS.Client/Components/Pages/Reports/SlaughterhouseReport.razor`
   - Current issue:
     - Uses `<div class="spinner">Loading...</div>`
   - Replace with a contextual skeleton that mirrors the real page:
     - hero banner skeleton
     - toolbar/filter skeleton
     - report/document table skeleton
   - Suggested CSS helpers:
     - `slr-loading`
     - `slr-sk-center`
     - `slr-sk-gap`
     - `slr-table-skeleton`

## Remaining medium-priority pages to fix

1. `EEMOCantilanSDS.Client/Components/Pages/Reports/PastFollowUpQueue.razor`
   - Current issue:
     - Uses `<div class="fh-state" aria-busy="true">Loading @periodLabel…</div>`
   - Replace with contextual history skeleton:
     - keep controls/filters visible
     - skeletonize follow-up sections/tables only
   - Suggested CSS helpers:
     - `fh-loading`
     - `fh-loading-section`
     - `fh-table-skeleton`

2. `EEMOCantilanSDS.Client/Components/Pages/Reports/ClosedAccounts.razor`
   - Current issue:
     - Uses `<div class="ca-state">Loading closed accounts…</div>`
   - Replace with:
     - summary card skeletons
     - document/list/table skeleton
   - Suggested CSS helpers:
     - `ca-loading`
     - `ca-sk-center`
     - `ca-sk-gap`
     - `ca-table-skeleton`

3. `EEMOCantilanSDS.Client/Components/Pages/Menus/Accounts.razor`
   - Current issue:
     - Admin tab and Collector tab each use:
       - `<div class="empty-state"><div class="empty-title">Loading…</div></div>`
   - Replace with table-level skeletons only:
     - table header placeholder
     - 5–6 row placeholders
   - Suggested CSS helper:
     - `accounts-table-loading`

## Pages already judged okay or mostly okay

These pages already have contextual loading and should not be refactored unless a concrete issue is found:

- Dashboard/Menu page: uses facility/card skeletons; hero remains visible.
- Collectors page: table/data area skeleton only.
- Vendors/stalls pages: table/data area skeleton only.
- Collection Manager: contextual list/calendar loading.
- Online Payments: already adjusted to avoid unrelated full-page skeleton.
- Settings: already adjusted previously, but re-check if Opus has changed it.

## Important safety notes

- Do not modify financial calculations, report DTOs, API endpoints, or repositories for this task.
- Do not overwrite unrelated ongoing work from Opus.
- Existing dirty file to avoid unless explicitly needed:
  - `EEMOCantilanSDS.Mobile/MauiProgram.cs`
- Keep skeletons screen-only where print/export pages are involved by using `no-print` if the page already uses that convention.
- Prefer reusing the existing `Skeleton` component instead of custom shimmer divs.

## Verification still needed

After completing the remaining patches and CSS:

1. Run:
   - `dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --no-restore`

2. Review:
   - `git diff --check`
   - `git diff --stat`

3. Manually verify in browser:
   - Navigate to Reports dashboard.
   - Navigate to Month-End Report.
   - Navigate to Export Data.
   - Navigate to Stall Holder List.
   - Navigate to Slaughterhouse Report.
   - Navigate to Past Follow-up.
   - Navigate to Closed Accounts.
   - Navigate to Accounts page and switch Admin/Collector tabs.

Expected result: page shell appears immediately, only the relevant data section skeletonizes, and there is no full blank/flickering page.

## Current status

Partial. Razor patches for Report, MonthEndReport, ExportData, and StallHolderList are in place, but CSS and remaining high/medium pages still need completion. No build was run yet after these changes.

