# V14 Mobile collector consistency — implementation checkpoint

Baseline: production source commit c3e5aae5. Worktree: C:\dev\stalltrack\v14-collector-consistency-20261024; branch feat/v14-collector-consistency-20261024.

## Implemented locally (Phase A)
- Mobile Records canonical items open the existing NCC-style sheet pattern instead of expanding the card. The operation-specific Records component uses the same presentation component.
- Both render true server SRC, actual document number/instrument, posting status, itemized canonical collection lines, and amounts without rewriting records.
- Friendly display labels for known canonical source codes; retain server identifiers and avoid assuming OR/CT from month/day.
- Filtered record searches dismiss stale detail sheets.
- Shared CollectionRecordedSheet for **server-confirmed** collections. Hooked into governed field-collection (including Transportation/Parking), canonical Slaughterhouse, monthly rental, Tabo-an, and successful multi-item CollectionSession checkout. The latter shows the server-confirmed session amount and each child SRC separately. Offline queue and rejected/attention states remain inline, without a false success claim.
- Mobile Menu no longer renders the optional offline-prefetch counter, but background fetching and operational attention remain enabled.
- Mobile position no longer includes the lengthy historical remittance explanatory note; historical totals remain distinct from accounted remittances.
- Shared payer search list expands into available mobile viewport rather than a fixed 272 px pane.

## Still pending from original 12 requests
- Confirm Fiesta/Araw event-lot eligible source predicates against authoritative data and expected pre-event payment policy.
- Add diagnostic identification of which preload requests failed (16/18 screenshot does not expose names).
- Consider remaining operation-specific legacy success paths after device validation; covered canonical paths already use the shared sheet.
- Display session-grouped parent transaction cards across Mobile Records and Head Portal Collection Activity without merging child SRCs/ORs/CTs.
- Implement partial fine settlement end-to-end: quote, allocation, posting, concurrency, remaining liability, correction/remittance tests.
- Audit and hard-block verified duplicate NPM occupancy links/registrations; same-name warnings are not exact identity.
- Evaluate optional Missing OR urgency versus obligatory accountable instrument requirements in history.

## Release safeguards
No database migrations, production changes, deployment, or APK distribution were made in this worktree. Existing official collections and stored identifiers are not modified. Focused Mobile Records tests: 3/3 passed. Full Mobile component tests: 173/173 passed on repeat; an unrelated rapid-search test was intermittent once and passed in isolation and full repeat. Final Android Debug build succeeded (0 errors, 51 warnings, primarily pre-existing dependency constraints). Final full Mobile component suite passed 173/173; targeted Fiesta/Araw registration-source tests passed 2/2 when isolated after one intermittent full-suite failure. Component tests warn about a pre-existing AngleSharp 1.2.0 advisory (NU1902). No production promotion until tests are green and source changes are reviewed.

## October 24 pre-release double-check
- Direct-search source edit now clears stale holder rows synchronously on input. This closes the transient window where an old Fiesta/Araw source could be selected while a new search is loading.
- Targeted source, modal and presentation tests: 18/18 passed. Full Mobile component tests after search fix: 173/173 passed. Fiesta/Araw source-search test repeated 5 times without failure.
- Monthly Collection no longer shows both the inline recorded banner and the success sheet.
- This is **Phase A presentation work only**. The production missing-Fiesta-vendor eligibility issue remains unconfirmed and is not resolved by the stale-row fix.
- Itemized completed checkout is displayed in the success sheet, NOT yet as one parent transaction card in Mobile Records or Web Collection Activity.
- Partial fines/penalties, strict NPM duplicate validation, optional Missing OR policy, and offline-preload failure diagnostics remain pending separately.
- Shipping the API or Head Portal alone does not update the installed Android Collector; the final signed APK and real-device smoke testing are separate release gates.