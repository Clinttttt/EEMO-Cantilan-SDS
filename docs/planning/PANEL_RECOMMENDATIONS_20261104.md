# Panel recommendations: findings, plan and progress

**Started:** 2026-11-04. **Status:** all slices written. F2 was fast-forwarded into this branch on 2026-11-05 at the owner's request, with no push and nothing on master. The browser end-to-end test ran on the demo office. Awaiting the owner's visual review. See sections 8 and 9. Apart from that merge, nothing is committed.
**Inputs:** INF-4D minutes of 2026-10-28 (Group 4, StallTrack), the owner's `q&a.md` and `Approval.md`, this repository, and `platform/apps/payor`.
**Worktrees:** `.worktrees/panel-compliance-20261104` (eemo, from `b8f09b84`) and `platform-panel-compliance-20261104` (platform, from `origin/main` `194a374`), both on branch `feat/panel-compliance-20261104`.
**Authority note:** On 2026-11-04 the owner ruled that the new rules run as the main path, are configurable per LGU, and need no MEEDO answer first. Where MEEDO has not answered, the rule is a **StallTrack Rule** (IA-070 to IA-074): an owner ruling, changeable by each LGU. It is not a MEEDO or Treasury confirmation, and `Approval.md` still records 0 of 8 LGU confirmations.

The "Needs LGU ruling?" column in section 1 is the investigation-time view. The owner's rulings in section 7 now answer it.

## 1. Where each panel item stands

| Panel item (who) | What exists today | Real gap | Needs LGU ruling? |
|---|---|---|---|
| Arrears carry-forward (Maligsa, Flores) | Rulebook section 2 already carries unpaid balances as debt. `ReceivableClassification` implements IA-019/IA-069. `/operations/arrears`, Follow-up and the Mobile arrears workspace read it. | The Payor portal omits months with no billing row, so the portal and office balances can differ (see 2.9). | No for display |
| Billing presentation (owner) | Stall profile shows ledger totals and term balances. | No single statement of previous balance, current charges, adjustments, payments and total. | No |
| FIFO (Flores) | NPM is already oldest-first within a month and across months (`NpmOutstandingRentWorkflow`, `QuoteNpmAmount`, payor fish days). | Monthly rent and space rental pay an explicitly chosen month, so a newer month can be settled while an older one stays open. | **Yes** |
| Advance payment (Flores, Duero) | Refused for collectors (`MobileBillingMonth`) and NPM (`NpmOutstandingRentWorkflow:136`). `advanceWholeMonthApproved` policy evidence gates it in `NpmRentMonthWorkflow:232`. | No general policy. Online checkout has no guard against a month that has not begun (see 2.8). | **Yes** |
| Excess payments (Duero, Flores) | Office and Mobile refuse excess (`AMOUNT_EXCEEDS_OUTSTANDING`). | Excess arises only from online captures, and those are currently hidden (see 2.1, 2.6). | Disposition: **yes** |
| Annual payment (Maligsa) | No payment-frequency concept. Contract term is separate from payment cadence. | New. | **Yes** |
| Interest and penalties (Maligsa, Flores) | `PenaltyDefinition` (append-only, effective-dated, fixed or direct amount with ceiling), `FineAssessmentPolicy` (governing reference), `FineAssessment`, `/operations/fines`, dedicated report line. | No percentage or per-month basis, grace period or computed cap. Rulebook line 305: "Delinquency never creates an automatic fine." | **Yes** |
| Due dates (Flores) | None configured. Delinquent means a fully elapsed unpaid month (IA-019, constant). | New concept. Must not silently redefine Delinquent. | **Yes** |
| Billing policy visible in system (Flores) | Rates are visible. `/settings/revenue` is a read-only directory. | No statement of interest, overdue or advance policy. Not covered by `q&a.md`. | Content: yes |
| Failed transactions (Duero) | Good foundation: signature checks, tenant pinning, idempotent posting, `ReconciliationRequired`. | See section 2. | No |
| Notifications (Flores) | Collector: FCM and device tokens (production activation was on the branch `feat/v17-fcm-production-activation-20261027`, merged into this branch on 2026-11-05). Staff and Payor: SignalR only. | No stored inbox, no reminder job. Payors have no device tokens and the Payor app is web-only, so FCM cannot reach them. | Reminder content: yes |
| Dark/Light mode (Flores) | No toggle. `app.css` pins `color-scheme: light`. Bundled `site.min.css` carries DaisyUI dark rules. | New. 2,022 hard-coded hex colours across 216 scoped CSS files (8,510 `var()` uses); 51 Razor files have inline styles. | No |
| Audit log timestamps (Flores) | `AuditLog.LoggedAt` with actor and before/after values; `AuditTrail` shows Philippine time. | Done. Demonstrate it. | No |
| Rule-based billing (Flores) | `IFeeRateResolver`, `DomainRules`, `NpmMonthBasis`. | Done. Document it. | No |
| Monthly reports (Flores) | Month-end report, financial summary, Monthly Income, collections register. | Done. Demonstrate it. | No |
| MVP / clean UI (Flores) | V3 direction in progress. | Ongoing. | No |
| Realistic Jan-Dec data (Flores) | Multi-tenant platform; seeders cover reference data only. | Needs a clearly separate demo tenant. Never fabricate in Cantilan. | No |
| Manual, user guidance (Bucalon, Flores) | No user manual found. | New document. | No |
| Process flow revision (Maligsa) | No repository artifact. | Draw from the implemented system, after rulings. | No |
| Chapter 4, formatting, objectives, SDLC (Dahonog) | Manuscript work. | Outside this repository. | No |

"Please refer to the document for compliance" (Bucalon, Maligsa, Dahonog, Flores) points to an annotated manuscript that was not provided. The "Consultation Requirement" at the top of page 6 precedes the Group 4 heading in the PDF content order, so it reads as a carry-over from Group 3. Duero's "consult the concerned office before implementation" is the equivalent comment for StallTrack.

## 2. Online-payment defects found

Found by reading code, not by running it. Each fix needs a test that fails first.

| # | Defect | Evidence | Consequence |
|---|---|---|---|
| 2.1 | Operators cannot see payment exceptions, and `ReconciliationRequired` has no exit | `OnlinePaymentRepository.cs:260` counts only Paid/Completed; `OnlinePayments.razor` has no exception view; the entity has no transition out | Captured money can sit unresolved and unseen |
| 2.2 | Payor is told "Your account has been credited" while the rent is still unpaid | `OnlinePaymentTransaction.cs:238` counts `ReconciliationRequired` as settled; `ConfirmOnlinePaymentCommandHandler.cs:32` returns Paid | False statement to the payor |
| 2.3 | The same period can be paid again while a capture is under review | `InitiateOnlinePaymentCommandHandler` has no such check; the period stays payable | Double payment |
| 2.4 | An amount mismatch is not recorded | `OnlinePaymentSettlementService.cs:65` returns Conflict and saves nothing | Capture stays Pending; each webhook retry gets 409, and PayMongo disables webhooks after repeated failures (`PayMongoWebhookRegistration.cs:13`), which would affect the whole tenant |
| 2.5 | Lost webhook has no automatic recovery | The only hosted service is `DailyTenantBackupService`. **Corrected:** staff can verify through `POST {reference}/reconcile`, but no page called it, so the path existed without a way to use it | If the webhook is lost and the payor does not return, the payment stays Pending until someone checks it |
| 2.6 | A duplicate capture on a receipted legacy row is hidden | `SettleAsync` marks the transaction Paid (`:91`, `:122`); `GetAwaitingOr` requires `r.ORNumber == null` (`OnlinePaymentRepository.cs:124`) | Counted in dashboard totals, absent from the queue, no refund flag |
| 2.7 | Failed and Expired are effectively unreachable | `ParseEvent` uses the resource id (`pay_...`) as the gateway reference (`PayMongoPaymentGateway.cs:245`) while lookup uses `cs_...`; `RetrievePaymentStatusAsync` never returned Failed. **Changed after reading PayMongo's docs:** I found no session-expiry webhook, so `checkout_session.expired` must not be subscribed; a failed attempt does not end a session, because the payor can retry in it; a session stays open after its QR code lapses | Rows stayed Pending for good. Fixed by reading the session from the gateway rather than by adding webhooks |
| 2.8 | Online checkout can open a month that has not begun | `InitiateOnlinePaymentCommandHandler.cs:69` checks contract overlap only; the validator checks ranges only; Mobile refuses (`MobileBillingMonth.cs:16`) | Unapproved advance reachable by a crafted request. The portal does not offer it. |
| 2.9 | Payor balance can understate the debt | `PayorRepository.cs:286` lists existing rows plus the current month; the office counts "unpaid months without a record" (`FacilityReportsRepository.Compliance.cs:243`). NPM lists the current month only. | Two totals for one account. **Fixed in Slice B** by adding the office's unpaid months to the portal. Remaining limits: see section 8. |
| 2.10 | A retired checkout link stays payable | `Initiate:128` marks it Expired locally only; `PayMongoCheckoutExpiryService.cs:32` refuses non-test keys | Old link can still capture money, despite the code comment saying otherwise |
| 2.11 | Nothing enforces one open checkout per period | Unique indexes only on `Reference` and `GatewayReference` (`OnlinePaymentTransactionConfiguration.cs:17,54`) | Concurrent requests can create two live sessions |
| 2.12 | Legacy partial rows can lose evidence and be over-cleared | `SettleAsync:129` guards only `Unpaid`; `PaymentRecord.MarkPaidOnline` (`:154`) nulls the OR and collector and zeroes `PartialAmount` | Earlier cash OR overwritten on the row. Exposure depends on how many rows are still Legacy. |
| 2.13 | Refunds are not tracked | `payment.refunded` maps to Unknown | A PayMongo refund leaves no local record. **Partly done:** the Head can record a refund on a held capture. **Still open:** a refund of an already settled payment is not detected, and the month stays paid. It needs a sandbox payload and a correction path. |
| 2.14 | "Nothing was charged" is stated without evidence | `payment-success.html:29` | Copy may be false |

## 3. Business-rule conflicts to surface

1. **"Arrears" means different things.** The panel means any unpaid balance. IA-020/IA-069 reserve Arrears for prior-year or ended-occupancy debt; elapsed current-year months are Delinquent. Label the statement line **Previous balance**, with the existing categories beneath it.
2. **The Arrears report line. Decided: IA-076.** IA-020/IA-069 say recovered Arrears report under the dedicated line, and no writer posts to it. A writer stays out, for the reasons in IA-076. The report places old rent on the row at read time, by one rule for legacy and canonical money. Receipts, lines and totals do not change.
3. **FIFO moves official figures, not only months.** Which month a payment settles decides whether cash reports as rent or Arrears, so it can move a Cantilan report line. It cannot be treated as display logic.
4. **Advance cap versus annual payment.** A three-month cap and twelve-month annual payment contradict each other unless annual has its own permission.
5. **Excess.** Rulebook: "A collection beyond the obligation is revenue, not a negative debt" (`EEMO_BUSINESS_RULES.md:124`, NPM's 31st installment). The panel asks to carry overcharges forward. `q&a.md` asks to flag them. These need reconciling by ruling.
6. **Automatic penalties** conflicted with rulebook line 305. Resolved by owner ruling IA-072, prospectively. Not a MEEDO ruling.
7. **IA-022** (who owns online-payment exceptions) was NEEDS MEEDO INPUT. Resolved by StallTrack Rule IA-070.
8. **Two rates for a month with no record. Decided: IA-077.** The term's rate wins on every reader, and a running term's rate cannot be edited away from it.
9. **Oldest-first against unrecorded history. Decided: IA-075.** As first built, it held up 74 of 75 monthly accounts on the local Cantilan copy. It now counts from a start month each office sets.

## 4. Design

- **One policy record, not a rules engine.** An additive, append-only, effective-dated `BillingPolicyVersion` per tenant and source: due day, grace days, FIFO on/off, max advance months, annual allowed, penalty settings, governing reference, actor, recorded-at. **With no row, the StallTrack Rule defaults apply (on).** Each LGU's Head can change them. Rules apply prospectively only: no backfill, and posted Collections are untouched. Head-only and audited. Same evidence pattern as `FineAssessmentPolicy`.
- **Statement is a read model.** Reuse the compliance walk and `ReceivableClassification`, with no new arithmetic. A test must prove statement total equals the Follow-up figure for the same tenant, facility and as-of date. Check before reuse: `StallLedgerSummaryDto` is documented as rolling 12 months while Follow-up is whole-account.
- **Advance and annual are one mechanism.** Future-month obligation rows plus normal canonical allocations in one Collection, so 12 identifiable months remain traceable. No credit ledger.
- **No credit balance entity.** Excess is a captured online payment with a recorded disposition: applied through normal posting when amounts match exactly, refunded (evidence recorded), or held by the office.
- **FIFO boundaries.** Per occupancy (`AnsweringForMonth`), never across lessees. Per source, no mixing with utilities or fines. Stops at any month that is `PendingCutover` or needs reconciliation. Each allocation keeps its own period.
- **Penalties are automatic, and the Head can review and adjust them.** A scheduled pass creates normal `FineAssessment` rows with a unique per-period key. No duplicates, compounding or retroactive recalculation, and no cash is posted. **A rate is never invented:** with no Head-set rate there is no charge, and the screen says "not configured" (fake-zero rule). The Head adjusts through a governed, audited path.
- **Due date drives display and reminders only.** Delinquent/Arrears classification stays as ruled.
- **Theme.** Dark tokens under `html[data-st-theme=dark]` (not `data-theme`, which would activate DaisyUI's dark rules), default from `prefers-color-scheme`, saved locally, applied by an inline head script to avoid a flash on prerender. The block sits in `@media screen`, so print always uses the light values. Each light-only colour became `var(--dark-role, X)`: without the dark block it is exactly `X`, so light mode cannot change. `tools/dark-theme/apply_dark_tokens.py` makes that rewrite, asserts it, and can be rerun after a merge.
- **Notifications.** A stored, tenant-owned inbox with a dedupe key. A background pass reads the owning records and writes only messages, so nothing in a payment path depends on it. Head and Admin: online-payment exceptions and monthly penalty counts. Payor: payment status, penalties and rent reminders. Collector: existing FCM, untouched.

## 5. Sequence

Each slice is separately reviewed and committed. No production deployment, production migration or backfill without explicit authorization.

| Phase | Slice | Gate | Size |
|---|---|---|---|
| 0 | Worktree; unit and component baseline; capture sandbox payloads for failed, expired and refunded | None | S |
| A | A1 Durable and truthful capture: 2.2, 2.3, 2.4, 2.8, 2.12, 2.14 | None | S-M |
| A | A2 Exceptions queue and staff "Verify with provider": 2.1, 2.5 (manual), 2.6 | None | M |
| A | A3 Head disposition with additive columns and audit, including refund evidence: 2.1, 2.13 | Done (IA-070) | M |
| A | A4 Background reconciler, webhook coverage, gateway expiry, one-open-checkout index: 2.5, 2.7, 2.10, 2.11 | Sandbox proof; live-key expiry approved (IA-073) | M |
| B | Statement query; stall profile block; Payor API and portal alignment: 2.9 | Warn the office first | M |
| C | Theme: tokens and toggle, shell and shared components, then pages by worst offender | Owner review per slice | L |
| D | Manual, separate demo tenant data, billing flowchart, panel compliance matrix | None | M |
| E | Policy record and Head-only Billing Policy page | None | M |
| E | Due dates, FIFO, advance/annual, automatic penalties. The Arrears-line writer is held for a ruling (section 3, item 2) | On by default (IA-071, IA-072), configurable per LGU | L |
| F | F1 Inbox and Head/Admin exceptions; F3 Payor status updates. F2, the FCM branch, merged into this branch at the owner's request (section 8) | F2 reaches production only with a release | M |
| F | F4 Billing reminders | Due dates | S |

The order is A, B, E, then F, with C and D running alongside. Phases A and B reduce real money risk.

## 6. Risks

- **Moving a Cantilan figure.** Policy defaults are on by owner ruling, so from the release date forward new payments and penalties follow the new rules. Mitigation: prospective only, no backfill, posted Collections untouched, and tests proving months before the effective date report unchanged. Cantilan's production rollout still needs explicit deployment authorization.
- **Visible payor balances will rise** once the portal lists months it omitted. The figures are not new debt, but the office should hear first.
- **Settlement authority.** Rows may be Legacy, PendingCutover or Canonical; every new path must respect all three.
- **Sweeper.** Must be tenant-pinned, compare-and-set, and rate-limited against PayMongo.
- **The unique index** could not be checked against production data. The migration therefore skips the index with a `WARNING` if duplicates exist, rather than failing the release. After deploying, check the release log for `UX_OnlinePaymentTransactions_OneOpenCheckoutPerRecord`. If it was skipped, run `OnlinePaymentOneOpenCheckout20261104.CreateIndexUnlessDuplicatesExist` again a day later, once the reconciler has closed the duplicates.
- **First reconciler pass in production** will close every unpaid checkout older than 24 hours at PayMongo. That is intended (IA-073), but the office should know that old payment links stop working.
- **Shared checkout.** The primary checkout holds uncommitted work from other sessions, including `app.css`, `print.css` and `OnlinePayments.razor`. Preserve it.
- **Theme** touches many scoped CSS files, and one unbalanced brace breaks the whole bundle.

## 7. Owner rulings and open questions

**Owner rulings, 2026-11-04:**
1. This is compliance, not a re-demo. Due as soon as possible in November 2026, with no exact date.
2. New rules run as the main path (on) and are configurable per LGU.
3. Penalties are automatic, as the panel suggested. The Head can review and update them.
4. PayMongo checkout expiry extends to live keys.
5. Work happens in a new worktree from `b8f09b84`.
6. Head and Admin both see online-payment exceptions. Missing MEEDO answers do not block the work: they become StallTrack Rules.
7. The focus is the system. The manuscript is reference only.

**Still open:** the annotated manuscript the panel refers to was not provided.

**Ask MEEDO when there is time.** These do not block the work, because each has a StallTrack Rule default the Head can change:
1. Does advance rent report in the month received or the month it covers?
2. Does a rent change apply to months already prepaid?
3. If an occupant leaves with prepaid months, is it a refund or a transfer?
4. Should prior-year rent report under the Arrears line? (StallTrack Rule IA-076 says yes.)
5. Are there FIFO exceptions, such as a disputed month? From which month should Cantilan count? (StallTrack Rule IA-075 says December 2026.)
6. Who owns online-payment exceptions (IA-022)?
7. What is the governing ordinance or section for interest and penalties?

## 8. Progress log

| Date | Item | Result |
|---|---|---|
| 2026-11-04 | Read panel minutes, `q&a.md`, `Approval.md`, rulebook, registry and payment, billing, payor, fines, notification and theme code | Complete. No tests or builds run. |
| 2026-11-04 | Unit baseline in the new worktree | 3,269 passed |
| 2026-11-04 | A1 Truthful capture (2.2, 2.3, 2.4, 2.8, 2.12, 2.14) | 15 new tests failed before the fix. Fixed. |
| 2026-11-04 | A2 and A3 "Needs attention" queue, "Check with gateway" (manual 2.5), duplicate-capture hold (2.6), Head decision (2.1, 2.13 evidence side). Migration `OnlinePaymentOfficeDecision20261104`: 7 nullable columns | 3,297 unit passed, 0 failed. Component filter `OnlinePayments`: 8 passed. Payor app: 84 passed. Snapshot clean. |
| 2026-11-04 | A4 Reconciler (15-minute check, 24-hour close, 50 per pass), webhook coverage, gateway expiry on live keys, one-open-checkout index. Migration `OnlinePaymentOneOpenCheckout20261104` | Six defects reintroduced: 9 failures, then reverted. Build clean, 3,319 unit, 1,514 component, 954 integration passed. |
| 2026-11-04 | B Statement of Account (office profile), portal alignment (2.9), metered electricity and water on both | Rent defects reintroduced: 11 failures across two rounds. Utility defects reintroduced: 3 integration and 2 component failures. All reverted. Portal: 86 passed. Whole-suite rerun with the utility fix: build 0 errors; unit 3,323, component 1,517, integration 961 passed, 0 failed, 14 skipped (opt-in snapshot runs); no pending model changes; `git diff --check` clean; edited `.razor.css` braces balanced. |
| 2026-11-05 | E Billing Policy page and versions, due dates, oldest-month-first on every office and online writer, advance limit, office pay-ahead and annual payment, automatic late penalties with the Head's update and waiver, penalties on the statement and portal. Migrations `BillingPolicyVersions20261104`, `FineAssessmentAdjustment20261105` | 40 defects reintroduced in three rounds (21, 14, 5), each caught by its intended test, then restored byte-exact by hash. Whole-suite run: build 0 errors; unit 3,354; integration 1,006 passed, 0 failed, 14 skipped; component 1,532 passed and 1 failed (`aria-pressed` on the Pay ahead toggle, fixed, rerun with Slice C); no pending model changes. |
| 2026-11-05 | C Dark mode for the office portal: toggle at the foot of the sidebar, saved per browser, first visit follows the OS, print stays light, payor pages stay light. 130 stylesheets rewritten by tool | The tool asserted light values byte-identical in every file. An automated audit visited 124 office routes in headless Chrome on a throwaway database: 0 bright blocks; 5 low-contrast items, none introduced (colours that do not change between themes, or a decorative separator). Print emulation in dark gave white paper. A real click toggled, and the choice survived a reload. No horizontal scroll at 400px. `DarkThemeTests`: every dark colour defined, AA on all text pairs, all stylesheets balanced. |
| 2026-11-05 | F1, F3, F4 Inbox for Head, Admin and Payor, filled by a background pass that reads the owning records and writes only messages; rent reminders; `/notifications`, Sidebar badge, portal page and bell. Migration `AddInboxNotifications`: one table, two indexes. IA-074 | 25 .NET defects reintroduced one at a time, each caught by its intended test. One first survived (a removed unique index, because two "simultaneous" passes did not really overlap); that test was replaced by one that asks the database directly, then caught it. 4 portal defects: 3 caught, 1 survived and got a new test, then caught. Portal: 97 passed. |
| 2026-11-05 | D Demo office (`tools/demo-tenant`), user manual, billing flow, compliance matrix | The tool refused a remote host, a database not named demo or preview, and a database that already has an office. Seeded: 48 spaces, 877 receipts, 81 late penalties, 31 messages. |
| 2026-11-05 | Two fixes found while checking the demo office: the Page-not-found document now follows the dark choice; a Head whose gateway is disconnected can get back from setup when money is still held or awaiting an OR | The gateway fix's defect was reintroduced once and caught by its intended test, then restored. Unit 3,354 and component 1,548 passed, 0 failed. No pending model changes. `git diff --check` clean. 137 changed stylesheets balanced. Portal: 97 passed (no portal change since). |
| 2026-11-05 | Three decisions the owner left to the developer: start month (IA-075), recovered old rent on the Arrears line (IA-076), the term's rate on every reader (IA-077). Migration `BillingRulesCountFromMonth20261105`: one column | The tests found a defect in the first version: while the start month was still ahead, the month in progress did not count, so next month's rent could be paid over it. Fixed with `PaidInOrder`. 12 defects reintroduced across three rounds, each caught by its intended test, then restored and compared byte for byte. Integration, on Postgres: 1,032 passed, 0 failed, 14 skipped. No pending model changes. `TwoPassesAtOnce_LeaveOneChargePerMonth` failed 2 runs in 6 on its own: a Postgres deadlock that EF wraps as `InvalidOperationException`, which its `catch` did not expect. It is not a double charge, and the service already logs it and carries on. Test corrected, then 12 of 12 passed. Final run on this code: solution build 0 errors, including the Mobile app; unit 3,379 passed, 0 failed; component 1,551 passed, 0 failed. `EEMOCantilanSDS.Mobile.ComponentTests` is outside the solution and was not run; no file in the Mobile app changed. |
| 2026-11-05 to 06 | Browser end-to-end test on the demo office at the owner's request, Settings reorganized, and the fixes it led to, including IA-078 and IA-079 | Results below. Defects reintroduced: last login and IA-079, 4 failures; IA-078, 1 failure; all restored. Unit 3,391 and component 1,558 passed, 0 failed. Portal 99 passed. Integration, on Postgres: 1,035 passed, 0 failed, 14 skipped. Solution build succeeded, exit code 0. No pending model changes. `git diff --check` clean. Edited stylesheets balanced. |
| 2026-11-06 | The owner's four follow-ups: IA-081 receipts of a part-paid month, IA-080 each office's own Monthly Income lines, **Set receipt type** on Revenue Sources, Vacant spaces on the registry. Migration `AddPaymentInstalments`: one table | The PostgreSQL test found a defect in the first version: a receipt added to a saved month was written as an update and the save failed. Fixed by declaring the receipt key never generated. Defects reintroduced: receipts in the domain, 4 failures; the reader ignoring receipts, 1; Cantilan's sheet for every office, 1; all restored. Solution and demo tool build, 0 errors. Final runs: unit 3,399 and component 1,561 passed, 0 failed. Integration, on Postgres: 1,052 run, 14 skipped, 1 failure, a test asserting every Cantilan line for another office; corrected, and its class then passed 21 of 21. The Operations receipt type came after that run; its operation-settings integration tests (29) and Operations component tests passed. No pending model changes. `git diff --check` clean. Edited stylesheets balanced. Portal: no change since its 99 passed. |
| 2026-11-06 | Committed on `feat/panel-compliance-20261104` at the owner's request: code `0e886439` (the slices share files, so they are one commit), demo tool `1c3c9d1c`, then these docs; the payor portal in the platform worktree. Not pushed or merged. | Before committing: no pending model changes, `git diff --check` clean, nothing sensitive staged. The branch still needs the 36 commits on `origin/master` before release. |

**End-to-end test, 2026-11-05 and 06.** Claude in Chrome, on the throwaway demo database, as Head, Admin and three payors.

- **Passed:** sign-in and session reload; billing rules, start month and rule history; statement; oldest-first refusal; OR reuse refused; one OR over all outstanding months; Pay ahead over two months on one OR; Follow-up Queue; online exception decision; notifications; penalty waiver and its event log; term renewal at a new rate (old term and past payments unchanged, new charge at the new rate); rate edit refused during a term; Audit Trail; Admin's read-only and locked views; portal balances, history, profile and the Pay ahead lock.
- **Figures checked against the database:** Monthly Income, Year to date, Receivables aging (₱261,280 = Delinquent ₱182,100 + Arrears ₱79,180), and a ₱72 penalty paid at the office (November income ₱36,480 rent + ₱72 fine = ₱36,552; outstanding fines fell by ₱72).
- **Print:** checked by applying the print rules on screen in dark mode, on Reports Summary and the Financial Summary: white paper, dark text. The print dialog itself was not opened, because it blocks browser automation.
- **Fixed:** payment errors invisible in Record payment; Pay ahead unreachable for a paid-up payor; Year to date wrong on screen and in print; Collection Manager year arrows; Last login always "Never" for office staff; penalties that could not be collected outside Cantilan (IA-078); a receipted month could be marked Excused (IA-079); the whole-year figure on the renewal form not following the typed rent; a portal seal shown broken when stored as a path; month names cut off on portal rows; a stub scrollbar on the Fines tabs; report labels ("Rolling 12 months", "Collected", "canonical", "Year to date").
- **Settings:** one page with one row per destination. Titles, breadcrumbs, margins, date icon and sidebar highlight are consistent, the Head-only rows are disabled for Admin, and Database health zeros, raw table names and a hard-coded Cantilan sentence on Operation Settings are gone. Seen in light and dark at desktop width, except that last removal, which came after the apps were stopped.

**Open findings from the test, not fixed:**

1. **Money placement.** *Fixed 2026-11-06 (IA-081).* Finishing a partial month reported the whole rent in the month it was finished and overwrote the first OR, date and collector. Each receipt is now kept and read in its own month and day.
2. **Setup dead end.** *Fixed 2026-11-06; superseded by `master`'s V18 guided setup at the merge.* Revenue Sources offers **Set up** / **Review** on an operation that is not ready: a drawer with the server's blockers, the approved OR or CT from today or later, and a confirmation. Operations now shows the receipt type the office's own policy sets today, or **Not set** until it chooses one; before, it showed Cantilan's for every office.
3. **Cantilan names for other offices.** *Fixed 2026-11-06 (IA-080) for Monthly Income.* Cantilan keeps its sheet; another office sees only its own lines under its own names. Since the merge, lines it does not hold are hidden, not removed, as `master`'s V18 does, so every row key stays in the data. The demo office now stores its declared operations (Market Fees, Weight & Measure), so Menu and Operations list only those.
4. **Two outstanding figures.** *Answered 2026-11-06.* The Dashboard's ₱82,240 is right. The Registry billed 3 spaces with no contract (₱9,240). A space nobody holds in the month is now listed as **Vacant** and owes nothing; a lessee who left during the month still answers for it.
5. **Accountability and Collections tabs** read canonical collections only, so an office on legacy records shows ₱0 collected and unremitted while money was collected that day. Unremitted also still read ₱0 on Reports Summary after the Head recorded the ₱72 fine; the cause was not investigated.
6. **Minor:** internal codes (TCC, NCC) where the short name is DTC, DCC; a facility row's View / Record Payment opens a receipt-like window with ₱0 on an unpaid month; Manage Collection does not preselect the payor; the profile's 12-month count can differ from Collection Manager's; the Collection Manager drawer opens below the fold; a Head-only URL sends a signed-in Admin to the sign-in page; the login page shows "Version 1.0" and a network link on the demo office; "The server will issue the SRC" on the fine dialog; a stale amount in the Review drawer; "Total outstanding" excludes fines without saying so.

**Limits from IA-080, IA-081 and the setup screen (2026-11-06):**
- A month finished before this change keeps the single receipt it holds. No backfill.
- Importing history with a backdated receipt moves only the record's date, not its kept receipts.
- The Monthly Income history page still labels a line by Cantilan's sheet name. It shows only where another office records a target or adjustment; fixing it needs the line name in the history DTO.
- Revenue Sources and Operation Settings still use the catalog's legacy label ("Kanmanggay") for an office that never stored its operations. Every office activated through onboarding stores them.
- The V18 setup drawer (from `master`) refuses a scheduled policy, a retired source and Vegetable / Fruits' split OR/CT, and uses a native dropdown rather than the portal's `FhSelect`.
- The registry's Vacant row follows a contract's dates for the month read. A month a space was held before a term was entered is not billed.

**Verification notes:**
- `ALandingTicket_IsInTheStatementAndTheSourceRegister_Once_WithTransactionalFiguresOnly` and `Revenue_performance_uses_the_official_terminal_rows_without_counting_subtotals_twice` were changed for IA-080 on 2026-11-06, then restored to `master`'s version at the merge, because IA-080 now hides lines rather than removing them and both tests guard the full row catalog.
- `Amount_mismatch_conflicts_and_posts_nothing` was changed because it asserted defect 2.4.
- `NoServiceTypeIsRegisteredTwice` exempts `IHostedService`, which is meant to be registered many times.
- 14 integration tests skip by design. They are opt-in runs that need a private database copy or an environment variable.

**Limits in what is done:**
- The Head's decision moves no money. "Applied" cites a free-text OR reference that is audited but not checked against the OR register.
- Legacy NPM paths still do not detect excess. New checkouts are refused by the month guard, so only in-flight ones reach them.
- A refund of an already settled payment is not detected (2.13). It needs a sandbox payload and a correction path.
- The 24-hour close is a StallTrack Rule set under IA-073.
- The portal reads the office's walk once per linked stall, so a payor with many stalls costs more queries.
- Portal records from an earlier occupancy and excused months still appear. Months are added to the records, not replaced by the walk.
- On the stall profile, "Current Term Balance" now states the whole-account total, while "Months Unpaid" and "Total Collected" beside it still read the rolling 12 months.
- A meter bill now reaches the portal for any facility, read-only. Its internal kind is still named `NpmUtility`.
- A month holding both a legacy `PaymentRecord.ElecAmount` and a `UtilityBill` is not guarded. Cutover already treats mixed records as needing review.
- Statement utility figures read the bill's own status fields, as Follow-up does. A part in `PendingCutover`, which the register marks NeedsReview, is still counted.

**Limits from Slice E:**
- Oldest-first is on by default from the start month (IA-075). An open month on or after it still refuses a newer one at the office, on a collector's same-day quote and online.
- No penalty is charged until a Head sets a rate with a governing reference. Day-billed NPM has no penalties. Penalties are paid at the office, not online.
- Repeat-monthly is an office option, off by default. It goes beyond IA-072's original "one per period" wording.
- Only automatic late penalties can be updated or waived. Manual fines stay immutable. The automatic "Late payment penalty" definition may also appear in the manual Assess-fine list.
- A percent penalty on a legacy month includes any `ElecAmount` or `WaterAmount` on that record. A late-recorded on-time payment leaves the penalty for the Head to waive.
- Money queued offline is exempt from the oldest-first guard, because it was already taken. Collectors cannot take rent ahead; only the portal and the office can. The portal pays ahead one month at a time; a year is office-only.
- Office pay-ahead needs an OR number and writes legacy rows only: a canonical or paused month ends the run. It is not offered in the modal for an NPM monthly-rent market. Advance reports differ on legacy (month covered) and canonical (month received).
- Pay-ahead has no client operation key. A retry after success is refused by the first-month check, but two truly simultaneous requests were not tested.

**Limits from IA-075, IA-076 and IA-077:**
- The office's statement still shows every open month with a note on the older part, while the portal shows only counted or recorded months, so their totals can differ by the stated amount. The office's month list still starts at the oldest listed month.
- If Cantilan's data is wiped and re-entered, set the start month to the month it goes live.
- A 2026 sheet read after release moves old rent to the Arrears row with the same total, so it differs in placement from one printed earlier. A facility row's annual-target attainment falls by what moved. Report adjustments are stored as differences, so they carry over unchanged. A later backdated termination can move a row's placement again, never the total.
- Ice Plant old rent stays on its own row.
- A wrong rate on a running term can only be corrected by renewal. Edit forms and the list rows bound to them show the space's own rate.

**Limits from Slice C:**
- The route audit ran again on the demo office's data, in dark, and recorded 135 of 137 planned visits, 125 distinct pages because some routes redirect, before the machine ran low on memory and stopped it. `/vendors` was not reached. Result: 0 bright blocks, 7 low-contrast items. Those are a decorative separator, white on the active filter chip and on gold (recorded below), a report note on the blue band, and `--text-disabled` on a "not configured" dash and its screen-reader text on `/operations`. Open modals were not audited.
- Staff whose OS is set to dark will see dark mode on first visit after release, until they choose.
- The sign-in and setup pages follow the saved choice or the OS, and have no toggle of their own.
- The Payor portal has no dark mode: the Angular app was out of scope, and the Blazor payor pages stay light. A browser without `:has()` stays light everywhere.
- 130 stylesheets changed, so any branch touching them can conflict. Take the other side and rerun the tool.
- Seen, not changed, not caused by this slice: at 400px the menu button overlaps the page title; white text on gold and on the active filter chip is below AA in both themes.

**Limits from Slices F and D:**
- F2: `feat/v17-fcm-production-activation-20261027` was fast-forwarded into this branch on 2026-11-05 at the owner's request (HEAD `f6c36872`). Nothing was pushed, and nothing reached master or production. 7 of its 9 commits were already on `origin/master`; `71c6ad0e` and `f6c36872` are not. Its own record says activation already happened, and no activation step was run here. This branch is 36 commits behind `origin/master` and must take them before any release.
- Messages are in-app only, with no email, SMS or browser push. A pass runs every 10 minutes, and reminders once a day per office.
- Payment and penalty messages look back 7 days, so a pause longer than that loses them. New Head and Admin accounts do receive messages for exceptions still open.
- Reminder timings are fixed in code (IA-074), not a Head setting. Rent reminders reach only payors linked to a space.
- A Head's update to a penalty is not announced to the payor. The monthly penalty message states the count when it was sent.
- The office's count refreshes on a full load and on leaving Notifications. The portal's refreshes on each screen.
- The portal's Notifications screen asks for the inbox twice, once for the page and once for the bell.
- The demo office holds monthly rent only. Market days, slaughter, terminal, water and reports built on them show as empty. Its online-payment gateway is not connected, so **Online Payments** opens on the setup screen: choose **Back to payments**. It takes a new, empty local database and is the only office in it.
- The Mermaid flowcharts render on GitHub. No image export was made for the manuscript.
- The manual was checked against screen labels in the code, not walked through by a reader.

**Not yet verified:** real PayMongo sandbox payloads and a real checkout (none captured, and the demo office has no account); the Collector Mobile app on a device; the print dialog itself; Settings at a narrow width; an office-paid penalty lowering the portal balance, because the local apps were stopped for low memory before that step; two truly simultaneous pay-ahead requests; and the owner's own visual review, including dark mode.

## 9. Pre-commit review answers

From `stalltrack-financial-safety` and `stalltrack-backend-review`, for the whole branch. Each is answered from the diff and tests.

| Risk | Answer |
|---|---|
| Business rule | Owner rulings IA-070 to IA-074, IA-075 to IA-079 which the owner left to the developer, and IA-080 and IA-081 written to the owner's direction of 2026-11-06, all labelled StallTrack Rules. Not MEEDO or Treasury confirmations. |
| Duplicate settlement authority | Pay-ahead writes Legacy rows only; a canonical or paused month ends the run. Late penalties live in `FineAssessment`. Statement and inbox are read models. |
| Double counting | A penalty is its own statement line. Pay-ahead writes one row per month. A captured online payment that cannot post is held, not applied. |
| Source vs classification | No classification is inferred from a facility or from a report line. No writer posts Arrears: the report places old rent at read time from the billing period and the term's dates, and the line keeps its classification (IA-076). |
| Assessment vs collection | Penalties post no cash. A Head's online-payment decision moves no money. A message is never evidence. |
| Physical document reuse | Pay-ahead uses `IsMonthlyOrAvailableForStallAsync`. One OR covers several months on the same stall only. "Applied" cites a free-text reference and does not consume an OR. |
| Retries | Same online capture posts once. Penalty key, message key and one-open-checkout index are unique in the database. Pay-ahead retry is refused by the first-month check. **Two truly simultaneous pay-ahead requests were not tested.** |
| Historical evidence | A Head's penalty update sets `AdjustedAmount`, keeps `AssessedAmount` and appends an event. Policy versions are append-only. No backfill. Prospective only. |
| Tenant boundary | Reconciler and sweeps are pinned per office. Tests: `AnotherOffices_Penalty_CannotBeReachedOrUpdated`, `AnotherOffice_HearsNothingOfThisOne`, `AnInbox_RefusesAnotherOffice_AndAnyOtherRole`. |
| Cross-period correction | Not applicable. No correction path or as-of read changed. |
| Fake zero | No rate means no charge and "Not configured". A failed statement or inbox read says so. |
| Arbitrary rates | Rates and penalties are resolved by the server from Head policy. |
| Payor merging | None. Recipients come from explicit `PayorStallLinks` and the transaction's own `PayorUserId`. |
| Cutover | No source activation, backfill or report switch. Advance reports differ between legacy (month covered) and canonical (month received), as recorded in section 8. |
| IA-078, IA-079 | IA-078 writes one Fines source and one Official Receipt policy, scoped to the office, when its first fine is assessed. A unique index on office and code stops a second source; an existing or retired one is left alone. It posts no money, and the fine's cash still arrives only as its own Collection. IA-079 only refuses a request: it changes no payment, OR or history. |
| IA-080, IA-081, setup, Vacant | IA-080 changes which rows another office sees and their labels, never an amount: a row with money or a target is always shown, and Cantilan's sheet is pinned row by row. IA-081 keeps each receipt of a month and places the month's fee part over them rent first, capped at that fee part, with the newest taking the remainder, so the parts add up to the old single figure and nothing is counted twice. The single-receipt path is excluded where receipts exist, so a month is never read both ways. An earlier receipt's OR stays used (`OrNumberRegistry`). A correction lowers the newest receipt and never deletes one. Tenant: the new table is `IMunicipalityOwned` and filtered. The setup screen files the Head's chosen receipt type through the existing API, which still validates it; nothing is assumed. Vacant removes from Outstanding only a space nobody held in the month. |
| Migrations | Seven, all additive (`AddPaymentInstalments`: one table). `has-pending-model-changes` reported none in the final run. The one-open-checkout index skips itself with a warning if duplicates exist. |
| UI files in backend work | Changed on purpose. The owner authorized this branch to cross the UI-only limit on 2026-11-04. |

**Proof that tests catch the dangerous paths:** defects reintroduced one at a time on A, B, E and F, and 12 more on IA-075 to IA-077 (section 8), each caught and restored.

**UI review (`stalltrack-ui-review`): ready for localhost review, not yet accepted.** Static safety checks pass: no hard-coded tenant names or peso amounts in added markup, no added inline styles or native selects, stylesheets balanced. Data loading changed on purpose in two places: Online Payments reads exceptions before its cached-list return, and the Sidebar reads the inbox count, which is cached and refreshed on leaving Notifications. Seen in a browser on demo data during the end-to-end test: Settings and its pages in light and dark, the stall profile, the Online Payments queue, `/settings/billing-policy`, `/notifications`, `/operations/fines`, Follow-up, Collection Manager, the Record payment and renewal modals, and reports for the demo office. Not seen: any of these at phone width. Visual acceptance belongs to the owner.
