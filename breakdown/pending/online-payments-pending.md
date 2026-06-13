# Pending / Future Work — Online Payments & Payor Portal

Status notes for work intentionally deferred. Each item lists the problem, why it was deferred, and a
suggested approach. Nothing here is blocking; the core online-payment lifecycle (initiate → PayMongo
checkout → webhook → Paid/clear delinquency → staff OR encode → Completed) is implemented and tested.

---

## 1. Admin + Payor signed in at the same time in ONE browser  (auth)

**Problem.** Admin web and the `/payor` portal are the same Blazor Server app sharing one auth cookie.
Logging into one in the same browser replaces the other's session. A path-based dual-cookie "selector"
scheme was tried but is unreliable in Blazor Server: the interactive circuit runs over the `/_blazor`
SignalR connection (no `/payor` path), so the selector mis-resolves and the payor circuit reads the
admin identity/token. It was reverted to the single working cookie.

**Current behavior (acceptable).** One session per browser. A payor logging in overwrites the cookie →
the portal correctly shows the payor. Admins and payors on **separate browsers/devices** (the real-world
case) work fine. Cookie is `SameSite=Lax` so it survives the PayMongo return redirect.

**Suggested fix (proper).** Run the Payor Portal as its **own app / origin** (separate port or subdomain,
e.g. `pay.eemo.local`). Different origin = separate cookie jar = true independence, and each app keeps
single-cookie simplicity. The plan already anticipated this ("can be split into a standalone Payor Portal
project later"). Alternative (heavier, same app): a properly-tested `PersistentAuthenticationStateProvider`
that flows the per-area principal/token from prerender into the circuit via `PersistentComponentState`.
`Client/Securities/AuthSchemes.cs` is left in place for whoever picks this up.

---

## 2. Payor balances — show ALL past unpaid months (full arrears)

**Problem.** The payor portal currently surfaces the **current month** obligation (synthesized when no
record exists) plus any existing unpaid/partial records. Past months that were never recorded do not
appear, so the payor's outstanding can understate the admin 12-month ledger.

**Suggested fix.** Reuse the contract-aware monthly logic from
`PaymentRepository.GetStallLedgerSummaryAsync` (and `CountCollectableDays`) inside
`PayorRepository.BuildPayableItemsAsync`: iterate the rolling 12-month window, and for each month the
stall is active + under an effective contract with no Paid record, emit a payable item (synthesized at
`Stall.MonthlyRate`). `InitiateOnlinePayment` already find-or-creates the record per (stall, year,
month), so no initiate change is needed. NPM stays excluded (daily-billed — see its own rule, plan §4).

---

## 3. Collector side — reflect online payments clearly

**Problem.** When a payor pays online, the webhook flips the monthly `PaymentRecord` to **Paid**
immediately, so the collector's monthly-collection list already shows **PAID** (no risk of double
collection). But there is no visual cue that it was **paid online**, and the OR is blank until staff
encode it — which can look odd to a collector.

**Suggested fix (small backend + UI).**
- Expose a `PaidOnline`/channel flag (or surface `Remarks`, which `MarkPaidOnline` already sets to
  "Paid online via GCash · ref …") on `MobileMonthlyStallCollectionDto` from
  `StallRepository.GetMobileMonthlyCollectionAsync`.
- On the collector stall card, show a subtle "Online · awaiting OR" tag for those rows (and once staff
  encode the OR, the existing OR chip shows it). This makes it obvious the money is in and not to collect
  again.
- The OR itself is encoded by staff in the new admin **Online Payments** page — no collector OR entry is
  needed (and must not be, to keep "OR is manual staff input" intact).

---

## 4. Realtime notification when a payor pays online (toast)

**Problem.** Staff/collectors don't get a live heads-up when an online payment lands; they only see it on
the next refresh of the collection list / the admin Online-Payments page.

**Suggested approach.** Add a lightweight SignalR hub (e.g. `OnlinePaymentNotificationHub`) the webhook
handler publishes to on a successful `Paid` (payor name, facility · stall, period, amount). The admin web
and collector mobile subscribe and show a top toast: "Ryan Gosling paid TCC · Stall 5 online — ₱2,400
(awaiting OR)", and auto-refresh the relevant list. Keep it best-effort (don't block the webhook on
notification). A simpler interim option is periodic polling of `awaiting-or` with a delta badge.

---

## 5. Hardening / ops (smaller)

- **Rate-limit anonymous endpoints.** `ActivatePayorAccount` and `PayorLogin` (and the webhook) have no
  throttling yet. Add per-IP / per-code attempt limits to deter brute force. (Login lockout already exists
  at the account level; activation does not.)
- **Activation key icon on the NPM page.** The per-stall "generate activation code" key icon was added to
  the monthly-rental collection page (TCC/NCC/BBQ/ICE) only; the NPM daily page does not have it yet.
- **PayMongo go-live (LGU policy, plan §7).** TIN + business verification for live payouts; convenience-fee
  decision (LGU absorbs vs. ordinance); treasury/COA reconciliation sign-off. Out of scope for the test/
  sandbox build.
- **Webhook config (ops).** Set `PayMongo:WebhookSecret` (the `whsk_…` from the dashboard webhook) and
  register the webhook URL (via an HTTPS tunnel in dev) so received payments auto-confirm.
