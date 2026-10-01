# Operation Reports V3 Direction

Every implemented financial operation should have a truthful report where authoritative data exists. The **shell** is shared
(the ECF report's language); the **body** fits the operation. Figures come from the server; the client presents, filters,
prints and exports.

## Shared shell

- `WorkspaceHeader` — "{Operation} Report", operation code where applicable, instrument chip(s), period control, Print, and
  Export CSV **only when real rows exist**.
- `ReportLetterhead` — tenant seal, Republic of the Philippines, municipality and province, `Branding.OfficeName`, title,
  period; facts (Classification, Instrument or Basis), Prepared by, Date prepared. No hard-coded office or municipality.
- `OperationMonthlyIncome` — the operation's row of the server's official Monthly Income (Annual target, Jan–Dec, Total, %).
  "—" means no figure is available (not zero); a failed read or a missing row is stated in the note. No target is invented.
- `v3-report-section` / `v3-report-table` — formal tables, right-aligned tabular figures, thin rules; print hides screen
  controls and prints the sheet as a document.

## Bodies

| Type | Operations | Sections |
|---|---|---|
| A — Obligation | ECF, WCF, Fish/Meat Vendor Fee, Kanmanggay, Fiesta/Araw | Monthly Income row, obligation position (accounts, with balance, assessed/approved, settled, outstanding), account register |
| B — Transactional | Market Fees, Landing/Berthing, Transportation, Transfer Large Cattle, Vegetable/Fruits | Monthly Income row, collection position (transactions, OR/CT documents, collectors), collection register; **no outstanding** |
| C — Quantity/rate | Slaughterhouse (`/reports/slaughterhouse`), Weight & Measure (no report yet) | Facility report language (not yet migrated to the shared shell) |
| D — Receivable | Arrears | Position (server delinquency totals, recovered this month), Monthly Income "Arrears" row, by facility, account register |

## Matrix

| Operation | Workspace | Report | Instrument | Body | Period basis | Print | CSV | Known limitation |
|---|---|---|---|---|---|---|---|---|
| Market Fees | `/operations/market-fees` | `/operations/market-fees/report` | CT | B | Reporting month | Yes | Yes | — |
| ECF | `/operations/ecf` | `/operations/ecf/report` | OR | A | Billing month | Yes | Yes | Direct approved amount; no meter columns |
| WCF | `/operations/water-consumption-fees` | `…/report` | CT | A | Obligations through month | Yes | Yes | Direct approved amount; no meter columns |
| Tabo | `/facility/tpm` | `/facility/tpm/report` | OR | Facility | Market day / month | Yes | Existing | Facility report language, not the shared shell |
| Fish/Meat Vendor Fees | `/operations/fish-meat-vendor-fees` | `…/report` | OR | A | Position to date + fiscal year | Yes | Yes | Position is to date, not per month |
| Landing/Berthing | `/operations/landing-berthing` | `…/report` | CT | B | Reporting month | Yes | Yes | — |
| Transportation Fees | `/operations/transportation` | `…/report` | CT | B | Reporting month | Yes | Yes | Governed activity carries no vehicle class; the register shows Reference. Legacy TRM trips stay in the TRM facility report |
| Weight & Measure | `/operations/weight-and-measure` | — | OR | C | — | — | — | No authoritative writer/read yet; no report built |
| Transfer Large Cattle | `/operations/transfer-large-cattle` | `…/report` | OR | B | Reporting month | Yes | Yes | — |
| Ice Plant | `/facility/ice` | `/ice/reports` | OR | Facility | Month | Yes | Existing | Facility report language; classification stays ICE_PLANT |
| NPM / NCC / TCC / BBQ | `/facility/{code}` | `/{code}/reports` | OR | Facility | Month | Yes | Existing | Facility report language |
| Arrears | `/operations/arrears` | `/operations/arrears/report` | None stated (source-defined) | D | As-of month | Yes | No | No recovery register (recovered arrears post under their source); Arrears qualification and Monthly Income placement unresolved |
| Vegetable/Fruits | `/operations/vegetable-fruit` | `…/report` | OR (whole) / CT (daily) | B | Reporting month | Yes | Yes | Instrument stated per row by mode |
| Kanmanggay | `/operations/kanmanggay` | `…/report` | OR | A | Position to date + fiscal year | Yes | Yes | Not BBQ |
| Fiesta/Araw | `/operations/fiesta-araw` | `…/report` | OR | A | Position to date + fiscal year | Yes | Yes | Event date is not a billing date |
| Fines | `/operations/fines` | — | OR | — | — | — | — | No governed report read yet |
| Slaughterhouse | `/facility/slh` | `/reports/slaughterhouse`, `/slh/reports` | OR | C | Month | Yes | Existing | Facility report language; approved animals; historical custom rates kept |

## Rules

- No fake report: an operation without authoritative data has no Report link (Weight & Measure, Fines).
- No client-calculated official totals: Monthly Income figures and account/obligation figures are the server's; the positions
  add up server rows exactly as the ECF report always has.
- Utility reports carry no reading, kWh, cubic-meter or rate columns (IA-053).
- Arrears is a management view over existing receivables: no ledger, obligation, write-off, adjustment or fixed instrument.
