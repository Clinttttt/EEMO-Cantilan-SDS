# Task 2A — exact NPM monthly settlement

Status: investigation / file-level design and pending acceptance tests. No financial correction authorized beyond the narrow boundary in the Task 2A brief. Starting checkpoint `17b9d100`, branch `codex/local-unified-v8-v10-20261013`, primary checkout `C:\dev\stalltrack\eemo`, initially clean.

## Initial design decision

The assessment already knows the correct remaining monthly amount. Whole Payment rounds it down while selecting whole daily installments. Changing only the quote cannot safely fix posting: the existing writer allocates to DailyCollection identities and checks that allocations equal the amount received.

There are three available representations to investigate before implementation:

1. A genuine partial allocation against a real day using existing canonical partial-payment evidence. This preserves the approved daily base fee and records actual received money. However, a terminal monthly payment of ₱25 against a ₱35 day leaves `RemainingBaseFee = 10` and `IsPartlyPaid = true` after the monthly obligation has reached zero. Mobile arrears sums that residual separately. Partial payments also have an explicit feature gate. A narrow quote-loop change cannot prove safe closure, correction or future eligibility.
2. Add the remainder to a paid day as a month-end adjustment. Existing adjustment evidence represents a calendar/eligible-installment shortfall, not rounding down an otherwise sufficient set of daily installments. Reusing it for a different reason needs explicit approval; it cannot be silently relabeled or written into historical paid cash.
3. A source-owned occupancy/month allocation anchor with explicit exact-balance evidence. This is conceptually clean, but the current poster accepts DailyCollection allocations only. Any new anchor/persistence/source kind is outside this task's implementation authorization.

Do not fabricate a ₱25 “full day,” reduce an approved ₱35 base fee, attach money to a fictitious date, round up to ₱910, rewrite a past Collection/SRC, or bypass the gated partial-payment workflow. The final choice requires verified compatibility; otherwise stop with these alternatives for owner approval.

## File-level design before implementation

| File | Required role if an approach is approved |
| --- | --- |
| `EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.cs` | Use one reviewed allocation plan for quote and settlement; exact remaining monthly obligation rather than the sum of whole payable days. Preserve PureDays and existing advance/elapsed timing. |
| `EEMOCantilanSDS.Application/Common/Payments/NpmMonthSettlementService.PartPayment.cs` | Review terminal remainder/part-paid-day interaction; current plan excludes a whole day when it exceeds the month's ceiling. Do not enable its feature gate implicitly. |
| `EEMOCantilanSDS.Application/Common/Revenue/NpmWholePaymentWorkflow.cs` | Bind quote to exact allocations and current evidence; preserve intent/replay-first checks, serializable settlement, stale-quote refusal, exact allocation sum and cache invalidation. |
| `EEMOCantilanSDS.Application/Common/Revenue/NpmDailyCanonicalPoster.cs` | Preserve one Collection/SRC, tenant-owned daily allocations, original cash, delta-only adjustment evidence and exact correction allocation. New source kinds/event meanings require approval first. |
| `EEMOCantilanSDS.Domain/Entities/Payments/DailyCollection.cs` | Preserve approved daily base fee, partial receipt evidence, original Collection references and reversal behavior. A new month-complete/day-partial meaning cannot be assumed. |
| `EEMOCantilanSDS.Infrastructure/Repositories/Facilities/StallRepository.Arrears.cs` | Prevent a daily residual being presented as monthly debt after an exact month settlement, if the day-partial alternative is approved. Do not alter F-02/F-03 or independently invent a balance. |
| `EEMOCantilanSDS.Application/Command/DailyCollections/SettleNpmMonth/SettleNpmMonthCommandHandler.cs` | Match exact allocation under the same closed-month scope; retain Head/Admin authorization, historical occupancy selection, delta posting and ordinary elapsed-day semantics. |
| `EEMOCantilanSDS.Application/Dtos/Mobile/NpmWholePaymentDtos.cs`; existing Head/Mobile clients and callers | Consume server exact amount and truthful allocation evidence; no UI division, fake day count or changed payment authority. |
| Existing NPM canonical, correction, remittance and tenant integration suites | Prove replay, race handling, source isolation, no duplicate SRC/collection/remittance, reversal and historical attribution using separately authorized test infrastructure. No new database/API for this task. |

First additions are acceptance specifications and compatibility counterexamples only. F-02 annual reports, F-03 coverage, PureDays policy, onboarding, rates, OR/CT, Revenue Setup and collection-arrangement configuration remain excluded.

## Verified root cause and exact source locations

The existing monthly assessment is not rounded down. `ResolveMonthAsync` in `NpmMonthSettlementService.cs` computes the obligation, collected amount, credits and remaining amount at lines 310–318. The loss is introduced when the quote and settlement select only complete daily installments:

| Source | Current behavior |
| --- | --- |
| `NpmMonthSettlementService.cs:94`, especially line 102 | `ComputeWholeMonthPayableAsync` breaks before the next fee exceeds `remaining - adjustment`; it returns the sum of whole days plus the existing adjustment. |
| Same file, lines 38 and 163 | Ordinary month quote and settlement have equivalent whole-fee cutoffs. Raising only the whole quote would still leave the day writer short. |
| Same file, line 333 | Month-end adjustment exists only when the remaining eligible installments cannot reach the month's obligation, after excluding partial-day leftovers. This is why February at ₱35 / ₱1,000 already works (₱980 + ₱20), but a 31-day month does not. |
| `NpmMonthSettlementService.PartPayment.cs:44–49` | The partial planner also excludes the final day from its payable list if its full outstanding installment exceeds the monthly ceiling. It then cannot offer even a fractional amount against that day. |
| `NpmWholePaymentWorkflow.cs:37–42`, `:54–72` | Quote hashes eligible days, count, amount and adjustment. Posting checks replay before re-quoting, uses a serializable settlement boundary, rejects changed quotes and asserts charge sum equals received amount. These checks must not be relaxed to make a new quote post. |
| `NpmDailyCanonicalPoster.cs:137`, `:164–168`, `:215`, `:244–249` | Allocations are actual `DailyCollection` IDs and DailyFee parts. Partial and adjustment projections are distinct; void restores exact allocation effects. A new month source is not already supported by this poster. |
| `DailyCollection.cs:104`, `:111–128` | A ₱25 receipt against a ₱35 real day is valid partial evidence, but leaves ₱10 outstanding on that day and does not mark the day paid. It does not express a separate month-completion decision. |
| `StallRepository.Arrears.cs:127–130` | Mobile arrears separately adds partial-day `RemainingBaseFee` without capping that sum to the month's remaining obligation. The counterexample below matters to a real reader, not just a status label. |
| `SettleNpmMonthCommandHandler.cs:102`, `:150–166` | Head's separate elapsed-day writer counts fully paid rows in its already-collected sum and skips partial rows while selecting whole installments. Reusing terminal partial allocations requires reviewing this path as well. |

For a 31-day ₱900 month at ₱35, `remaining=900`, eligible daily sum is ₱1,085, and calendar-shortfall adjustment is zero. Twenty-five whole fees total ₱875; the next would reach ₱910. A ₱25 monthly remainder is left. For ₱1,000, 28 fees total ₱980 and the next would reach ₱1,015, leaving ₱20. After those whole fees have already been paid, the Whole quote and partial planner can both report nothing payable despite the positive monthly balance.

Cantilan's ₱900 / ₱30 divides evenly; February's existing ₱60 calendar adjustment also retains its established meaning. PureDays still owes the actual day-derived amount. No rate authority, amount, month basis or historical cash needs to be changed to *identify* F-01.

## Allocation evidence: the approval boundary

The new in-process counterexample builds 25 genuinely paid ₱35 days and one real ₱35 day with a canonical partial receipt of ₱25. The monthly calculation correctly sees ₱900 collected and zero outstanding. The terminal day nevertheless remains partial with `RemainingBaseFee=10`; its reversal correctly restores that day's original ₱35 base. Current Mobile arrears would add the ₱10 separately. Marking this day fully paid would falsely claim ₱35 was settled; lowering its base to ₱25 would change the approved daily evidence. Neither is an acceptable shortcut.

`NpmPartPaymentWorkflow.QuoteAsync` has an explicit `NpmPartPaymentOptions.Enabled` gate, default off; DI binds `StallTrack:NpmPartPayments:Enabled`. Its source commentary requires reader verification before use. This review did not inspect or change the running process's effective switch and does not claim it is enabled or disabled there. Regardless of the switch, the current partial planner's cutoff still strands the remainder. Enabling it is not the fix, and internal use from Whole must not quietly bypass its governance.

I have **not proved that a new database model is technically unavoidable**. There is a schema-free candidate using existing canonical partial-day allocations, but it is not currently a safe two-line correction: it needs a reviewed meaning for a month paid in full while one underlying day remains partly paid, coordinated quote/write/read/correction handling, and the required concurrency evidence. No such meaning was silently selected. A new month-level anchor or a newly defined adjustment event is expressly outside Task 2A's implementation boundary. Consequently **no production correction was applied**.

## Recommended correction and alternatives for owner approval

### A. First candidate: existing real-day partial evidence, with explicit terminal-month semantics

Consider this first to minimize persistence change, **only after approving the following semantics and scope**:

- Plan exact monthly remaining money server-side, allocate complete real installments oldest-first and a final genuine partial receipt against an eligible real day. Keep that day's approved BaseFee unchanged and preserve its individual allocations/SRC evidence. Do not claim it is a fully paid day.
- Derive month completion from the authoritative monthly obligation/credits/net allocations. A residual portion of a day must not become additional **monthly debt** once that month is paid. Decide how the calendar should label that day, and distinguish any separately authorized ordinary daily collection beyond the monthly rent. Preserve Cantilan's existing ordinary extra-day policy rather than silently abolishing or extending it.
- Keep general part-payment eligibility unchanged unless separately authorized. Explicitly approve whether Whole-only terminal partial allocation may use this representation while general part payment is gated.
- Introduce one application allocation plan used by quote and post, including already partial-paid days. Hash exact source IDs, amounts/settled-before facts, month obligation/credits and applicable rate evidence. A changed allocation or month position must invalidate a stale quote even if its rounded/displayed amount happens to be unchanged.
- Make Head's same-scope closed-month operation and Mobile Whole consume the same reviewed allocation plan. Do not change the intentional distinction between Head elapsed-day settlement and Mobile whole-month advance scope.
- Reconcile Mobile arrears, relevant day/ledger readers, void/replacement and every writer that can revisit the terminal day. A reversal must reopen only the correct source/month amount while leaving other Collections untouched. No residual may be re-posted as an automatic extra daily charge.

This is a broader existing-model correction requiring review, not permission to implement monthly-only onboarding. File changes would be the service/plan, Whole workflow, canonical charge application where needed, Head month writer and the narrowly affected partial-day readers; DTO/client presentation only if needed to report truthful evidence. Existing rates, monthly assessment rule, general collector permissions and remittance remain authoritative. No migration is presumed, but zero-schema feasibility must be proved, not asserted.

### B. Explicit month-balance adjustment event

An approved residual allocation could be represented separately from a full day and from the existing short-calendar adjustment. This requires a defined event meaning, reason/evidence, correction behavior and source identity. Reusing `MonthEndAdjustment` without that distinction would disguise the remainder and change historical field semantics. This alternative needs owner approval and may require persistence; it is not implemented.

### C. Source-owned occupancy/month allocation anchor

The clearest boundary is an explicit NPM monthly position/anchor with exact allocations, preserving legacy DailyCollections separately. It would need stable tenant/occupancy/month identity, canonical source support, idempotency/concurrency, correction and report integration, plus a separately approved additive migration if required. It is architecturally larger and outside this task. Do not create a second assessment ledger or use the generic monthly writer merely to bypass the specialized NPM rules.

Recommended next decision: review A's terminal-day/month semantics first. If those are unacceptable to the office or cannot preserve every reader/correction guarantee, choose B or C explicitly. Until then keep the existing writer intact; do not advertise F-01 as corrected.

## Acceptance evidence and pending verification matrix

New file: `EEMOCantilanSDS.Testing/Application/Payments/NpmExactMonthlySettlementAcceptanceTests.cs`. It reuses the existing Task 1 fixture through a test-only `private` → `internal` visibility change; no production service or test expectation was changed.

The exact-amount theory was first executed **without Skip** against unchanged financial code: **8 failed / 8 passed / 0 skipped** across the new file. Failures cover ₱900 at ₱35 in February and August, with zero or two earlier paid days, plus ₱1,000 in August with zero or two earlier days, and already stranded ₱25/₱20 balances. A representative failure after two ₱35 payments is **expected ₱830, actual ₱805**. These are failed target acceptance cases, not passing characterization of desired behavior.

The eight target cases are now explicitly marked **pending/Skip with the owner-decision reason**. Their assertions remain intact. This avoids representing a blocked financial change as implemented or introducing intentional failures into every routine suite. No existing characterization or safety test was removed, weakened or skipped.

The eight executed passing checks comprise five quote controls (Cantilan February/August, existing ₱1,000/₱35 short-February adjustment, historical PureDays February/August), the month-zero/day-residual/reversal counterexample, and two partial-planner stranded-remainder cases. They operate on in-process mock repositories and domain objects only, not posted municipal cash. Mock GUIDs are not represented as genuine issued SRCs.

| Required acceptance | Evidence / current limit |
| --- | --- |
| Exact ₱900/₱35 and ₱1,000/₱35 | Eight target failures reproduced; now explicitly pending. No false green result. |
| Cantilan ₱900/₱30, February and 31 days | Passing quote controls plus retained Task 1 installment/settlement tests. |
| Existing cash reduces remaining balance | Target cases include earlier valid installments; original Task 1 ₱90 → ₱810/repeated-settlement controls retained. |
| No negative balance/advance | Counterexample confirms monthly outstanding floors at zero while exposing the separate day residual; future exact writer not verified. |
| Head / Mobile authoritative equality | Both paths inspected. Mobile quotes through Whole service; Head's elapsed writer is separate and also cuts off whole fees. Same-scope exact-plan convergence is pending; current-month scope differences must remain explicit. |
| Replay, stale quote and concurrency | Existing replay-before-calculation, re-quote/intent checks and serializable boundaries inspected. New exact allocations must be tested under real concurrent posting; no such correction was posted/tested here. |
| Tenant/source isolation | Existing tenant-scoped repositories and canonical daily allocations inspected; existing unit suite exercised. New financial path not implemented or integration-verified. |
| No duplicate Collection/SRC/remittance | No real collection or remittance was created. Future exact-path idempotency/lost-response/race/remittance checks remain mandatory, not claimed passed. |
| Historical cash and corrections | No production records/source were changed; in-process old daily fees/references stay unchanged, and reversing the simulated partial retains the base fee. Real posted correction/reversal compatibility is pending. |

Relevant existing PostgreSQL suites include `NpmDailyCanonicalTests.cs`, `.Productivity.cs`, `.PartPayment.cs`, `.PartPaymentReaders.cs`, `.PartPaymentSession.cs`, `.Api.cs` and collection-session atomicity tests. They must be reused for the eventual approved correction. They were **not run in this task** because their Testcontainers fixtures would create another database, forbidden by the current one-backend/database boundary. No permission to write test cash into the shared municipal database is inferred.

## Executed verification

All commands ran in the primary EEMO checkout on `codex/local-unified-v8-v10-20261013`, HEAD `17b9d100`. The initial working tree was clean.

```powershell
# Red acceptance run, before adding the explicit pending marker:
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests

# Final focused run:
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~NpmExactMonthlySettlementAcceptanceTests|FullyQualifiedName~NpmMonthlyBillingCharacterizationTests'

# Final full unit suite, using the binaries built by the focused run:
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-build --no-restore
```

| Run | Actual result |
| --- | --- |
| Red acceptance | 8 failed, 8 passed, 0 skipped; total 16. Confirms the defect before any correction. |
| Final focused | 18 passed, 0 failed, 1 skipped theory; total 19 reported by the runner. |
| Final full unit | 2,644 passed, 0 failed, 1 skipped theory; total 2,645 reported by the runner. |

xUnit reports the skipped theory as **one skipped test**, without expanding its eight inline cases. Those eight pending specifications must not be represented as eight passing tests or as a completed exact-settlement implementation. Logs are local temporary files: `%TEMP%\stalltrack-npm-task2a-red.log`, `%TEMP%\stalltrack-npm-task2a-focused.log`, and `%TEMP%\stalltrack-npm-task2a-unit.log`.

The Release test build succeeded. No separate Client/Mobile build, browser acceptance, PostgreSQL integration run, concurrent financial posting or real SRC/remittance verification was performed. These are not substitutes for the pending approved-correction checks. `git diff --check` is part of the final review; no production financial code was changed.

## Safety, Git and remaining decision

No implementation is committed while exact-amount acceptance and compatibility gates remain pending. Expected task files are this design, the new acceptance file and the one-line test fixture visibility change. No production file, API/DTO, persistence model, migration, launch/connection setting, Platform file, financial record, classification, SRC generator or remittance writer was modified. No new API/database/worktree, migration, push or deployment was performed. The existing single API listeners remain untouched.

Owner decision needed: approve A's real terminal-partial evidence and coordinated monthly-cap/lifecycle semantics for a broader existing-model correction, or authorize B/C's explicit month-level evidence design. Task 2A stops at this boundary; it does not proceed to F-02, F-03 or onboarding.

## Owner clarification, 2026-10-16: Cantilan's 31st-day stall rent

**Owner-approved requirement for future design (not implemented):** Cantilan's NPM stall rent is a monthly obligation of PHP 900 in the established PHP 30-per-installment example. When 30 valid PHP 30 installments have fully settled that PHP 900 monthly rent, the 31st calendar day must **not** create, request, post, or display an additional PHP 30 **stall-rent** debt for the same stall and month. The month has zero remaining stall rent. This overrides source comments describing an additional ordinary daily stall-rent collection beyond the monthly rent as expected behavior **for this stated Cantilan scenario**. It does not decide whether separately approved non-rent fees remain collectable, authorize changing historical cash, or change any other municipality's policy. Office ordinance/evidence verification may still be required before operational deployment.

The design and acceptance tests must consistently enforce the monthly rent cap in Whole Payment, daily payment eligibility, Head and Mobile quotes, collection posting, arrears, closed accounts, and reports. In particular, no unsettled *daily* row remainder may be represented or collected as extra monthly stall-rent debt when that month's authorized rent balance is zero. Feb 28/29 and 30-/31-day month behavior must remain coherent with the approved monthly obligation and legitimate adjustments/credits; do not assume all calendar days must generate payable rent. Replay, refund/reversal, SRC/remittance, accrual timing, and period/occupancy boundaries still require explicit design and verification.

**Approval boundary unchanged:** this decision establishes the Cantilan rent-cap outcome, not approval to implement Option A, B, or C or to modify financial code/database. Codex Task 2B should use this as a hard acceptance invariant and return evidence on representation and compatibility before any money-posting change.
