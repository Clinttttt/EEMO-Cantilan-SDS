# EEMO Operational Rulebook

**Status:** Canonical working record for StallTrack V2 business rules confirmed from EEMO Head/staff discussions and office reference material.  
**Purpose:** Prevent repeated questioning, preserve confirmed operational meaning, and isolate only the remaining questions that materially block implementation.  
**Scope:** Cantilan EEMO unless explicitly stated otherwise.

This document is intentionally practical. It records what the office has already clarified, what the current office references show, and what still requires a direct answer before StallTrack V2 should encode behavior.

When this document conflicts with a later explicit EEMO ruling, update this document and the affected decision record rather than preserving both interpretations.

## 1. Confirmed debt terminology

### Delinquent

An active stall/account becomes **Delinquent** after at least one fully elapsed unpaid month.

- One month owed is already delinquent.
- Additional month-age may increase follow-up severity.
- Three months is not the delinquency threshold; it is only a higher-age/severity boundary where applicable.

### Arrears

**Arrears** refers to old/lapsed owed stall debt, not ordinary current active-month delinquency.

Working rule from the Head discussion:

- active current account owing one or more elapsed months = Delinquent;
- lapsed/old-year stall obligation that remains owed = Arrears.

Do not revive the superseded rule `1–2 months = Arrears`.

A remaining reporting question still exists about how **cash collected against Arrears** should appear in Monthly Income; see Open Question Q8.
## 2. Monthly rental obligations and flexible collection cadence

TCC, NCC, BBQ, ICE and other monthly rental facilities remain **monthly obligations** even when staff collect in installments.

Example:

- September obligation = ₱2,400
- Sep 3 collection = ₱200
- Sep 8 collection = ₱500
- Sep 20 collection = ₱1,000
- remaining September balance = ₱700

Each collection may have its own Official Receipt where the charge belongs to OR.

The payment cadence does not change the billing basis.

If the month closes with an unpaid remainder, the exact outstanding amount carries forward as debt.

## 3. Revenue grouping: current working model

The office references use more than one presentation grouping. StallTrack must not assume that every board/report heading is the same semantic level.

The Monthly Income reference presents a Market Operations-oriented structure including:

- Income from Market
- Rent Income (Stall Rental)
- Space Rental

The office board additionally shows separate headings such as:

- Income from Malinawa
- Income from Ice Plant
- Receipts from Market (CT)
- Berthing / Landing Fees
- Income from Slaughterhouse
- Rent Income
- Lot Rental
- Other Income

This difference is important. StallTrack may use an operational navigation grouping that differs from the final official reporting classification, but report classification must ultimately follow the EEMO-approved official catalog.

The exact canonical reporting-group structure remains an open question; see Q1.
## 4. Confirmed Cantilan instrument policy

The following Cantilan mappings are confirmed for the current V2 design:

| Revenue / service | Instrument |
|---|---|
| Permanent stall / applicable permanent rental | OR |
| Market Fees | CT |
| ECF | OR |
| WCF | CT |
| Tabo | CT |
| Fish / Meat Vendor Fee | OR |
| Transportation / Parking | CT |
| Vegetable / Fruit Space Rental | CT |
| Landing / Berthing | CT |
| Weight & Measure / Registration | OR |
| Penalties / Fines | OR |
| Current approved slaughterhouse charges | OR |

Rules:

- OR and CT lines must not be mixed on one accountable document.
- One OR may contain several compatible OR line items for the same payer/context.
- ECF may be paid separately from rent and remains OR.
- WCF may be paid separately and remains CT.
- WCF does not move onto an OR merely because the same person is also paying rent.
- Fines/penalties should appear as a visible receipt line rather than being hidden inside another amount.
- Where instrument policy is not confirmed, do not guess.
## 5. Cash Ticket operating model

Cash Tickets are accountable physical forms consumed from an assigned batch/series.

Working operating model:

1. A collector/accountable officer receives a Cash Ticket batch/series.
2. Tickets are issued to individual payers as collections occur.
3. Ticket usage decreases the remaining accountable stock.
4. Collections are reported by revenue category.
5. When the assigned Cash Ticket stock/range is consumed, the officer remits/accounts for the full covered amount.
6. Partial remittance is not considered a valid normal workflow.

Future StallTrack support should therefore be capable of representing:

- form type;
- series/range;
- assignment/custody;
- used count/value;
- remaining count/value;
- spoiled/cancelled handling;
- remittance/reconciliation against the consumed accountable stock.

Do not reduce this to a boolean `Remitted = Yes/No`.

The exact relationship between daily RCD preparation and booklet/range-exhaustion remittance still needs one direct clarification; see Q9.
## 6. Cash Ticket payer identity

For transactional Cash Ticket collections, payor identity is **optional**, not universally mandatory.

The UI should allow staff to enter a person/business name when known or useful, while still supporting legitimate small/random/aggregate transactions where a registered Payor account does not exist.

Do not force anonymous CT transactions into a permanent stall/payor account model.

## 7. RCD / collection reporting

An accountable officer may report multiple Cash Ticket revenue categories in one collection-reporting context, for example:

- Market Fees
- Transportation Fees
- Berthing Fees
- WCF
- Vegetable/Fruit Space Rental
- other Cash Ticket categories collected by that officer

The classification must remain itemized so monthly and accountable-form reporting can reconcile by official revenue line.

Every recorded collection line should map to one approved revenue classification once the final catalog is confirmed.
## 8. Public Market versus New Public Market

The broader **EEMO/Public Market revenue operation** is not the same thing as the **New Public Market (NPM) facility**.

Charges such as Market Fees, ECF, WCF, Fish/Meat Vendor Fees, Landing/Berthing, Transportation and Weight & Measure belong to the broader EEMO market/revenue operation unless EEMO explicitly assigns them to NPM.

NPM remains a specialized permanent-stall/rental workspace.

Vegetable/Fruit Space Rental is separate from permanent NPM Vegetable stalls.

Do not put all market-related revenue inside the NPM stall page merely because the charge occurs around the market.

## 9. Fish, meat, and weighing-related collections

Confirmed points:

- Fish/Meat Vendor Fee is a distinct reportable revenue line.
- Weight & Measure / Registration is also a distinct reportable revenue line in office references.
- Fish/meat-related charges can be based on daily weighed quantity.
- Working examples given in discussion include fish around ₱1/kg and meat around ₱66/kg.
- These are not Slaughterhouse charges.
- Fish/Meat Vendor Fee is currently treated as OR in the approved Cantilan mapping.
- Weight & Measure / Registration is currently treated as OR in the approved Cantilan mapping.

However, the historical notes contain inconsistent wording about whether the per-kilo amount belongs to Fish/Meat Vendor Fee, Weight & Measure, or both. This must be clarified before the backend model is finalized; see Q4.
## 10. Transportation / parking

Current V2 rule:

- the office concern is primarily collection of transportation/parking fees through Cash Tickets;
- the old StallTrack concept of tracking detailed trips/drivers/plates is not the primary business requirement;
- rates vary by vehicle type;
- the referenced schedule is accepted for current V2 planning and should not be re-questioned unless EEMO supplies a superseding schedule.

Known vehicle examples include:

- Public Utility Bus
- Public Utility Baby Bus
- Jeepney
- Van
- Multicab
- Tricycle

Future configuration should remain effective-dated and flexible by vehicle class.

## 11. Slaughterhouse

Confirmed:

- slaughterhouse charges use OR;
- charge amounts are fixed according to the approved animal/service package;
- the receipt/report should transparently itemize the package components;
- the itemization is for transparency, not permission for collectors to invent arbitrary prices;
- approved additional charge types may be configured where authorized.

The office board shows **Income from Slaughterhouse** as its own visible income heading. This is stronger evidence than treating Slaughterhouse as Market Fees. Final reporting-group placement should be confirmed once, then encoded consistently; see Q6.
## 12. Space rental and event rental

Confirmed:

- Vegetable/Fruit Space Rental is temporary/open-space revenue and uses CT.
- It is not permanent NPM stall tenancy.
- Fiesta is Aug 15.
- Araw is Oct 16.
- Those are event dates, not billing dates.
- Lot Rental is priced per lot.
- Area/square-meter details may be captured as supporting detail where useful.
- Kanmanggay belongs conceptually under Space Rental.
- Fines may arise in rental/market contexts and should appear as explicit OR line items.

Still unresolved:

- accountable instrument and exact charging basis for Kanmanggay;
- accountable instrument for Lot Rental;
- whether a paid fine reports to a dedicated Fines classification or to the originating revenue source.

See Q7 and Q8.
## 13. BBQ, Ice Plant, Slaughterhouse, and additional enterprises

### BBQ Stand

Office board evidence places **Barbecue Stands** under Rent Income. Treat BBQ as a rental workspace unless EEMO later provides a different official classification.

### Ice Plant

The office board shows **Income from Ice Plant** as a separate heading, while the Monthly Income sheet places Ice Plant within the broader market-operation table and current StallTrack models ICE as a monthly rental facility.

This is not yet safe to collapse into one assumption. See Q5.

### Slaughterhouse

Treat as its own specialized operation/workspace. Do not silently classify it as Market Fees.

### Malinawa and missing EEMO operations

The office board includes an Income from Malinawa family with entries such as:

- Catering Services
- Dormitory Operations
- Function Hall
- Entrance Fees
- Cottages

These are in eventual StallTrack scope because the goal is to cover the missing EEMO operations, not only the facilities already implemented.

Their actual active status, instruments and charging bases still need confirmation; see Q10.
## 14. Revenue targets and reporting

Confirmed target direction:

- StallTrack should eventually store annual revenue targets.
- Monthly actuals should be derived from classified collections.
- Reports should support Annual Target → Jan–Dec → YTD/Total → Percentage.
- Each collection line should map to one official revenue classification so the Monthly Income report can be generated cleanly.

Do not confuse Revenue Target Attainment with Collection Efficiency.

Target edit authority/governance can be finalized later; it does not block the current UI catalog.

## 15. What should no longer be re-asked

Unless EEMO provides contradictory new evidence, do not spend staff time re-asking:

- whether Delinquent begins after one elapsed unpaid month;
- whether old/lapsed-year owed stall debt is treated as Arrears;
- whether monthly rentals may be paid in daily/multiple installments;
- whether unpaid monthly remainder carries forward;
- whether each installment may have its own OR;
- whether Vegetable/Fruit Space Rental is separate from permanent NPM stalls;
- whether WCF is CT;
- whether ECF is OR;
- whether Market Fees are CT;
- whether Transportation/Parking is CT;
- whether Landing/Berthing is CT;
- whether Fish/Meat Vendor Fee is OR;
- whether Penalties/Fines are itemized on OR;
- whether CT payor name may be optional;
- whether partial remittance is allowed in the normal workflow;
- whether current transportation vehicle-rate evidence is usable for V2 planning;
- whether StallTrack should eventually include missing EEMO operations beyond the original NPM/TCC/NCC/BBQ/ICE/SLH/TRM/TPM set.
## 16. High-value open questions for EEMO staff

Only ask questions that materially change the domain model, report mapping or accountable workflow.

### Q1 — Which income grouping is the official current reporting structure?

The Monthly Income sheet groups items under **Income from Market / Rent Income / Space Rental**, while the office board separately shows **Malinawa, Ice Plant, Receipts from Market (CT), Berthing/Landing, Slaughterhouse, Rent Income, Lot Rental, Other Income**.

**Question:** Which grouping should StallTrack reproduce as the official Monthly Income/report classification?

### Q2 — What exactly belongs to “Market Fees”?

The office references show Market Fees alongside other CT items such as Comfort Room, Tabo and Parking/Transportation.

**Question:** What specific transactions are classified as **Market Fees**? Is Comfort Room part of Market Fees or its own revenue line?

### Q3 — Transfer Large Cattle

**Question:** What transaction triggers **Transfer Large Cattle**, how is the amount computed, and does it use OR or Cash Ticket?

### Q4 — Fish/Meat Vendor Fee versus Weight & Measure

The office references list these separately, but earlier notes mix the per-kilo basis.

**Question:** Are **Fish/Meat Vendor Fee** and **Weight & Measure/Registration** two separate charges? If yes, what is the charging basis for each? A simple fish-vendor and meat-vendor example would settle this.

### Q5 — Ice Plant income

**Question:** What does **Income from Ice Plant** actually represent today: rental of spaces, ice sales/services, or both? Is StallTrack's current monthly-rental ICE model only one part of the Ice Plant income?

### Q6 — Slaughterhouse official report grouping

**Question:** Should **Income from Slaughterhouse** remain its own official income group, or is it rolled into another group in the current Monthly Income report?

### Q7 — Kanmanggay and Lot Rental instruments

**Question:** Which accountable instrument is used for **Kanmanggay** and **Lot Rental (Fiesta/Araw)** — OR or Cash Ticket? For Kanmanggay, what determines the amount charged?

### Q8 — Fines and Arrears reporting classification

**Question:** When a fine is paid, does the cash report under a dedicated **Fines/Penalties** line or under the original rental/service line? Also, when old Arrears are finally paid, does that cash report under **Arrears** or back under NPM/TCC/NCC where the debt originated?

### Q9 — Daily RCD versus Cash Ticket exhaustion/remittance

We know CT stock is consumed and normal remittance should be whole, not partial.

**Question:** If the Cash Ticket batch is not yet exhausted at day-end, does the collector still prepare/remit a daily RCD, or is physical remittance/accounting triggered only when the assigned CT batch/range is consumed?

### Q10 — Malinawa scope

**Question:** For Malinawa, which services are currently active — Catering, Dormitory, Function Hall, Entrance Fees, Cottages — and what instrument/rate basis does each use?

## 17. Questions to ask later only if needed

These do not block the current presentation/UI pass and should not distract staff now:

- online-payment exception ownership;
- final signatory matrix for every official report;
- full revenue-target approval workflow;
- future cross-tenant facility-code display policy;
- canonical stable account/detail route identities.

---

This rulebook should be updated immediately whenever EEMO answers one of the open questions. Once a question is resolved, move it into the confirmed section and update the Decision Registry / Revenue Architecture where applicable.
