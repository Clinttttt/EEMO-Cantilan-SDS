---
name: stalltrack-ui-review
description: Strict design and implementation review of a StallTrack Web UI change against the V3 visual direction and the frontend safety boundary. Use before handing a UI slice to Clint/Core Brain, when asked to review a UI diff/branch/page, or to audit an existing page for V3 readiness.
---

# StallTrack UI review

Review against `docs/interface/STALLTRACK_UI_V3_DIRECTION.md` (presentation), root `CLAUDE.md` (boundary/runtime), and `docs/interface/DESIGN_SYSTEM.md` (states, a11y, terminology). Be strict; report findings, not reassurance. If `taste-skill` is available, you may use it for an extra composition/typography critique — StallTrack rules win on any conflict.

## Inputs

- The diff (`git diff`, `git diff <base>...HEAD`) or the page files if auditing.
- The page's component tests and the pre-change version of the `.razor` file.

## A. Safety checks — any failure blocks handoff

1. **Backend leakage:** `git diff --name-only` contains nothing under Domain/Application/Api/Infrastructure/HttpClients/Mobile/Migrations, no DTO/contract edits.
2. **Lifecycle/data:** diff the `@code` block. Unchanged `OnInitializedAsync`/`OnAfterRenderAsync` order, `EnsureLoadedAsync`, `[PersistentState]` caches, cache-match conditions, early returns, `_loading` handling, API calls and their triggers. No logic moved into a new child component that loads data.
3. **Behavior:** routes (`@page`), `[Authorize]` roles, `NavLink` matching, event handlers, `@key`, uncontrolled inputs, form fields/required-ness, confirmation flows all unchanged.
4. **Business content:** group membership, row order mirroring the Monthly Income sheet, OR/CT markers, classifications, status wording, money figures unchanged; nothing calculated in markup. Payor behavior unchanged (no implicit linking by name/phone/OR).
5. **Tenant awareness:** no hard-coded municipality, office, facility names, rates or ₱ amounts; failed tenant lookups still show an error, not a fallback list.
6. **States:** loading, empty, failure + retry, unavailable still present with the same conditions and roles.
7. **CSS integrity:** every touched `.razor.css` brace-balanced; no inline styles; no new framework/dependency.
8. **Tests:** focused component tests run; new failures distinguished from pre-existing ones.

## B. Design quality — V3 criteria

Answer each briefly with evidence (selector/line):

- Does it read as one coherent StallTrack product, consistent with the sidebar/shell and sibling pages?
- Government-ready and calm — or admin-template / marketing-dashboard?
- Is hierarchy obvious in a 3-second scan (title → scope → primary surface → rows → actions)?
- Too wordy? Repeated eyebrows, subtitles, helper text, labels restating headings?
- Excessive cards, nested surfaces, tiles for single lines?
- Whitespace balanced — no giant empty regions, no cramped blocks, no raw text floating in white space?
- Rows scan-friendly: name first, aligned columns, tabular numbers, predictable height, hover + focus, chevron only on navigable rows?
- Flat/dry (no header, no alignment, no tonal separation) or overdesigned (gradients, glows, shadows, rainbow colors, pill-heavy)?
- Badges: only for real exceptions; never color-only.
- Color: tokens only; civic blue for interaction/active, navy for identity/text, gold only as small accent; no arbitrary hex.
- Duplicated CSS that an existing token/shared class already covers? A shared primitive that could be reused **safely** (same semantics)?
- Keyboard: focus visible and in logical order; interactive `div`s have role/tabindex/keys or became buttons/links.
- Contrast AA; one `<main>`; accessible names; `prefers-reduced-motion` respected.
- Responsive: usable at ~400px, registers stack, no page-level horizontal scroll, touch targets ≥40px.
- **Polish standard:** is it polished and cohesive? Check typography (contemporary face, clear heading/body hierarchy, comfortable line-height, consistent and actually-loaded font weights), spacing, alignment across columns, color contrast, surface texture, borders, shadows, and consistency of buttons, cards, forms, navigation, tables and modals where present.
- Does the primary register use the full content width, or leave an unexplained blank band?
- Rendering: does anything paint outside the intended surfaces (e.g. a dark canvas below short pages when the OS is in dark mode)?

## Output format

```
UI REVIEW — <page/slice>
Verdict: READY FOR LOCALHOST REVIEW | CHANGES REQUIRED | BLOCKED

Safety (A): pass/fail per item, with file:line for each failure
Design (B): findings ranked most severe first — issue, evidence, suggested fix
Business conflicts for Core Brain: (none | list)
BACKEND GAP blocks: (none | list)
Localhost checklist: routes, states, breakpoints Clint should look at
```

Never approve on build success alone. Visual acceptance belongs to Clint at `https://localhost:7167`.
