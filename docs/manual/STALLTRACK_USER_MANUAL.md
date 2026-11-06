# StallTrack user manual

StallTrack records what a local government's economic enterprise office bills and collects: stall and space rent, market and terminal fees, slaughterhouse charges, water, and the fines that go with them. Each municipality works in its own separate office data. Screen names below are the ones you will see. Your office's name, facilities and rates come from your own records.

**Who uses what**

| Role | Where they work | What they do |
|---|---|---|
| Head | Office portal | Everything an Admin does, plus billing policy, collectors, audit trail, and decisions on online payments and late penalties |
| Admin | Office portal | Records payments, issues receipts, follows up accounts, reads reports |
| Collector | Collector app (Android) | Collects in the field, including offline, on the operations assigned to them |
| Payor | Payor portal (web) | Sees their own balances and history, pays rent online, reads their notifications |

## 1. Getting started

**Signing in.** Use the username and password your office gave you. You may be asked to change the password the first time.

**Moving around.** The left menu holds Overview, Operations, Collection Activity, Online Payments, Spaces & Occupants, Monitoring, Remittance & Liquidation, Reports, Collectors, Audit Trail, Settings and Notifications. Collectors and Audit Trail are locked for an Admin.

**Dark or light.** *Dark mode* at the foot of the menu switches the look on this browser. Your first visit follows your device's setting. Printed documents always come out on white.

**Notifications.** The number beside *Notifications* is what you have not read. It updates when a page fully loads and when you leave the Notifications page. See section 8.

## 2. Recording a rent payment (Head, Admin)

1. Find the occupant in **Spaces & Occupants** and open the space's profile, or start from an account in **Monitoring**.
2. Choose **Record payment on this term**. On a follow-up row, choose **Record payment**.
3. Under **Outstanding**, confirm the month.
4. Enter the Official Receipt number from your booklet. StallTrack never makes one up.
5. Save.

**"Pay [month] first."** Your office takes the oldest unpaid month first, so a newer month is refused while an older one is open. Pay the month it names. A month the office is reviewing does not hold up the months after it, and neither does a month from before your office's start month (section 5). The Head can turn this rule off.

**Paying ahead.** In **Record payment**, choose **Pay ahead**. Choose how many months, or a whole year where your office allows it. One receipt covers those months, each of which stays separately recorded. The months must run without a gap, and the limit comes from your billing policy.

**A month paid in parts.** Record what the payor hands over as a partial payment with its Official Receipt. When the rest comes in, record it with its own new receipt. Each receipt keeps its own number, date and collector, and reports in the month it came in.

## 3. Reading an account (Head, Admin)

Open a space's profile from **Spaces & Occupants**. **Statement of Account** states, in one place:

| Line | Meaning |
|---|---|
| Previous balance | Unpaid months that have already ended, split into Arrears and Delinquent beneath it |
| Electricity and water | Unpaid metered bills, where billed |
| Fines and late penalties | Penalties still owed |
| This month is due | The due date under your billing policy |
| Charges this month, Paid this month | This month's rent, and what has come in against it |
| Total due | Everything still owed on the account |

The total is the same figure Monitoring uses for that account. The statement only reads what was recorded. It never changes anything.

A month with no record is owed at the rate its term agreed. While a term runs, a monthly space's rate cannot be changed. Set the new rate when the term is renewed.

If part of the balance is from before your office's start month, a note under the statement states the amount. Check it against your records. A payor sees only the months that have a record.

**Vacant** on Spaces & Occupants means nobody holds the space in the month shown. It owes nothing and is not counted in Outstanding.

**Delinquent and Arrears are different words here.** An active account with at least one fully ended unpaid month is **Delinquent**. **Arrears** is old debt: prior-year months or a lapsed occupancy. **Previous balance** covers both.

**On the Monthly Income**, rent that was already Arrears on the day it was paid reports on the **Arrears** line, not its facility's line. The total does not change, and the reconciliation states how much of each facility's money that is. Ice Plant rent stays on its own line.

## 4. Online payments (Head, Admin)

Payors pay rent through the Payor portal. The money goes through the payment gateway, and the office still issues the receipt.

If no gateway is connected, the page opens on its setup screen. Choose **Back to payments**, where it is offered, to reach the list.

- **Awaiting OR.** The money has arrived. Enter the Official Receipt number to complete it.
- **Needs attention.** The gateway took money that could not post on its own: the amount differed, or the month was already paid. The month stays held and the payor is told not to pay it again. Both Head and Admin see this list.
- **Check with gateway.** Asks the gateway about a payment now, without waiting for its message. StallTrack also checks open payments by itself every 15 minutes.
- **Record decision (Head only).** Records what happened to held money: refunded through the gateway, or applied through an Official Receipt the office issued. Give the reference. **This records the decision only. It does not move money.** Do the refund at the gateway first.

An unpaid checkout closes after 24 hours, and an old payment link stops working.

## 5. Billing Policy (Head changes it, Admin reads it)

**Settings → Billing Policy.** These are StallTrack Rules your office can change. They are not rulings by any one office.

| Setting | With nothing set | What it does |
|---|---|---|
| Due day of the month | Last day of the month | When rent is due |
| Grace days | 0 | Days after the due date before rent counts as late |
| Take the oldest unpaid month first | On | Refuses a newer month while an older one is open |
| Count unpaid months from | December 2026, or the month a new office went live | Earlier months stay on the office's statement and can still be paid. They hold up no later rent, earn no penalty, and are not asked of a payor unless they have a record |
| Months payable ahead | 3 | How far ahead a month can be taken |
| Allow a whole year's rent in one payment | On | Lets the office take twelve months on one receipt |
| Late penalty | None | Percent of the unpaid rent or a fixed amount, with an optional monthly ceiling. **No penalty is charged until the Head sets a rate and names the ordinance or authority for it.** |

A change applies from the date you choose. It never rewrites what was already recorded. Every version stays listed.

## 6. Late penalties and fines (Head, Admin)

**Operations → Fines & Penalties** lists assessments.

With a rate set, StallTrack adds a late penalty by itself to a monthly space's unpaid rent once the grace days pass. It is charged once per month of rent, or again each month if the Head chose that. A month on hold for review does not grow a penalty.

- **The Head can update or waive** an automatic late penalty, giving a reason. The original amount stays on record and the change is added to the history.
- **Penalties are paid at the office**, not online.
- A fine the Head assessed by hand cannot be edited afterwards.

## 7. Reports, follow-up and audit

- **Reports** holds the month-end report, financial summary, Monthly Income and collections register. Monthly Income lists your office's own lines, under its own names.
- **Monitoring** lists accounts to follow up, with their months and figures.
- **Audit Trail (Head)** shows who changed what and when, in Philippine time, with the values before and after.

## 8. Notifications

**Head and Admin hear about:** each online payment needing review, and once a month, late penalties that were added.

**Payors hear about:** their payment being received, the receipt being issued, a payment going under review and how it was decided, a failed or closed checkout, a late penalty on their current space, rent coming due within 3 days, and rent past due once a month.

Messages appear within about ten minutes of the event, on the Notifications page. Reminders go out once a day. A message only repeats what the records say. It is never proof of payment. The Official Receipt is.

## 9. Collector app

1. Sign in with the account the office gave you. Your device is bound to it.
2. **Menu** shows the operations assigned to you. Choose one and find the payor.
3. Enter the amount and the receipt, then save. With no signal, the app queues the collection and sends it when you are back online.
4. **Records** shows what you collected. **Reports** gives your totals.

The same oldest-month rule applies to same-day rent. You cannot take rent for a month ahead: send the payor to the office. Money you already took offline always posts.

## 10. Payor portal

| Screen | What it shows |
|---|---|
| Accounts | The spaces linked to you |
| Balances | What you owe, month by month. **Pay** opens checkout for rent. Utilities and penalties are listed but paid at the office |
| History | What you paid, with the receipt number once issued |
| Notifications (the bell) | Updates from your office |
| Profile | Your name and number, as the office records them |

**"Pay [month] first"** means your office takes the oldest unpaid month first.

**"Under review"** means the gateway reported money and the office is checking it. Do not pay that month again. You will be told what was decided.

**If a checkout failed or closed** and money left your account anyway, contact the office with the reference shown.

## 11. When something looks wrong

| You see | What it means | What to do |
|---|---|---|
| "Could not be loaded" | The screen could not read its data. It does not mean zero | Choose *Try again*. If it persists, tell your administrator |
| A balance is higher than expected on the portal | It now lists unpaid months that were only on the office's records before | Check the months on Balances, or ask the office |
| A payment is under *Needs attention* | The gateway's report and the checkout differ | Check with the gateway, then the Head records the decision |
| Rent refused with "Pay [month] first" | The oldest-first rule | Pay that month, or the Head reviews the policy |
| An operation reads *Setup required* in Settings › Revenue Sources, or *Not set* under Receipts on Operations | It has no receipt type yet, or no collector | The Head chooses **Set up** on the operation: the drawer lists what still needs attention, and the Head picks the approved Official Receipt or Cash Ticket from today or later, confirms it, and chooses **Confirm receipt policy**. Then assign collectors |

## 12. Where these rules come from

Items your office has not formally ruled on run as **StallTrack Rules**, which your Head can change: exceptions (IA-070), billing rules (IA-071), penalties (IA-072), checkout closing (IA-073), notifications (IA-074), the start month (IA-075), the Arrears line (IA-076), the term's rate (IA-077), collecting an assessed fine (IA-078), not excusing a month with a recorded payment (IA-079), your office's own Monthly Income lines (IA-080) and keeping each receipt of a month paid in parts (IA-081), all in `docs/decisions/DECISION_REGISTRY.md`. The flow of a bill is in [BILLING_FLOW.md](BILLING_FLOW.md).
