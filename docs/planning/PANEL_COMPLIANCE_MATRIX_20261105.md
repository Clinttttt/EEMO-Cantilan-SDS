# Panel compliance matrix

**Source:** INF-4D minutes of 2026-10-28, Group 4 (StallTrack). **Branch:** `feat/panel-compliance-20261104`, committed and up to date with `master`, not yet deployed. **Evidence and limits:** [PANEL_RECOMMENDATIONS_20261104.md](PANEL_RECOMMENDATIONS_20261104.md), section 8.

"StallTrack Rule" means the project owner's ruling, changeable by each office's Head. It is not a MEEDO or Treasury confirmation.

## System items

| Panel comment | Panelist | Status | What was done | Show it at |
|---|---|---|---|---|
| Carry unpaid balances forward as arrears | Maligsa, Flores | Done | Statement of Account states the previous balance, split into Arrears and Delinquent, and says how much is from before the start month. The portal lists counted and recorded months. Old rent, once paid, reports on the Monthly Income's Arrears line (IA-075, IA-076). MEEDO confirmed on 11 November that unpaid rent carries over to January, where the system counts it as Arrears | Stall profile; portal **Balances**; Monthly Income |
| Clear billing presentation | Owner | Done | One statement: previous balance, electricity and water, penalties, this month, total due | Stall profile |
| Apply payments to the oldest month first | Flores | Done, on by default | Office, collector same-day quote and online checkout refuse a newer month while an older one is open, counting from a start month the Head sets, so years of unrecorded history don't block today's rent. Head can turn it off (IA-071, IA-075) | **Record payment**; `/settings/billing-policy` |
| Advance payment | Flores, Duero | Done | Office pay-ahead over several months on one receipt, with a limit set by the Head. Portal pays ahead one month at a time. Collectors cannot take rent ahead. MEEDO confirmed on 11 November that advance payment of stall rent happens, though only occasionally | **Record payment → Pay ahead** |
| Annual payment | Maligsa | Done | A whole year in one receipt at the office, each month separately recorded. Head can turn it off | **Pay ahead** |
| Excess or wrong payment amounts | Duero, Flores | Done | Office and collectors refuse excess. An online capture that differs is held under **Needs attention** and the Head records a refund or an application. No credit balance is created | `/online-payments` |
| Interest and penalties | Maligsa, Flores | Done, automatic | Late penalties are assessed by themselves once a Head sets a rate with its authority. No rate means no charge. Head can update or waive, and the original stays on record (IA-072). Asked on 11 November, MEEDO named no additional fee for Arrears or Delinquent accounts, only that the balance carries over, so Cantilan has no rate and is charged nothing | `/settings/billing-policy`; `/operations/fines` |
| Due dates | Flores | Done | Due day and grace days set by the Head, shown on the statement and portal. They do not redefine Delinquent or Arrears | `/settings/billing-policy` |
| Billing policy visible in the system | Flores | Done | The rules in force, their history and their reference on one page, readable by Head and Admin | `/settings/billing-policy` |
| Failed transactions | Duero | Done, sandbox proof outstanding | 14 online-payment defects addressed: truthful messages, no double payment during review, held mismatches, automatic check every 15 minutes, old links closed at the gateway, one open checkout per month | `/online-payments`; portal payment result |
| Notifications | Flores | Done for office and portal | Stored inbox for Head, Admin and Payor, with reminders (IA-074). Collector push is merged into this branch and reaches phones only after a release | `/notifications`; portal bell |
| Dark and light mode | Flores | Done, owner review outstanding | Toggle at the foot of the menu, saved per browser, first visit follows the device, print stays white | Any office page |
| Audit log with timestamps | Flores | Already existed | Actor, time and before/after values | `/audit-trail` |
| Rule-based billing | Flores | Already existed, extended | Rates resolved by the server, one figure per month at its term's rate (IA-077), plus the billing policy above | [BILLING_FLOW.md](../manual/BILLING_FLOW.md) |
| Monthly reports | Flores | Already existed | Month-end, financial summary, Monthly Income, collections register | `/reports` |
| Clean, simple interface | Flores | Ongoing | V3 presentation direction continues | Any office page |
| Realistic January to December data | Flores | Done for monthly rent | A separate, clearly labelled demo office, never in Cantilan's records: `tools/demo-tenant` | Sign in as the demo Head |
| User manual and guidance | Bucalon, Flores | Done | [STALLTRACK_USER_MANUAL.md](../manual/STALLTRACK_USER_MANUAL.md) | Repository document |
| Revised process flow | Maligsa | Done | [BILLING_FLOW.md](../manual/BILLING_FLOW.md), drawn from the implemented system | Repository document |

**Running the demo office.** It needs a new, empty local PostgreSQL database with `demo` or `preview` in its name. The tool refuses anything else, and it holds monthly rent only.

```bash
STALLTRACK_DEMO_CONNECTION='Host=127.0.0.1;Port=<port>;Database=stalltrack_demo;Username=<user>;Password=<password>' \
  dotnet run --project tools/demo-tenant -- seed <file for the sign-in details>
```

Point the API at the same database through `ConnectionStrings__DefaultConnection`, not your everyday database.

## Not system items

| Panel comment | Panelist | Status |
|---|---|---|
| Chapter 4, formatting, objectives, SDLC | Dahonog | Manuscript work, outside this repository |
| "Please refer to the document for compliance" | Bucalon, Maligsa, Dahonog, Flores | The manuscript on file is dated 27 October, the day before the panel, so it carries none of the panel's written marks. System compliance follows the minutes |
| Consult the concerned office before implementation | Duero | Done before implementation, with a follow-up on 10 and 11 November. The Cantilan MEEDO Head and office staff were consulted on 27 September and 4, 6, 9 and 14 October 2026. On 11 November the office confirmed that advance payment of stall rent happens, though only occasionally, and that unpaid rent carries over to January; it named no additional fee ([evidence](../evidence/2026-11-11_meedo_advance_payment_arrears_carry_over.md)). They confirmed the rates in use and their basis in the office's records, when an account is Delinquent or in Arrears, that unpaid balances carry forward, that rent may be paid in installments with an OR for each, which receipt (OR or Cash Ticket) each collection uses, and that fines are itemized on the OR and reported on their own line. Recorded in the [operational rulebook](../business/EEMO_OPERATIONAL_RULEBOOK.md) and the [evidence notes](../evidence/). No interest or penalty rate is hard-coded: none is charged until the Head enters the approved rate and its legal basis |

## What the panel may ask about

1. **What MEEDO confirmed, and what is a StallTrack Rule.** MEEDO confirmed the existing rules before implementation and, on 11 November, that advance payment happens and unpaid rent carries over to January with no added fee named (see Duero's row below). The remaining rules added for the panel, such as oldest-first, the pay-ahead limit, due dates and the handling of overcharges, run as StallTrack Rules under the owner's direction, each changeable by the Head. No further question can be sent to MEEDO.
2. **No penalty rate is invented.** The demo office's rate and ordinance are labelled illustrative. Cantilan charges nothing until its Head sets a rate.
3. **Not deployed.** This is on a branch. Production needs the owner's explicit authorization, and the office should be told first that portal balances may rise and old payment links will stop working.
4. **Decided by the developer, as the owner directed:** where oldest-first starts (IA-075), recovered old rent on the Arrears line (IA-076), and the term's rate winning (IA-077). These are StallTrack Rules a Head or MEEDO can change. On the local Cantilan copy the Arrears placement moves about ₱27,900 of 2026's collections between rows with no change to the total, so a sheet printed earlier differs in placement.
5. **Also decided by the developer, after the end-to-end test:** a fine that can be assessed can be collected (IA-078), and a month with a recorded payment cannot be excused (IA-079).
6. **Fixed on 2026-11-06 (IA-081):** finishing a partial month used to report the whole rent in the month it was finished. Each receipt is now kept and reported in its own month. Months finished before the fix keep their single receipt; there is no backfill.
