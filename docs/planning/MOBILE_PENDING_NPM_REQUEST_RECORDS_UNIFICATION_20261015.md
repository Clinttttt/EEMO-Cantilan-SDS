# Mobile NPM pending-request recall and Records consistency — October 15, 2026

## Root-cause verification

The authorized local migration `20261015030030_NpmRegistrationRequests20261015` is applied, and the municipal **local** PostgreSQL database contains exactly **one submitted NPM registration request**, for Josephine Reyes. That request was retained after returning from the mobile form. No new registration request was created by this UI repair.

The native Mobile `NpmRegistrationRequestForm` previously stored its status only inside the current Blazor component (`_saved`). After navigating back to the vendor it mounted a fresh instance and rendered “Request NPM registration” again, although the backend already prevented duplicate pending requests for the same collector/vendor. It was a missing live status reader on re-entry, not a missing migration or data-loss bug.

The legacy facility/NPM Records cards used the shared collection-row primitive but sent it many extra visible fields (time, stall, paid status, missing-OR placeholder and merged-date context). Canonical SRC cards above were only name/reference + amount, date, and revenue line, creating the observed inconsistent height.

## Changes

* Add `GET /api/office-sources/npm-requests/pending/{vendorRegistrationId}`, collector-authorized and tenant-scoped; the server filters by both authenticated collector ID and stable Fish/Meat registration ID, and returns only current `Submitted` state. No name matching, cross-collector information leakage, collection posting or extra database schema.
* Extend `IMobileApiClient`, real HTTP client and caching decorator. The request lookup is **never cached** and returns `NotFound` only when no active request exists.
* Form retrieves current pending state when the vendor is selected or re-entered. Shows `Pending review`, the proposed stall and the My NPM requests link, without another submission button. Missing/offline/unauthorized lookup **fails closed** with a retry status button instead of assuming no pending request. When a stale submit gets a conflict, look up pending server status again. Existing submitted intent and client operation ID are preserved for uncertain network results. The form never receives money or issues SRC.
* Records: lower legacy cards now use the same compact three-line hierarchy as the canonical SRC cards: verified payor name and actual collected amount, collection date, one source/nature line (with corresponding amount). Fully paid status, missing receipt placeholders, per-day period chips and stall details moved to existing click-through receipt/details sheet; genuine partial/absence indicators remain visible and complete historical details are unchanged. **Never synthesize a legacy SRC.**
* Protect the existing eight dirty primary-checkout files and preserve original Head Portal, collection writers, database records and API 7597.

## Verification and local preview

* Read-only PostgreSQL query before the fix: `total_requests=1`, `pending_requests=1`, `josephine_requests=1`.
* Mobile component tests: **139 passed, 0 failed**. Regression tests cover pending state surviving page remount, unavailable status failing closed, conflict recovery, old form retry and compact Records presentation.
* API Release build: **0 errors**; Windows MAUI Debug build: **0 errors**; existing dependency warnings remain.
* Current Windows preview from isolated branch and API `http://localhost:5117` are running, and new endpoint appears in Swagger; unauthenticated endpoint returns 401.
* No new migration, real collection, SRC, remittance, production publication or APK change. Authenticated real-user visual acceptance is still required. Head review requires separately deploying/updating the appropriate Head-side portal/API.
