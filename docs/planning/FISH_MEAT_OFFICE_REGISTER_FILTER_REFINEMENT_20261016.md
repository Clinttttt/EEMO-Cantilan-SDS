# Fish / Meat Office Register — Compact Filters and Reporting Notes
Date: 2026-10-16

## Purpose
Reduce clutter in the Fish / Meat Office Register on the Head Portal, without losing original office-paper columns, tax-year accuracy, recorded collection figures, or Head-only editing authority.

## Scoped changes
- Keep native month picker (with its calendar affordance and maximum current month). Shorten visible label to **Month**.
- Replace the native registration tax-year and vendor-type dropdowns with the shared `FhSelect` control: internal captions **Tax year** and **Type**, visible choices and active checkmark, keyboard/screen-reader naming inherited from the shared component. Switching tax year still calls `ManageRegistrationsAsync`; switching type still filters only the loaded rows.
- Group the Print and Back actions; put all filters on a clean, bounded single toolbar, wrapping sensibly on narrower layouts.
- Replace two long on-screen paragraphs with one short note explicitly distinguishing recorded collections from amounts due and stating the walk-up fee total is excluded from registered-vendor rows. The longer explanation remains available in a collapsed **About this register** disclosure. Historical paper totals are *never* imported as collections.
- Retain all source columns (including missing/blank identity fields, address, nature of business, separate Vendor Fee and Weight & Measure revenues), the printable table, and the Head-authorized **Edit details** action introduced in the immediately preceding work.
- No DB, migration, API, amount calculation, registration status, SRC, historical row or payment changes.

## Verification
Focused Office Register regression passed (1/1), including office-paper columns, walk-up exclusions, year/type selector captions, Fish/Meat filtering and tax-year API reload. Full Head component suite passed **1,228/1,228** on rerun; the initial full run reported one unrelated Collection Activity timing failure (the isolated rerun passed). Head Portal was restarted from this isolated Release build: `https://localhost:7567/login` HTTP 200 and the protected office-register route redirects an unauthenticated request to login (expected). Local API `https://localhost:7597/health` HTTP 200. Manual authenticated browser acceptance is still needed for popover position and responsive spacing.
