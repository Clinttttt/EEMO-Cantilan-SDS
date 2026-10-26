# Web Admin smooth navigation and transitions (2026-10-26)

One restrained motion layer in `wwwroot/app.css` ("MOTION" section), applied as DEFAULTS so every page, dialog and drawer gets it without per-component edits:

- **Tokens:** `--motion-fast` 120ms, `--motion-base` 180ms, `--motion-slow` 240ms, `--motion-ease`.
- **Page navigation:** the page in `.admin-main` eases in (opacity only, so a fixed child never shifts) whenever the route changes. Sidebar: icon colour eases with the label, and the new destination's bar grows in (`Sidebar.razor.css`); width/margin collapse already eased.
- **Dialogs and drawers (every variant):** backdrops/overlays/scrims fade; any `role="dialog"` / `aria-modal` rises slightly; docked drawers (`aside`/`role=dialog` classes containing "drawer", `.v3-drawer`, `.cfg-drawer`) slide in from the edge. Components that already declare their own animation (e.g. Facility Configuration, SideDrawer) keep it: the defaults are zero-specificity or lower than a scoped rule, and every keyframe animates only its `from` state, so an element always settles on its own styles.
- **Controls:** buttons, links, inputs, tabs, menu items and table rows ease colour/border/shadow changes (zero specificity; an existing transition wins). Notices that appear after an action ease in.
- **Accessibility:** everything that moves is inside `prefers-reduced-motion: no-preference`; with reduced motion the same screens render with no animation. No exit animations (a closing dialog leaves at once, as before). No JavaScript, no dependency, no layout change.

Verified in a real browser against the scratch Head Portal: route changes between Operations, Collection Activity, Settings and Reports carry the page animation; the Revenue Sources details drawer and its backdrop animate (`motion-drawer-in`, `motion-fade-in`) and settle at transform none / opacity 1 inside the viewport; the Facility Configuration drawer keeps its own animation; with reduced motion the page animation name is none; the sidebar active bar animates; no console errors. Component suite 1,507 pass. Brace balance checked on every edited stylesheet.

Not done: exit transitions, shared-element/route-to-route cross-fades (would need a JS view-transition hook), and per-page choreography. Clint's review at https://localhost:7167 is still the visual approval.
