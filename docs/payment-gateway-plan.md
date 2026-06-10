# Online Payment + Payor Portal — Implementation Plan

**System:** EEMO Cantilan SDS (Clean Architecture · DDD · CQRS · MediatR · Blazor Server + .NET MAUI · PostgreSQL)
**Feature:** PayMongo/GCash online payment with a self-service Payor Portal
**Status:** Approved plan — pending phased implementation
**Last updated:** 2026-06-10

---

## 0. Confirmed decisions

1. **Provider:** PayMongo **hosted Checkout** with **GCash** (no card data handled → no PCI scope), behind a swappable `IPaymentGateway` abstraction.
2. **Portal delivery:** Same Blazor app, in a **separate `/payor` area first** (own layout + `Payor`-role auth + anonymous activation pages). Can be split into a standalone Payor Portal project later if needed.
3. **Activation:** Self-service using **one-time activation code + registered contact number** (both must match the payor's existing record). No admin-created logins.
4. **Delinquency timing:** Clears **when PayMongo confirms payment** (money received). The payment then remains **Awaiting OR** until EEMO staff encode the Official Receipt number.
5. **Scope (v1):** **Monthly-rental facilities (TCC, NCC, BBQ, ICE) + NPM.** Pay-as-you-go facilities (TRM, SLH, TPM) excluded from online payment v1 (point-of-service).

---

## 1. Grounding in the current system

- **Payments today:** `PaymentRecord` (per stall per month) carries `Status` (Paid/Partial/Unpaid), computed `TotalBill`/`AmountPaid`/`BalanceDue`, fee breakdown (rental + utilities + NPM fish fee), `ORNumber` (manually encoded by admins), `CollectorId` (collector attribution; `null` for admin/online). NPM also uses `DailyCollection` for daily ₱30 marking.
- **Auth:** `BaseUser` (TPH) → `AdminUser`, `CollectorUser`. JWT access (15 min) + hashed refresh tokens, lockout, `PasswordHasher`. **Payors are not users today** — they are `Contract` occupants (`ActualOccupant` / `NameOnContract`) on a `Stall`.
- **Business rules preserved:** OR numbers are **never auto-generated** — always manually encoded by staff; adding an OR never alters the original payment/timestamp. Every financial mutation is audited via `AuditSaveChangesInterceptor`. Philippine time (UTC+8) for business-day logic. All fee rates in `FeeRates`.

**Key principle:** Online payment = the *payment-received* step. The **Official Receipt remains staff-encoded** afterward. This maps cleanly onto the existing `RecordPayment` → `SetOrNumber` split.

---

## 2. Architecture design

### Domain
- `OnlinePaymentTransaction : AuditableEntity`
  - `Reference` (internal, e.g. `EEMO-OP-2026-000123`), `PayorUserId`, target `PaymentRecordId`, `Amount`, `Status`, `Provider` ("PayMongo"), `GatewayReference` (checkout session id + payment id), `Method` ("gcash"), `PaidAt`, `RawPayload` (jsonb, audit), `ORNumber` (set when staff encode).
- `OnlinePaymentStatus` enum: `Initiated`, `Pending`, `Paid` (= Awaiting OR), `Completed` (OR encoded), `Failed`, `Cancelled`, `Expired`.
- `PayorUser : BaseUser` (Role = `Payor`; reuses password hashing, refresh tokens, lockout).
- `PayorStallLink` (a payor can own multiple stalls/contracts).
- `PayorActivationCode` (or a field on the payor/contract): one-time code + must match registered contact number; single-use, expirable.

### Application
- `IPaymentGateway` (`Common/Interface/Services`):
  - `CreateCheckoutSessionAsync(amount, reference, description, successUrl, cancelUrl)` → checkout URL + gateway ref
  - `VerifyWebhookSignature(payload, signatureHeader)`
  - `ParseEvent(payload)` → typed event (paid / failed / expired)
- Commands: `InitiateOnlinePaymentCommand`, `HandlePaymentWebhookCommand`, `IssueOnlinePaymentOrNumberCommand` (staff), `ActivatePayorAccountCommand`, `PayorLoginCommand`.
- Queries: `GetPayorBalancesQuery`, `GetPayorPaymentHistoryQuery`, `GetOnlinePaymentsAwaitingOrQuery` (staff reconciliation).

### Infrastructure
- `PayMongoPaymentGateway : IPaymentGateway` (typed `HttpClient`, base64 secret-key auth, sandbox keys in config/secrets).
- Repositories + EF configs + **one migration** (`OnlinePaymentTransactions`, `PayorUsers`, `PayorStallLink`, activation codes).
- Register `OnlinePaymentTransaction` in the audit interceptor's financial set.

### API
- `PayorAuthController` — `activate`, `login` (`[AllowAnonymous]`).
- `OnlinePaymentsController` — `POST /initiate` (`Payor`), `POST /webhook` (`[AllowAnonymous]`, **signature-verified + idempotent**), staff `GET /awaiting-or` + `POST /{id}/or-number`.

### Client (Blazor) — `/payor` area
- Anonymous: activation (stall no. + activation code + contact number → set password), login.
- Authenticated (`Payor` role): dashboard (balances, due dates), pay flow (redirect to PayMongo), payment history/status.
- Distinct payor layout; existing admin surface untouched.

---

## 3. Payment lifecycle (state machine)

1. **Initiate** — payor selects a balance → create `OnlinePaymentTransaction (Initiated)`, validate `Amount == PaymentRecord.BalanceDue`.
2. **Pending** — call PayMongo (Checkout Session) → store checkout URL + gateway ref → `Pending` → redirect payor to GCash.
3. **Webhook `payment.paid`** — verify signature, **dedupe on gateway ref (idempotent)**, validate amount → `Paid`. Set linked `PaymentRecord` to `Paid` with `CollectorId = null`, `ORNumber = null`, `Remarks = "Paid online via GCash · ref …"`. **Delinquency clears here.**
4. **Awaiting OR** — appears in staff queue "Online-paid · OR pending."
5. **Completed** — staff encode OR (reuse `SetOrNumber`) → transaction `Completed`, OR linked, audited.
- **Failed / Cancelled / Expired** — payor abandons or session expires → terminal-fail; `PaymentRecord` untouched; payor may retry.

---

## 4. NPM inclusion — design note

NPM is **daily-based** (`DailyCollection`, ₱30/day + utilities + fish fee), not a single monthly balance like TCC/NCC/BBQ/ICE. Including NPM online requires a defined **"what is payable online for NPM"** rule, e.g.:
- Present NPM as the **outstanding amount for the current billing period** (unpaid/partial `PaymentRecord` for the month, which already aggregates the period), OR
- Present **outstanding daily collections** for a chosen range.

**Recommendation:** treat NPM online payment via the **monthly `PaymentRecord` balance** (same mechanism as the other facilities) for v1, so the lifecycle stays uniform. Confirm the exact NPM billable definition during Phase 1 before wiring the pay button for NPM.

---

## 5. Scope boundaries (capstone-appropriate)
- **In (v1):** online payment for monthly-rental balances (TCC/NCC/BBQ/ICE) **+ NPM** (per §4); payor portal (view balances/due/history + pay); webhook confirmation; staff OR encoding + reconciliation view.
- **Out (v1, future):** TRM/SLH/TPM online (point-of-service); refunds; auto-emailed e-receipts; partial online payments (start full-balance only).

---

## 6. Phased plan
- **Phase 0** — PayMongo sandbox keys; define `IPaymentGateway`.
- **Phase 1** — `PayorUser` + activation (code + contact number) + payor login + read-only `/payor` portal (balances, due dates, history). Confirm NPM billable rule.
- **Phase 2** — lifecycle: initiate → checkout → webhook → mark Paid (Awaiting OR, clear delinquency) → staff OR encode → reconciliation view.
- **Phase 3** — hardening: webhook idempotency/signature, failed/cancelled/expired, audit coverage, validation, regression tests (lifecycle, idempotency, amount mismatch, delinquency clearing).

---

## 7. Risks / policy flags (LGU-specific)
- **Convenience fee:** PayMongo charges ~2–2.5% on GCash. LGUs generally cannot pass a fee to payors without an ordinance → **policy decision** (LGU absorbs vs. ordinance).
- **Treasury / COA:** online collections must reconcile to the municipal treasury; the OR remains the official document. The "webhook-paid → staff OR" flow respects this; formal treasury sign-off is out-of-scope policy.
- **Security:** HTTPS only; webhook signature verification; idempotency on gateway ref; rate-limit activation/login; never store card data (hosted checkout); single-use activation codes.
- **Reconciliation:** PayMongo payouts settle on their own schedule — the staff "Awaiting OR" / reconciliation view keeps EEMO books aligned.

---

## 8. Open items to finalize during Phase 1
- Exact **NPM billable definition** for online payment (per §4).
- **Activation code issuance:** where it is generated/printed (on the bill?) and its lifetime/single-use rules.
- Convenience-fee policy (above).
