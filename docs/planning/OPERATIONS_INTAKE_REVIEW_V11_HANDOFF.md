# Operations intake, review and UI consistency V11 — handoff

Branch `codex/operations-intake-review-ui-v11`, worktree `C:\dev\stalltrack-v2-worktrees\ops-intake-review-v11`, baseline `3fde28ca` (Utility V9). Local commits only; nothing pushed, merged, deployed or migrated in production. Independent of the Rental UI V10 branch (`codex/rental-ui-consistency-v10`), which this branch does not contain.

## 1. Fish / Meat payors who are only a name

**How a payor is only a name.** The Collector may record a Fish/Meat Vendor Fee against a typed payer name with no registered vendor. The Collection line then has no source kind and no source id. That stays a posted, counted Collection.

**What was added.**
- `FishMeatDirectCollectionLink` (append-only, one per Collection, restrict-delete both sides): the Head's explicit link from a Collection to a registration of the same tax year. Additive migration `…_FishMeatDirectCollectionLinks20261013`.
- `FishMeatPayorFollowUpWorkflow` and `api/fish-meat-payor-follow-up`: list, detail (document, similar-name suggestions), resolve. Read is Head/Admin; **resolve is SuperAdmin only**, like the vendor registry.
- Resolve either links an existing registration (must be active and of the Collection's own tax year) or registers a new vendor for that year and links it. It never edits the Collection, its SRC, payer snapshot, amount, remittance or accountable document, and never issues a second SRC. A repeated operation id answers with what it recorded; a second link to the same Collection is a conflict.
- A name match is a suggestion only (exact-or-contained name, same tax year, active). Nothing is chosen for the Head.
- The registry attributes a linked fee to its vendor instead of the walk-up total (moved once, never counted twice).

**State dimensions kept apart.** Posting (Posted/Reversed), financial review (Awaiting/Confirmed/Needs correction/Resolved), registration (Needs registration / Registered: name), NPM link, collection eligibility (collector operation assignment) and cash accountability are separate. "Needs registration" never changes review state, and review never changes registration.

## 2. Head screens
- **Fish / Meat Vendors**: `Registered (n)` and `Needs registration (n)` lists; the follow-up table shows payor, SRC and date, **Amount paid** (not a balance) and a Review drawer. `?tab=needs&collection=<id>` deep-links from the review.
- **From NPM occupants**: one row per occupant (name, area · stall, Linked / Not linked, link icon), Fish / Meat / All filter, history and other areas under "More views". The first step follows the situation: an occupant already linked shows the link; a similar name starts at linking (nothing preselected); otherwise "Register from NPM". Every server safeguard is kept (verification note required, exception and duplicate confirmations, SuperAdmin-only writes).
- **Direct Collection Review**: blue header, one-line subtitle, tooltip for the explanation, compact controls, a **Needs registration** tab, and **View** now opens the collection's detail (items, document, collector, posting, review, registration, append-only history) inside the dialog. The old View was a same-page URL change under an open modal, so it appeared to do nothing. Payer-only Vendor Fees are now direct collections (before, with no source kind, they were not listed at all). Bulk confirm skips an unidentified payor and a possible duplicate; a single financial Confirm stays available.
- **Market Fees / Transfer Large Cattle**: Collections and Setup share one row height at desktop (total stays at the card's foot) and stack at natural height when narrow; the long amount-rule banner is one short line at the fee types it concerns (Setup still carries the rule). Transfer Large Cattle uses the Market Fees hero (readable subtitle, report action).
- **Audit Trail**: a non-create/update/delete event name is shown as wrapping words (`WcfMobileCollectionEnabled` → "WCF mobile collection enabled") with the stored value in the tooltip.

## 3. Mobile
- **Reports → Position**: Collected / Remitted / Unremitted as three figures; the explanatory paragraphs are a tooltip; forms on hand stay a separate paper count ("12 on hand"). Report views are buttons that never wrap. Unused tickets never block remittance and are never shown as money.
- **Records**: "Correct a collection recorded today" opens the existing recent-collections sheet. The server decides eligibility; a correction is a replacement under a new SRC or an audited reversal, never an in-place edit; a closed window shows the server's reason and "Ask the office to review it". Edit now reads **Correct record**. No correction or reversal rule changed.

## 4. Audit Trail and protected work
The main checkout holds uncommitted Audit Trail work (`AuditTrail.razor`, `.razor.css`) that replaces the page hero with the shared `V3Hero`. It was read only and **not copied, staged or touched**; the hero rework is intentionally left to that work. V11 adds only the readable event label (a new `AuditActionLabel`, one badge line, appended CSS), which sits in different lines from the protected hero change.

## 5. Verification
Release build of `EEMOCantilanSDS.slnx`: 0 errors. Unit 2561, Head component 1074, Mobile component 113: all pass. PostgreSQL integration: 533 passed, 7 skipped (snapshot-database tests), 0 failed. `git diff --check`: clean. Runtime: isolated Docker PostgreSQL, API and Head Portal, and the Windows MAUI Debug app against the same API (temporary port edit in `MauiProgram.cs`, reverted and never committed). Screenshots were taken of the Giorgia Macapagal ₱43 case end to end.

## 6. Open office policy decisions
1. A correction replaces a Collection with a new SRC. A payer-only replacement of a payor who was already linked appears again under Needs registration. Should the replacement inherit the original's link? (Left safe: the Head links it again.)
2. The Records page header strip (Collected / Paid / Partial) counts the older payment records only, so it can read ₱0 while the Operation collections below total more. Left as is.
3. Registry Collected figures now follow explicit links. Confirm that is the intended attribution.
4. Audit Trail hero: integrate the protected V3Hero rework.

## 7. Overlap with other work
No edits to Rental UI V10 files, Onboarding V8 / tenant operation-naming components, `integration-v8`, the Platform repo, or the disposable `mobile-v9-smoke` worktree. `FishMeatVendorFees.razor`, `NpmVendorRegistrationWorkflow.razor`, `DirectCollectionReview.razor`, `GovernedServiceWorkspace.razor` and `RecentCollections.razor` are the files most likely to conflict with other branches.
