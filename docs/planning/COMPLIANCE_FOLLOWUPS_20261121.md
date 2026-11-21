# Follow-ups after the compliance submission

Found by `tools/compliance-e2e` on 21 November 2026. Neither blocks the submission; the owner chose to fix both after it. Fix 1 first, because the compliance matrix answers a panelist with it. When both are done, rerun `node tools/compliance-e2e/run.mjs`.

## 1. An office overpayment on monthly rent is recorded as the bill, not refused

**What happens.** In **Record payment** an office user picks Partial and types more than the month owes, for example ₱2,200 on a ₱1,200 month. The API answers 200. `PaymentRecord.UpdateStatus` (`EEMOCantilanSDS.Domain/Entities/Payments/PaymentRecord.cs`) turns a Partial at or above the bill into Paid and records the bill. The ₱1,000 above it is recorded nowhere, and nobody is told.

**Why it matters.** It answers the matrix row "Excess or wrong amounts" (Duero, Flores). The cash counted at the window is more than the system holds, and the clerk's OR may show the larger amount. Money above what is owed is refunded or applied only on the Head's decision (IA-082), so the system should stop it when it is entered. Until this is fixed, the matrix says "recorded as the bill" instead of "refused".

**Where it reaches.** All monthly rent goes through `RecordPaymentCommandHandler`.

| Caller | Today |
|---|---|
| Office: `FacilityPaymentModal.razor`, `MonthlyPaymentCalendar.razor`, `PayBillModal.razor`, `CollectionExceptions.razor` | The input has `max`, but `@bind` does not enforce it |
| Collector online: `MobileController.cs:319` | The app's collect-balance sheet refuses ("Amount can't exceed the remaining balance"); the main sheet does not |
| Collector offline replay: `SyncOfflineCollectionsCommandHandler.cs:285` | A refusal comes back as a Rejected sync result |

Pay ahead, the collection composer (`AMOUNT_EXCEEDS_OUTSTANDING`) and the NPM part payment (`ExceedsPayable`) already refuse an overpayment.

**Recommended fix.**

1. In `RecordPaymentCommandHandler`, refuse a Partial above the month's bill before `UpdateStatus`. `PartialAmount` is the month's running total, so compare it with the bill. Suggested message: "₱2,200.00 is more than the ₱1,200.00 this month owes. Record ₱1,200.00, and return the rest or take it on another receipt." Keep the upgrade to Paid when the amount equals the bill. Leave the domain's upgrade in place for the other callers.
2. Office payment modal: show the same message before saving, as the collector's balance sheet does.
3. Collector main sheet: the same check before saving. This one needs an app release; the server refusal protects without it.
4. Offline replay: let the refusal come back as Rejected with its message. The collector already holds the cash, so check how the app shows a Rejected item before shipping.
5. Tests:
   - The handler: above the bill is refused, equal becomes Paid, below stays Partial. Reintroduce the cap once to see the test fail.
   - A component test for the modal's message.
   - In `tools/compliance-e2e`, the check "An office amount over the month's bill records only the bill" changes to expect a refusal.
6. Set the matrix row back to "refused".

**Size.** One guard, two screen messages, tests. No migration.

## 2. Online checkout references read "EEMO-OP-" for every office

**What happens.** `InitiateOnlinePaymentCommandHandler.GenerateReference` (line 207) builds `EEMO-OP-<date>-<random>` for every office, so the demo office's checkouts read `EEMO-OP-20261121-…`, and Carmen's would too. Staff see the reference on **Online Payments** and in the OR queue, and it goes to PayMongo as `reference_number` (`PayMongoPaymentGateway.cs:73`).

**Why it matters.** An office name is hard-coded, against the tenant rule in `AGENTS.md` and `CLAUDE.md`. Money and matching are not affected: the return check and webhooks match the whole reference, and the unique index covers the whole string.

**Recommended fix.**

1. Pass the office's short code to `GenerateReference`. Use upper-case letters and digits only, at most 8 characters, and fall back to no prefix when there is none. Format: `<CODE>-OP-<yyyyMMdd>-<12 hex>`, at most 33 characters, under the column's 40 (`OnlinePaymentTransactionConfiguration.cs:16`).
2. **Owner's choice:** use the office's `Code` (`DEMO`, `CANTILAN`) or its acronym. Either way, Cantilan's new references stop reading `EEMO-OP-`.
3. Leave existing references as they are. Old and new references stay unique.
4. A unit test for the generator: `DEMO` gives `DEMO-OP-…`, the length stays within 40, and odd characters are stripped. The tests that use `EEMO-OP-` literals are fixtures and need no change.

**Size.** One method, its caller and one test. No migration.
