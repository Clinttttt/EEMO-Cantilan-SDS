# Financial Reports V3 Direction

`/reports` is the office's financial report, not a dashboard. It is document-first: report title, period, section,
official grouping, figures, totals, print/export — before any analysis.

Design evidence (not a runtime dependency): the office's paper *Monthly Income 2026* sheet, photographed locally at
`C:\Users\ASUS VIVOBOOK\Downloads\ce8e6144-563c-4b93-8098-2465245d8e29.png`. It shows the letterhead, the columns
Annual Target / Jan–Aug / Total / Percentage, the lettered rows a–r under *A. Income from Market*, *Rent Income (Stall
Rental)* and *Space Rental*, and the final *Total Income Market Operation*.

## Screen hierarchy

1. Topbar — "Financial Reports", office breadcrumb, generated time, account chip.
2. Section strip — Summary · Monthly Income · Collections · Accountability · Receivables · Trends & Targets. Underlined
   active tab in civic blue; no floating buttons inside a card.
3. Period toolbar (one line) — Monthly | Annual, Year, Month (monthly only), and **Facility / source only on Summary,
   Receivables and Trends**. Right side: the contextual export ("Export Financial Summary" on Summary only).
4. Report content, full main-column width.

The official statements (Monthly Income, Collections, Accountability) are keyed by the period alone and carry their own
Print beside their heading. No button is labelled as an export that does not exist.

## Monthly Income (screen)

- Document heading: office · municipality (tenant branding), "Monthly Income {year}", period, generated time, Print.
- Rows lettered continuously a, b, c… across the official groups, as on the office sheet; group rows are uppercase
  bands; each group ends with a subtotal; the statement ends with a navy **TOTAL INCOME** row.
- Annual mode: Revenue line · Annual target · Jan–Dec · Total · %. Sticky header and first column, horizontal scroll,
  hairline month separators.
- Monthly mode: Revenue line · Annual target · selected month · YTD · %.
- Targets are never invented: without configuration the cells show "—" and one legend explains it once.
- Percentage is YTD actual ÷ annual target, never collection efficiency.
- Lines whose official placement is unresolved (Slaughterhouse, BBQ rent, other rental) stay in their own
  "Awaiting an approved official grouping" band; a note says when Total income includes them.
- Legacy/canonical split lives in a collapsed **Reconciliation details** disclosure; the normal view shows one
  authoritative amount.

## Monthly Income (print)

Letterhead (tenant branding), "Monthly Income {year}", period, prepared-by and date; the full statement at 7.5pt with
black rules; no navigation, filters or buttons.

## Other sections

- **Summary** — one statement strip (assessed, collected, unpaid, collection rate) plus the cash/accountability strip
  (collected, YTD, target attainment, outstanding obligations, unremitted collections, exceptions). Outstanding
  obligations ≠ unremitted collector money.
- **Collections** — the official register of posted Collections; an itemized OR is stated once with its revenue lines
  indented beneath it. The facility records of sources not yet cut over are listed separately as *Legacy-source
  collections*; a collection appears in only one of the two.
- **Accountability** — collector × instrument: assigned, issued, spoiled, returned, on hand, review (counts) and
  collected, remitted, unremitted (pesos). Counts are never pesos.
- **Receivables** — delinquency and follow-up from the existing receivable reader.
- **Trends & Targets** — revenue trend and **Facility / source performance** (the old "Revenue by Facility"): secondary
  operational analysis, not the official statement.

## Financial Summary document

`/reports/financial-summary`: I. Collection position · II. Monthly Income by official group (server statement, group
subtotals) · III. Collection by model · IV. Outstanding position · V. Receivable aging · VI. Period comparison ·
VII. Facility / source performance · VIII. Accounts needing follow-up · IX. Miscellaneous.

## Responsive

Desktop office workspace (1366–1920). Tabs scroll horizontally when narrow; the annual table scrolls inside its surface;
the statement never collapses into cards.

## Known gaps

- No annual-target source or governance exists; target and % show "—".
- Official placement of Slaughterhouse, BBQ rent, other rental and recovered Arrears is unresolved (Decision Registry).
- The Financial Summary's Miscellaneous section is still the legacy utility view (see the NPM utility decoupling pass).
