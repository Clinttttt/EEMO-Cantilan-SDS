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
