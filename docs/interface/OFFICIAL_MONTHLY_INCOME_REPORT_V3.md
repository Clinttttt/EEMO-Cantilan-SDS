# Official Monthly Income — V3 target structure

Route: `/reports/monthly-income/official?year=YYYY` (Head/Admin authorization per current implementation).

**Business authority:** the 2026-09-27 office Monthly Income reference plus the 2026-10-06 continuation supplied by MEEDO.
**Important:** the previously implemented one-page structure is incomplete. The 2026-10-06 office clarification confirms a second report section/page containing Terminal, Slaughterhouse, the overall total and signatories.

See:

- `docs/reference/monthly-income-office-reference.png`
- `docs/evidence/2026-10-06_terminal_income_monthly_report.png`
- `docs/evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md`

## 1. Formal report hierarchy

The report uses one official annual/monthly statement with three top-level income families.

### A. Income From Market

Keep the existing office-confirmed Market structure, including the documented internal subsections:

- the existing market revenue rows;
- Rent Income (Stall Rental);
- Space Rental.

BBQ belongs under Rent Income under IA-066. Do not move Slaughterhouse into this family.

### B. Income From Terminal

Use the office wording exactly:

1. **a. COMFORT ROOM**
2. **b. PULL PUL VANS, CARGO VANS**
3. **c. TRICYCAD**
4. **Total Income from Terminal**

All three Terminal lines use Cash Ticket.

### C. Income from Slaughterhouse

Slaughterhouse is a separate top-level official section.

### Closing total

The statement closes with:

**OVERALL TOTAL MARKET COLLECTION**

This total is derived from the complete official report scope; it is not a manually duplicated cash ledger.

## 2. Table columns

The official statement keeps the office structure:

- revenue line;
- Annual Target;
- monthly actual columns Jan–Dec;
- Total/YTD;
- Percentage.

Target attainment is YTD/Total actual divided by the approved annual target. It is not Collection Efficiency.

A reached month with no recorded amount may show `0.00`; a future/unreached month remains blank according to the server report contract.

## 3. Terminal source rules

Terminal is **not** Transportation/Parking.

### Comfort Room

- CT;
- direct aggregate amount;
- optional Cash Ticket count as supporting evidence.

### Pull Pul Vans, Cargo Vans

- CT;
- direct aggregate amount;
- optional Cash Ticket count;
- assisted vehicle-class context may use Jeepney, Multicab, Van, Public Utility Bus and Public Utility Baby Bus.

### Tricycad

- CT;
- direct aggregate amount;
- optional Cash Ticket count;
- assisted vehicle-class context may use Tricycle.

The peso total is sufficient for the approved aggregate Terminal workflow. Vehicle-class/rate detail must not be required to reproduce an aggregate section total.

## 4. Transportation/Parking reporting boundary

Transportation/Parking remains a separate CT revenue source under the approved Market classification structure. It uses direct amount and does not own the Terminal vehicle-class/rate setup prospectively.

Historical rows must not be moved between Terminal and Transportation/Parking by display-name matching or guessed migration.

## 5. Fish/Meat and Weight & Measure boundary

Fish/Meat Vendor Fee remains its own official revenue line under the Market family and is independent from NPM.

Weight & Measure / Registration remains a separate line.

The Fish Retailing reference supplied on 2026-10-06 contains monthly report totals; those values are evidence/reference, not automatic transaction imports.

## 6. Official report adjustments

Only the **Head** may adjust an official Monthly Income cell when the system-derived amount is not the office-approved reported figure.

The UI should present this as **Adjust reported amount**, not free editing of ledger rows.

Required audit evidence:

- system-calculated amount;
- official adjusted amount / signed delta;
- required reason;
- optional reference;
- actor;
- timestamp;
- immutable revision/supersession history.

The adjustment changes official report presentation only.

It must never edit, delete or fabricate:

- Collections/SRCs;
- source obligations/balances;
- collector position;
- remittance;
- accountable-form history.

Later genuine cash changes remain visible in the system basis; the current official amount follows the approved report-revision contract.

## 7. Annual targets

Annual targets are approved external office figures configured by Head under IA-066.

Do not infer targets from:

- previous-year revenue;
- growth percentages;
- assessed balances;
- collection efficiency.

Target revisions remain audited.

## 8. Signatories

Prepared by and Certified Correct are configurable Office Settings.

Reference evidence currently shows:

- **JED O. GANANCIAS — Admin. Aide III**
- **RODANIE D. GUAZON — Market Supervisor IV**

Do not hard-code those people.

The report configuration must support name + position for both roles.

Where a finalized/exported report version is persisted, preserve the signatory snapshot used by that version so later settings changes do not silently rewrite historical official output.

## 9. Branding and header

Keep the existing formal office-document structure:

- municipal seal;
- Republic of the Philippines;
- Province of Surigao del Sur;
- MUNICIPALITY OF CANTILAN;
- current MEEDO office name/acronym from tenant branding;
- Bagong Pilipinas mark where the approved office template includes it.

Do not hard-code the old EEMO name.

## 10. Screen and print behavior

- Formal government-document styling, not dashboard-card styling.
- A4 landscape.
- Hide application chrome and controls when printing.
- Repeat table headers when a page break is necessary.
- Never clip Total or Percentage columns.
- Keep rows together where practical.
- The standard report intentionally uses two sheets. Page 1 contains the letterhead, Receipts and complete Section A (Market, Rent Income and Space Rental). Page 2 begins with Section B (Terminal), then Section C (Slaughterhouse), overall total and configured signatories together. Do not strand signatures on a third page or start Terminal at the bottom of page 1.
- Both tables share the same sixteen column definitions and widths. Page 2 repeats the column header for continuity, without repeating the large letterhead. The screen preview shows two separate document sheets.
- Preserve recognizable office ordering and labels.

## 11. Failure and review states

If a financial line has money but no approved official placement, do not guess its group.

Show a concise report-review warning and keep the amount included only according to the server's governed official-report contract.

However, the following placements are now confirmed and must no longer be treated as unresolved:

- BBQ → Rent Income;
- Slaughterhouse → C. Income from Slaughterhouse;
- Terminal lines → B. Income From Terminal.

## 12. Implementation note

This document records the UI/report contract after the 2026-10-06 office clarification and 2026-10-08 pagination ruling. Local implementation uses explicit first/continuation sheet containers. Verify the current release checkpoint and actual A4 landscape PDF before claiming printed or deployed acceptance.
