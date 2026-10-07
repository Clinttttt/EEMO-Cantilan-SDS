# MEEDO Operational Rulebook

> **Filename compatibility note:** The historical `EEMO_OPERATIONAL_RULEBOOK.md` path is retained so existing repository links remain stable. Current office-facing terminology is **Municipal Economic Enterprises Development Office (MEEDO)**.

**Status:** Canonical working record for StallTrack V2 business rules confirmed from MEEDO Head/staff discussions and office reference material.
**Purpose:** Prevent repeated questioning, preserve confirmed operational meaning, and isolate only the remaining questions that materially block implementation.
**Scope:** Cantilan MEEDO unless explicitly stated otherwise.

This document is intentionally practical. It records what the office has already clarified, what the current office references show, and what still requires a direct answer before StallTrack V2 should encode behavior.

When this document conflicts with a later explicit MEEDO ruling, update this document and the affected decision record rather than preserving both interpretations.

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

The complete office reference now confirms three top-level report families:

- **A. Income From Market**
  - includes the existing market revenue lines plus the documented **Rent Income (Stall Rental)** and **Space Rental** subsections;
- **B. Income From Terminal**
  - a. COMFORT ROOM
  - b. PULL PUL VANS, CARGO VANS
  - c. TRICYCAD
  - Total Income from Terminal;
- **C. Income from Slaughterhouse**
  - separate top-level Slaughterhouse income.

The report closes with **OVERALL TOTAL MARKET COLLECTION**, then Prepared by / Certified Correct signatories.

The office board remains valid operational/tally evidence and may show the same revenue in a more practical monthly-working arrangement, including headings such as:

- Income from Malinawa
- Income from Ice Plant
- Receipts from Market (CT)
- Berthing / Landing Fees
- Income from Slaughterhouse
- Rent Income
- Lot Rental
- Other Income

The board is therefore useful for workflow and tally context, but the **sheet governs the formal report grouping** unless MEEDO later replaces it with a newer official report format.

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
## 4A. Official Receipt (Accountable Form No. 51) operating model

Direct MEEDO Head confirmation on 2026-10-04 establishes the current office practice for Official Receipts:

- **Accountable Form No. 51 is the OR currently used by MEEDO.**
- Collectors physically hold the OR booklets they use for collection after the forms are received for MEEDO operations.
- One physical OR may contain **several compatible OR-based charges for the same payor/context**. Confirmed example: Stall Rental + ECF + penalty.
- Each charge remains its own **Nature of Collection** / classified collection line. Sharing one OR never collapses several revenue meanings into one undifferentiated amount.
- StallTrack does **not** replace the existing physical OR issuance process. It may record, itemize, reconcile and report the physical receipt; it must not silently introduce a computerized-receipt process.
- Preserve the **exact printed receipt identifier**. If a printed identifier includes a suffix letter, preserve it as part of the observed identifier unless the office/accountability authority later confirms a separate semantic treatment. Do not strip, regenerate or infer the suffix.
- A Treasurer software account, login or approval step is **not required for ordinary MEEDO collection** merely because formal accountable-form inventory is under treasury accountability. StallTrack models the operational assignment/custody evidence needed by MEEDO without inventing an additional approval chain.

Authoritative Philippine treasury/accountability research completed on 2026-10-04 adds the following national baseline (IA-060):

- pre-numbered ORs are issued in **strict numerical sequence**;
- one AF No. 51 serial is one accountable receipt **set**; Original/Duplicate/Triplicate are copies under that one serial, not three separate receipt numbers;
- the Local Treasurer is the formal custodian of accountable forms requisitioned by the LGU and maintains receipt/issue/transfer records, while authorized collectors may physically hold assigned forms;
- actual quantity and inclusive serial ranges must be tracked. Do not hard-code `50 receipts per booklet` even though 50-set booklets are common in BLGF notices;
- accountable forms are issued to bonded officers in sufficient quantities not to exceed three months' use; StallTrack records operational custody/provenance but does not invent a mandatory Treasurer-login or bond-administration workflow;
- RAAF/CRAAF accountability tracks beginning balance, receipts, issued/cancelled forms and ending balance by quantity/range;
- a spoiled/cancelled AF No. 51 is never reusable and creates no revenue. For printed ORs without fixed money value, the LTOM requires the cancelled original and duplicate copies to accompany the RCD with cancellation properly noted;
- accountable officers may not destroy accountable forms on their own and then treat accountability as cleared;
- a lost serial/copy/range is blocked from normal use. Loss is immediately reported to the Treasurer, who issues a notice/circular identifying kind, quantity, inclusive serials, place and approximate date of loss to prevent fraudulent use;
- StallTrack may record the loss/cancellation evidence and reference, but it does not itself grant legal relief from accountability;
- returning unused forms is a custody transfer, not collection, cancellation or remittance;
- copy-level exceptions must be representable because official BLGF notices include losses of only an Original, Duplicate or Triplicate copy.

Still unresolved and therefore not to be guessed:

- the formal semantic meaning of a printed suffix such as `A` after the serial (IA-059). BLGF notices prove trailing letters occur, while AF 51-A / AF 51-C are separately named form variants, so the two concepts must not be conflated;
- the final UX depth of the first MEEDO OR-accountability release (for example how much booklet/range administration is exposed at once). The data model should still preserve the full accountable range/custody facts required above.

Implemented in StallTrack on 2026-10-04 (IA-061; code on `release/v3-final-closure`):

- **Register** a range from its first and last serial exactly as printed (for example `2315601 A` to `2315650 A`). The quantity is counted from the range; ranges that cannot be counted are refused. Source ("Municipal Treasurer"), reference and received date are provenance text only; nobody at the Treasury signs in or approves anything.
- **Assign, transfer and return** unused receipts to collectors by range; each serial has one custodian at a time and a full custody history. An issued, cancelled or lost receipt never moves or returns.
- **Next receipt (superseded by IA-062).** Collection screens no longer suggest or select a receipt; the Accountable Forms register is optional back-office custody and never gates a collection.
- **Cancellation** is recorded at once with serial and reason (actor and time automatic). The RCD reference is added later; until then the receipt shows "Needs follow-up".
- **Lost** receipts (a serial or a range; the whole set or selected copies) are blocked at once with place, approximate date and what happened. The notice reference is added later. An already issued receipt whose copy is missing keeps its financial record and gets an exception on it.
- **One OR, several lines** works through the existing Current Collection / ECF composer for sources that are already on Canonical settlement authority. Sources still on Legacy authority need a controlled cutover first and are not part of this.
- **Position and history** count forms, never pesos. The "Accountability" view supports preparing the prescribed RAAF; it is not that report.


### SRC-first collection (IA-062, 2026-10-04)

- Every canonical Collection is identified by its **StallTrack Reference Code** (`SRC-YYYY-NNNNNN`, global monotonic, server-allocated, immutable). The collector or Web user never enters or chooses a physical OR/CT serial to record a collection, and the absence of one never blocks, quarantines or delays a collection.
- SRC is not an Official Receipt, a Cash Ticket or a government receipt. Where the office still hands over a physical receipt, that stays the office's own procedure; StallTrack may keep the form in the optional Accountable Forms register (IA-057/IA-060) but a collection does not consume it.
- Cash Ticket versus Official Receipt remains **policy metadata** of the classification (which instrument the office's policy approves); it no longer controls stock, custody or Mobile readiness.
- Corrections keep the original SRC; a replacement collection gets its own SRC. Offline Mobile collections show "Waiting to sync" until the server returns the SRC.
- Legacy rows have no SRC and keep the source's own document number.
See [2026-10-04 MEEDO Head AF No. 51 confirmation](../evidence/2026-10-04_meedo_head_af51_official_receipt_confirmation.md) and [2026-10-04 authoritative Philippine AF No. 51 rules](../evidence/2026-10-04_af51_authoritative_philippine_rules.md).

## 5. Cash Ticket operating model

Cash Tickets are accountable physical forms consumed from an assigned batch/series.

Working operating model:

1. A collector/accountable officer receives a Cash Ticket batch/series.
2. Tickets are issued to individual payers as collections occur.
3. Ticket usage decreases the remaining accountable stock.
4. Collections are reported by revenue category.
5. Collected money may be remitted while unused tickets remain on hand. Accountable-form custody and cash remittance are related but distinct (IA-052): the collector remits the money collected with the tickets already issued, and the unused tickets stay under the collector's accountable custody.
6. A shortfall against the collected amount is recorded as a visible difference for review; a remittance never exceeds what was collected (the office's 2026-08-25 answer) and never creates revenue.

Future StallTrack support should therefore be capable of representing:

- form type;
- series/range;
- assignment/custody;
- used count/value;
- remaining count/value;
- spoiled/cancelled handling;
- remittance/reconciliation against the consumed accountable stock.

Do not reduce this to a boolean `Remitted = Yes/No`.

Remittance is **not** triggered by, or blocked by, exhaustion of the assigned Cash Ticket range (IA-052); canonical collections in a remittance are identified by SRC (IA-062), and unused paper stock is never money. Complete accountability of a physical batch remains issued/consumed + spoiled/cancelled + returned + remaining + reconciliation exceptions, tracked separately from the money.
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

The broader **MEEDO/Public Market revenue operation** is not the same thing as the **New Public Market (NPM) facility**.

Charges such as Market Fees, ECF, WCF, Fish/Meat Vendor Fees, Landing/Berthing, Transportation/Parking and Weight & Measure belong to broader MEEDO revenue classifications and are not automatically NPM-owned.

**Income From Terminal is a separate operation/report family from Transportation/Parking.** Terminal contains Comfort Room, Pull Pul Vans/Cargo Vans and Tricycad. Do not use TRM/Terminal as a synonym for Transportation/Parking in the target model.

NPM remains a specialized permanent-stall/rental workspace.

**ECF and WCF are broader MEEDO utility operations, not NPM-owned revenue types.** An NPM stall may be a utility service subject/context, but the target architecture must not require every ECF/WCF assessment to belong to NPM. NPM may surface contextual utility links for its occupants without becoming the global parent of Utility Operations. Existing NPM-bound `UtilityBill` records remain valid legacy/current source evidence and must not be destructively rewritten merely to generalize the future model.

Vegetable/Fruit Space Rental is separate from permanent NPM Vegetable stalls.

Do not put all market-related revenue inside the NPM stall page merely because the charge occurs around the market.

## 9. Fish, meat, and weighing-related collections

Confirmed points from the 2026-10-06 office clarification:

- **Fish/Meat Vendor Fee and Weight & Measure / Registration are separate charges and separate revenue lines.**
- Fish/Meat Vendor Fee is owned by an **independent Fish/Meat vendor registry**, not NPM occupancy.
- A vendor registration has one type: **Fish** or **Meat**. Do not silently treat one registration as both.
- The registry is annual/tax-year oriented and records the office's New/Renew status where applicable.
- Fish/Meat Vendor Fee has **no approved fixed rate** in the current clarification. The collector records the **actual amount received**.
- Fish/Meat Vendor Fee remains OR under the current instrument mapping.
- A legitimate Fish/Meat fee may be posted with a typed vendor/payer name before that vendor exists in the registry. That name is frozen collection evidence; it is not an automatic permanent identity/link.
- NPM stall rent is independent and never settles Vendor Fee or weighing.
- Weight & Measure remains OR, uses weighed quantity and server-approved rate evidence, and **requires selection from the independent registered Fish/Meat vendor registry**.
- Weight & Measure has no free-text unregistered-vendor fallback.
- Working rate examples already accepted for planning remain fish ≈ ₱1/kg and meat ≈ ₱66/kg until an authorized rate change replaces them.
- These are not Slaughterhouse charges.
- Historical Fish Retailing sheet amounts supplied on 2026-10-06 are **monthly report totals**, not approved rates or transaction rows. Do not import them as new Collections without transaction-level evidence.

The earlier IA-064 requirement for an eligible NPM Fish/Meat stall and explicitly linked Business Payor is superseded prospectively by the independent vendor-registry rule. Existing historical Collections remain unchanged.

## 10. Transportation / parking

Current clarified rule:

- Transportation/Parking is **separate from Income From Terminal**;
- it remains Cash Ticket;
- there is **no required vehicle-class/rate basis**;
- the collector records the **amount received directly**;
- payer/reference may remain optional where the operation permits it;
- detailed trip/driver/plate and vehicle-class evidence is not required for the financial event.

Do not label the target Transportation/Parking operation as TRM/Transport Terminal.

The vehicle-class schedule previously placed here belongs to the Terminal operation prospectively. Preserve historical Transportation/vehicle-class evidence until a deliberate migration/version boundary is implemented; do not rewrite posted history by guess.

## 10A. Income From Terminal

Income From Terminal is a separate official operation/report family and uses Cash Ticket.

Confirmed official sections:

- **COMFORT ROOM** — direct total amount; optional Cash Ticket count.
- **PULL PUL VANS, CARGO VANS** — direct total amount; optional Cash Ticket count. Jeepney, Multicab, Van, Public Utility Bus and Public Utility Baby Bus belong to this section.
- **TRICYCAD** — direct total amount; optional Cash Ticket count. Tricycle belongs to this section.

The peso total alone is sufficient for the office's aggregate/high-volume entry. Vehicle-class rates may remain useful for an assisted individual-entry path, but they are not mandatory evidence for an approved aggregate Terminal total.

Use the office's printed wording in the formal report unless MEEDO issues a corrected official form.

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

## 12A. Unified collection identity and NPM Daily batch

The 2026-10-06 office clarification supersedes the target requirement for a separate Business Payor management workflow.

- Source-owned records are the collection-eligibility authority: occupancy/stallholder, rental account/space holder, Fish/Meat vendor registration, utility account or another specialized source.
- Unified Mobile search is a cross-source search over those records. Equal display names never authorize automatic identity merging.
- After selecting a source record, show only the operations actually eligible for that source.
- If no relationship exists, only operations whose own policy permits optional/free-text payer context may be offered.
- The **Business Payors page/manual-linking workflow is retired from the target product**. Existing persistence may remain temporarily for safe compatibility migration, but new features must not depend on it.
- Posted payer/vendor/occupant snapshots remain historical evidence.

**NPM Daily Collect All is approved for today's daily charge only.**

- Staff may start with all eligible stalls selected and uncheck exceptions/absent payers.
- Unchecked stalls remain unpaid for the day.
- Each selected stall posts its own Collection/SRC.
- Do not create one synthetic aggregate Collection.
- Do not extend Collect All to TCC/NCC/BBQ/ICE/Kanmanggay monthly obligations without a later office ruling.

See [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md).

## 13. BBQ, Ice Plant, Slaughterhouse, and additional enterprises

### BBQ Stand

Office board evidence places **Barbecue Stands** under Rent Income. Treat BBQ as a rental workspace unless MEEDO later provides a different official classification.

### Ice Plant

For current V2 planning, treat **Income from Ice Plant** as a monthly space/rental-type income source associated with the Ice Plant. A working example given is around ₱1,000/month.

Do not invent a separate ice-sales transaction model unless MEEDO later explicitly requires one.

### Slaughterhouse

Treat as its own specialized operation/workspace. Do not silently classify it as Market Fees.

### Malinawa and configurable additional MEEDO revenues

The office board includes an Income from Malinawa family with entries such as:

- Catering Services
- Dormitory Operations
- Function Hall
- Entrance Fees
- Cottages

For StallTrack V2, these do not need a hardcoded semantic catalog before UI/backend work can proceed. Support them as **admin-configurable revenue/service entries** with a label and configured rate/basis as needed.

This same configurable pattern may support other future MEEDO revenue lines that are not part of the fixed core catalog, provided they remain tenant-scoped and auditable.

### Governed configurable service pattern

A configurable service is allowed only when the operation is structurally simple enough to be represented safely by approved setup. It must not become an unrestricted generic fee form.

Required governed setup may include stable service identity, approved Revenue Classification, effective-dated OR/CT policy, calculation basis/rate, Payor requirement, required transaction fields, active state, and allowed collection channels.

Use **Setup Required** while any required financial policy is incomplete. An operation in Setup Required may appear in authorized Web Operations/setup surfaces, but it cannot create a financial Collection.

Collectors never configure these rules. Collector Mobile may show an operation only when it is Active, Mobile-enabled, authorized/assigned to the collector, and compatible with accountable-document custody. The collector records transaction facts; StallTrack resolves the financial policy.

If the operation later proves to require specialized approval, assessment, regulatory, lifecycle, or reconciliation behavior, promote it to a specialized source domain while preserving the canonical Collection model and existing posted history.

**Transfer Large Cattle** is the first explicit use of this pattern. Current direction (Clint / Core Brain, 2026-09-30, IA-049): **Official Receipt**, Collector Mobile, occasional, **direct approved amount**, kept intentionally simple (business date, payer/owner, concise reference, approved amount). The Official Receipt is instrument policy; an assigned or typed OR serial is not required to record the collection, which is identified by its SRC (IA-062). It stays **Setup Required** and non-collectible until authorized setup records its approved amount rule and an OR policy is effective; no livestock registry or certificate system is built.

See [ADR-006](../decisions/ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md).

## 14. Revenue targets and reporting

Confirmed target direction:

- StallTrack stores approved annual revenue targets by tenant/year/official row; values are never inferred from history.
- Monthly actuals are derived from classified collections plus authorized report-only adjustments.
- Reports support Annual Target → Jan–Dec → YTD/Total → Percentage.
- Each collection line maps to one official revenue classification so the Monthly Income report can be generated cleanly.
- Annual target revisions and official Monthly Income adjustments are **Head-only**, audited and revisioned.
- An official report adjustment changes report presentation only; it must never edit/delete Collections, balances, remittance or collector position.
- Prepared-by and Certified-Correct names/positions are configurable office settings, not hard-coded identities. Historical finalized/exported output should preserve the signatory snapshot it used.

Do not confuse Revenue Target Attainment with Collection Efficiency.

### Goodwill / Refund

The office board includes a Goodwill/Refund line. For the current V2 scope, do not build a complex refund/reversal workflow around this evidence.

The report may support a simple bottom-level **Refund** label and manually entered amount where the office requires it. Treat this as report input/presentation for now, not as proof of a full financial reversal subsystem.

## 15. What should no longer be re-asked

Unless MEEDO provides contradictory new evidence, do not spend staff time re-asking:

- whether Delinquent begins after one elapsed unpaid month;
- whether old/lapsed-year owed stall debt is treated as Arrears;
- whether monthly rentals may be paid in daily/multiple installments;
- whether unpaid monthly remainder carries forward;
- whether each installment may have its own OR;
- whether Accountable Form No. 51 is MEEDO's current Official Receipt — it is (2026-10-04 Head confirmation);
- whether one physical OR may itemize several compatible OR-based charges for the same payor/context — it may, with each Nature of Collection kept separate;
- whether collectors physically hold the OR booklets used in MEEDO collection — they do;
- whether StallTrack should replace the current physical OR booklet process — it should not unless a later authorized computerized-receipt policy says otherwise;
- whether Vegetable/Fruit Space Rental is separate from permanent NPM stalls;
- whether WCF is CT;
- whether ECF is OR;
- whether Market Fees are CT;
- whether Transportation/Parking is CT;
- whether Landing/Berthing is CT;
- whether Fish/Meat Vendor Fee is OR;
- whether Penalties/Fines are itemized on OR;
- whether CT payor name may be optional;
- whether partial remittance is allowed in the normal workflow - a remittance covers whole collections; a shortfall against them is a visible difference, never an adjusted collection (IA-052);
- whether Cash Ticket remittance is triggered before the assigned range is consumed - it is not blocked by remaining stock; remittance and form custody are separate ledgers (IA-052);
- whether Transportation/Parking and Terminal are the same operation — they are not; Transportation/Parking is direct-amount CT, while Terminal is the separate B-section family;
- whether the Terminal vehicle mapping is known — Jeepney, Multicab, Van, Public Utility Bus and Public Utility Baby Bus belong to PULL PUL VANS, CARGO VANS; Tricycle belongs to TRICYCAD;
- whether Terminal requires per-vehicle entry — it does not; a direct section total is sufficient and Cash Ticket count is optional;
- whether the Monthly Income sheet is the formal reporting grouping — it is, and the 2026-10-06 continuation confirms A. Income From Market, B. Income From Terminal and C. Income from Slaughterhouse;
- whether Fish/Meat Vendor Fee and Weight & Measure are separate charges — they are;
- whether Fish/Meat depends on NPM or Business Payor — it does not;
- whether Weight & Measure permits an unregistered free-text vendor — it does not; it requires the Fish/Meat vendor registry;
- whether a Business Payors page is required for the target workflow — it is retired in favor of source-native records;
- whether NPM Daily Collect All collects the monthly ₱900 — it does not; it collects today's daily charge for selected stalls only;
- whether Kanmanggay is Space Rental — it is, charged monthly per space;
- whether Fines/Penalties report to a dedicated revenue line — they do;
- whether Kanmanggay and Lot Rental use OR — they do;
- whether paid old/lapsed Arrears report under the dedicated Arrears revenue line — they do, while the originating facility/debt reference remains traceable;
- whether Slaughterhouse should be treated as Market Fees — it should remain a separate specialized operation/income presentation;
- whether Malinawa-style additional revenues must all be hardcoded — they may be admin-configurable label/rate entries;
- whether StallTrack should eventually include missing MEEDO operations beyond the original NPM/TCC/NCC/BBQ/ICE/SLH/TRM/TPM set.
## 16. High-value open questions for MEEDO staff

Only ask questions that still materially change the domain model, rate calculation, instrument policy, or report mapping.

### Q1 — Market Fees grouping — RESOLVED FOR REPORT CLASSIFICATION

The MEEDO Head directed StallTrack to use the office Monthly Income 2026 sheet as the grouping reference because the market/terminal income is already itemized there.

Under **Income from Market**, Market Fees is a sibling row alongside General Distribution/ECF, WCF, Tabo, Fish/Meat Vendor Fees, Landing/Berthing, Transportation Fees, Weight & Measure/Registration, Transfer Large Cattle, and Ice Plant. These must not be treated as sub-items hidden inside Market Fees.

This resolves the report-classification question for A. Income From Market.

**2026-10-06 update:** Comfort Room now has separate office evidence under **B. Income From Terminal**, alongside **PULL PUL VANS, CARGO VANS** and **TRICYCAD**. These Terminal lines are not Market Fees sub-classifications.

See [2026-09-27 EEMO Head clarification](../evidence/2026-09-27_eemo_head_monthly_income_clarification.md) and [2026-10-06 MEEDO office clarification](../evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md).
### Q2 — How are ECF and WCF amounts calculated?

**Confirmed latest Head direction (2026-09-27):**

- **ECF = OR**.
- For the current Cantilan workflow, the Head prefers **direct approved amount entry** for ECF rather than requiring meter computation in the collection screen.
- **WCF = Cash Ticket**.
- The Head stated **WCF = PHP 10** for the current office workflow.
- "Direct amount" never means arbitrary collector authority: the amount/rate must come from approved office policy/configuration and should remain effective/configurable rather than hard-coded into UI markup.
- ECF/WCF remain separate revenue/utility operations from stall rent.
- IA-053 (2026-10-01): ECF/WCF are broader utility operations; NPM may be the source/context subject but never owns them. Meter readings are not required financial evidence for new current workflows; historical reading fields are preserved only as legacy evidence and never reprice a recorded charge.
- Fiesta/Araw temporary electricity may still carry its own event/context detail; do not silently assume it is an NPM stall utility.

This direct Cantilan clarification supersedes the earlier IA-047 interim metered/shared/fixed presentation hypothesis for the current demo/target workflow. Keep the underlying architecture flexible enough to preserve approved source/basis evidence if MEEDO later supplies a meter/bill/rate schedule.

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
- **instrument = Official Receipt** (confirmed by Clint / Core Brain 2026-09-30, IA-049; no longer a Philippine-precedent placeholder);
- exact Cantilan fee schedule and any mandatory local attestations remain configurable rather than hard-coded.

See [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md) and [Interim Philippine Reference Basis](../evidence/2026-09-27_interim_philippine_reference_basis.md).

## 17. Deferred questions that do not block current V2 work

Do not distract staff with these during the presentation sprint unless they become necessary:

- online-payment exception ownership;
- signatory requirements for other official reports beyond the confirmed configurable Monthly Income Prepared by / Certified Correct settings;
- target models beyond the confirmed annual-target revision workflow;
- future cross-tenant facility-code display policy;
- canonical stable account/detail route identities.

---

This rulebook should be updated immediately whenever MEEDO answers one of the remaining open questions. Once resolved, move the answer into the confirmed section and update the Decision Registry / Revenue Architecture where applicable.
- IA-054 (2026-10-01): WCF direct Mobile entry. Head/Admin may prepare a WCF amount in advance; when none is prepared, the authorized collector enters the Water amount directly on Mobile for an eligible source (Cash Ticket, direct amount, no meter). A prepared amount always takes precedence. WCF Mobile collection is enabled once per tenant after server-checked readiness; new WCF activity is then canonical prospectively, while historical legacy Water money stays legacy.
