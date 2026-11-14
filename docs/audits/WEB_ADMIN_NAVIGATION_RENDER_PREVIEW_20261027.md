# Web Admin navigation render preview — 2026-10-27

**Status:** Isolated local preview. Not merged, pushed or deployed.
**Worktree:** `C:\dev\stalltrack\v18-sidebar-navigation-render-20261027`
**Branch:** `fix/v18-sidebar-navigation-render-20261027`, from `b8f09b84`.

## Cause and changes

The 2026-10-26 motion layer applied `motion-fade-in` to nearly every root child of `.admin-main`, replaying on route transitions and visually resembling full-page reloads. Its sidebar active-bar animation also replayed on each selection.

- Removed the global route-root entrance animation. Kept dialog, drawer, backdrop, alert and hover transitions.
- `MainLayout` now explicitly schedules a shell render only when the standalone/sidebar visibility changes. Routed content continues to update through Blazor's route parameters.
- `Sidebar` does not recheck resolved municipality branding at every route change, and only invokes mobile scroll locking when the drawer is open. Sidebar active-state updates remain.
- Removed repeated active indicator entrance animation; preserved icon hover/selection feedback.

## Verification

- `dotnet build EEMOCantilanSDS.Client` (Debug): 0 errors; existing warnings.
- `dotnet build EEMOCantilanSDS.Api` (Debug): 0 errors; existing warnings.
- Focused sidebar/navigation/branding tests: 68 passed.
- Full Web component test suite: 1,507 passed, 0 failed, 0 skipped.
- `git diff --check`: clean. CSS braces balanced.
- Preview API: `http://127.0.0.1:5138/health` = 200; `/health/ready` = 200; Swagger = 200.
- Preview Client: `http://127.0.0.1:5298/login` = 200; `/overview` redirects unauthenticated requests as expected.

## Safety and remaining acceptance

The preview uses the existing **local** development PostgreSQL connection (verified localhost only); database startup migrations are disabled. PayMongo base URL points at a non-serving localhost port and gateway keys are unset in the preview process. No production deployment, schema migration, business-rule change, primary-checkout edit or database write was intentionally performed. Readiness does not prove authenticated workflows or all UI transitions.

**Remaining:** Interactive, authenticated browser review of sidebar navigation and document reloads; assess subjective animation feel. Keep preview unmerged until visual acceptance. Port/PID allocation is local-session evidence, not a persistent deployment guarantee.
