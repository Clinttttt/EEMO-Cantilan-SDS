# Mobile native Records / NPM UX follow-up — 2026-10-15

Base: `48bb5d45` (`codex/mobile-reports-npm-reuse`, includes Fish/Meat UI branch and Mobile Records). Isolated worktree branch `fix/mobile-native-records-npm-polish-20261015`.

## Reproduced issues and precise causes

1. Legacy facility/NPM card details expanded every settled `FeeDate` into a visible line, e.g. 29 daily NPM lines at ₱30 each, although the compact card already stated one combined payment. `Record.razor` also printed `#` before an absent OR and used literal `&amp;` in a date label. It is not permitted to synthesize an SRC from those legacy rows.
2. The charge-selection sheet nested the nonfinancial NPM form inside the Fish/Meat charge list with a verbose explanation and low-contrast controls. This request is not itself a charge; no cash or SRC may be posted during the request.
3. My NPM Requests directly invoked `GET /api/office-sources/npm-requests` against the running **old main-baseline** API on `http://localhost:5117`. The new endpoint is present only in the completed Codex branch, which has an unapplied migration `20261015030030_NpmRegistrationRequests20261015`. The API returns NotFound with no error text, leaving generic `Could not load requests.` and hard-to-read disabled buttons. This is a version/schema mismatch, not evidence that the collector has no requests.

## Implemented

- New scoped `DailyReceiptBreakdown`: one visible summary of paid-date coverage (e.g. 29 covered days, Aug 1–29, 2026), with all daily entries retained under an accessible expandable `details` control. Nonconsecutive dates explicitly say they are not continuous; excused records are excluded from the paid-date summary. Real OR labels only when recorded, no fake `#` or SRC.
- Native Record details no longer duplicates one fully paid receipt total in an additional row. Source, collection date, amount, status, historical receipt evidence and full expandable audit detail remain.
- Compact Fish/Meat missing-NPM registration callout with explicit colors, clear action, one-line policy safeguard and persistent `My NPM requests` link. Form identity/replay and no-cash behavior remain unchanged.
- Native My NPM Requests page now has a compact civic-blue hero, legible states for loading, NotFound API, unauthorized, empty results, returned/approved/pending, compact evidence behind `details`, and pagination only when needed. A missing endpoint explains the required *approved* service rollout and **never reports an empty history**.
- No financial posting, Fish/Meat registration, NPM occupancy, old Collection/SRC, offline queue, tenant ownership, or backend code was changed.

## Tests, build and rollout boundary

- 3 new regression tests: 29-day compact range with all 29 intact entries, nonconsecutive coverage, missing endpoint.
- Full Mobile ComponentTests: 127 passed / 0 failed.
- Windows MAUI Debug `net10.0-windows10.0.19041.0`: build succeeds with 0 errors (15 pre-existing dependency/nullability warnings, including high-severity SignalR advisory).
- The current municipal PostgreSQL database **has not** been migrated for NPM requests. **Do not run the new API against that database or advertise the NPM request functionality as live until the owner separately approves a backed-up schema migration**. The old 5117 API can serve old Records but cannot provide new NPM history/submit.
- No APK publish, cloud deployment, financial data manipulation, or modification to the eight protected dirty files. Native Windows acceptance requires operator's visual test of updated preview.

## 2026-10-15 follow-up: editable stall-number suggestion and section chooser

The collector's new NPM registration request now uses the existing native MobileChoice component (instead of OS-native dropdown). It has four labels: Office to verify, Fish Area, Meat Area, Vegetable Area. It displays a bounded accessible selection list within the scrolling charge sheet.

A new read-only `GET api/office-sources/npm-requests/stall-suggestion?section=FishSection` endpoint uses the authenticated, assigned collector, same-tenant NPM stalls in the selected section, and the same max+1/prefix/padding concept as the web NPM drawer. Only a string suggestion is returned; the endpoint does not allocate a stall or create registration, rent, payment or SRC. A manual value is never overwritten by late responses or later section changes, and unavailable/offline APIs leave the field editable and blank. No database migration is required for this lookup itself, but the broader NPM registration-request feature still requires the separately approved additive migration; do not restart the local API against live schema without approval.

Added regression tests for numeric/prefixed/padded/custom patterns, autopopulation, editable override and old-API fallback. Windows preview can be built without changing the currently running old local API or municipal data.