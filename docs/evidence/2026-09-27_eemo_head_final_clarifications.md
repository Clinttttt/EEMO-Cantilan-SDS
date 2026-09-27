# EEMO Head Final Clarifications — 2026-09-27

**Source:** Direct chat clarification from the Cantilan EEMO Head, relayed by Clint on 2026-09-27.
**Authority:** Direct Cantilan office clarification. Where this conflicts with earlier working assumptions or IA-047 interim Philippine-reference guidance, this note takes precedence for StallTrack presentation and target policy.

## 1. Market Fees

Question raised: whether Market Fees meant rent, Fish/Meat, Comfort Room, or another category.

Head response:

> "Try to refer ang report makita man sir didto..an sa Cr Terminal man"

Operational interpretation for StallTrack:

- Continue using the EEMO Monthly Income / office report as the classification authority.
- Do not make Fish/Meat, ECF, WCF, Tabo, Landing/Berthing, Transportation, Weight & Measure, Transfer Large Cattle, or Ice Plant children of Market Fees.
- The exact internal operational composition of the Market Fees line must follow the office report/source configuration rather than being invented in UI code.

## 2. Vegetable / Fruit Space Rental — OR versus Cash Ticket

Head response:

> "sa vegetable naay man mag bayad Buo which is OR then daily transactions gajud kay Cash Tickets."

Confirmed Cantilan resolver:

- **Full / whole payment:** Official Receipt (OR).
- **Daily transaction/collection:** Cash Ticket (CT).
- Cash Ticket remains the common day-to-day collection instrument for this space-rental operation.
- Instrument selection is therefore contextual and must not be an arbitrary collector preference.

This supersedes the prior IA-047 presentation-only inference based on regular/fixed versus transient/temporary occupancy.

## 3. ECF / WCF

Head response:

> "prefer siguro an sa ECF kay Direct amount kay OR, Wcf is 10pesos which is CAsh Tickets"

Current Cantilan office direction for StallTrack presentation/target workflow:

- **ECF:** OR; use a **direct approved amount** workflow rather than requiring meter computation in the collection UI.
- **WCF:** CT; current stated amount is **PHP 10**.
- The amount must still come from approved office policy/configuration; "direct amount" does not mean free-form collector authority.
- Rates/amounts should remain effective/configurable rather than permanently hard-coded into UI markup.

## 4. Transfer Large Cattle

Head response:

> "an sa Large cattle siguro direct input na siguro kay transfer man na which is naa syay corresponding amount pero panyagsa da sija magamit sa am."

Current Cantilan office direction:

- The operation is a **transfer** transaction and has a corresponding fee/amount.
- It is used only occasionally.
- The practical StallTrack entry can therefore be a **direct approved amount** workflow governed by configured policy.
- Philippine regulatory references in IA-047 may still guide the optional ownership/animal/certificate details and OR-oriented presentation, but exact Cantilan amount, accountable form, and regulatory fields remain configurable rather than hard-coded.

## 5. Utility ownership / NPM boundary

The EEMO office reports show ECF and WCF as their own revenue lines rather than as NPM rent components. Earlier Head clarification also established that ECF, WCF and other market revenues belong to the broader EEMO/Public Market operation unless specifically assigned to NPM.

Therefore:

- **ECF and WCF are EEMO utility operations, not globally owned by the NPM facility.**
- An NPM stall may be one service subject/context for ECF/WCF, but NPM must not be the architectural parent of all utility assessments.
- NPM UI may show contextual utility links/assessments for NPM occupants, but the canonical Operations directory keeps Utility Operations separate.
- Existing NPM-bound `UtilityBill` data is legacy/current source evidence and must not be destructively rewritten merely to generalize the future model.
