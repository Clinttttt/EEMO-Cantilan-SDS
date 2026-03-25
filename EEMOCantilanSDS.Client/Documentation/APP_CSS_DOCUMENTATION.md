# `app.css` — Shared stylesheet documentation

Location: `EEMOCantilanSDS.Client/wwwroot/app.css`

Purpose
- Global/shared styles for the client app (layout, tokens, common components). Keep truly page/component-specific rules out of this file to avoid cross-page conflicts in Blazor Server.
Use CSS isolation (`Component.razor.css`) for page/component scoped styles (example: `Vendor.razor` → `Vendor.razor.css`).

Design tokens (root variables)
- Colors:
  - `--navy`: #0d2137 (primary text/nav)
  - `--navy-2`, `--navy-3`: darker/nav variations
  - `--gold`: #c8a84b (accent)
  - `--gold-light`: #e8cc76 (accent light)
  - `--green`: #2d7a5f, `--green-bg`: #e6f4ef
  - `--red`: #8b3a3a, `--red-bg`: #fdf0f0
  - `--bg`: #f0f4f8 (page background)
  - `--bg-card`: #ffffff (card background)
  - `--bg-icon`: #eef2f6 (subtle surface)
  - `--border`: #dde4ea (borders)
  - text tokens: `--text`, `--text-muted`, `--text-subtle`
- Layout tokens:
  - `--sidebar-w`, `--sidebar-w-col`, `--topbar-h`

What belongs in `app.css` (shared)
- Resets and global rules (`*, body, a`)
- Design tokens and color system
- Layout containers: `.admin-layout`, `.admin-main`, sidebar interactions
- Shared components used across pages: topbar (`.topbar*`), KPI cards (`.kpi-*`), global SVG resets, panels, data table base (`.data-table`), buttons (`.btn-*`), modals container (`.eemo-modal*` base rules), form inputs (`.form-*`), utilities (`.text-red`, `.mono`), responsive breakpoints
- Shared small components used by many pages: `.search-box`, `.filter-tabs`, `.action-btns`, `.fac-checkbox`, `.fac-check-box` etc.

What should be scoped to component/page CSS isolation
- Any styles that only apply to one page or component. Examples in this repo (moved to component-scoped CSS, e.g., `Vendor.razor.css`):
  - Detail modal layout and classes (`.detail-*`, `.fee-*`, `.cal-*`, `.hist-*`, `.util-*`, `.fee-checkbox-grid`, `.vendor-row`, `.stall-num-*`, `.occupant-*`, `.section-tag`)
  - Facility dropdown variant classes (`.fac-dd-*`, `.fac-trigger` variants used on the page)

Guidelines
- Prefer adding new page-specific rules to `Component.razor.css` (Blazor CSS isolation) to avoid leakage. Example: for `Vendor.razor`, use `Vendor.razor.css`.
- Put tokens and truly shared UI into `app.css` only.
- When adding new design tokens, add them to the `:root` section and update this document.
- For raw/injected SVGs (MarkupString), keep global SVG rules in `app.css` because isolated styles do not apply to injected markup.

How to add a component-specific style
1. Create or edit `Components/Pages/Path/YourComponent.razor.css` next to the `.razor` file.
2. Place selectors that only apply to that component/page there.
3. Avoid redefining color tokens — use the `--*` variables.

Notes
- Example: `Vendor.razor.css` contains vendor-detail, calendar, fees, and history styles. `app.css` contains a top-level note indicating component-scoped files (e.g., `Vendor.razor.css`) are used for page-scoped styles.

