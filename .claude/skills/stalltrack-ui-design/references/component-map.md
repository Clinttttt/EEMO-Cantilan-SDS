# StallTrack Web: UI principle to existing component

The Platform apps (Angular, `@stalltrack/ui`, repo `C:\dev\stalltrack\platform`, skill `stalltrack-platform-ui`) share these principles with the Head Portal. **The Head Portal is the visual reference; the Platform copied it.** Here the direction runs the other way: use what EEMO already has, in Razor. Never port Angular code or add a second UI library. A shared piece is added only when the same semantics repeat on a second page.

All paths are under `EEMOCantilanSDS.Client/`. Behaviour rules (lifecycle, caches, API counts) still come from the workflow in `../SKILL.md`; none of this changes business meaning.

| Principle | Use this | Notes |
|---|---|---|
| Hero band + KPI figures | `Components/Shared/V3Hero.razor` + `V3HeroFigure.razor` (params: `Title`, `Eyebrow`, `Subtitle`, `Meta`, `Icon`, `Figures`, `Actions`, `Progress`, `Compact`) | `FacilityHero`, `ImportHero`, `RentalRegistryHero`, `RentalWorkspaceHero` are older/specific heroes; do not add another. Panel colour is `--civic-panel` in `wwwroot/app.css`, white text, no gradient. |
| OR / CT evidence chip | `InstrumentChip.razor` (`Instrument` "OR"/"CT", `ShowName`, `Context`) | The page's approved policy decides the instrument; the chip never does. A canonical SRC is **not** an OR/CT: show it as its own reference, never in this chip. |
| Dropdown | `FhSelect.razor` (`Caption`, `Options` as `(Value, Label)`, `Value`/`ValueChanged`, `Block`, `RightAlign`, `DisabledValues`, `AriaLabel`) | No native `<select>` for a visible choice. Two or three options are buttons or a segmented control, not a dropdown. |
| Loading skeleton | `Skeleton.razor` (`Width`, `Height`, `Radius`, `Class`) inside a `role="status"` wrapper with a `v3-sr-only` label, as in `FollowUpAccountReview.razor` | Never bare "Loading..." text; some pages still do (drift, fix when touched). |
| Segmented / tab views | **No shared component yet.** Closest: `MonitoringTabs.razor` (links with `aria-current`, for route-level tabs) and per-page scoped `.filter-tab` / segmented CSS (`Accounts.razor.css`, `RevenueSetup.razor.css`, `NpmReports.razor.css`) | One table at a time. If a third page needs the same in-page toggle, extract one `SegmentedControl` (roving tabindex, arrow keys, counts). Do not do it ahead of need. `div @onclick` tabs (see `Accounts.razor`) lack keyboard support: use `button`/links. |
| Concise info tip | **No shared component yet.** Today: `title="..."` attributes (hover only, no keyboard/touch) | Cut descriptive prose first. Keep safety, legal and financial-authority wording visible. If an icon tip is needed on 2+ pages, add one `InfoTip` with `aria-describedby`, hover + focus + tap. |
| Icons | Inline SVG, stroke-based. Global rules in `wwwroot/app.css` (`stroke: currentColor; stroke-width: 1.8; round caps/joins`) also cover `MarkupString` SVG | One weight (1.8), sizes 16/20/24, `aria-hidden="true"` when decorative, accessible name when icon-only. No emoji. There is no icon component; do not invent per-page stroke widths. |
| Breadcrumbs / navigation | Sidebar (`Components/Pages/Shared/Sidebar.razor`, `Components/Layout/MainLayout.razor`) plus `OperationTopbar.razor`, `WorkspaceHeader.razor`, `RentalWorkspaceNavigation.razor` | No breadcrumb component exists. Add one only for a genuinely deep route, following `docs/interface/INFORMATION_ARCHITECTURE.md`; never rename IA terms. |
| Empty states | Per-page markup (`Menus/Arrears.razor`, `Menus/Collector.razor`, `Menus/Backups.razor`) | Icon (optional) + one line + one action, `role="status"`. Reuse the wording rules in `docs/interface/DESIGN_SYSTEM.md`. |
| Drawers / modals | `SideDrawer.razor`, `VendorDrawerShell.razor`, `eemo-modal*` classes | `PayBillModal.razor` (`Components/Pages/Shared/Actions/`) is the payment review reference. |
| Responsive + keyboard | `:focus-visible` on every control, AA contrast, tap targets 40px+, no page-level horizontal scroll at ~400px, one `<main>` per page | Checked in `../SKILL.md` section 8 and `stalltrack-ui-review`. |

## Colour and surface (same palette as Platform)

Civic blue (`--accent #1f5fbf`) for interactive/active, deep civic panel (`--civic-panel #1d4c86`) for identity bands, light cool canvas with white work surfaces, muted gold only as the OR chip tint. Take tokens from `wwwroot/app.css`; no raw hex in `.razor.css`. Pure white is a surface, not the page.

## What this map does not authorize

- Retrofitting every page: a page is restyled only in its own slice.
- Touching financial calculations, SRC/OR/CT behaviour, report grouping or row order for a visual reason.
- Hard-coding tenant names, facilities or amounts in markup.
- Copying Platform's `@stalltrack/ui` code or Tailwind classes into Razor.

## Keep in sync

If Platform's pattern changes (`platform/.claude/skills/stalltrack-platform-ui/references/patterns.md`), update this table by judgment. The two stay independent but visually aligned; the Head Portal wins any disagreement.
