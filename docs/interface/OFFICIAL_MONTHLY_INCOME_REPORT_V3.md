# Official Monthly Income — final report (V3)

Route: `/reports/monthly-income/official?year=YYYY` (Admin, SuperAdmin). Component:
`EEMOCantilanSDS.Client/Components/Pages/Reports/OfficialMonthlyIncome.razor`.

## Design evidence

The office's printed "Monthly Income 2026" sheet (photo supplied 2026-10-01, development evidence only at
`C:\Users\ASUS VIVOBOOK\Downloads\ce8e6144-563c-4b93-8098-2465245d8e29.png`; it is **not** a runtime dependency). The page
reproduces its information structure, not its paper imperfections.

## Structure

- Letterhead from tenant branding: seal, Republic of the Philippines, Province, Municipality, office name, "Monthly Income YYYY".
- One table: revenue line · Annual Target · Jan … Dec · Total · Percentage.
- "1. Receipts", then "A. Income from Market", "Rent Income (Stall Rental)", "Space Rental" with lines lettered a. … r. in
  the statement's order (the letter is presentation order, never classification authority).
- **No group subtotals** — the office sheet has none. One closing row, "Total Income Market Operation".

## Figures

- All figures are the server's `GetOfficialMonthlyIncomeQuery(year, null)`; the page computes nothing beyond what it shows.
- Annual Target and Percentage: "—" while no approved target exists (never 0 or 0%). Percentage is total ÷ target.
- Months: blank when not yet reached; **0.00** for a reached month with nothing collected (known zero); amounts otherwise.
- A line with money but no approved placement (Slaughterhouse, BBQ rent, other rental, unknown classifications) is listed
  under "Awaiting an approved official grouping", unlettered, included in the total with a note, and the screen shows
  "Official report requires review". It is never placed in a group by guess.
- A failed read shows an explicit error and disables Print; no partial statement is shown.

## Print

Browser Print / Save as PDF only (no server PDF exists, so none is claimed). A4 landscape, page margin 0 with body padding
(no browser header/footer), sidebar and controls hidden, table header repeated, rows not split.
