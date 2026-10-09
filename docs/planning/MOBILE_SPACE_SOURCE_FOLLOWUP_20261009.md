# Mobile collection and space-account follow-up — 2026-10-09

Status: local feature work; reopening contract proposal awaits approval. No integration or production action.
Checkout: `C:\dev\stalltrack\eemo`, `codex/vegetable-fruit-space-workspace`, starting `74b37157`.
Implementation/test checkpoint: `255db5c0` (`fix(mobile): expose monthly space collection and safe source transitions`).
The concurrent edits in ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE are preserved, not staged with this follow-up.

Authority: current task, [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md),
[office clarification](OFFICE_CLARIFICATION_REFACTOR_20261006.md),
[revenue architecture](../business/REVENUE_ARCHITECTURE.md) and the existing source writers.

## Reopening: verified blocker and proposed contract (not implemented)

`ObligationAccount` represents exactly one inclusive `ActiveFrom` / `ActiveTo` interval. `PeriodStarts` enumerates every calendar month in that interval. Clearing `ActiveTo` would erase closure evidence and expose the entire closed gap to quotation/assessment. There is no reopen endpoint or predecessor/occupancy-period relationship. Account periods have a tenant/account/month unique key, but that only prevents duplicate rows for one account; it does not prevent two accounts charging the same physical space/month.

`ObligationWorkflow.CloseAccountAsync` is Head/SuperAdmin-only, tenant-scoped and refuses closure before an assessed period. Current creation/import numbering reserves normalized source-kind/space numbers across open **and closed** accounts under the tenant space-account transaction. Reusing a closed number via ordinary Add New is rejected. The configuration has no verified monthly-space temporal exclusion constraint; current uniqueness is enforced by that workflow/transaction. Generic audit infrastructure does not supply a missing reopen lifecycle model.

Recommended next slice: **new successor account/occupancy period**, leaving the predecessor closed and all its rates, periods, allocations, Collections and SRCs intact. This is a proposal, not authorization to create it now.

Proposed preview and confirmation contracts:

```text
GET  /api/obligations/accounts/{closedAccountId}/reopen-preview?effectiveFrom=YYYY-MM-DD
     -> eligibility/reason, holder, source kind, space number, original effective date,
        original closure, proposed effective date, approved amount/rate effective date,
        predecessor/root registration IDs, preview version

POST /api/obligations/accounts/{closedAccountId}/reopen
     { clientOperationId, effectiveFrom, expectedPreviewVersion }
     -> successorAccountId, predecessorAccountId, spaceRegistrationId,
        effectiveFrom, approvedAmount, existingOutcome
```

Head must explicitly confirm **new occupancy/account period for the same registration**, not removal of the old closure. Copy source-owned occupant/basis/reference only from the selected predecessor; no name matching or new master Payor. Resolve the approved amount server-side and freeze the confirmed rate for the successor's start; never infer a closed account's missing rate as zero. A changed rate/closure/chain invalidates the preview. No Collection is created.

Proposed migration impact: additive nullable predecessor/root-registration references on accounts (tenant-composite FKs), a space-registration identity/number reservation table backfilled from existing monthly accounts, and an immutable reopen-operation/audit record with tenant/client-operation uniqueness. Existing closed rows retain their dates. Existing number allocation continues to reserve the root number; only the guarded reopen path may create a successor using that reservation. No mass reassessment, destructive data rewrite or migration is included here. Historical duplicate/ambiguous normalized numbers require explicit reconciliation before backfill; do not guess a chain.

Proposed date rule for approval: reopening begins on the **first day of a calendar month strictly after the closure month**, so the current full-month assessment model cannot bill the closure month twice. Reject invalid/backdated gap requests, non-monthly kinds, a non-closed predecessor, any successor already created, a later occupant or overlapping reservation/assessment. If midmonth reopening or reopening before a subsequent occupant is needed, office-approved monthly liability ownership must be specified first. Do not infer proration or transfer a settled month.

The server must lock the tenant/space registration, revalidate the preview and exclusivity, append successor/rate/audit/replay outcome atomically, and return the same successor for an identical retry. Changed replay intent conflicts. Different tenants cannot inspect the predecessor. Serial date checks alone are insufficient without handling concurrent reopen/create/import operations through the same reservation.

Required implementation coverage after approval: PostgreSQL same/future month validation; preserved closure/settled periods/SRCs; a genuinely unassessed closed gap; subsequent-occupant/overlapping-month conflicts; concurrent duplicate/replay/changed intent; Head-only and foreign-tenant refusal; audited creation; roster grouping/count scope and management navigation to the successor. Reopen integration tests are not claimed because no reopen implementation exists. Closed Web accounts therefore still expose Periods only; no unsafe button was shipped.

## Vegetable/Fruits Mobile

Root cause: the standalone governed page requests DailyTransaction only; Monthly now belongs to registered `VegetableFruitSpaceRental` accounts, not a second governed variant. `_variants.Count > 1` could never produce the desired selector. This was not proof of an old installed build.

`/operation/VEGETABLE_FRUIT_SPACE_RENTAL` now always offers Monthly / Daily below the header. Daily starts selected and keeps approved CT terms, optional payer/reference, server session business date, durable queue and retry ID. Monthly lazily mounts the existing `CollectionSessionEditor`, filtered to Vegetable/Fruits registered SpaceAccount identities. It shows source context and server period/outstanding choices, uses the existing OR quote/posting path and has no walk-in/direct fallback. Both mounted views retain their drafts when switching.

A failed/unavailable Daily capability never hides Monthly discovery. Monthly eligibility is established separately when selecting the actual source. Unavailable server reasons are displayed when supplied; an empty Monthly discovery says no collectible periods are available and asks the office to check the account/OR setup. The current discovery contract may omit detailed rejection reasons rather than distinguish fully paid from every configuration failure. No new configuration is inferred from the Daily capability.

## Fish/Meat, ECF and NPM during one visit

Verified registered vendor choices: independent Vendor Fee (direct amount) and Weight & Measure (server quantity/rate quote). Vendor registration grants neither utility nor NPM occupancy identity. Native discovery offers ECF only for an explicitly selected occupancy with the corresponding stall utility source; quote/posting revalidates source ownership and amount/version. Prepared ECF balances may accept amounts within their server-approved balance; no independent vendor-to-ECF direct amount capability exists.

NPM Daily/Whole require the selected occupancy, actual stall, authorized collector/date and source eligibility. `NativeItemMatchesAsync` rejects an item from a different source. The session has **one** SourceIdentity; per-item identities are not supported. Existing instrument totals/outcomes can separate OR/CT documents for compatible items on that one source; they do not authorize unrelated vendor/occupancy identities in one basket.

The Fish/Meat editor now offers **Choose electricity / ECF account** (`/ecf`) and **Choose NPM stall** (`/market`) as separate-source transitions, outside Add Item. They never match a person by name or carry the vendor identity into those flows. Pending items, an open item sheet or an active request disable the transitions, avoiding silent loss of a draft. Record/remove the current items first; recorded confirmation retains the existing New Collection action. No cross-source posting contract was added. Returning to Fish/Meat uses the existing operation navigation; no draft/identity is transferred to the other operation.

## NPM Daily safeguards: verified, unchanged

Daily amount comes from `NpmDailyFee` / effective rate resolver and the applicable stall/occupancy/business date, not a new 30-peso literal. The observed 30 / 900 Cantilan values are configuration evidence, not an application-wide policy. Native Daily quotation rejects `ConfirmedAmount != quoted Total`; the daily command does not accept an arbitrary received stall-rent amount. Paid-day exclusion, policy/canonical activation, source version and idempotency remain server-owned. A positive typed value alone never settles a day.

For more money: use Days Still Owed for eligible unpaid past/current days, or Whole Payment for the server's remaining month balance. Whole can cover future days only through its existing approved monthly rule; it is not arbitrary cash credit. No unrestricted Daily input, excess recognition or new advance/overpayment allocation was introduced. The current UI does not offer arbitrary selection of any requested two days: use the available quoted arrears/whole paths.

`NpmMonthSettlementService` excludes recorded market closures, paid/excused dates and dates outside the correct occupancy window. It does not invent weekend office closures. RentGoal and PureDays remain distinct through the shared month rule, credits and month-end adjustment. Existing legacy/canonical cutover may block canonical Daily/Collect All; this follow-up does not activate tenant policy or convert legacy history.

## Registered Payors discovery assessment

- Search already spans assigned facility occupancies (including NPM/commercial facilities), active Kanmanggay / Vegetable-Fruits accounts, applicable Fiesta/Araw accounts and current-year active Fish/Meat registrations. Results carry typed source IDs and source context. Facility search currently matches occupant text, not a global all-source stall-number search; result limits are cross-source. Closed space accounts and future event registrations are not offered by current active-as-of source search even though history remains readable elsewhere.
- Verified relationships: occupancy → actual stall and eligible rent/utilities; space account → its own obligation periods; registered Fish/Meat vendor → Vendor Fee / Weight & Measure. Compatibility Payor links may supply historical display but never justify same-name source merging.
- No verified person-level grouping of arbitrary registrations exists. A Registered Payors finder can improve names/context/filtering while retaining separate rows/IDs. A person master or vendor ↔ electricity/NPM association is not required and must not be invented.
- A genuinely multi-source checkout needs an approved per-item source contract, eligibility and authorization for each source, updated fingerprints/replay/atomicity, payer/document compatibility and instrument separation. The current one-source session cannot be extended merely by adding menu options.
- Today's Recent Collections is currently the immediate same-day, own/unremitted correction/reversal entry point with required reasons. Records does not expose equivalent correction actions. Removing the list now would remove a useful workflow; move those actions deliberately to Records first, then consider simplifying New Collection.
- Walk-in/direct remains through unmatched-name continuation and dedicated direct operation pages (including Vegetable/Fruits Daily), with only server-approved direct capabilities and optional snapshots where permitted. Weight & Measure, Monthly rent and utility sources keep their source prerequisites.

Recommended next usability slice: improve the existing finder label to Registered sources, add source/facility filters and clearer context, preserve typed rows, move same-day correction access to Records, and keep an explicit capability-driven Walk-in entry. Do not replace New Collection or introduce person merging in this follow-up.

## Verification

Final runs (passed / failed / skipped):

| Check | Result |
|---|---|
| Focused Mobile components | 47 / 0 / 0 |
| Full Mobile components | 80 / 0 / 0 |
| Focused Web space-account components | 59 / 0 / 0 |
| Relevant Unit tests (NPM, month rules, collection sessions, obligations) | 335 / 0 / 0 |
| PostgreSQL integration (NpmDailyCanonicalTests, CollectionSessionTests, ObligationAccountTests) | 110 / 0 / 0 |
| Windows Mobile Release, net10.0-windows10.0.19041.0 | Passed; 13 warnings, 0 errors |
| Mobile scoped CSS sources and generated Windows bundle | 31 brace checks passed |
| git diff --check | Passed |

The PostgreSQL run used disposable Docker/Testcontainers databases. No production data/configuration was touched. No API, Web or shared financial contract changed, so API/Client builds and a full Web rerun were not needed for this Mobile slice.

One earlier full Mobile run was 79 / 1 / 0: the existing asynchronous `Several_items_each_get_their_own_server_src_in_the_result` test timed out in its bUnit wait. Full-suite retries passed 80 / 0 / 0, including the final run after the last UI edit. The source-only Monthly guard was also verified red-before-green: temporarily restoring the old direct fallback made the new Monthly no-fallback test fail (0 / 1 / 0); restoring the guard passed.

Actual rendered component fixtures were inspected with app tokens and source CSS at 346, 390 and 1280 pixels: Vegetable/Fruits Daily, Monthly and Fish/Meat transitions, nine viewport checks with no horizontal overflow and a reachable Daily submit action above bottom navigation. Screenshots, fixture markup, layout facts, test logs and TRX files remain in ignored `artifacts/space-holder-workspace/`; they are not authenticated application screenshots. The UI automation inventory exposed no app/browser surfaces. Live authenticated browser, Windows runtime and Android checks therefore remain pending; no Android validation is claimed.

Changed implementation/test files: Mobile `OperationCollection.razor` and its CSS; `CollectionSessionEditor.razor` and its CSS; Mobile component-test project, `CollectionSessionEditorTests.cs` and new `VegetableFruitModeTests.cs`. This handoff is the only documentation file added by this slice. The three preexisting dirty planning documents remain unstaged and unchanged by this work.

No reopening implementation or migration is included: the proposed lifecycle contract and monthly ownership rule require acceptance under the task's explicit scope gate before implementation. No integration, backend contract change, API deployment, push or APK publication occurred. User visual acceptance on the current feature branch remains pending.
