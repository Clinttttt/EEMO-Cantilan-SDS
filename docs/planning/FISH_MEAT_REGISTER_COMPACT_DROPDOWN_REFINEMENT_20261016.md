# Fish/Meat Head UI — compact register and selection clarity
Date: October 16, 2026

## Scope and existing work
Isolated branch `fix/fish-meat-clean-review-20261016` incorporates prior commits `66fda975` (Fish/Meat versus NPM identity, working office details editor), `16444428` (register filter cleanup), and `60e5b747` (exact NPM proposal prefill). This refinement changes only Head Portal components and their tests.

## Improvements
- Office register displays a compact default table of vendor/business, fee registration and year, deduplicated display-only address, business activity, **actual recorded** Fish/Meat vendor fees and Weight & Measure collections, and a functional **Edit details** action.
- A visible **Full register / Compact view** toggle retains the original official-paper columns (business/person name parts, sex, registration, tax year, nature and revenue splits). Printing always uses the full register, not a truncated summary.
- Registration tax year and vendor type use the established FhSelect drop-downs with single concise external labels, consistent heights, and constrained menu width; changing year still reloads the correct server data and changing type still filters locally.
- Long explanatory report text stays in a collapsed **About the figures** section. The always-visible note is reduced to recorded amounts and separate walk-up fees.
- Registered Fish/Meat selection for NPM clearly separates *Fish/Meat fee registration* from *unverified NPM tenancy*. A concise status distinguishes **NPM linked** from **NPM not linked**. Picker has aligned year/type controls and shorter labels. Exact vendor ID, duplicate warning and explicit Head verification workflow remain unchanged.
- Fish/Meat dashboard's outstanding `_npmUnresolved` count is labeled **NPM review**, rather than misleading **NPM links**.
- Existing office-details mutation remains guarded by prior-state concurrency, tenancy and collection records untouched. No new payor or stall is auto-created.

## Safeguards
The UI-only change does not modify the municipal PostgreSQL database, revenue, posted collections, SRC, OR/CT, contracts or collector Mobile build. Protected files in primary checkout `C:\dev\stalltrack\eemo` remain unchanged. Unauthenticated smoke checks are HTTP 200 for preview login and HTTP 302 to login for protected register, API health HTTP 200. Authenticated visual acceptance is still required.

## Verification
- Focused tests: 11 passed, 0 failed.
- Full Head component suite: **1,229 passed, 0 failed**.
- New Debug client compiles in isolated checkout; old Release client remained live on port 7567 until verified preview.
