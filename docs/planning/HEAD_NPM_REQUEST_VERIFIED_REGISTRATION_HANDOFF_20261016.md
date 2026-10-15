# Head NPM request — verified registration handoff and roster diagnostic
Date: October 16, 2026

## Current workflow and why approval is blocked
Josephine's collector-submitted Fish/Meat-origin NPM registration request is `Submitted`, registration-only, and has no verified NPM vendor association. Approval requires an existing active NPM occupancy correctly linked to that *same* Fish/Meat vendor registration, with explicit Head review evidence and a chosen association. Proposed Stall 2 in Fish Area does not reserve the stall and does not authorize revenue collection.

The preceding Head screen merely linked to `/npm` and showed a second generic empty dashboard, requiring the Head to discover **Add New → Registered Fish / Meat** independently. The request-review drawer lacked the shared entry-animation opt-in. The NPM operation also reused a previously cached empty roster for the entire month and treated a failed stall API call as a silent zero-result.

## Changes
- Head request's **Register NPM occupancy** opens `/npm?registerVendor=<exact registration GUID>&registrationYear=<request year>` in a new tab. **Check again** stays available in the original Head drawer.
- The NPM page deep link opens the existing Add New → Registered Fish/Meat chooser automatically; it does *not* submit anything.
- The chooser filters first by the exact registration GUID from the request (never by a coincident name), and tax year, with **Show all vendors** as an explicit alternative. The Head still clicks **Use for a new NPM occupancy**, reviews the contract, actual stall availability, section and office evidence, and submits the existing guarded creation operation.
- Opt into the existing `VendorDrawerShell.AnimateEntry` transition for the Head request drawer and Fish/Meat registry chooser. Existing reduced-motion preference is honored.
- An empty NPM roster is reloaded on the next navigation instead of persisting an empty cached state for the month. Failed/exceptional roster reads now show a retryable error instead of pretending that the database is empty. No changes to historical contract filtering or financial rules.

## Database read-only findings
The existing local PostgreSQL database `EEMOCantilanSDS.DB` contains **34 NPM stalls**, all with current stall status Active; there are **34 active NPM contracts**. Of these, 28 are three-year contracts effective June 7, 2023 (past their expiry by October 2026), while the other six have 2026 contract effectivity dates. This proves the screenshot's zero-stall message is not evidence of an empty underlying NPM table. The screenshot's precise authenticated read failure/stale cache cause could not be independently established without a live authenticated browser trace, so the UI now distinguishes read failures and retries an empty cache.

## Safety and verification
- No request approval, tenancy creation, migration, payment, OR/CT, SRC, or remittance.
- The existing verified-association dropdown is unchanged; approval remains disabled until an exact saved association is selected and review evidence is given.
- Scoped Head component Release tests: **1,226 passed, 0 failed**, including exact-vendor handoff and pending approval safety.
- Release preview (HTTPS 7579) authenticated routes redirect to login, and login responds HTTP 200. The final user-authenticated workflow remains for Head acceptance.
- Changes reside only in isolated `fix/mobile-native-records-npm-polish-20261015` worktree. Primary checkout's eight protected files remain untouched.
