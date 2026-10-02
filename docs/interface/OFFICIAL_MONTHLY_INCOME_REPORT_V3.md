# Official Monthly Income — final report (V3)

Route: `/reports/monthly-income/official?year=YYYY` (Admin, SuperAdmin). Component:
`EEMOCantilanSDS.Client/Components/Pages/Reports/OfficialMonthlyIncome.razor`.

## Design evidence

The office's printed "Monthly Income 2026" sheet, now in the repository at `docs/reference/monthly-income-office-reference.png`
(development evidence only; it is **not** a runtime dependency). The page reproduces its information structure, not its
paper imperfections (the sheet's own lettering skips "j." and repeats "g."; StallTrack letters lines consecutively).

## Structure

- Letterhead as on the sheet, in one centred row: the tenant's municipal seal (left), "Republic of the Philippines",
  Province and Municipality from tenant branding (centre), the national Bagong Pilipinas mark (right); then the office name.
  The mark is served from `wwwroot/images/bagong-pilipinas-logo-320.png`, a 320 px copy of the supplied 17 MB original.
- The statement's title, "Monthly Income YYYY", is the table's first column heading, as on the sheet (an `h1` with the same
  words is kept for assistive technology). The period and generation time are the first note under the table.
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

Geometry: the print helper leaves 273 mm of line width (297 mm − 2 × 12 mm). The table is `table-layout: fixed` with paper
widths line 31 mm, Annual Target and Total 18 mm each, twelve months 16 mm each, Percentage 13.6 mm (272.6 mm), at 6.4 pt,
so Total and Percentage are never clipped. "Percentage" breaks as "Percent / age", as on the sheet. A full year with all
eighteen lines and the notes fits one sheet (checked by rendering an A4 landscape PDF with Chromium, 2026-10-01).

On screen the sheet uses the workspace width; when the months need more room than the screen gives, they scroll inside the
sheet while the line column stays pinned.
