# ECF / WCF utility workspace — checkpoint handoff (Core Brain V6)

Branch `codex/utility-workspace-ecf-wcf` (base `63a70e86`). Local commits only: nothing pushed, merged or deployed; no production configuration, data or migration touched. `StallTrack:NpmPartPayments:Enabled` stays `false`.

## What exists

- **Eligibility is source-native.** `UtilityServiceEligibility` (`Domain/Common`): a space is an ECF or WCF source when it carries that fee in `Stall.Fees` (NPM spaces remain always-eligible, for back-compatibility). No name matching, no assumption that every vendor has service. Applied in the composer, the Mobile ECF/WCF source discovery, the WCF workflow and the reading handler. Collector scope: assigned facilities (WCF also requires the WCF operation assignment and keeps NPM scope).
- **Server register** `GET api/utility-register` (office only): filtered, counted and paged in the database (page cap 100); statuses Unprepared / Unpaid / Partial / Settled / Direct / NeedsReview are server-derived; a direct source never shows an invented balance.
- **Preparation** `POST api/utility-register/prepare` (Admin/SuperAdmin): single and batch, per-source outcomes, expected-version and duplicate checks, through the existing writers. Creates no Collection, SRC or document.
- **One Web workspace** (`UtilityWorkspace`) for both pages: compact hero, filtered/paged register, selected-source panel (Prepare amount Review→Save), "Prepare multiple amounts" (review then per-row results), compact activity. No permanent "Current collection" panel.
  - ECF page: Record payment (Official Receipt) for prepared **canonical** sources through the existing draft contract (add → review → record under a stable operation id; amount ≤ outstanding; refresh-for-today; discard); "Review authority" for legacy; the unfinished-collection notice appears only when a server draft exists (Resume / Discard).
  - WCF page: Collector Mobile status/enable, legacy migration review, compact activity with search/status filter, reconciliation exceptions. **Web WCF recording stays retired** (Collector Mobile only).
  - Activity rows deep-link to `/collections/activity?src=<SRC>`.
  - `/operations/ecf/accounts` and `/operations/water-consumption-fees/accounts` remain as redirects to the workspace.

## Gated, by design (needs an owner decision)

1. **Web WCF recording** — retired (`WEB_CHANNEL_RETIRED`); un-retiring needs a ruling on who is recorded as collector.
2. **Vendor-registration, Fiesta/Araw and space-account utility sources** — no authoritative service record exists; would need an explicit, Head-authorized, effective-dated association (additive).
3. **Web ECF recording for legacy-authority or direct sources** — only prepared canonical sources can be recorded on the Web; direct amounts are Collector/Mobile.

## Database

No migration was required by V6 (reads and writes use existing tables). Nothing was applied to any persistent database.

## Verification

- Unit 2558 passed · Web component 1045 passed · Mobile component 100 passed · PostgreSQL integration 514 passed, 7 skipped (snapshot-dependent, pre-existing), 0 failed.
- PostgreSQL integration (Testcontainers): proven cross-facility eligibility, same names/stall numbers kept separate, 74-source stable paging, filters/totals, prepared vs direct, tenant/role isolation, mode freeze, partial/over-payment, refusals, batch outcomes, Mobile discovery (see `UtilityRegisterTests`, `UtilityCrossFacilityTests`).
- Not done here: Windows MAUI acceptance run and browser review at `https://localhost:7167` — Clint reviews the UI slice before integration.

## Protected files

The eight pre-existing modified files (Login.razor/.css, AuditTrail.razor/.css and four planning docs) were never staged.

## Final review corrections (follow-up to `22afdeb9`)

| # | Correction | Evidence |
|---|---|---|
| 1 | **WCF Collector facility scope.** `FacilityScopeAsync` no longer adds NPM to explicit assignments: scope = the Collector's assigned facilities; only a Collector with the WCF operation assignment and *no* facility assignment keeps the legacy NPM context. The obligations read (`GetObligationsAsync`) previously had no facility filter for Collectors and now applies the same scope. Discovery, quote, direct and prepared posting all use the one rule. | `WcfCollectorFacilityScopeTests` (op-only, op+TCC, op+NPM, multiple, no operation, cross-tenant, forged bill post) and the updated `UtilityCrossFacilityTests` |
| 2 | **Register search** matches a payer name only through the occupancy answering for the selected month (latest-starting covering occupancy), never an earlier/later occupant; historic arrears stay with the occupant of that month. Payer snapshots untouched. | `UtilityRegisterTests.SearchFindsASourceOnlyThroughTheOccupantAnsweringForTheSelectedMonth…` |
| 3 | **Money labels.** The figure is "Net settled" (net settlement applied to the billing month: opening settlement + recorded payment + Collections, net of corrections), never "Received this period". Source panel, hero, Statements and both Reports now say so; the register does not add opening money to canonical Collections. | `UtilityWorkspaceTests` label assertions; browser check |
| 4 | **Cache invalidation.** `RecordUtilityReadingCommandHandler` invalidates the views of the space's own facility (period-wide when unknown), not NPM. | `RecordUtilityReadingDirectOnlyTests.AUtilityChange_InvalidatesTheViewsOfThatSpacesOwnFacility` |
| 5 | **Diff hygiene.** Trailing whitespace removed; `git diff --check origin/master...HEAD` clean. | |
| 6 | Browser acceptance found one more defect, fixed: WCF "Review authority" was offered for routine legacy sources; it is now offered only for a legacy source with recorded payment or one already in cutover (the old page's rule). Modal headers aligned. | `UtilityWorkspaceTests.Wcf_LegacySourceWithRecordedPayment…` |

### Verification (final)

- Unit 2560 · Web component 1045 (three consecutive clean runs) · Mobile component 100 · PostgreSQL integration 520 passed / 7 skipped (pre-existing snapshot-dependent) / 0 failed · Release build succeeds · `git diff --check` clean.
- Intermittent Web component failures seen earlier, by name: `ReportPageTests.NpmRow_Expands_To_Show_Fish_And_FullMonthCoverage`, `RentalConsolidationTests.ClosedRegisterCanPrintManySourceOwnedAccounts`, `RentalConsolidationTests.FacilityRegistryConsumesTheSameRegistryTable(ICE)`. All are 1-second `WaitForAssertion` timeouts, each failing in a different run and only while other heavy builds/tests were running on the same machine; none failed in three consecutive clean full runs or when the two classes were run alone (68/68). They are timing-sensitive, in code this workstream did not change; not reproducible without CPU contention.

### Browser acceptance (executed, Chrome automation against the freshly built Head Portal)

Disposable PostgreSQL container, isolated API (:5217/:5117) and Client (:5298); the persistent dev database and production were never touched; everything was torn down afterwards. Seed: 64 TCC + 10 NCC + 3 NPM sources, one 100-character payor name, prepared/legacy/canonical/direct bills.

Verified: shared ECF/WCF layout and compact header; facility/area/status filters and facility context; 73-source register paged 25/25/… with exact totals (TCC 64: pages 1–25, 26–50, 51–64, Next disabled); long-name wrapping; search of the long name; selected-source panel; single preparation (review → save, no collection created); batch preparation with invalid-amount refusal, review totals and per-row results; ECF Record Payment (review → record, SRC issued, row → Partial, activity updated); unfinished draft recovery (close → reload → notice → Resume → record) ; WCF authority review dialog (legacy with payment only, after the fix); Collector Mobile enable flow; activity SRC deep link (`/collections/activity?src=…` opened the collection); both Accounts redirects; Statements and Reports load; 390 px viewport (no page overflow, register scrolls inside its surface, panel stacks below); API-down error row with Try again.

Not verified in a browser: Reports/Statements print and CSV export.

### Windows Mobile acceptance (executed — the actual Debug Windows MAUI app, driven through its WebView2)

Rebuilt `net10.0-windows10.0.19041.0` Debug, launched the fresh exe against the isolated API (:5117) with a TCC-only Collector (WCF operation assignment). Verified: Collector login; WCF source list shows exactly the 64 TCC water sources (no NPM or NCC); prepared sources show outstanding, others "Direct collection"; direct WCF ₱125.50 → CT collection SRC-2026-000002 with no manufactured assessment; prepared WCF ₱100 against ₱450 → partial, over-payment refused ("cannot exceed the outstanding ₱450.00"); New Collection offers ECF/WCF per source; ECF prepared ₱200 against the outstanding ₱700 → OR collection; ECF direct ₱350 on an unprepared space → OR collection; Head register shows the same figures (T-001 Partial ₱500 outstanding; T-009/T-008 Direct; hero "of it direct"); with the API stopped the app lists no sources ("eligibility could not be verified. Retry while online") so nothing can be posted offline. Narrow layout checked at the app window's native 346 px width. Android acceptance remains separate and was not done.

### Still gated (pending financial-policy / source-model decisions)

Web WCF posting (needs a confirmed collected-by/recorded-by policy); utility eligibility for independent vendor registrations, Fiesta/Araw lots and any source with no authoritative service record; Web ECF direct posting.

## UI quality and consistency pass (commits `5ec76d40`, `cf31d751`)

**Defects found (rendered, not just read):**
1. Mobile WCF/operation status messages used `menu-state-card`, a class with no style definition, so text inherited a near-white colour on a light background (reproduced in the running Windows app by injecting the old class beside the new notice).
2. Every temporary and permanent failure looked the same; permanent office blockers gave no clear instruction and temporary ones had no Try again.
3. WCF source rows showed only "Stall N", so equal stall numbers at different facilities were indistinguishable (`WcfMobileSourceDto` had no facility).
4. Title "Water Consumption Fee" and a fixed "Direct amount" subtitle that was wrong for prepared sources; redundant "Direct amount / COLLECTION AMOUNT" heading and label.
5. Head Portal: stall numbers wrapped at the hyphen ("V-" / "01") in narrow cells; the WCF enable dialog said "Water Consumption Fee".

**Fixes:** shared Mobile `StateNotice` (info / warning / danger tones from the existing `--mobile-*` tokens); Try again only for temporary failures (eligibility unverified, sources not loaded, collections not loaded) with Back to menu always present; office-side blockers (not assigned, not enabled, no policy, inactive) name the office action and offer no retry; additive `FacilityName`/`Facility` on `WcfMobileSourceDto`, shown on the list, selected panel and search; "Water Consumption Fees" with "Month · Prepared or direct amounts"; the form heading follows the source (Prepared balance / Direct amount); word-joiner after the stall-number hyphen on the Head register. No financial rule, flag, OR/CT, SRC, mode-freeze, scope or offline fail-closed behaviour changed.

**Rendered and personally inspected** (Chrome automation for the Head Portal; the freshly rebuilt Debug Windows MAUI app driven through its WebView2 against an isolated API and disposable PostgreSQL):
- Head: ECF and WCF workspaces at 1440, 1024, 768 and 390 (no page overflow); Prepare multiple amounts dialog; selected-source panel; WCF enable bar and dialog. (Single preparation, ECF Record Payment, draft Resume/Discard, authority review, Activity deep link, Statements/Reports and error state were inspected in the previous acceptance pass and not changed.)
- Mobile at 346 px: WCF not-enabled blocker, not-assigned blocker, source list with facility lines, two T-001 spaces at different facilities, empty search, direct form, prepared form, offline "Water sources not loaded" with Try again + Back to menu and successful recovery; ECF new-collection list, select charges, amount, review and completion; bottom navigation. Prepared form and ECF review also at 390 and 430 (no horizontal overflow).
- Not rendered: storage-fault and waiting-to-sync notices (they use the same component but were not triggered); on-screen keyboard behaviour (desktop only); Android.
- Amount entry: typing into an empty field at human speed is correct (120.75 → ₱120.75). A zero-delay select-all-and-retype automation sequence once left the field at "80" while the button read ₱8.25; the button (the bound value) is what is recorded. Not reproduced at human speed; worth a manual check on a device.

**Tests executed:** Unit 2560, Head component 1045, Mobile component 100, PostgreSQL WCF/Utility subset 54 passed (full integration last run 520 passed before this pass; the DTO change is additive), Release build, `git diff --check origin/master...HEAD` clean.

**Environment note:** the Windows app is hard-wired to API port 5117, which an onboarding worktree API occupied; the app was built with a temporary local port change (reverted, never committed) and run against an isolated API on 5118. That process was not touched.

## V9 pass — utility workspace polish, Mobile payment awareness, Fish/Meat utilities, Head direct-collection review (2026-10-12)

Baseline: branch `codex/utility-workspace-ecf-wcf`, HEAD `aa697f3d`; the eight protected files untouched. No push, merge, deploy, APK or production setting. One additive migration (below).

### What changed
- **Source register.** One toolbar row (month, search, facility, a Filters popover holding Area and Status, Refresh and Prepare-multiple as icon buttons with tooltips and accessible names; an active-filter dot). The whole row selects its source by stall id. Source Details is one short line per state instead of paragraphs.
- **Vacant sources.** Root cause: the register SQL treated a contract with no `EndedOn` as covering every later month, but the domain ends it at its term expiry (`StallOccupancy`). Spaces with a lapsed, never-terminated term were listed as "No occupant on record". The SQL now uses the domain rule (`EndedOn ?? term last day`). A source with a genuine bill for the month still lists (historic obligation, no prepare/collect). Test: lapsed term not listed; one space with two successive occupants stays separate per month.
- **Mobile recent collections.** `GET api/mobile/collection-session/recent-utility` (Collector, assigned-facility scoped, tenant scoped) reads the posted allocations and corrections for one stall + utility + period. The Mobile notice shows amount, time and SRC (three rows, "and N earlier"); a reversed collection is shown as Reversed and does not count. A repeat payment asks "Record another payment?" in place of Record. Found live in the Windows app: a tap before the history check answered bypassed the notice, so Record / Add item are now disabled until the check answers (or 8 seconds pass, when it is shown as "not checked"); test added. If the server cannot be asked, the notice says so and never claims none exists. The Mobile caching client forwards the call live and never caches it (found in the real Windows app: the wrapper fell through to the interface default; regression test added). The same ECF/WCF source and period cannot be added twice to one unsent session (edit the existing line instead).
- **Fish/Meat vendors.** A vendor's office-confirmed NPM association (existing `FishMeatNpmAssociation`, exact occupancy still answering) now also advertises which of ECF/WCF that stall really offers this Collector (the stall's own discovery decides). They appear under **Utilities** in the vendor's picker and open the stall as its own source and its own checkout. No new service-declaration authority was invented; no shared OR/CT checkout.
- **Head review.** Append-only `DirectCollectionReviewEvents` (additive migration `20261012111213_DirectCollectionReviewEvents20261012`; in tenant backup/export). `api/direct-collection-review` (Head/Admin): server-paged list with state/date/operation/collector/search filters, review (Confirmed / NeedsCorrection with a note / Resolved), bulk confirm (max 100; only posted, awaiting, non-duplicate items). Direct = Collector-recorded collections with a governed service, weighing, vendor fee/registration, terminal section or direct fine line, or a utility part whose bill is direct. Review never creates or changes a Collection, SRC, allocation or amount; a posted collection stays collected and remittable whether or not it is reviewed. A possible duplicate (same utility source, part, day and amount, still valid) is flagged and excluded from bulk confirm. UI: one icon button with a count right after the Collection Activity search.
- **Statements.** ECF and WCF share one hero and one toolbar row; the instrument label is readable; columns Payor, Facility / source, Assessed, Net settled, Balance. Print: letterhead, "Statement of Account", utility and period, payor, facility/source, instrument, one-row table, two-up signatories; the payment-instruction sentence is gone; one statement per page.

### Evidence (scratchpad/browser/shots, v9-*)
Head Portal in Chrome against a disposable PostgreSQL + isolated API: ECF toolbar and selection (`v9-ecf-1440`, `v9-ecf-selected`, `v9-ecf-filters`), WCF at 1440/1024/390, statements screens and print-media output + PDF for one and all statements (`v9-*-print-*`), Collection Activity and the review panel (`v9-review-panel`). Windows MAUI app (net10.0-windows, real WebView2) at 346 px against the isolated API: source list with facility lines, the recent-collection notice with SRCs, the repeat-payment question.

### Tests
Unit 2560; Head component 1054; Mobile component 108; integration (disposable PostgreSQL, full suite before the final guard fixes) 527 passed / 7 skipped, plus the backup/export and review subsets re-run after; whole-solution Release build 0 errors.

### Decisions still open (not invented)
1. **Head reversal.** There is no Head-side collection reversal workflow in the system (only the Collector's same-day edit/remove on Mobile). The review therefore flags "Needs correction" and records who/why; reversing money from the Head needs its own authorised, audited workflow. Decision needed on who may reverse, approvals and the document consequences.
2. **Vendors with no NPM link.** A Fish/Meat vendor with no office-confirmed association has no stall to carry a utility. A first-use utility-service declaration would be a new source authority (evidence, Head review). Not built.
3. **Shared checkout.** Vendor fees and the stall's utilities remain separate payments. Combining them is a policy and instrument decision (OR/CT).
4. **Mobile-origin review states for non-utility direct operations** are included by source kind; confirm which operations the Head wants in the queue by default.

### Not verified
Mobile ECF recent-collection notice and the Fish/Meat Utilities group were verified by component and integration tests but not driven in the real app (only WCF was, for the live notice and the confirmation); the not-assigned blocker was verified in the previous pass and not re-driven; Android and the on-screen keyboard were not tested; Head Record Payment, draft Resume/Discard and authority review were not re-inspected.

### Integration note
Another session committed `ce9cc606` (statements toolbar/hero/placeholder/dedupe) on this branch during the pass; it is inside this workstream and was kept.
