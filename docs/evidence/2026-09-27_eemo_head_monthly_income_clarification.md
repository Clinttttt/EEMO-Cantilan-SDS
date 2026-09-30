# EEMO Head clarification — Monthly Income grouping and instrument corrections

**Date recorded:** 2026-09-27
**Authority:** Direct clarification from the Cantilan EEMO Head, accompanied by a photographed Municipal Economic Enterprises Development Office **Monthly Income 2026** sheet.
**Purpose:** Preserve the latest office ruling so later UI, classification, accountable-document, and reporting work does not reuse superseded assumptions.

**Precedence note:** The later same-day final clarification keeps the Market grouping and Tabo ruling, and additionally resolves Vegetable/Fruit as full/whole payment = OR and daily transaction = CT. See 2026-09-27_eemo_head_final_clarifications.md.

## 1. Monthly Income sheet is the grouping reference

When asked what belongs "inside" **Market Fees**, the EEMO Head directed StallTrack to use the office Monthly Income table because the market/terminal income is already itemized there.

The visible **Income from Market** rows are:

1. Market Fees
2. General Distribution / ECF
3. Water Consumption Fees / WCF
4. Tabo
5. Fish / Meat Vendor Fees
6. Landing / Berthing
7. Transportation Fees
8. Weight & Measure / Registration
9. Transfer Large Cattle
10. Ice Plant

Therefore these rows are sibling report classifications/grouped income lines. They must not be treated as sub-items hidden inside the Market Fees classification merely because they are market-related.

The same paper also shows separate **Rent Income (Stall Rental)** and **Space Rental** sections, including NPM/NCC/TCC/Arrears and Vegetable/Fruits/Kanmanggay/Lot Rental/Fines.

This paper is evidence for official reporting/grouping. Operational navigation may still group work differently where that improves workflow, provided reporting preserves the office grouping.

## 2. Tabo instrument correction

The EEMO Head clarified that **Tabo uses Official Receipt (OR)**.

This supersedes the earlier StallTrack working assumption that Tabo uses Cash Ticket.

Any shadow/configuration/UI code that still presents Tabo as CT must be treated as stale and corrected before any Tabo financial cutover.

Historical records are not to be rewritten merely because the target policy is corrected.

## 3. Vegetable / Fruit Space Rental instrument correction

The EEMO Head clarified that **Vegetable / Fruit Space Rental may use either Official Receipt or Cash Ticket**, while **Cash Ticket is the usual/majority practice**.

Confirmed:

- OR is valid.
- CT is valid.
- CT is the predominant/default operational practice described by the Head.

Still to confirm:

- the exact business condition that determines when OR is used instead of CT.

Until that condition is clarified, StallTrack must not invent a threshold or rule. Target instrument policy must be capable of representing more than one allowed instrument for this classification while resolving exactly one instrument for each posted collection/document.

## 4. Market Fees interpretation

For V2 classification/reporting, **Market Fees is one official row/classification**, while ECF, WCF, Tabo, Fish/Meat Vendor Fees, Landing/Berthing, Transportation Fees, Weight & Measure/Registration, Transfer Large Cattle, and Ice Plant are separate sibling rows on the office Monthly Income sheet.

Do not create additional official Market Fees sub-classifications solely from interface assumptions. If a specific activity such as Comfort Room needs a separate official revenue line, require separate office evidence/confirmation.

## 5. Immediate impact

- Operations UI must show **Tabo = OR**, not CT.
- Operations UI must not show Vegetable/Fruit as CT-only; use a dual-instrument state/label until the OR-vs-CT selection rule is confirmed.
- Revenue/instrument policy architecture must support an allowed-instrument set or equivalent contextual resolution for Vegetable/Fruit rather than assuming one immutable instrument per classification.
- Existing OR/CT no-mixing rule remains unchanged: one posted accountable document resolves to one instrument family.
- The Monthly Income paper governs report grouping; it does not force the Operations navigation to copy the report layout.
