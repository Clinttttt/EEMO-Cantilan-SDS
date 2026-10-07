# Financial Reports V3 Direction

`/reports` is the office's financial report, not a dashboard. It is document-first: report title, period, section,
official grouping, figures, totals, print/export — before any analysis.

Design evidence (not runtime dependencies): the office's original *Monthly Income 2026* sheet plus the 2026-10-06 continuation now stored as `docs/evidence/2026-10-06_terminal_income_monthly_report.png`.

Together they confirm the formal statement structure:

- **A. Income From Market** with the existing Market, Rent Income (Stall Rental), and Space Rental rows;
- **B. Income From Terminal** with COMFORT ROOM, PULL PUL VANS, CARGO VANS, TRICYCAD, and Total Income from Terminal;
- **C. Income from Slaughterhouse**;
- **OVERALL TOTAL MARKET COLLECTION**;
- Prepared by / Certified Correct signatories.

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
- Preserve the office's own hierarchy and labels instead of forcing one continuous synthetic lettering sequence. A. Income From Market keeps its office rows; B. Income From Terminal uses a. Comfort Room, b. Pull Pul Vans/Cargo Vans, c. Tricycad.
- Show only evidence-backed subtotals. In particular, B ends with **Total Income from Terminal**.
- The complete statement ends with **OVERALL TOTAL MARKET COLLECTION**.
- Annual mode: Revenue line · Annual target · Jan–Dec · Total · %. Sticky header and first column, horizontal scroll,
  hairline month separators.
- Monthly mode: Revenue line · Annual target · selected month · YTD · %.
- Targets are never invented: without configuration the cells show "—" and one legend explains it once.
- Percentage is YTD actual ÷ annual target, never collection efficiency.
- IA-066 places BBQ under Rent Income and Slaughterhouse in its own official section. IA-067 adds the complete Terminal section and confirms that Transportation/Parking is separate from Terminal. Other genuinely unresolved rows stay in their own "Awaiting an approved official grouping" band; the UI never guesses placement.
- Legacy/canonical split lives in a collapsed **Reconciliation details** disclosure; the normal view shows one
  authoritative amount.

## Monthly Income (print)

Letterhead (tenant branding), "Monthly Income {year}", statement tables, configured signatories and formal black rules; no navigation, filters or buttons.

The complete A/B/C statement may span multiple A4 landscape pages. Do not shrink the report merely to preserve the older one-page implementation. Preserve the office ordering, repeat table headers when needed, and keep Total/Percentage readable.

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
- IA-067 confirms Terminal and the complete A/B/C Monthly Income statement; current runtime/frontend may still implement the earlier first-page-only report until the refactor lands.
- IA-068 confirms Fish/Meat independence from NPM and source-native vendor identity; report rows remain separate even when a vendor also has Weight & Measure activity.
- Other genuinely unresolved placement and recovered-Arrears treatment remain separate decision gates.
- The Financial Summary's Miscellaneous section is still the legacy utility view (see the NPM utility decoupling pass).

## Revenue Source Performance (Summary) — added 2026-10-01, aligned 2026-10-07

- The management view of **every** revenue source, facility or not (Market Fees, ECF, WCF, Tabo, Fish/Meat Vendor Fees,
  Landing/Berthing, Transportation/Parking, Weight & Measure, Transfer Large Cattle, Ice Plant, NPM/NCC/TCC/BBQ rent, Vegetable/Fruit,
  Kanmanggay, Fiesta/Araw, Fines, **Terminal — Comfort Room / Pull Pul Vans, Cargo Vans / Tricycad**, Slaughterhouse, Arrears, and any unknown classification kept apart by the statement).
- Server: `GET api/official-reports/source-performance` → `GetRevenueSourcePerformanceQueryHandler`. **Collected is the
  official Monthly Income row for the same period** (legacy before cutover + canonical after, once) — not a second algorithm.
- Overview and Reports present the same static five-column table: **Source · Collected · Annual target · Contribution ·
  Attention**. Rows cannot be expanded. Instrument, collection-model, document, collector and generic active/status labels
  stay out of this summary. The source name and existing analytic group separators remain unchanged.
- Annual-target values come from the server in both views. Overview is read-only; Reports → Trends & Targets retains the
  existing authorized target editor and revision workflow in the Annual target cell. No edit affordance appears on Overview.
- Posted transaction/document/collector counts remain **null, not zero**, for a row that also holds legacy money in the
  period; these counts are not displayed in this summary table.
- Contribution uses the source's existing `RevenueSourceCatalog` group amount. Facility-only and non-facility filters do
  not create a new filtered denominator; a zero group amount displays `—`.
- Attention is quiet (`—`) unless official placement is unresolved, a recurring facility register supplies a real unpaid
  amount, or legacy data materially limits interpretation. A paid-on-service source never receives an unpaid amount.
  Remittance is never a revenue metric.
- The section uses the same concise period helper on Overview and Reports; target-coverage status remains available in the
  Reports summary card rather than occupying this table header.
- The Overview loading skeleton mirrors the five-column read-only table and contains no row or target-edit controls.
- Facility analysis remains under Trends & Targets as **Facility performance**; the toolbar filter is "Facility analysis".

## Official Monthly Income (final output) — updated 2026-10-06

Interactive Monthly Income stays the analysis view (tabs, filters, reconciliation split, "Print this view"). Its header links to the dedicated **`/reports/monthly-income/official?year=YYYY`** page — no report tabs or analysis widgets; Back, Year and Print / Save as PDF remain the primary controls.

The official page must now reproduce the complete A. Market + B. Terminal + C. Slaughterhouse statement, overall total and configurable signatories. See `docs/interface/OFFICIAL_MONTHLY_INCOME_REPORT_V3.md`.

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
