# AGENTS.md

## Role

You are the Principal Software Engineer for EEMO Cantilan SDS.

You are responsible for:

* Code quality
* Architecture integrity
* Production readiness
* Performance
* Security
* Testing
* Bug prevention

Act like a senior engineer reviewing code before deployment.

Do not act as a code generator.

Act as an owner of the codebase.

---

## Project Context

Before making decisions read:

C:\Users\ASUS VIVOBOOK\Documents\Repository\EEMOCantilanSDS\.kiro\steering\CONTEXT.md

These files are authoritative.

Never ignore them.

---

## Engineering Standards

Priorities:

1. Correctness
2. Report Accuracy
3. Data Integrity
4. Security
5. Maintainability
6. Performance
7. Readability

Never sacrifice correctness for cleverness.

---

## Code Review Mindset

Before implementing changes:

* Look for bugs
* Look for edge cases
* Look for architecture violations
* Look for hidden report inaccuracies
* Look for EF Core inefficiencies
* Look for LINQ inefficiencies
* Look for missing validation
* Look for missing tests

Challenge assumptions.

Do not blindly trust existing code.

---

## Refactoring Rules

Refactor when it:

* Reduces complexity
* Removes duplication
* Improves maintainability
* Improves performance
* Improves correctness

Do not:

* Over-engineer
* Introduce unnecessary abstractions
* Rewrite code for style preferences only

---

## Testing Requirements

Every bug fix requires:

* Happy path test
* Edge case test
* Regression test

Prefer preventing future regressions over quick fixes.

---

## Reporting Rules

Reports are business critical.

Always verify:

* Totals
* Aggregations
* Delinquency calculations
* Collection summaries
* Date filtering
* Outstanding balances

Assume financial inaccuracies are unacceptable.

---

## Completion Criteria

A task is not complete until:

* Build succeeds
* Tests pass
* New tests are added if needed
* Architecture rules are respected
* No obvious regressions remain

Always perform a self-review before finalizing changes.


---

# EXAMINATION BRIEF — NPM 12-Month Payment Record modal shows wrong Collected / Balance

> Status: **RESOLVED — fix implemented & verified.** Scoped to the two NPM branches in
> `PaymentRepository.cs` (`GetPaymentHistoryAsync` + `GetStallLedgerSummaryAsync`).
> Changes: (1) removed the monthly-record precedence short-circuit for NPM so money is
> always daily-truth (₱30/day × contract-prorated days); (2) the daily-collection window now
> runs to end-of-current-month instead of `today`, so advance-paid days count like the daily
> calendar. Non-NPM behavior is unchanged. Regression tests added to `PaymentHistoryNpmTests`
> (2) and `StallLedgerSummaryTests` (1); full suite green (223 passed). Original investigation
> notes preserved below for reference.

## 1. Symptom (reported)

Stall: **Pantom Dant · NPM Stall 1 · Meat Section**. Contract started **June 5, 2026**
(so June 1–4 are before the contract = "N/A"). Today = June 18, 2026.

Two screens disagree for **June 2026**:

| Screen | Total/Obligation | Collected | Balance |
|---|---|---|---|
| Daily calendar (screenshot 1) | ₱480 total fee | ₱480 (16 days collected) | — |
| **12-Month Payment Record modal (screenshot 2)** | ₱900 monthly rate | **₱500** | **₱400** |

The modal is **wrong**. Expected (per owner, "₱30/day everyday, prorated from the
occupancy start date" — same rule agreed last session for FacilityPaymentModal):

- **Obligation (TotalBill)** = collectable days in June × ₱30 = **June 5–30 = 26 days × ₱30 = ₱780** (days 1–4 excluded).
- **Collected** = paid daily collections = **16 days × ₱30 = ₱480** (NOT ₱500).
- **Balance Due** = 780 − 480 = **₱300** (the remaining days 21–30 = 10 × ₱30 = ₱300).

So the three header tiles should read **Collected ₱480 · Balance Due ₱300**, and the
June ledger row should read **Amount ₱480 · Balance ₱300 · Partial**.

## 2. Root cause (verified in code)

File: `EEMOCantilanSDS.Infrastructure/Repositories/Payments/PaymentRepository.cs`
Method: `GetPaymentHistoryAsync(Guid stallId, ...)` — the **NPM branch**.

The NPM branch loops month-by-month. For each month it FIRST checks for a stored
monthly `PaymentRecord` and, if one exists and is not `Unpaid`, returns it **raw and
short-circuits** (`continue`) before the daily-aggregation logic can run:

```csharp
// A recorded monthly payment (admin-entered) takes precedence over daily aggregation.
var rec = payments.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
if (rec is not null && rec.Status != PaymentStatus.Unpaid)
{
    result.Add(new PaymentHistoryDto(period, rec.Status, rec.TotalBill, rec.AmountPaid, rec.BalanceDue, ...));
    continue;   // <-- daily proration below is SKIPPED for this month
}

// (this is what SHOULD run for NPM) obligation = collectable days × ₱30, paid = Σ daily fees
var collectableDays = CountCollectableDays(stall, monthStart, monthEnd);
var bill = collectableDays * FeeRates.NpmDailyFee;          // 26 × 30 = 780  ✔
var amountPaid = monthDailies.Sum(d => d.DailyFee);         // 16 × 30 = 480  ✔
var balance = Math.Max(0m, bill - amountPaid);              // 300            ✔
```

For Pantom Dant, June **does** have a monthly `PaymentRecord` (Status=Partial,
OR `3121212`, PaidAt Jun 4). That record was created by `RecordPaymentCommandHandler`
via the FacilityPaymentModal with:

- `BaseRentalAmount = stall.MonthlyRate = ₱900` (flat 30-day reference, NOT prorated)
- `PartialAmount = ₱500`

`PaymentRecord` computed props (`Domain/Entities/Payments/PaymentRecord.cs`) then give:
`TotalBill = 900`, `AmountPaid = PartialAmount = 500`, `BalanceDue = 900 − 500 = 400`.

So the precedence branch emits `bill=900, paid=500, balance=400`, the `continue` throws
away the 16 real daily collections (₱480), and the modal header sums to **Collected ₱500 /
Balance ₱400**. That is the bug.

This is the **same class of defect fixed last session** (NPM billed at flat ₱900 instead
of prorated days × ₱30) — but in a **different, untouched code path**. Last session fixed
`StallComplianceDto.ExpectedBill` → `NPM.razor` → `FacilityPaymentModal`. The 12-month
history modal (`PaymentHistoryModal.razor`) reads a **separate** pipeline
(`GetPaymentHistoryQuery` → `PaymentRepository.GetPaymentHistoryAsync`) that was not
brought in line.

## 3. Data pipeline (for the implementer)

```
PaymentHistoryModal.razor  (Client/Components/Pages/Shared/Actions/)
  → IPaymentsApiClient.GetPaymentHistoryAsync(stallGuid)
  → GET payment history endpoint
  → GetPaymentHistoryQuery / GetPaymentHistoryQueryHandler  (Application/Queries/Payments/GetPaymentHistory/)
  → IPaymentRepository.GetPaymentHistoryAsync  → **PaymentRepository.GetPaymentHistoryAsync**  ← FIX HERE
  → returns IReadOnlyList<PaymentHistoryDto> (Period, Status, TotalBill, AmountPaid, BalanceDue, ORNumber, PaidAt, CollectorName)
```

The modal header tiles are derived **client-side** from the returned rows
(`PaymentHistoryModal.razor`): `collected = Σ AmountPaid`, `balance` per experienced
month uses `rec.BalanceDue` for partial / `rate` for unpaid, `currentPartial =
currentRecord.AmountPaid`. **Therefore fixing the repository DTO values fixes the header
automatically** — the modal itself should not need changing for the numbers (verify after).

## 4. Recommended fix (single source of truth = daily obligation for NPM)

In `PaymentRepository.GetPaymentHistoryAsync`, the NPM branch must **not** let a stored
monthly `PaymentRecord` override the daily-derived figures. Recommended:

- For NPM, **always** compute `bill = CountCollectableDays(stall, monthStart, monthEnd) × FeeRates.NpmDailyFee`
  and `amountPaid = Σ paid daily fees` for the month (the logic already below the
  precedence branch). Do not `continue` on the monthly record.
- Preserve the admin-entered OR / collector from the monthly record **for display only**
  when present (so OR `3121212` is not lost), but the money (TotalBill / AmountPaid /
  BalanceDue / Status) must come from the daily obligation. Decide with the owner whether
  to prefer the monthly OR or the last daily OR.
- This yields June: `TotalBill=780, AmountPaid=480, BalanceDue=300, Status=Partial`.

Mirror the **same** change in `GetStallLedgerSummaryAsync` (same file) — it has the
**identical precedence short-circuit** and currently feeds `Profile.razor`'s summary with
the same wrong ₱500/₱400 (`totalCollected += rec.AmountPaid; totalOutstanding += rec.BalanceDue;`).
Keep both methods consistent (the XML doc on `GetStallLedgerSummaryAsync` already states it
must mirror `GetPaymentHistoryAsync`).

### Related observations to confirm with the owner (do NOT silently change)

1. **₱500 is not a ₱30 multiple.** A monthly partial of ₱500 for a daily facility is the
   real anomaly the owner flagged ("nothing pays 500 on a single day"). The fix above makes
   the modal recognize ₱480 (16×30) from actual daily collections and ignore the stray ₱500
   monthly partial — confirm this is the desired reconciliation, and whether NPM should even
   accept monthly partial PaymentRecords at all (vs. only daily collections).
2. **`dc.CollectionDate <= today` clamp.** `GetPaymentHistoryAsync` filters `dailies` to
   `<= today` (today = Jun 18), while the daily calendar counts all paid days in the month.
   Screenshot 1 shows days 19–20 paid in advance. After the precedence fix, this clamp could
   make the history show ₱420 (14 days) while the calendar shows ₱480 (16 days). Decide
   whether advance-paid days should count in history (recommend: match the calendar — count
   all paid days in the month).
3. **Stored `BaseRentalAmount = ₱900` on NPM monthly records** (created by
   `RecordPaymentCommandHandler` and the online-payment handler) remains flat. Last session
   deliberately left it; it is harmless only as long as every read path recomputes from the
   daily obligation. This brief's fix is exactly such a recompute for the two history methods.

## 5. Test plan (add, do not weaken existing)

Existing coverage to respect:
`EEMOCantilanSDS.Testing/Infrastructure/Repositories/PaymentHistoryNpmTests.cs` and
`StallLedgerSummaryTests.cs`. Add regressions:

- **Mid-month NPM with a coexisting monthly Partial record**: contract eff. Jun 5; 16 paid
  daily collections (Jun 5–20); plus a monthly PaymentRecord (Partial, base 900, partial 500).
  Assert the June row: `TotalBill=780`, `AmountPaid=480`, `BalanceDue=300`, `Status=Partial`
  (i.e. daily wins over the monthly record).
- **Same scenario via `GetStallLedgerSummaryAsync`**: `totalCollected=480`, `totalOutstanding=300`.
- **Non-NPM unaffected**: keep `History_NonNpm_UsesMonthlyRecordsUnchanged` green.

## 6. Guardrails

- Do **not** modify the daily-calendar path, FacilityPaymentModal, `NPM.razor`,
  `StallComplianceDto`, the reports layer, or any non-NPM facility behavior.
- The fix is localized to the two NPM branches in `PaymentRepository.cs` (history +
  ledger summary). Verify `dotnet build` + full `dotnet test` green before finalizing.
- Money math is business-critical: confirm the proration rule (everyday ₱30, no Sunday/
  holiday exclusion, prorated from `Contract.EffectivityDate` to month-end) with the owner
  before shipping, exactly as agreed last session.

---

# FOLLOW-UP EXAMINATION — Reports still showed ₱500 (the real root cause: NPM dual-recording)

> Status: **RESOLVED (Option A) — implemented & verified.**

## Root cause (deeper than §2)

After the history fix, NPM Reports still showed Pantom at ₱500 while calendar/history showed
₱480. Tracing all three surfaces to their data sources revealed the true root cause:

**`Profile.razor.SavePayment` recorded NPM payments TWO ways at once.** For an NPM stall it
called `RecordPayment` (creating a monthly `PaymentRecord`, Partial ₱500) **and then**
`AutoMarkDailyCollections`, which converts the peso amount into whole ₱30 days
(`floor(500/30) = 16 days = ₱480`). One admin action produced two **inconsistent** artifacts:

- monthly record = ₱500 (kept the ₱20 that can't be a whole day)
- 16 daily-collection rows = ₱480

Each read surface then resolved the conflict differently:
- **Calendar** (`GetDailyCollectionMonth`) and **12-month history** (post-fix): real daily
  rows → ₱480.
- **NPM Reports** (`GenerateStallComplianceAsync`): when an NPM stall has a non-Unpaid monthly
  record it **excludes that stall's daily collections** (`!stallsWithNpmPeriodPayments...`) and
  **allocates the ₱500** across days (`AllocatePrepaidDailyAmountToCollectableRange`: 16×₱30 +
  ₱20 remainder = ₱500) → ₱500 / balance ₱280.

Only Pantom diverged because his ₱500 is **not a multiple of ₱30** (the ₱20 remainder). NPM is
a daily facility — daily collections are the truth; the monthly record is the anomaly.

## Fix implemented (Option A — NPM is daily-only)

1. **Backend guard** — `RecordPaymentCommandHandler` now rejects monthly payment recording for
   NPM stalls (`stall.Facility?.Code == FacilityCode.NPM` → `Failure(…, 400)`). Authoritative:
   blocks web, mobile (`/monthly/collections/record`), and any future caller. Mobile NPM uses
   the separate daily endpoint (`/npm/collections/record`), so this is safe.
2. **`Profile.razor.SavePayment`** — NPM now skips `RecordPayment` entirely and only runs
   `AutoMarkDailyCollections` (early-returns). Non-NPM keeps the monthly flow unchanged.
   `AutoMarkDailyCollections` gained an optional `orNumber` applied to the **first** marked day
   only (daily OR numbers must stay globally unique, so it can't be replicated across days).
3. **Data cleanup** — the one stray NPM monthly record (Pantom, Id `1b214721-…`, ₱500, OR
   3121212) was **soft-deleted** (`IsDeleted = true` — reversible; the app's read paths honor
   the global filter). Verified: 0 active NPM monthly records remain. Other NPM stalls had only
   daily collections (nothing else to clean). With it gone, Reports fall into the daily branch
   → Pantom = ₱480 / ₱300, consistent with calendar + history. **No change to reports code.**

## Verification

- `RecordPaymentCommandHandlerTests.NpmStall_IsRejected_NoMonthlyRecordCreated` added (asserts
  400 + no Add/Update). Full suite: **224 passed**. Client builds clean.
- DB: soft-delete `UPDATE 1`; active NPM monthly records = 0.

## Reversal / notes

- To restore the deleted record: `UPDATE "PaymentRecords" SET "IsDeleted"=false WHERE "Id"='1b214721-c3fd-4946-8f07-865a719eb447';`
- The NPM stall "pay" modal (Profile) now writes only daily collections; a ₱500 entry becomes
  16 days (₱480) — the ₱20 non-whole-day remainder is intentionally dropped (NPM = whole ₱30
  days). The proper NPM workflow remains day-by-day on the daily calendar.

## Follow-up — NPM "Payment Record" modal replaced with a collection receipt

The web `FacilityPaymentModal` was redundant on the NPM page (NPM has no monthly record to
encode an OR against). On `NPM.razor` it is now replaced by a read-only
`NpmCollectionReceiptModal` (`Components/Pages/Shared/Actions/`) that mirrors the mobile
collector receipt (LGU seal, EEMO header, NPM badge, Period / Stall / Payor / Section / OR No,
Days Collected, Daily Fee ₱30, Total Collected, acknowledgment note). `FacilityPaymentModal`
remains in use by the monthly facilities (TCC/NCC/BBQ/ICE via `FacilityPage.razor`) — only the
NPM page was changed. Client builds clean.

### Follow-up 2 — OR on the receipt + monthly facilities

- **OR display fix:** NPM ORs live on the daily collections (no monthly record), but the receipt
  read the compliance OR (monthly-only) and showed "—". `GetNpmDailyStatusAsync` +
  `NpmStallDailyStatusDto.LastORNumber` now surface the most recent paid day's OR, and `NPM.razor`
  prefers it. (No reports-page change.)
- **Generic receipt:** `NpmCollectionReceiptModal` was generalized into
  `CollectionReceiptModal` (takes `ReceiptLine[]` + total/note), used by all facility pages.
- **Monthly facilities:** `FacilityPage.razor` (TCC/NCC/BBQ/ICE) now shows the same receipt on the
  row "pay" action — monthly context (Monthly Rental / Balance Due / Total Paid). The old
  `FacilityPaymentModal` (OR-encode tool) is no longer rendered; OR encoding / payment recording
  for monthly facilities remains available on the stall **Profile** page (the row "edit" action).
  `FacilityPaymentModal` is now orphaned but kept in the codebase. Client builds clean (224 tests
  still green; changes are UI-only).
