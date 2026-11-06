# Panel compliance matrix

**Source:** INF-4D minutes of 2026-10-28, Group 4 (StallTrack). **Branch:** `feat/panel-compliance-20261104`, not yet committed or deployed. **Evidence and limits:** [PANEL_RECOMMENDATIONS_20261104.md](PANEL_RECOMMENDATIONS_20261104.md), section 8.

"StallTrack Rule" means the project owner's ruling, changeable by each office's Head. It is not a MEEDO or Treasury confirmation.

## System items

| Panel comment | Panelist | Status | What was done | Show it at |
|---|---|---|---|---|
| Carry unpaid balances forward as arrears | Maligsa, Flores | Done | Statement of Account states the previous balance, split into Arrears and Delinquent, and says how much is from before the start month. The portal lists counted and recorded months. Old rent, once paid, reports on the Monthly Income's Arrears line (IA-075, IA-076) | Stall profile; portal **Balances**; Monthly Income |
| Clear billing presentation | Owner | Done | One statement: previous balance, electricity and water, penalties, this month, total due | Stall profile |
| Apply payments to the oldest month first | Flores | Done, on by default | Office, collector same-day quote and online checkout refuse a newer month while an older one is open, counting from a start month the Head sets, so years of unrecorded history don't block today's rent. Head can turn it off (IA-071, IA-075) | **Record payment**; `/settings/billing-policy` |
| Advance payment | Flores, Duero | Done | Office pay-ahead over several months on one receipt, with a limit set by the Head. Portal pays ahead one month at a time. Collectors cannot take rent ahead | **Record payment → Pay ahead** |
| Annual payment | Maligsa | Done | A whole year in one receipt at the office, each month separately recorded. Head can turn it off | **Pay ahead** |
| Excess or wrong payment amounts | Duero, Flores | Done | Office and collectors refuse excess. An online capture that differs is held under **Needs attention** and the Head records a refund or an application. No credit balance is created | `/online-payments` |
| Interest and penalties | Maligsa, Flores | Done, automatic | Late penalties are assessed by themselves once a Head sets a rate with its authority. No rate means no charge. Head can update or waive, and the original stays on record (IA-072) | `/settings/billing-policy`; `/operations/fines` |
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
| "Please refer to the document for compliance" | Bucalon, Maligsa, Dahonog, Flores | The annotated manuscript was not provided |
| Consult the concerned office before implementation | Duero | Not yet done. See below |

## What the panel may ask about

1. **MEEDO has not confirmed these rules.** They run as StallTrack Rules under the owner's direction, each changeable by the Head. Seven questions are listed for MEEDO in section 7 of the tracking document.
2. **No penalty rate is invented.** The demo office's rate and ordinance are labelled illustrative. Cantilan charges nothing until its Head sets a rate.
3. **Not deployed.** This is on a branch. Production needs the owner's explicit authorization, and the office should be told first that portal balances may rise and old payment links will stop working.
4. **Decided by the developer, as the owner directed:** where oldest-first starts (IA-075), recovered old rent on the Arrears line (IA-076), and the term's rate winning (IA-077). These are StallTrack Rules a Head or MEEDO can change. On the local Cantilan copy the Arrears placement moves about ₱27,900 of 2026's collections between rows with no change to the total, so a sheet printed earlier differs in placement.
5. **Also decided by the developer, after the end-to-end test:** a fine that can be assessed can be collected (IA-078), and a month with a recorded payment cannot be excused (IA-079).
6. **Known and not yet fixed:** finishing a partial month reports the whole rent in the month it was finished. The all-time total is right; the month is not. See the open findings in section 8 of the tracking document.
