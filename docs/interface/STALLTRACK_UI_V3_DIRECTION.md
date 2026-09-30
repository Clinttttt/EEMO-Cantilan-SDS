# StallTrack UI V3 Direction

**Status:** Approved Web Office presentation direction (Clint, 2026-09-29). Authority for **presentation only**.
**Applies to:** the Web Office portal (`EEMOCantilanSDS.Client`). Collector Mobile, the Payor portal and the Platform Operator console are out of scope unless a separate rendered review approves them.
**Does not override:** business rules, revenue classification, OR/CT policy, Payor identity, source authority, financial arithmetic, authorization, tenancy, routes, or Blazor lifecycle behavior. Those remain governed by `docs/README.md` authority order.

## 1. Authority for presentation decisions

For visual presentation only, use this order:

1. Clint's latest explicit V3 visual direction.
2. This document.
3. Earlier approved UI direction that is compatible with V3 (see §22).
4. Existing production UI — as an **implementation and interaction reference**, not as the visual target.

Existing pages (NPM, TCC, Operations, reports, modals) remain the reference for working behavior: lifecycle, caching, forms, routes, source data, authorization, domain workflows and accessibility. Their current colors, heavy navy sections, card structure, spacing and typography are **not** automatically the V3 target.

Business meaning is never decided here. Where a presentation choice touches classification, instrument, Payor linkage, group membership or report arithmetic, follow the canonical business documents and report the conflict instead of choosing.

## 2. Design philosophy

StallTrack is a **modern municipal operating system**: calm, credible, structured, government-ready. It is not an admin template and not a marketing dashboard.

The reference quality is the Philippine eGov Agent interface Clint supplied: light canvas, white work surfaces, a clean civic blue for interaction, quiet navigation with one clearly selected destination. **Do not clone it** — no logo, layout, copy or branding transfers. We take its calm and polish and combine it with:

- StallTrack operational density (office staff scan registers all day);
- StallTrack municipal identity (seal, wordmark, deep navy, muted gold);
- Cantilan/EEMO business correctness.

Polish comes from spacing, rhythm, typography, alignment, surface treatment, small accents and interaction states — not decoration.

## 3. Visual vocabulary and color roles

Color communicates role or state, never decoration. Keep the palette small and semantic.

| Role | Use | Notes |
|---|---|---|
| Canvas | App background behind work surfaces | Subtle cool neutral, near off-white |
| Surface | Primary work surfaces, sidebar, topbar | White |
| Surface muted | Table headers, grouped rows, quiet wells | Slightly tinted neutral |
| Border / border strong | Dividers, inputs, surface edges | Thin, cool neutral |
| Civic blue (accent) | Links, primary buttons, focus ring, active navigation, selected rows/tabs | The **main interactive accent** |
| Accent soft | Active-nav background, selected-row tint, hover wells | Very light blue |
| Deep navy | Identity, page titles, primary text, key figures | No longer used for large fills |
| Muted gold | Seal ring, tiny brand marks, rare identity accents | Never a large fill; never body text (fails contrast) |
| Success / danger / warning | Real states only | Always paired with text |
| Disabled | Unavailable controls | Neutral, not faded blue |

### Starting token proposal (tunable at localhost review)

V3 is **not** locked to the reference screenshot's exact blue. These are starting values for Clint's localhost review. They extend the existing tokens in `EEMOCantilanSDS.Client/wwwroot/app.css`; existing names (`--navy`, `--gold`, `--bg`, `--bg-card`, `--border`, `--text`, `--green`, `--red`) stay so current pages keep working.

```css
/* Proposed V3 additions — not yet applied. Introduce in the first approved V3 slice. */
--canvas:          #f5f7fa;   /* app background */
--surface:         #ffffff;
--surface-muted:   #f8fafc;
--border-strong:   #cfd7e2;
--accent:          #1f5fbf;   /* civic blue — interactive */
--accent-hover:    #194f9f;
--accent-soft:     #eaf1fb;   /* active nav / selected row */
--accent-ring:     rgba(31, 95, 191, .35);
--text-secondary:  #4a5d73;   /* replaces low-contrast muted text for readable copy */
--text-tertiary:   #64768b;   /* smallest metadata; verify ≥4.5:1 on white */
```

Rules:

- Add tokens only when a real role needs one. Do not create hundreds of tokens or a per-page palette.
- A feature page consumes tokens; it does not define raw hex colors.
- When blue is tuned, change the token, not the pages.
- The current `--text-muted` (`#8faabf`) is below AA contrast on white for body-size text; do not use it for readable copy in V3 work.

## 4. Surface hierarchy

1. **Canvas** — the light neutral page ground.
2. **Work surface** — one white surface per real working area (a register, a form, a report). Thin border, at most a hairline shadow.
3. **Inner structure** — rows, dividers, label/value pairs, muted wells. Not nested cards.
4. **Overlay** — modal/drawer surfaces with the only noticeable elevation in the app.

A page normally has one to three work surfaces. If a surface contains only one line of text, it should not be a surface.

## 5. Sidebar

- White (or near-white) surface with a thin right border; no dark navy wall, no gradient dividers.
- Brand block: municipal seal (gold ring allowed), StallTrack wordmark in navy, portal label as quiet secondary text.
- **Only the active destination receives strong emphasis:** accent-soft background, accent (or navy) text, accent icon, optionally a short accent bar. Semibold weight.
- Inactive items: secondary text color, regular weight, subtle hover well. Readable, never faded to illegibility.
- Group labels, if kept, are small and quiet; prefer a thin divider before administrative destinations (Collectors, Audit Trail, Settings) over labels.
- Locked Head-only items stay visibly locked with an accessible explanation.
- Collapsed rail and ≤768px drawer behavior, route-matching logic, badge sources and authorization **must not change**. This is a presentation change of `Sidebar.razor.css` (and markup only where accessibility requires it).

## 6. Typography

- Working typeface: **DM Sans** (existing). EB Garamond may remain for the wordmark or a rare formal/print heading; it is not the routine page-title face in V3.
- Clear, compact scale. Suggested starting steps (tune on review): page title ~20–22px/600; section title ~15–16px/600; body/table ~13–14px/400; labels and metadata ~12px/500; micro/uppercase ≤11px only when meaningful.
- No giant titles. No marketing headlines.
- Numbers: tabular figures (`font-variant-numeric: tabular-nums`) and right alignment in registers; money emphasized by weight, not color.
- Line-height ~1.4–1.5 for body; tighter for headings.
- Avoid stacking eyebrow + title + subtitle + helper text. One title and, only if it adds information, one short subtitle.

## 7. Spacing rhythm

- Use a 4px base with a small set of steps (4, 8, 12, 16, 20, 24, 32, 40).
- Consistent page gutter; consistent vertical gap between surfaces.
- Row height predictable within a register (~40–44px for navigable rows, denser for data tables).
- Balance whitespace: neither giant empty areas nor cramped blocks. A half-empty wide surface should become a narrower layout or a two-column register, not stay empty.

## 8. Borders, radius, shadow

- Borders: 1px, cool neutral. Prefer a divider over a new box.
- Radius: modest and consistent — ~8px surfaces, ~6px controls, ~4px chips. No pill-heavy consumer styling.
- Shadow: none or hairline on surfaces; a single soft elevation for overlays and the mobile drawer. No glow, no glass, no gradients.
- A left gold/colored bar on every card is an anti-pattern in V3; accent bars are reserved for the active/selected state.

## 9. Tables and registers

- Registers are the default composition for lists of operations, stalls, payors, collections and reports.
- Entity/operation name first, then context (code, instrument, period), then figures, then actions.
- Header row in surface-muted with small semibold labels; body rows separated by thin dividers.
- Hover well on interactive rows; a visible focus state for keyboard users; the whole row is the link only when the row has one destination.
- Numeric columns right-aligned with tabular figures. Dates in a consistent format.
- A non-navigable row must not show a navigational affordance (chevron/arrow).
- Wide data scrolls horizontally inside its surface; the page body does not.
- Do not calculate or reinterpret financial values in markup to make columns uniform.

## 10. Forms

- Labels above inputs, associated with `for`/`id`. Required marker plus text, not color alone.
- Inputs: white, 1px border, accent focus ring. Keep text inputs uncontrolled per `ARCHITECTURE_RULES.md` §8.
- One primary action (civic blue), secondary actions as outline/ghost, destructive actions visually distinct and confirmation-gated where current behavior requires it.
- Validation messages next to the field and summarized when the form is long.
- Read-only facts use label/value pairs, not disabled inputs.
- Do not add fields, change required-ness, or change what a form submits as part of a visual change.

## 11. Drawers and modals

- Modal: short, bounded tasks (confirmations, a single record edit). Drawer: previews. Full page: canonical financial history (per `REVENUE_ARCHITECTURE.md` §11).
- White surface, clear title, close control with an accessible name, footer with primary action on the right.
- `role="dialog"`, `aria-modal="true"`, labelled by its title; focus moves in and returns on close; Escape closes when current behavior permits.
- Preserve existing open/close state, event handlers and what the modal submits.

## 12. Filters and scope

- Scope (facility, period, as-of) visible above the working surface and still visible after filtering.
- Filter controls in a single quiet toolbar: search, segmented filters, period selector, then actions at the right.
- Selected segment uses accent-soft + accent text; not a heavy fill.
- A filter the destination does not consume must not look active (`DESIGN_SYSTEM.md` §8 still applies).

## 13. Status presentation

- Status is text first. A small tinted chip is acceptable for real states (Paid, Partial, Unpaid, Delinquent, Closed); never color alone.
- Normal rows carry **no** status badge. Show a short state only for an exception (e.g. *Collection unavailable*, *Records only*, *Needs setup*, *Status unavailable*, *Collection inactive*), using wording owned by the canonical documents.
- Instrument markers (OR/CT) are small, neutral, consistent, and carry an accessible name ("Official Receipt", "Cash Ticket"). Their values come from canonical policy — a visual redesign never changes which instrument a row shows.
- Do not collapse status, urgency, lifecycle and evidence into one badge (`DESIGN_SYSTEM.md` §10).
- Avoid badge clutter: at most one state marker per row in routine registers.

## 14. Operation directory rows (`/operations`)

The Operations directory is a set of grouped registers, not a mosaic of dark-headed tiles.

- Group heading: section title in navy, optionally one short line of context. No eyebrow + label + description stack.
- Rows: operation or facility name first (tenant-resolved), code secondary, instrument marker, exception state beneath the name only when needed, trailing chevron **only** for navigable rows.
- Groups may be arranged as balanced columns on desktop and stack on narrow widths.
- Approved pilot pattern (Clint, 2026-09-30): one full-width, sheet-like register with a column header (Income line · Receipt · Pages), tonal group bands carrying a small gold marker and a line count, single-line rows, and a CSS-only detail card on hover/keyboard focus that holds the full receipt meaning and links to existing related pages. Row sub-labels and readiness prose are not shown in the default view.
- Group membership, row order that mirrors the office Monthly Income sheet, routes, instruments and classifications are **business-owned**. A visual redesign preserves them exactly; inconsistencies are reported to Core Brain (see `CLAUDE.md` "Backend gaps and conflicts").
- Loading, failed-lookup (with retry) and empty states stay explicit; a failed tenant lookup must never fall back to a hard-coded facility list.

## 14a. Accountable Forms and the WCF workspace (Clint / Core Brain, 2026-09-30)

**Accountable Forms** (`/accountable-forms`, sidebar between Monitoring and Reports, Head/Admin) is intentionally exposed for the implemented **Cash Ticket inventory and collector custody** capability only: registering a received pre-numbered book, assigning ticket ranges to collectors, and viewing book, collector-custody and per-ticket state as the server reports it (In office, Assigned, Consumed, Needs review, Voided). Assignment transfers physical custody; it records no money.

Its scope is limited. It does **not** declare support for remittance or deposit, complete Official Receipt custody, void/replacement policy, the signed RCD process, or reconstruction of historical accountability. New sections appear only when those capabilities exist.

**WCF on the Web** is monitoring, source review, collection readiness and reporting. Field collection is recorded on Collector Mobile; the WCF page shows obligations, read-only collection activity, Cash Ticket exceptions, a one-line Mobile readiness fact and a link to Accountable Forms. It shows no Web collection entry and no custody forms. Business rules and server enforcement are unchanged by this placement.

## 14b. ONE SOURCE OWNER — NPM Fish / Meat (Clint / Core Brain, 2026-09-30)

**NPM owns the Fish / Meat operational source**: stall and occupancy, the vendor's operational identity, section context, and the Fish / Meat weighing records. Focused financial workspaces may *read* authoritative NPM source facts, but must never recreate source management — no add/assign vendor, stall CRUD, occupant changes, collector assignment, generic collect/post actions, kilo entry or weighing calculations outside NPM. They link back with **Open NPM source** (the existing `/profile/npm/{stallId}` route) or **Open NPM**.

These are three different charges and must stay visibly distinct in every future UI, even when one person owes all three:

**Stall rental ≠ Fish / Meat Vendor Fee ≠ Weight & Measure**

- A Fish / Meat Vendor Fee is never derived from stall rent or any other figure in the frontend.
- Weight & Measure amounts are the server's figures; the UI never multiplies kilos by a rate.
- One OR may carry compatible lines for several of these charges; the document and its classification lines are different things and are never merged or duplicated in the UI.
- Document custody belongs to Accountable Forms, posted collection evidence to Collection Activity, and reporting to Reports.

Current pages: `/operations/fish-meat-vendor-fees` (structure plus a truthful "not yet available" state until the server exposes the obligation) and `/operations/weight-and-measure` (NPM Fish weighing evidence; Meat shown as not yet available).

## 15. Responsive behavior

- Desktop is the primary office environment; pages remain usable at ~400px.
- Registers collapse to a single column; row keeps name, instrument and tap target (≥40px) without horizontal page overflow.
- Wide tables scroll inside their surface.
- Sidebar drawer behavior below 768px is owned by `Sidebar.razor(.css)`; do not reimplement it per page.
- Never hide financial basis, status or action meaning to fit a narrow layout.

## 16. Accessibility

Baseline (`DESIGN_SYSTEM.md` §15 still applies) plus V3 specifics:

- Visible focus on every interactive element: 2px accent ring with offset (`:focus-visible`). Never remove focus without an equal replacement.
- Body and label text ≥4.5:1 contrast; large text and UI boundaries ≥3:1. Gold is not a text color on white.
- One `<main>` landmark per page (the layout already renders `<main class="admin-main">`; pages should not nest another `<main>`).
- Clickable non-button elements (e.g. `div @onclick` tabs) should be real `<button>`/`<a>` or carry role, tabindex and key handling when touched by a V3 slice.
- Icon-only controls have accessible names; decorative SVGs are `aria-hidden`.
- Loading regions use `role="status"`/`aria-busy`; failures use `role="alert"`.
- Respect `prefers-reduced-motion`.

## 17. Motion

- Short, functional transitions only: 120–200ms on color/background/border/transform for hover, focus, open/close.
- No animated gradients, parallax, bouncing or decorative loaders.
- Disable non-essential motion under `prefers-reduced-motion: reduce`.

## 18. Loading, empty and error states

- Every async page keeps its existing loading, success, empty, failure and retry behavior. A redesign restyles these states; it does not remove or merge them.
- Loading: skeleton rows in the register shape, or a quiet status line — not a spinner over a blank page.
- Empty: one sentence using the page's canonical terminology, stating the active scope; an action only when one really exists.
- Error: what failed, in plain language, plus retry when the page already supports it. Never substitute fallback tenant data.

## 19. Anti-patterns

- Full-height dark navy sidebar or wide dark hero bands.
- Decorative gradients, radial glows, glassmorphism, drop-shadow stacks.
- Dashboard tile mosaics for data that belongs in one register.
- Cards inside cards; every section as its own tile.
- Eyebrow + title + label + description stacks; repeated helper copy.
- Badge on every row; rainbow category colors; random one-off colors.
- Giant titles, giant empty whitespace, or raw text floating in blank white areas.
- Chevrons on rows that do not navigate.
- Visually dry tables with no header, alignment or hierarchy.
- Overly rounded, pill-heavy consumer-app styling.
- Hard-coded tenant names, rates or amounts in markup.
- A visual change that also changes data loading, routes, events, or financial meaning.

## 20. Component reuse rules

- V3 workspace primitives (introduced with ECF/WCF, 2026-09-30): `WorkspaceHeader` (breadcrumb, title + code, receipt/meta line, right-aligned actions — replaces the dark `FacilityHero` on V3 workspaces), `InstrumentChip` (OR/CT with spoken name), `SummaryStrip` (displays server-supplied figures; formats and calculates nothing), and the global `v3-` layer in `app.css` (`v3-page`, `v3-panel`, `v3-toolbar`, `v3-table`, `v3-btn`, `v3-field`, `v3-notice`, `v3-modal`, `v3-facts`). New V3 pages use these instead of page-local copies.
- V3 report primitives (introduced with the ECF/WCF reports, 2026-09-30): `ReportLetterhead` (tenant seal, "Republic of the Philippines", municipality/province and office from branding — never a hard-coded LGU — plus title, period, "Prepared by" and "Date prepared") inside a `v3-report-sheet print-report-sheet` article printed through `stalltrackPrint.reportDocument`; `v3-report-table` for thin-ruled, number-aligned tables with repeating headers; `v3-report-note` for basis statements. Screen chrome sits in `.no-print`. Print and export are disabled while loading or after a failed load, so an empty or partial document can never be issued. Official monthly-income rows show "—" (unavailable, not zero) until the server supplies approved figures.
- Revenue-line primitives (Market Fees, Landing / Berthing, 2026-09-30): `RevenuePolicyPanel` (effective policy of one revenue classification by semantic code, with Head-only, failed and not-configured states) and `MonthlyIncomeUnavailable` (the official Monthly Income row printed as "—" until approved targets and classified income exist). A revenue line whose collections StallTrack does not yet record shows an honest "not recorded yet" collection panel — never sample rows or an entry form that saves nothing.
- Facility workspaces (NPM, TCC, NCC, BBQ, ICE, SLH, TRM, TPM) and their dialogs, calendars and sub-pages were brought onto V3 on 2026-09-30 **through presentation only** — their loading, caching and collection logic is untouched. The shared `FacilityHero` and `FacilityPage` identity bands are light V3 bands; `Toolbar`, `FacilityStallsTable` and the shared legacy classes in `app.css` (topbar, data-table, btn-*, eemo-modal-*, form-*, search-box, filter-tab — see the "V3 ALIGNMENT FOR SHARED LEGACY CLASSES" block) use V3 tokens; each page stylesheet ends with a marked "V3 ALIGNMENT" block. Inside those pages the legacy `--text-muted` is re-pointed to `--text-tertiary` and `--gold` (used there for interaction) to `--accent`; 9–10px labels were raised to 11px; charts are flat (no gradients or 3-D bar faces). Semantic status colours are unchanged.
- The remaining office pages (Overview, Collection Activity, Online Payments, Payors & Accounts, Monitoring / Follow-up, Reports and every facility report, Collectors, Audit Trail, Settings and its sub-pages, imports, exports) were aligned the same way on 2026-09-30, stylesheets only: dark `vs-hero`, `rpt-topbar` and similar banners became the light identity band; small navy fills (buttons, active tabs, badges) became civic blue; gold text became civic blue; labels under 11px were raised; serif headings use DM Sans. The legacy `--text-muted` / `--text-subtle` tokens now carry AA-readable values. Sign-in, account-setup and two-factor screens keep their branded presentation and are a separate review item.
- Sample data is never shipped in a workspace or report. Where a page once carried illustrative accounts, it now reads the server contract or states that the figure is unavailable.
- Reuse existing shared components first: `FacilityHero`, `FacilityPage`, `FacilityStallsTable`, `Toolbar`, `Skeleton`, `FacilityMark`, `PayorPicker`, `AccountChip`, existing modal classes.
- Restyling a shared component affects every consumer — check all usages and review them together at localhost.
- Create a new frontend primitive only when the same semantics repeat on at least two pages (e.g. a register row, a section heading, an instrument marker). Keep it small, scoped (`.razor.css`), and presentation-only.
- No new frontend stack: no Tailwind, Bootstrap replacement, Material UI, CSS-in-JS or JS SPA framework. No inline styles for routine design.
- Visual similarity alone never justifies sharing a component across different business models.

## 21. Using taste-skill

If the third-party `taste-skill` is installed, use it as a **design-review aid**: composition, spacing, typography, hierarchy, generic/AI-looking UI detection, polish. It is never business, financial or architecture authority and never permission to change working behavior. When it conflicts with StallTrack rules or this document, StallTrack wins. Do not copy its examples literally.

## 22. Relationship to earlier presentation guidance

This section supersedes earlier guidance **for Web presentation only**. Every business, accessibility, terminology, state and source-authority rule in those documents remains in force.

| Earlier source | Earlier presentation rule | V3 status |
|---|---|---|
| `DESIGN_SYSTEM.md` §1.1, V2 master spec §3/§18, `AGENTS.md` "Interface V2 preserves the production UI", IA-037 | Preserve the current production visual treatment; navy structure; old dark sidebar | **Superseded for Web presentation.** The *behavioral* preservation (workflows, lifecycle, routes, terminology) still applies in full. V3 visual changes still ship as small, reviewed slices, never a wholesale rewrite. |
| `DESIGN_SYSTEM.md` §3–§4, `REVENUE_ARCHITECTURE.md` §11 | Navy/gold identity dominates; gold for active context | **Refined.** Navy remains identity/text; gold becomes a small accent; civic blue becomes the interactive accent and active-state color. |
| Unmerged `docs/ui-theme-direction` branch (2026-09-28 "light Web renewal", UpSkwela-inspired) | Light shell, navy primary actions, gold active indicator | **Historical/reference evidence only (Core Brain, 2026-09-29).** Not merged or cherry-picked. Visual ideas may be learned from selectively; its color choices do not override V3 and its business/readiness assumptions are not imported. |
| Current `Operations.razor.css`, `FacilityHero.razor.css`, `Sidebar.razor.css` | Dark hero bands, navy card icons, gold card bars, dark sidebar | Transitional implementation. Reference for behavior only. |

What V3 does **not** change: page anatomy (`DESIGN_SYSTEM.md` §2), card restraint (§6), money/date basis rules (§11), standard states (§12), confirmation UX (§13), status vocabulary (§10), and the human localhost visual gate.

## 23. Delivery rules

- One page or shared surface per slice; independently reversible.
- Build, run focused component tests, check brace balance of every edited `.razor.css`, then Clint reviews at `https://localhost:7167`. Build success is not visual approval.
- A slice that would need a backend change stops and records a **BACKEND GAP** instead of simulating behavior.
