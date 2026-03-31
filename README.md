EEMO Cantilan SDS — Quick CSS Cheat Sheet

Location: `EEMOCantilanSDS.Client/wwwroot/app.css`

Purpose
- Shared/global CSS tokens and common components used across the Blazor client.
- See full doc: `EEMOCantilanSDS.Client/wwwroot/APP_CSS_DOCUMENTATION.md`

Quick tokens
- Colors: `--navy`, `--gold`, `--gold-light`, `--green`, `--green-bg`, `--red`, `--red-bg`, `--bg`, `--bg-card`, `--bg-icon`, `--border`
- Text tokens: `--text`, `--text-muted`, `--text-subtle`
- Layout tokens: `--sidebar-w`, `--sidebar-w-col`, `--topbar-h`

Common shared classes
- Layout: `.admin-layout`, `.admin-main`
- Topbar: `.topbar`, `.topbar-title`, `.topbar-right`
- KPI: `.kpi-strip`, `.kpi-card`, `.kpi-value`, `.kpi-label`
- Data table: `.data-table`, `.table-wrap`
- Buttons: `.btn-primary`, `.btn-ghost`, `.btn-outline`, `.btn-danger`
- Modals: `.eemo-modal`, `.eemo-modal-overlay`, `.eemo-modal-body`, `.eemo-modal-footer`
- Forms: `.form-input`, `.form-row-2`, `.form-label`, `.form-error`
- Utilities: `.text-red`, `.mono`, `.code-badge`

Guidelines
- Put page/component-specific rules into `Component.razor.css` (Blazor CSS isolation). Example: `Vendor.razor.css` contains vendor detail, calendar and fee styles.
- Keep tokens and SVG global rules in `app.css` because isolated CSS does not apply to MarkupString-injected SVG.
