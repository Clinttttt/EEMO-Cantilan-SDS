# How a month of rent moves through StallTrack

Drawn from the implemented system for monthly-rent spaces. Day-billed market rent follows the same collection and report steps, but it is priced by day and has no due date or late penalty.

The rules shown are **StallTrack Rules** each office's Head can change (IA-071, IA-072), not MEEDO rulings.

## From billing to reports

```mermaid
flowchart TD
    A[Space is let: a contract states occupant, term and rate] --> B[Each month inside the term is billed]
    B --> C{Paid by the due date, plus grace days?}
    C -- Yes --> P
    C -- No --> D{Has the Head set a penalty rate with an authority?}
    D -- No --> E[No penalty. Screen says Not configured]
    D -- Yes --> F{Is the month held for review?}
    F -- Yes --> E2[No penalty while it waits]
    F -- No --> G[Late penalty assessed: one per month of rent, no cash posted]
    G --> H[Head may update or waive it, with a reason. Original kept]
    E --> I
    E2 --> I
    H --> I[Statement of Account: previous balance, electricity and water, fines and late penalties, this month, total due]
    I --> J[Reminder: 3 days before due, then monthly while past due]
    J --> P[Payment attempt]

    P --> K{Channel}
    K -- Office --> L[Staff enter the Official Receipt number]
    K -- Collector app --> M[Collector records it, offline if needed]
    K -- Payor portal --> N[Online checkout: see below]

    L --> Q{Older month open? Month too far ahead?}
    M --> Q2{Same-day quote: older month open? Month not begun?}
    Q -- Yes --> R[Refused, naming the month to pay first]
    Q2 -- Yes --> R
    Q -- No --> S[Payment posted to that month]
    Q2 -- No --> S
    M -- Money already taken offline --> S
    S --> T[Reports read what was posted: month-end, financial summary, Monthly Income]
    S --> U[Audit trail records who, when, before and after]
```

## An online payment

```mermaid
flowchart TD
    A[Payor chooses Pay on Balances] --> B{Older month open, month too far ahead, or month under review?}
    B -- Yes --> C[Refused with the reason. No checkout opens]
    B -- No --> D[Server prices the month and opens one checkout. An older unpaid link is closed at the gateway]
    D --> E{What the gateway reports, by webhook or the 15-minute check}
    E -- Paid, amount matches --> F[Month is settled. Payor told: received]
    F --> G[Staff enter the Official Receipt. Payor told: receipt issued]
    E -- Paid, amount differs or month already paid --> H[Held under Needs attention. Payor told: under review, do not pay again]
    H --> I[Head records the decision: refunded, or applied on an Official Receipt. No money moves in StallTrack]
    I --> J[Payor told: review finished]
    E -- Failed --> K[Payor told: failed. Contact the office if money left the account]
    E -- Unpaid after 24 hours --> L[Checkout closed. Same message, never saying nothing was charged]
```

## Boundaries

- A due date drives display, reminders and penalties. It does not redefine Delinquent or Arrears (IA-019, IA-020, IA-069).
- A new or changed rule applies from its effective date forward. Posted collections and past months are not recalculated.
- Oldest-first stops at any month held for review, and works per occupancy, never across different occupants. It starts at the month the office counts unpaid rent from (IA-075).
- A month with no record is owed at its term's rate (IA-077). Rent already Arrears on the day it is paid reports on the Monthly Income's Arrears line, and the total does not change (IA-076).
- Pay-ahead from the office is one receipt over several separately recorded months.
- A collector cannot take rent ahead. Pay-ahead is at the office or, one month at a time, on the portal.
