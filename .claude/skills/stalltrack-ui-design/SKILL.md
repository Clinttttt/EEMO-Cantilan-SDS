---
name: stalltrack-ui-design
description: Implement a StallTrack V3 Web visual change (page, shared component, sidebar, tokens) in the Blazor Client without changing business semantics or runtime behavior. Use before editing any .razor markup or .razor.css for presentation work, e.g. the /operations redesign, restyling a facility page, register, form, modal, or the sidebar.
---

# StallTrack UI design — implementation workflow

Presentation authority: `docs/interface/STALLTRACK_UI_V3_DIRECTION.md`. Business authority: `docs/README.md` order. Role/boundary: root `CLAUDE.md`. Work through the steps in order; do not skip the investigation steps because the change "is just CSS".

## 1. Scope the slice

- Name the exact page/component and route(s). One page or one shared surface per slice.
- Check `docs/planning/ACTIVE_WORKSTREAMS.md` for file ownership/locks on shared files (`Sidebar`, `app.css`, `FacilityHero`, layout).
- If the request implies a business change (group membership, instrument, classification, new field, new action), stop and ask Core Brain.

## 2. Investigate business context

- Find the operation/source in `docs/business/EEMO_OPERATIONAL_RULEBOOK.md`, `REVENUE_ARCHITECTURE.md` and `DECISION_REGISTRY.md` (IA-044…IA-048 for Operations).
- List what the page displays that is **business-owned**: groupings, row order, OR/CT markers, classifications, statuses, money figures, readiness states. These are preserved verbatim.
- Note any mismatch between current UI and canonical docs → record for Core Brain; do not fix silently.

## 3. Map runtime behavior (write it down before editing)

Read the `.razor` `@code` block and every child component it renders. Record:

- lifecycle methods and their order (`OnInitializedAsync`, `OnParametersSet*`, `OnAfterRenderAsync(firstRender)`);
- `EnsureLoadedAsync` calls, `[PersistentState]` caches, cache-match conditions and early returns;
- every API call and what triggers it; `_loading`/`_error` flags; `StateHasChanged`/`PaintLoadingStateAsync`;
- event handlers, `@key`, uncontrolled inputs, `NavLink` matching, `[Authorize]` roles, route templates;
- component tests that target this page (`EEMOCantilanSDS.ComponentTests`, search by component name) and the selectors/text they assert.

Rule: the diff to `@code` should be empty or trivially presentational (a CSS-class helper). If a visual goal seems to need lifecycle/data changes, stop and flag it.

## 4. Inventory existing presentation assets

- Tokens in `EEMOCantilanSDS.Client/wwwroot/app.css` (and any V3 tokens already introduced).
- Shared components: `FacilityHero`, `FacilityPage`, `FacilityStallsTable`, `Toolbar`, `Skeleton`, `FacilityMark`, `AccountChip`, modal classes (`eemo-modal*`).
- Global rules for `MarkupString` SVGs and `::deep` needs for `NavLink`.
- If you restyle a shared component, list all consumers (`Grep` the component name) — they all change.

## 5. Decide hierarchy before styling

Write a short outline: page title → scope → primary surface(s) → rows/columns → actions → states. Remove words that repeat the title; drop eyebrows/labels/descriptions that add nothing; choose register rows over cards; one primary action per local task.

## 6. Implement

- Markup: semantic elements (`section`/`h2`, `ul`/`li`, `table`, `button`, `a`), preserve all bindings, handlers, conditions and test-targeted selectors/text unless tests are deliberately updated with the same meaning.
- CSS: scoped `.razor.css`; tokens only (no raw hex except when adding a token to `app.css`); 4px spacing steps; modest radius; hairline borders; civic-blue focus/active; tabular numbers; `prefers-reduced-motion`.
- No inline styles, no new dependencies, no new frontend framework.
- Prefer rewriting a scoped stylesheet block-by-block with the Edit tool; after editing, **count `{` vs `}`** in every touched `.razor.css`:
  ```bash
  f=path/File.razor.css; echo "$(tr -cd '{' < $f | wc -c) $(tr -cd '}' < $f | wc -c)"
  ```

### Polish standard (Clint, 2026-09-30)

Every slice must end polished and cohesive, not merely correct. Pay particular attention to:

- **Typography:** DM Sans (the loaded contemporary face); clear heading/body hierarchy; comfortable line-height (~1.4 body); consistent weights using only loaded weights (400/500/600/700 — never in-between values like 650).
- **Spacing and alignment:** 4px steps; columns aligned across header, group bands and rows; consistent gutters.
- **Color contrast:** AA for text; tokens only.
- **Surface texture, borders, shadows:** hairline borders, at most two elevation levels, subtle tonal bands for grouping — depth without decoration.
- **Component consistency:** buttons, cards, forms, navigation, tables and modals (where the page has them) use the same radius, border, focus and hover language as the rest of V3.
- **Width:** working registers use the full available content width; don't cap a primary register at an arbitrary narrow width that leaves a blank band.

## 7. Preserve states

Loading, success, empty, failure (+ retry), unavailable/unsupported — each still renders, with the same trigger conditions, `role="status"`/`role="alert"`, and canonical wording.

## 8. Responsive and accessibility pass

- Check ~1440, ~1024, ~768, ~400px mentally against the CSS; registers stack; no page-level horizontal overflow; wide tables scroll in their surface; tap targets ≥40px.
- `:focus-visible` on every interactive element; AA contrast; one `<main>` per page; accessible names for icon-only controls; decorative SVG `aria-hidden`; no color-only meaning; no chevron on non-navigable rows.

## 9. Validate

```bash
dotnet build EEMOCantilanSDS.slnx --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter "FullyQualifiedName~<Page>"
git diff --stat
git diff --name-only | grep -E "Domain/|Application/|Api/|Infrastructure/|HttpClients/|Mobile|Migrations/" && echo "BACKEND LEAK — revert"
```

Report pre-existing failures separately from failures you introduced.

## 10. Hand off for localhost review

Run the `stalltrack-ui-review` skill on your own diff, then summarize for Clint:

- what changed visually, per area; files touched;
- confirmation that `@code`, routes, auth, API calls and business-owned content are unchanged (or exactly what changed and why);
- states and breakpoints to check at `https://localhost:7167/<route>`;
- open questions, business conflicts, and any `BACKEND GAP` blocks.

Do not commit, merge, or push unless asked.
