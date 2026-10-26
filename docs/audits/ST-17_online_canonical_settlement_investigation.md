# ST-17 — Online PayMongo settlement → canonical Collection/SRC: read-only evidence and implementation plan

Date 2026-10-26 · Author Claude 1.1 (Sonnet 5.5) · Status: **investigation only. No code, data, authority or cutover was changed.**

## 1. What the code does today (verified by reading, with file references)

`OnlinePaymentSettlementService.SettleAsync` (`EEMOCantilanSDS.Application/Common/Payments/OnlinePaymentSettlementService.cs`):

| Step | Behaviour |
|---|---|
| L28 | `transaction.IsSettled` → no-op success. This is the exactly-once guard for webhook + payor-return retries. |
| L32 | Captured amount must equal the initiated amount (2 dp) or the settlement fails with `Conflict`. |
| L36–41 | NPM day/whole-month/fish kinds against an *activated* month → `PreserveCaptureForReconciliationAsync` (transaction marked Paid + `SettlementReconciliationRequired`; nothing posted). |
| L44–52 | `MonthlyRecord` target: the linked `PaymentRecord` must be in the same tenant, else Conflict. If `SettlementAuthorityState != Legacy` → **reconciliation-required, nothing posted**. |
| L86–99 | Legacy record: `MarkPaidOnline` / `MarkPartiallyPaidOnline` on the legacy `PaymentRecord` (cross-channel safe: an already-Paid record is not overwritten). **No `Collection`/SRC is created.** |
| L140–180 | NPM day/whole-month: `NpmMonthSettlementService.SettleWholeMonthAsync / SettleUnpaidDaysAsync` (legacy day rows). |

So the ticket's "verified code cause" is accurate, with one qualification that matters for the plan:

* While a monthly source is **Legacy**, `MarkPaidOnline` on the legacy record **is the designed authority** — the money is real and is shown by `CollectionActivityReader.RentAsync` while legacy money is authoritative. Madrid's ₱1 "PAID / Online" with no SRC is consistent with that design. It is **not** a lost payment.
* The genuine gap is the other branch: once a row's authority is **Canonical** (or transitioning), a captured online payment is **only parked** (`RECONCILIATION_REQUIRED`). The payor has paid, the office has no SRC, and a human must reconcile. That is acceptable as a safety net, not as the steady state.

## 2. The reusable canonical writer (no new settlement writer is needed)

* `CanonicalCollectionPostingCoordinator.PostAsync(municipalityId, clientOperationId, intentVersion, normalizedIntent, origin, actor…, lines, document?, collectorId?, payorId?, payerName?, sourceProjections, …)` is the **single atomic writer** (Collection + `PostingOperation` + optional document consumption + source projection in one `SaveChanges`). Idempotency is the unique `(municipality, clientOperationId)` posting operation.
* `CollectionComposerWorkflow.PostRentReceiptAsync` (`…MobileRent.cs`) is the existing rent path for a **Canonical** `PaymentRecord`: it loads facts through `MonthlyRentCollectionSourceAdapter`, rejects `SOURCE_NOT_CANONICAL / SOURCE_VERSION_STALE / SOURCE_REVIEW_REQUIRED / SOURCE_NOT_COLLECTIBLE / AMOUNT_EXCEEDS_OUTSTANDING`, builds one `CollectionLineDraft` with a `PaymentRecord` allocation, applies `ApplyCanonicalRentProjection`, posts with **no physical OR serial** and returns the SRC. A duplicate `DbUpdateException` resolves to the prior outcome.

## 3. Proposed prospective design (needs authorization before any code)

Add one branch in `SettleAsync` **only** where today's code parks the capture, i.e. `TargetKind == MonthlyRecord && SettlementAuthorityState == Canonical`:

1. `clientOperationId` = deterministic GUID derived from the `OnlinePaymentTransaction.Id` (never random; webhook, payor-return confirm and a retry all compute the same value → the unique posting operation makes them one money event).
2. Reuse the `PostRentReceiptAsync` validation/posting body with `origin = "OnlineRent"`, actor = the payor identity of the transaction (system actor if none), `document = null` (the physical OR stays a **separate manual step** — the existing "Awaiting OR" queue; never synthesized).
3. Persist the returned SRC and the PayMongo payment id on the transaction (`ReferenceCode` link; `EEMO-OP-…` stays the gateway correlation reference).
4. Any validation rejection (stale version, mixed legacy components, over-balance, cross-tenant) keeps today's behaviour: capture preserved, `SettlementReconciliationRequired`, no Collection.
5. Legacy rows keep `MarkPaidOnline` untouched. **No backfill and no SRC for historic legacy money.**

Not changed by this plan: settlement authority state, cutover gates, the legacy path, historical records, instrument/classification policies.

## 4. Acceptance matrix to build with the change (none exists yet for the online→canonical branch)

PostgreSQL integration (isolated Testcontainers): canonical row paid online posts exactly one Collection/SRC; webhook + payor-return race (two concurrent `SettleAsync`) → one SRC; same webhook replayed → no second posting; amount mismatch → Conflict, nothing posted; balance changed after checkout → reconciliation-required; double-paid same month (offline first, online second) → reconciliation-required, offline SRC untouched; partial payment; cross-tenant transaction/record → Conflict; Payor Portal balance/history, Collection Activity, Monthly Income, remittance totals and the OR queue all show the same single money event. Browser/PayMongo test-mode: signed webhook and confirmation race, both portals. Existing tests that must stay green: `OnlinePaymentSettlementServiceTests`, `HandlePaymentWebhookCommandHandlerTests`, `ConfirmOnlinePaymentCommandHandlerTests`, `OnlinePaymentLifecycleHandlerTests`.

## 5. Decisions required before implementation

1. Authorize the single new branch above (Canonical monthly record only) — this is a change to settlement behaviour for a source type, so it needs explicit approval and the Q43 cutover gate stays untouched.
2. Is the canonical online line a `PermanentStallRent` classification with instrument OR *awaiting the physical OR* (as the mobile path does with no serial), or must an online payment wait for a document before posting?
3. Actor identity for an online post (the Payor vs a fixed system actor) for audit and remittance attribution.
4. NPM daily/whole-month online rows (legacy day rows, `NpmRentMonth` canonical allocations) have their own coordinated workflow (`NpmRentMonthPostingPermit`); decide whether they are in scope — ST-13 narrows new Payor checkout to monthly stall rent, which makes this the only family to design for.

## 6. Not verified

No live PayMongo test-mode webhook or confirmation race was run; no PostgreSQL test for the online→canonical branch exists because the branch does not exist yet.
