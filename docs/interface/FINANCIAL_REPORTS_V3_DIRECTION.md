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
- IA-066 places BBQ under Rent Income and Slaughterhouse in its own official section. Other unresolved rental rows stay in their own
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
- **Trends & Targets** — revenue trend and **Facility performance** (the old "Revenue by Facility"): facility analysis only,
  not the complete revenue view and not the official statement.

## Financial Summary document

`/reports/financial-summary`: I. Collection position · II. Monthly Income by official group (server statement, group
subtotals) · III. Collection by model · IV. Outstanding position · V. Receivable aging · VI. Period comparison ·
VII. Facility performance · VIII. Accounts needing follow-up · IX. Miscellaneous.

## Responsive

Desktop office workspace (1366–1920). Tabs scroll horizontally when narrow; the annual table scrolls inside its surface;
the statement never collapses into cards.

## Known gaps

- IA-066 supplies approved annual-target revisions and report-only adjustment contracts. Frontend setup wiring remains a separate lane; unconfigured target cells show "—". OfficialAmount is SystemAmount plus the latest report delta; operational cash remains SystemAmount.
- BBQ and Slaughterhouse placement is resolved by IA-066. Other rental placement and any genuinely unresolved recovered-Arrears treatment remain separate decision gates.
- The Financial Summary's Miscellaneous section is still the legacy utility view (see the NPM utility decoupling pass).

## Revenue Source Performance (Summary) — added 2026-10-01

- The management view of **every** revenue source, facility or not (Market Fees, ECF, WCF, Tabo, Fish/Meat Vendor Fees,
  Landing/Berthing, Transportation, Weight & Measure, Transfer Large Cattle, Ice Plant, NPM/NCC/TCC/BBQ rent, Vegetable/Fruit,
  Kanmanggay, Fiesta/Araw, Fines, Slaughterhouse, Arrears, and any unknown classification kept apart by the statement).
- Server: `GET api/official-reports/source-performance` → `GetRevenueSourcePerformanceQueryHandler`. **Collected is the
  official Monthly Income row for the same period** (legacy before cutover + canonical after, once) — not a second algorithm.
- Dashboard Overview presents a static five-column table: **Source · Collected · Activity · Contribution · Attention**.
  Rows cannot be expanded. Instrument, collection-model, document, collector and generic active/status labels stay out of
  this management view. The source name and existing analytic group separators remain unchanged. This reduction applies
  only to Overview; Reports → Trends & Targets retains its existing analysis columns, expandable detail and target editor.
- Counts come from posted Collections and are **null, not zero**, for a row that also holds legacy money in the period.
  Activity shows a transaction count where available, `Legacy included` when the server cannot state that count, and
  facility-register paid/expected coverage for a recurring facility only when that same-period register is available.
- Contribution uses the source's existing `RevenueSourceCatalog` group amount. Facility-only and non-facility filters do
  not create a new filtered denominator; a zero group amount displays `—`.
- Attention is quiet (`—`) unless official placement is unresolved, a recurring facility register supplies a real unpaid
  amount, or legacy data materially limits interpretation. A paid-on-service source never receives an unpaid amount.
  Remittance is never a revenue metric.
- Annual-target values and editing remain in Reports → Trends & Targets; they are not part of the Overview table.
- Facility analysis remains under Trends & Targets as **Facility performance**; the toolbar filter is "Facility analysis".

## Official Monthly Income (final output) — added 2026-10-01

Interactive Monthly Income stays the analysis view (tabs, filters, reconciliation split, "Print this view"). Its header
links to the dedicated **`/reports/monthly-income/official?year=YYYY`** page — no report tabs, no analysis widgets; Back,
Year and Print / Save as PDF (browser, A4 landscape via `stalltrackPrint.landscape`). See
`docs/interface/OFFICIAL_MONTHLY_INCOME_REPORT_V3.md`.

Exactly-once relationship: Official Monthly Income, interactive Monthly Income, Revenue Source Performance and the Financial
Summary all read the same server statement; Collection Activity, operation reports and collector reports read the same
posted Collections. A Landing/Berthing CT therefore appears once in each.

## Controls and scope — 2026-10-01 consistency pass

- **Monthly | Annual** is one joined segmented control (shared border, single radius, internal divider, no seam).
- **Year · Month · Facility / Source Analysis** form one joined group at the same height. Each keeps its own value; the
  analysis scope defaults to **All sources** and narrows only the facility figures of Summary, Receivables and Trends.
- The section strip is a quiet sticky band under the page header on long reports (static in print).
- Revenue Source Performance has its own scope: All sources (default), Facilities only, Non-facility operations, or one
  analytic group. A cross-group scope lists rows only and states no subtotal: money totals stay the server's.
- The Dashboard uses the same server Revenue Source Performance row list and money (one source list, shared truth), with
  the five-column management presentation above; its hero figures are labelled as facility figures because the dashboard
  reader is facility-scoped.
- Collection Activity (`/collections/activity`) reads the legacy facility feed only; it says so and points to the
  Collections register for operation, utility and space-rental collections. A combined activity feed remains a backend gap.
