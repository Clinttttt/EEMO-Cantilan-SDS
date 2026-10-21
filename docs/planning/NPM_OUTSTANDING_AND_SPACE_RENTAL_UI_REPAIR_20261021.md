# NPM Outstanding and Space Rental UI repair (working-copy handoff, 2026-10-21)

## Scope and isolation
Changes are on local branch `codex/local-unified-v8-v10-20261013`, in a shared dirty working tree with other Claude/Codex agents. No reset, clean, commit, production deployment, or database mutation was authorized or performed. Never use retained municipal PostgreSQL 5432. The isolated workcopy database 127.0.0.1:55491 was inspected using read-only transactions. Docker PostgreSQL integration tests create and destroy their own throwaway database.

## NPM Outstanding Rent diagnosis and fix
Windows Mobile reported "Unable to reach the office API" despite the API being alive. The workcopy API log contained repeated cancellation exceptions in `GET /api/Mobile/npm/outstanding/accounts`: `NpmOutstandingRentWorkflow.AccountsAsync` iterated every NPM occupancy and called `QuoteAsync` for every monthly period before returning a single response. The previous connection explanation was misleading when the real trigger was server request duration.

An additive, authenticated `GET /api/Mobile/npm/outstanding/accounts/page?offset=...&pageSize=...` route and typed `NpmOutstandingRentAccountsPage` are present. The server validates page bounds and enumerates a fixed count of occupancy records per page, including zero-balance accounts when advancing the cursor. The Mobile client fetches one occupancy per call, displays amounts progressively, allows partial progress to remain visible on failures, and invalidates pending loads when the user leaves the screen. All monetary quotes and source access checks remain on the API. The old endpoint remains for backward compatibility. No prepayment, debt arithmetic, collections, or SRC posting rules were changed in this repair.

## Space Rental findings (workcopy PostgreSQL, read-only)
ObligationRates had the approved amount of ₱1,000 for Vegetable/Fruits Space 1, Polar Maglore, not a zero rate. Vegetable/Fruit policies were context-specific: default context=0 Cash Ticket, whole-payment context=1 Official Receipt, daily context=2 Cash Ticket. The Collection Session obligation adapter had incorrectly queried default context for all obligation kinds and displayed "No approved vendor-fee policy is effective" for a space rental. It now chooses `RevenuePolicyContext.VegetableWholePayment` for Vegetable/Fruit and preserves default for the other kinds, aligning with `ObligationCollectionSource.ResolvePolicyAsync`. The typed source and policy remain authoritative and collector assignment/payor checks remain mandatory.

Pony Tail has two distinct Kanmanggay Space 2 occupancy records: the first ended October 9, 2026, and the successor starts November 1, 2026. The source-authorized rates for both are ₱1,000. The Head account read model formerly resolved the rate only against today's date, therefore returned null for the future successor. It now resolves at the future account start, or at the closure date for an ended occupancy, or today for current occupancies. It never invents a rate, rewrites records, or shifts historical debt.

## UI cleanup
Head Space registration no longer asks separately for a contract reference or a duplicate "Monthly Rental per Contract" amount. The existing `ContractReference` and `ContractMonthlyRate` data remain stored, readable, and preserved; the approved monthly rate continues through the established audited rate workflow. The Space edit dialog replaces a long warning paragraph with a concise rate explanation. The shared rental-rate field grid no longer renders an invisible blank column. Windows Mobile monthly status cards now show selection with color borders on white, instead of filling the entire chosen card. The partial amount field and optional legacy OR entry have readable contrast and spacing. The existing canonical NPM amount-entry/SRC notice remains.

## Verified checks
- API project Release build succeeded, zero errors (from source, no shared process restart).
- Head Client Release build succeeded, zero errors.
- Mobile ComponentTests initially passed 156/156; subsequent paged UI tests passed 4/4.
- Disposable-Docker PostgreSQL integration: `SpaceHolder_created_midmonth_is_immediately_listed_and_collectible_only_by_assigned_operation` and `Vegetable_fruit_space_uses_its_approved_whole_payment_OR_policy_not_vendor_fee_policy` passed, 2/2, no skips.
- A future-dated approved-rate regression assertion was added to the integration test.
- Final Mobile component suite passed 157/157 after the navigation-cancellation change, with zero failures/skips. Final Head Client Release build also passed with zero errors.

## Still requires separate follow-through
1. Manual native Windows Mobile tests for NPM Outstanding Rent after **matching API and Mobile binaries** are deployed to an isolated or coordinated preview. The old workcopy API and currently open Mobile window do not automatically gain a new endpoint.
2. **Superseded in source by the October 21 registration/receipt continuation:** optional canonical NPM OR input now validates accountable-form custody and links the document transactionally. See [the consolidated NPM handoff](NPM_MONTHLY_SETTLEMENT_CONSOLIDATED_IMPLEMENTATION_20261017.md). Matching runtime deployment and interactive acceptance remain necessary.
3. **Superseded in source by the same continuation:** the monthly space holder editor supports an optional future approved rate through the existing rate workflow in one atomic transaction. Existing rate and holder evidence remain separate. See the consolidated handoff for validation/deployment status.
4. Browser screenshots at target widths and full release-installation acceptance are still pending.
