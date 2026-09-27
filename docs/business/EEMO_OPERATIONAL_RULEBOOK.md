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

When qualifying old/lapsed Arrears are finally paid, the collected cash reports under the dedicated **Arrears** revenue line. Preserve the originating facility/debt reference (for example NPM/TCC/NCC) separately for traceability; do not lose the source obligation simply because the cash is classified as Arrears.
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

The **Monthly Income sheet is the official reporting grouping** StallTrack should reproduce for the formal Monthly Income/report structure.

Its current Market Operations-oriented groups include:

- Income from Market
- Rent Income (Stall Rental)
- Space Rental

The office board remains valid operational/tally evidence and may show the same revenue in a more practical monthly-working arrangement, including headings such as:

- Income from Malinawa
- Income from Ice Plant
- Receipts from Market (CT)
- Berthing / Landing Fees
- Income from Slaughterhouse
- Rent Income
- Lot Rental
- Other Income

The board is therefore useful for workflow and tally context, but the **sheet governs the formal report grouping** unless EEMO later replaces it with a newer official report format.

StallTrack navigation may still organize work operationally; report classification and Monthly Income output must follow the official sheet structure.
## 4. Confirmed Cantilan instrument policy

The following Cantilan mappings are confirmed for the current V2 design:

| Revenue / service | Instrument |
|---|---|
| Permanent stall / applicable permanent rental | OR |
| Market Fees | CT |
| ECF | OR |
| WCF | CT |
| Tabo | OR |
| Fish / Meat Vendor Fee | OR |
| Transportation / Parking | CT |
| Vegetable / Fruit Space Rental | OR or CT (CT predominant) |
| Landing / Berthing | CT |
| Weight & Measure / Registration | OR |
| Penalties / Fines | OR |
| Kanmanggay / Space Rental | OR |
| Lot Rental (Fiesta / Araw) | OR |
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

Remittance is triggered when the assigned Cash Ticket range/batch is fully consumed. Do not model routine remittance while accountable CT stock remains unconsumed.
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

**ECF and WCF are broader EEMO utility operations, not NPM-owned revenue types.** An NPM stall may be a utility service subject/context, but the target architecture must not require every ECF/WCF assessment to belong to NPM. NPM may surface contextual utility links for its occupants without becoming the global parent of Utility Operations. Existing NPM-bound `UtilityBill` records remain valid legacy/current source evidence and must not be destructively rewritten merely to generalize the future model.

Vegetable/Fruit Space Rental is separate from permanent NPM Vegetable stalls.

Do not put all market-related revenue inside the NPM stall page merely because the charge occurs around the market.

## 9. Fish, meat, and weighing-related collections

Confirmed points:

- **Fish/Meat Vendor Fee and Weight & Measure / Registration are two separate charges.**
- Fish/Meat Vendor Fee follows a monthly-rent style obligation; the working example is about ₱900 monthly and may be collected as ₱30 daily installments.
- Weight & Measure is separate and uses weighed quantity.
- Working examples: fish ≈ ₱1/kg; meat ≈ ₱66/kg.
- These are not Slaughterhouse charges.
- Fish/Meat Vendor Fee is currently treated as OR in the approved Cantilan mapping.
- Weight & Measure / Registration is currently treated as OR in the approved Cantilan mapping.
- Keep their collection/report lines distinct even when one vendor pays both.
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

The office board shows **Income from Slaughterhouse** as its own visible income heading. Keep Slaughterhouse as a separate specialized income/operation presentation rather than treating it as Market Fees.
## 12. Space rental and event rental

Confirmed:

- Vegetable/Fruit Space Rental is temporary/open-space revenue and may use OR or CT. Full/whole payment uses OR; daily transactions use CT, which remains the common day-to-day instrument.
- It is not permanent NPM stall tenancy.
- Fiesta is Aug 15.
- Araw is Oct 16.
- Those are event dates, not billing dates.
- Lot Rental is priced per lot.
- Area/square-meter details may be captured as supporting detail where useful.
- Kanmanggay belongs conceptually under Space Rental.
- Fines may arise in rental/market contexts and should appear as explicit OR line items.

Additional confirmed rules:

- **Kanmanggay is Space Rental**, charged per space on a monthly basis, and uses **Official Receipt (OR)**.
- **Lot Rental (Fiesta/Araw)** is priced per lot and uses **Official Receipt (OR)**.
- **Fines/Penalties report under their own dedicated Fines/Penalties revenue line**, even when the fine originated from a rental or vendor context.
## 13. BBQ, Ice Plant, Slaughterhouse, and additional enterprises

### BBQ Stand

Office board evidence places **Barbecue Stands** under Rent Income. Treat BBQ as a rental workspace unless EEMO later provides a different official classification.

### Ice Plant

For current V2 planning, treat **Income from Ice Plant** as a monthly space/rental-type income source associated with the Ice Plant. A working example given is around ₱1,000/month.

Do not invent a separate ice-sales transaction model unless EEMO later explicitly requires one.

### Slaughterhouse

Treat as its own specialized operation/workspace. Do not silently classify it as Market Fees.

### Malinawa and configurable additional EEMO revenues

The office board includes an Income from Malinawa family with entries such as:

- Catering Services
- Dormitory Operations
- Function Hall
- Entrance Fees
- Cottages

For StallTrack V2, these do not need a hardcoded semantic catalog before UI/backend work can proceed. Support them as **admin-configurable revenue/service entries** with a label and configured rate/basis as needed.

This same configurable pattern may support other future EEMO revenue lines that are not part of the fixed core catalog, provided they remain tenant-scoped and auditable.

### Governed configurable service pattern

A configurable service is allowed only when the operation is structurally simple enough to be represented safely by approved setup. It must not become an unrestricted generic fee form.

Required governed setup may include stable service identity, approved Revenue Classification, effective-dated OR/CT policy, calculation basis/rate, Payor requirement, required transaction fields, active state, and allowed collection channels.

Use **Setup Required** while any required financial policy is incomplete. An operation in Setup Required may appear in authorized Web Operations/setup surfaces, but it cannot create a financial Collection.

Collectors never configure these rules. Collector Mobile may show an operation only when it is Active, Mobile-enabled, authorized/assigned to the collector, and compatible with accountable-document custody. The collector records transaction facts; StallTrack resolves the financial policy.

If the operation later proves to require specialized approval, assessment, regulatory, lifecycle, or reconciliation behavior, promote it to a specialized source domain while preserving the canonical Collection model and existing posted history.

**Transfer Large Cattle** is the first explicit use of this pattern. Its operation shell/directory may be designed now, but collection remains disabled until EEMO confirms and authorized setup records the exact trigger, fee/calculation basis, instrument, and required regulatory/reference details.

See [ADR-006](../decisions/ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md).

## 14. Revenue targets and reporting

Confirmed target direction:

- StallTrack should eventually store annual revenue targets.
- Monthly actuals should be derived from classified collections.
- Reports should support Annual Target → Jan–Dec → YTD/Total → Percentage.
- Each collection line should map to one official revenue classification so the Monthly Income report can be generated cleanly.

Do not confuse Revenue Target Attainment with Collection Efficiency.

Target edit authority/governance can be finalized later; it does not block the current UI catalog.

### Goodwill / Refund

The office board includes a Goodwill/Refund line. For the current V2 scope, do not build a complex refund/reversal workflow around this evidence.

The report may support a simple bottom-level **Refund** label and manually entered amount where the office requires it. Treat this as report input/presentation for now, not as proof of a full financial reversal subsystem.

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
- whether Cash Ticket remittance is triggered before the assigned range is consumed — normal remittance waits until the assigned CT range/batch is exhausted;
- whether current transportation vehicle-rate evidence is usable for V2 planning;
- whether the Monthly Income sheet is the formal reporting grouping — it is; the board remains valid working/tally evidence;
- whether Fish/Meat Vendor Fee and Weight & Measure are separate charges — they are;
- whether Kanmanggay is Space Rental — it is, charged monthly per space;
- whether Fines/Penalties report to a dedicated revenue line — they do;
- whether Kanmanggay and Lot Rental use OR — they do;
- whether paid old/lapsed Arrears report under the dedicated Arrears revenue line — they do, while the originating facility/debt reference remains traceable;
- whether Slaughterhouse should be treated as Market Fees — it should remain a separate specialized operation/income presentation;
- whether Malinawa-style additional revenues must all be hardcoded — they may be admin-configurable label/rate entries;
- whether StallTrack should eventually include missing EEMO operations beyond the original NPM/TCC/NCC/BBQ/ICE/SLH/TRM/TPM set.
## 16. High-value open questions for EEMO staff

Only ask questions that still materially change the domain model, rate calculation, instrument policy, or report mapping.

### Q1 — Market Fees grouping — RESOLVED FOR REPORT CLASSIFICATION

The EEMO Head directed StallTrack to use the office Monthly Income 2026 sheet as the grouping reference because the market/terminal income is already itemized there.

Under **Income from Market**, Market Fees is a sibling row alongside General Distribution/ECF, WCF, Tabo, Fish/Meat Vendor Fees, Landing/Berthing, Transportation Fees, Weight & Measure/Registration, Transfer Large Cattle, and Ice Plant. These must not be treated as sub-items hidden inside Market Fees.

This resolves the report-classification question. Do not invent additional official Market Fees sub-classifications from UI assumptions. A specific item such as Comfort Room needs separate office evidence before becoming its own official line.

See [2026-09-27 EEMO Head clarification](../evidence/2026-09-27_eemo_head_monthly_income_clarification.md).
### Q2 — How are ECF and WCF amounts calculated?

**Confirmed latest Head direction (2026-09-27):**

- **ECF = OR**.
- For the current Cantilan workflow, the Head prefers **direct approved amount entry** for ECF rather than requiring meter computation in the collection screen.
- **WCF = Cash Ticket**.
- The Head stated **WCF = PHP 10** for the current office workflow.
- "Direct amount" never means arbitrary collector authority: the amount/rate must come from approved office policy/configuration and should remain effective/configurable rather than hard-coded into UI markup.
- ECF/WCF remain separate revenue/utility operations from stall rent.
- Fiesta/Araw temporary electricity may still carry its own event/context detail; do not silently assume it is an NPM stall utility.

This direct Cantilan clarification supersedes the earlier IA-047 interim metered/shared/fixed presentation hypothesis for the current demo/target workflow. Keep the underlying architecture flexible enough to preserve approved source/basis evidence if EEMO later supplies a meter/bill/rate schedule.

See [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md).

### Q2B — Vegetable/Fruit OR-versus-CT selection rule

**Confirmed latest Head rule (2026-09-27):**

- when the Vegetable/Fruit Space Rental is paid **in full / whole ("buo")**, use **Official Receipt (OR)**;
- **daily transactions/collections** use **Cash Tickets (CT)**;
- CT therefore remains the common day-to-day instrument;
- the collector must not arbitrarily choose OR or CT outside that business context.

This closes the former IA-046 decision gate and supersedes the IA-047 presentation-only regular/fixed-versus-transient/temporary inference.

See [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md).

### Q3 — Transfer Large Cattle

**Latest Head direction (2026-09-27):** the operation is a **transfer** with a corresponding amount/fee, it is only occasionally used by the office, and the practical StallTrack workflow may use **direct approved amount input**.

System handling remains governed by IA-044 / ADR-006:

- the operation may be exposed as a specialized/configurable transfer workflow;
- the amount must come from approved/configured office policy, never arbitrary collector input;
- Philippine regulatory references may guide optional ownership, animal, certificate, transferor/transferee and verification fields;
- OR-oriented presentation from Philippine regulatory precedent may be retained until Cantilan supplies a different accountable-instrument rule;
- exact Cantilan fee schedule, accountable form/reference, and mandatory local attestations remain configurable rather than hard-coded.

See [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md) and [Interim Philippine Reference Basis](../evidence/2026-09-27_interim_philippine_reference_basis.md).

## 17. Deferred questions that do not block current V2 work

Do not distract staff with these during the presentation sprint unless they become necessary:

- online-payment exception ownership;
- final signatory matrix for every official report;
- full revenue-target approval workflow;
- future cross-tenant facility-code display policy;
- canonical stable account/detail route identities.

---

This rulebook should be updated immediately whenever EEMO answers one of the remaining open questions. Once resolved, move the answer into the confirmed section and update the Decision Registry / Revenue Architecture where applicable.
